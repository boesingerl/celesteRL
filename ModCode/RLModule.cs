using System;
using System.Collections.Generic;
using Monocle;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Threading;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using Microsoft.Xna.Framework.Graphics;

using System.Runtime.InteropServices;
using SkiaSharp;

namespace Celeste.Mod.RL
{


    public class IgnorePropertiesResolver : DefaultContractResolver
    {
        private readonly HashSet<string> ignoreProps;

        public IgnorePropertiesResolver(IEnumerable<string> propNamesToIgnore)
        {
            this.ignoreProps = new HashSet<string>(propNamesToIgnore);
        }

        protected override JsonProperty CreateProperty(
            MemberInfo member,
            MemberSerialization memberSerialization
        )
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            if (!this.ignoreProps.Contains(property.PropertyName))
            {
                property.ShouldSerialize = _ => false;
            }
            return property;
        }
    }

    public class RLModule : EverestModule
    {
        public static RLModule Instance;

        private ResponseSocket server;
        private Thread runningThread;
        private List<double> inputFrame;
        private List<double> inputFrameDisplay;

        private bool TPFlag;
        private bool runThread;

        private double distance;
        private double reward;
        private double fullReward;

        private readonly int DEFAULT_PORT = 7777;
        private int port;

        private double bestX;
        private double bestY;



        private double deltaX;
        private double deltaY;

        private Vector2 playerPos;
        private Vector2 prevPlayerPos;

        private int ts;


        private SKData obsBitmap;

        private Vector2 playerSpawn;
        private Vector2 playerSpawnC;
        private bool terminated;
        private const string StopFastRestartFlag = nameof(StopFastRestartFlag);
        private static Detour HGameUpdate;
        private static DGameUpdate OrigGameUpdate;
        private delegate void DGameUpdate(Game self, GameTime gameTime);

        public override Type SettingsType => typeof(RLSettings);
        public static RLSettings Settings => (RLSettings)Instance._Settings;
        private List<string> roomsVisited;

        public RLModule()
        {
            Instance = this;
            runningThread = null;

            port = DEFAULT_PORT;
            while (server is null)
            {

                try
                {
                    server = new ResponseSocket();
                    server.Bind($"tcp://*:{port}");
                }
                catch (AddressAlreadyInUseException)
                {
                    server = null;
                    port++;
                }
            }

            Console.WriteLine($"Connected to port {port}");

            inputFrame = null;
            inputFrameDisplay = null;
            distance = 0;

            playerSpawn = Vector2.Zero;
            playerPos = Vector2.Zero;
            terminated = true;

            roomsVisited = new List<string>();

            bestX = 0;
            bestY = 0;
            obsBitmap = null;

            reward = 0;
            ts = 0;

            TPFlag = false;
            runThread = false;
        }

        public override void Load()
        {



            runThread = true;

            OrigGameUpdate = (HGameUpdate = new Detour(
            typeof(Game).GetMethodInfo("Update"),
            typeof(RLModule).GetMethodInfo("GameUpdate")
            )).GenerateTrampoline<DGameUpdate>();

            using (new DetourContext { After = new List<string> { "*" } })
            {
                On.Monocle.Engine.Update += EngineUpdate;

                On.Monocle.MInput.GamePadData.Update += GpUpdate;
                Everest.Events.Player.OnSpawn += OnSpawn;
                Everest.Events.Player.OnDie += OnDie;
                On.Monocle.MInput.Update += MInput_Update;
                On.Celeste.Player.Update += PlayerUpdate;
                IL.Monocle.MInput.Update += MInputOnUpdate;
                On.Celeste.Level.Render += LevelOnRender;
                On.Celeste.Level.AfterRender += RenderObs;
                Everest.Events.Level.OnTransitionTo += OnTransitionTo;
            }

            SimplifiedGraphicsFeature.Initialize();
            CenterCamera.Load();
        }


        public override void Unload()
        {
            runThread = false;
            runningThread = null;


            On.Monocle.MInput.GamePadData.Update -= GpUpdate;
            Everest.Events.Player.OnSpawn -= OnSpawn;
            On.Monocle.MInput.Update -= MInput_Update;
            Everest.Events.Player.OnDie -= OnDie;
            On.Celeste.Player.Update -= PlayerUpdate;
            On.Monocle.Engine.Update -= EngineUpdate;
            IL.Monocle.MInput.Update -= MInputOnUpdate;
            On.Celeste.Level.Render -= LevelOnRender;
            On.Celeste.Level.AfterRender -= RenderObs;
            Everest.Events.Level.OnTransitionTo -= OnTransitionTo;

            SimplifiedGraphicsFeature.Unload();
            CenterCamera.Unload();


            server?.Dispose();
        }


        /// <summary>
        /// Get rendered target, interpolate it, and store in class
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void RenderObs(On.Celeste.Level.orig_AfterRender orig, Level self)
        {
            orig(self);
            RenderTarget2D target = GameplayBuffers.Level.Target;
            byte[] textureData = new byte[4 * target.Width * target.Height];
            target.GetData<byte>(textureData);

            // create an empty bitmap
            SKBitmap bitmap = new SKBitmap();

            // pin the managed array so that the GC doesn't move it
            var gcHandle = GCHandle.Alloc(textureData, GCHandleType.Pinned);

            // install the pixels with the color type of the pixel data
            var info = new SKImageInfo(target.Width, target.Height, SKColorType.Rgba8888);
            bitmap.InstallPixels(info, gcHandle.AddrOfPinnedObject(), info.RowBytes, delegate { gcHandle.Free(); }, null);

            using var subset = new SKBitmap();
            int width = 320;
            int height = 180;
            int squaresize = 32 * Settings.VisionSize;

            SkiaSharp.SKRectI rectI = new SkiaSharp.SKRectI(width / 2 - squaresize / 2,
                                                            height / 2 - squaresize / 2,
                                                            width / 2 + squaresize / 2,
                                                            height / 2 + squaresize / 2);
            var worked = bitmap.ExtractSubset(subset, rectI);


            obsBitmap = subset.Resize(new SKImageInfo(42, 42), (SKFilterQuality)Settings.Downsampling).Encode(SKEncodedImageFormat.Png, 0);

        }

        private static void MInput_Update(On.Monocle.MInput.orig_Update orig)
        {
            orig();
        }

        /// <summary>
        /// Update virtual gamepad with inputs from RL model
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void GpUpdate(
            On.Monocle.MInput.GamePadData.orig_Update orig,
            MInput.GamePadData self
        )
        {
            orig(self);

            if (!terminated && inputFrame != null && inputFrame.Count == 7)
            {
                // Convert agent action to keys pressed.
                GamePadButtons buttons = new GamePadButtons(
                    (inputFrame[0] > 0 ? Buttons.DPadLeft : 0)
                        | (inputFrame[1] > 0 ? Buttons.DPadRight : 0)
                        | (inputFrame[2] > 0 ? Buttons.DPadUp : 0)
                        | (inputFrame[3] > 0 ? Buttons.DPadDown : 0)
                        | (inputFrame[4] > 0 ? Buttons.A : 0)
                        | (inputFrame[5] > 0 ? Buttons.X : 0)
                        | (inputFrame[6] > 0 ? Buttons.B : 0)
                );
                GamePadDPad pad = new GamePadDPad(
                    inputFrame[2] == 1 ? ButtonState.Pressed : ButtonState.Released,
                    inputFrame[3] == 1 ? ButtonState.Pressed : ButtonState.Released,
                    inputFrame[0] == 1 ? ButtonState.Pressed : ButtonState.Released,
                    inputFrame[1] == 1 ? ButtonState.Pressed : ButtonState.Released
                );

                GamePadState state = new GamePadState(
                    new GamePadThumbSticks(new Vector2(0, 0), new Vector2(0, 0)),
                    new GamePadTriggers(0, 0),
                    buttons,
                    pad
                );

                MInput.GamePads[0].PreviousState = MInput.GamePads[0].CurrentState;
                MInput.GamePads[0].CurrentState = state;
                MInput.ControllerHasFocus = true;

                MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethod(
                    "UpdateVirtualInputs",
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                UpdateVirtualInputs.Invoke(null, new object[] { });

                if (Settings.FrameStep)
                {
                    inputFrame = null;

                }
            }
        }

        /// <summary>
        /// Access property or return null if it doesnt exist
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public static object GetProperty(Entity obj, string propertyName)
        {
            var prop = obj.GetType().GetProperties().FirstOrDefault(p => p.Name == propertyName);

            return prop is not null ? prop.GetValue(obj, null) : null;
        }

        /// <summary>
        /// On spawn, set initial position (used for computing the reward)
        /// </summary>
        /// <param name="player"></param>
        private void OnSpawn(Player player)
        {
            playerPos = player.Center;
            prevPlayerPos = playerPos;

            playerSpawn = player.Center;
            playerSpawnC = player.Position;
        }

        /// <summary>
        /// Additional reward when visiting a new room
        /// </summary>
        /// <param name="level"></param>
        /// <param name="next"></param>
        /// <param name="direction"></param>
        private void OnTransitionTo(Level level, LevelData next, Vector2 direction)
        {
            // Don't reward revisit of rooms.
            if (!roomsVisited.Contains(next.Name))
            {
                roomsVisited.Add(next.Name);
                reward += 10;
            }
        }

        /// <summary>
        /// On die, set flag to teleport to first room, lower reward
        /// </summary>
        /// <param name="player"></param>
        private void OnDie(Player player)
        {
            terminated = true;
            prevPlayerPos = playerPos;
            if (Settings.RespawnLvl1)
            {
                TPFlag = true;

            }
            reward -= 1;

            ts = 0;
        }

        /// <summary>
        /// Send rendered level observation to Gym RL env using NetMQ, recieve inputs to perform
        /// </summary>
        private void SendObs()
        {
            while (runThread)
            {
                if (obsBitmap is not null)
                {

                    string clpay = server.ReceiveFrameString();
                    inputFrame = JsonConvert.DeserializeObject<List<double>>(clpay);
                    inputFrameDisplay = inputFrame;


                    double dist = (playerPos.X - prevPlayerPos.X - (playerPos.Y - prevPlayerPos.Y)) / 10;
                    prevPlayerPos = playerPos;
                    fullReward = reward + dist;

                    string payload = JsonConvert.SerializeObject(
                        new List<object>() { obsBitmap.ToArray(), fullReward, terminated }
                    );
                    server.SendFrame(payload);

                    if (terminated || (inputFrame != null && inputFrame.Count == 1 && inputFrame[0] == 1))
                    {
                        terminated = false;
                        inputFrame = null;
                        distance = 0;

                        bestX = 0;
                        bestY = 0;
                        prevPlayerPos = playerPos;
                        roomsVisited.Clear();

                        if (Settings.RespawnLvl1)
                        {
                            TPFlag = true;

                        }
                    }
                    reward = 0;


                }
            }
        }

        private void updatePayload()
        {

        }

        /// <summary>
        /// Simply perform the original game update
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        /// <param name="gameTime"></param>
        private static void GameUpdate(
            On.Celeste.Celeste.orig_Update orig,
            Celeste self,
            GameTime gameTime
        )
        {
            OrigGameUpdate(self, gameTime);

        }

        /// <summary>
        /// Update controller even when the game is not focused
        /// </summary>
        /// <param name="il"></param>
        private static void MInputOnUpdate(ILContext il)
        {
            ILCursor ilCursor = new(il);
            ilCursor.Goto(il.Instrs.Count - 1);

            if (
                ilCursor.TryGotoPrev(
                    MoveType.After,
                    i => i.MatchCallvirt<MInput.MouseData>("UpdateNull")
                )
            )
            {
                ilCursor.EmitDelegate<Action>(UpdateGamePads);
            }

            // skip the orig GamePads[j].UpdateNull();
            if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(0)))
            {
                ilCursor.Emit(OpCodes.Ldc_I4_4).Emit(OpCodes.Add);
            }
        }

        /// <summary>
        /// Helper for MInputOnUpdate
        /// </summary>
        private static void UpdateGamePads()
        {
            for (int i = 0; i < 4; i++)
            {
                if (MInput.Active)
                {
                    MInput.GamePads[i].Update();
                }
                else
                {
                    MInput.GamePads[i].UpdateNull();
                }
            }
        }

        /// <summary>
        /// On render, draw HUD
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self)
        {
            orig(self);

            DrawInfo(self);
        }


        /// <summary>
        /// Draw HUD with current reward, actions the RL agent is taking, best X,Y positions
        /// </summary>
        /// <param name="level"></param>
        private void DrawInfo(Level level)
        {

            if (Settings.ShowHUD)
            {

                string text = $"Current Reward:{fullReward:F3}\nBest x: {bestX}\nCurrent x: {deltaX}\nBest y: {bestY}\nCurrent y: {deltaY}";
                string[] actions = new[] { "Left", "Right", "Up", "Down", "Jump", "Dash", "Grab" };
                string fulltext = text + "\n" + actions.Aggregate((a, b) => a + " " + b);

                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                int viewWidth = Engine.ViewWidth;
                int viewHeight = Engine.ViewHeight;

                float pixelScale = Engine.ViewWidth / 320f;
                float margin = 2 * pixelScale;
                float padding = 2 * pixelScale;
                float fontSize = 0.15f * pixelScale * 10f / 10f;
                float infoAlpha = 1f;
                float x = 10;
                float y = 10;
                float alpha = 0.8f;
                Vector2 Size = JetBrainsMonoFont.Measure(fulltext) * fontSize;

                Vector2 SizeBeforeActions = JetBrainsMonoFont.Measure(text) * fontSize;

                float maxX = viewWidth - Size.X - margin - padding * 2;
                float maxY = viewHeight - Size.Y - margin - padding * 2;

                Rectangle bgRect = new((int)x, (int)y, (int)(Size.X + padding * 2), (int)(Size.Y + padding * 2));

                Monocle.Draw.SpriteBatch.Begin();

                Draw.Rect(bgRect, Color.Black * alpha);

                Vector2 textPosition = new(x + padding, y + padding);
                Vector2 scale = new(fontSize);

                JetBrainsMonoFont.Draw(text, textPosition, Vector2.Zero, scale, Color.White * infoAlpha);

                float xActions = x + padding;
                float yActions = SizeBeforeActions.Y + y + padding;

                for (int i = 0; i < actions.Length; i++)
                {
                    string withSpace = actions[i] + " ";

                    try
                    {
                        JetBrainsMonoFont.Draw(actions[i],
        new(xActions, yActions),
        Vector2.Zero,
        scale,
        Color.White * (inputFrameDisplay != null && inputFrameDisplay.Count == 7 && inputFrameDisplay[i] > 0 ? 1f : 0.2f));
                        xActions += JetBrainsMonoFont.Measure(withSpace).X * fontSize;
                    }
                    // not that much of a problem, ignore
                    catch (ArgumentOutOfRangeException)
                    {

                    }


                }
                Draw.SpriteBatch.End();
            }
        }

        /// <summary>
        /// Update position for computing the reward
        /// </summary>
        private void PlayerUpdate(On.Celeste.Player.orig_Update orig, global::Celeste.Player self)
        {

            ts++;

            playerPos = self.Center;

            deltaX = self.Center.X - playerSpawn.X;
            deltaY = playerSpawn.Y - self.Center.Y;


            distance = (deltaX + deltaY) / 100;

            orig(self);
        }

        /// <summary>
        /// Update engine, frame stepping, speed up on death / room transition, teleport if flag is set
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        /// <param name="time"></param>
        private void EngineUpdate(
            On.Monocle.Engine.orig_Update orig,
            Engine self,
            GameTime time
        )
        {

            if (Engine.Scene is not Level level)
            {

                orig(self, time);

                return;
            }
            else
            {
                Player pl = level.Tracker.GetEntity<Player>();

                // Speed up intros
                for (
                    int i = 1;
                    i < Settings.RespawnRate
                        && (pl == null
                        || pl.StateMachine.State == Player.StIntroRespawn
                        || pl.StateMachine.State == Player.StIntroWalk
                        || pl.StateMachine.State == Player.StIntroJump
                        || pl.StateMachine.State == Player.StIntroWakeUp
                        || pl.StateMachine.State == Player.StIntroThinkForABit
                        );
                    i++
                )
                {
                    orig(self, time);
                }

                // Speed up level transitions
                for (int i = 1; i < Settings.RespawnRate && level.Transitioning; i++)
                {
                    orig(self, time);
                }

                // Other chapter speed ups
                for (int i = 1; i < Settings.RespawnRate && RequireFastRestart(level, pl); i++)
                {
                    orig(self, time);
                }

            }

            // Don't frame step if currently in pause
            if (level.Paused)
            {
                orig(self, time);
                return;
            }


            // Custom respawn rooms depending on which agent is currently playing
            // Temporary, only works for first level
            Player player = level.Tracker.GetEntity<Player>();
            string[] hardcodedLevelList = { "1", "2", "3", "4", "3b", "5", "6", "6a", "6b", "6c", "7", "8" };
            int respawnId = port - DEFAULT_PORT;


            // TP to first level room if flag is set
            if (Celeste.Scene is Level)
            {
                Level lvl = (Level)Celeste.Scene;
                if (TPFlag && player != null)
                {
                    if (lvl.Session.Level != hardcodedLevelList[respawnId])
                    {
                        MethodInfo CmdLoad = typeof(Commands).GetMethod(
                            "CmdLoad",
                            BindingFlags.Static | BindingFlags.NonPublic
                        );
                        CmdLoad.Invoke(null, new object[] { lvl.Session.Area.ID, hardcodedLevelList[respawnId] });
                    }
                    player.Position = playerSpawnC;
                    player.UpdateHair(true);

                    playerPos = player.Center;
                    prevPlayerPos = playerPos;
                    TPFlag = false;
                }
            }

            updatePayload();

            // Thread running to send observations to Gym environment
            if ((runningThread == null || !runningThread.IsAlive) && Celeste.Scene is Level)
            {
                runningThread = new Thread(SendObs);
                runningThread.Start();
            }

            // Step a single frame if setting is set, otherwise continuously run
            if (Settings.FrameStep)
            {
                if (inputFrame is not null)
                {
                    orig(self, time);
                    inputFrame = null;
                }
            }
            else
            {
                orig(self, time);
            }

        }

        /// <summary>
        /// Helper for detecting when to speed up game speed during respawn
        /// </summary>
        /// <param name="level"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        private static bool RequireFastRestart(Level level, Player player)
        {
            if (level.Session.GetFlag(StopFastRestartFlag))
            {
                return false;
            }

            bool result =
                !level.TimerStarted
                    && level.Session.Area.ID != 8
                    && !level.SkippingCutscene
                    && player?.StateMachine.State != Player.StIntroRespawn
                || level.TimerStarted
                    && !level.InCutscene
                    && level.Session.FirstLevel
                    && player?.InControl != true || level.Transitioning;

            if (!result)
            {
                level.Session.SetFlag(StopFastRestartFlag);
            }

            return result;
        }
    }





}
