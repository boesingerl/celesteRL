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
using TAS.EverestInterop.InfoHUD;

namespace Celeste.Mod.Ctrl
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

    public class CtrlModule : EverestModule
    {
        public static CtrlModule instance;

        private ResponseSocket server;
        private Thread runningThread;
        private List<double> inputFrame;
        private bool TPFlag;
        private bool runThread;
        private bool playerPresent;
        private Dictionary<string, object> observations;
        private List<double> previousRewards;
        private double distance;
        private double reward;
        private double bestX;
        private double bestY;
        private double deltaX;
        private double deltaY;

        private int ts;
        private int xUpdateTs;
        private int yUpdateTs;

        private Vector2 playerSpawn;
        private bool terminated;
        private double timesteps;
        public static int respawnSpeed = 20;
        private const string StopFastRestartFlag = nameof(StopFastRestartFlag);

        public CtrlModule()
        {
            instance = this;
            runningThread = null;
            server = new ResponseSocket();
            server.Bind("tcp://*:7777");
            inputFrame = null;
            distance = 0;
            previousRewards = new List<double>();
            playerSpawn = Vector2.Zero;
            terminated = true;
            timesteps = 0;
            bestX = 0;
            reward = 0;
            ts = 0;
            xUpdateTs = 0;
            yUpdateTs = 0;
            TPFlag = false;
            runThread = false;
            playerPresent = false;
            observations = null;
        }

        public override void Load()
        {
            On.Celeste.Celeste.Update += GameUpdate;
            On.Monocle.MInput.GamePadData.Update += GpUpdate;
            Everest.Events.Player.OnSpawn += OnSpawn;
            Everest.Events.Player.OnDie += OnDie;
            On.Celeste.Player.Update += PlayerUpdate;
            On.Monocle.Engine.Update += RespawnSpeed;
            IL.Monocle.MInput.Update += MInputOnUpdate;
            On.Celeste.Level.Render += LevelOnRender;
            

            runThread = true;
            runningThread = new Thread(SendObs);
            runningThread.Start();
        }

        public override void Unload()
        {
            runThread = false;
            runningThread = null;

            On.Celeste.Celeste.Update -= GameUpdate;
            On.Monocle.MInput.GamePadData.Update -= GpUpdate;
            Everest.Events.Player.OnSpawn -= OnSpawn;
            Everest.Events.Player.OnDie -= OnDie;
            On.Celeste.Player.Update -= PlayerUpdate;
            On.Monocle.Engine.Update -= RespawnSpeed;
            IL.Monocle.MInput.Update -= MInputOnUpdate;
            On.Celeste.Level.Render -= LevelOnRender;

            server?.Dispose();
        }

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
            }
        }

        public static object GetProperty(Entity obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName);

            return prop == null ? null : prop.GetValue(obj, null);
        }

        private void OnSpawn(Player player)
        {
            playerSpawn = player.Center;
        }

        private void OnDie(Player player)
        {
            terminated = true;
            TPFlag = true;
            //reward -= 10;

            ts = 0;
            xUpdateTs = 0;
            yUpdateTs = 0;
        }

        private void SendObs(){
            while (runThread)
            {
                if (observations is not null && (playerPresent || terminated))
                {
                    
                    string clpay = server.ReceiveFrameString();
                    inputFrame = JsonConvert.DeserializeObject<List<double>>(clpay);

                    double fullReward = distance - timesteps + reward;
                    string payload = JsonConvert.SerializeObject(
                        new List<object>() { observations, fullReward, false }
                    );
                    previousRewards.Add(fullReward);
                    server.SendFrame(payload);


                    terminated = false;
                    distance = 0;
                    timesteps = 0;
                    bestX = 0;
                    bestY  = 0;
                    TPFlag = true;
                    reward = 0;
                    previousRewards.Clear();

                    
                }
            }
        }

        private void updatePayload()
        {

            if (Celeste.Scene is not Level || Celeste.Scene.GetType() != typeof(Level))
                return;

            Level celesteLevel = (Level)Celeste.Scene;


            SolidTiles tiles = celesteLevel.SolidTiles;
            var mTextures = tiles.Tiles.Tiles.ToArray();
            var leveldata = celesteLevel.Session.LevelData;
            

            Player player = Celeste.Scene.Tracker.GetEntity<Player>();
            if (player == null)
                return;
            Vector2 playerPos = player.Position;


            Entity[] entits = Monocle.Engine.Scene.Entities.ToArray();
            List<Dictionary<string, string>> entities_ser =
                new List<Dictionary<string, string>>();

            playerPresent = false;

            foreach (Entity ent in entits)
            {
                Dictionary<string, string> attrs = new Dictionary<string, string>();

                attrs["Name"] = ent.GetType().ToString();

                if (attrs["Name"] == "Celeste.Player")
                {
                    playerPresent = true;
                }

                foreach (
                    string attrname in new[]
                    {
                        "X",
                        "Y",
                        "Width",
                        "Height",
                        "Left",
                        "Right",
                        "Bottom",
                        "Top",
                        "Direction"
                    }
                )
                {
                    var val = GetProperty(ent, attrname);
                    if (val != null)
                    {
                        attrs[attrname] = val.ToString();
                    }
                }

                entities_ser.Add(attrs);
            }


            
            bool canDash = (ReflectionExtensions.GetPropertyValue<int>(player, "dashCooldownTimer") <= 0 && player.Dashes > 0);
            Vector2 speed = player.Speed;
            bool climbing = player.StateMachine.State == 1;

            observations = new Dictionary<string, object>
                {
                    ["canDash"] = canDash,
                    ["speed"] = speed,
                    ["climbing"] = climbing,
                    ["solids"] = leveldata.Solids,
                    ["bounds"] = leveldata.Bounds,
                    ["player"] = playerPos,
                    ["entities"] = entities_ser
                };
        }

        private void GameUpdate(
            On.Celeste.Celeste.orig_Update orig,
            Celeste self,
            GameTime gameTime
        )
        {
            if (Celeste.Scene is Level)
            {
                Level lvl = (Level)Celeste.Scene;
                Player player = lvl.Tracker.GetEntity<Player>();
                if (TPFlag && player != null)
                {
                    if (lvl.Session.Level != "1")
                    {
                        MethodInfo CmdLoad = typeof(Commands).GetMethod(
                            "CmdLoad",
                            BindingFlags.Static | BindingFlags.NonPublic
                        );
                        CmdLoad.Invoke(null, new object[] { 1, "1" });
                    }

                    TPFlag = false;
                }
            }

            updatePayload();

            if (runningThread == null || !runningThread.IsAlive)
            {
                runningThread = new Thread(() => { });
                runningThread.Start();
            }

            orig(self, gameTime);
        }

        // update controller even the game is lose focus
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

    private void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);


        DrawInfo(self);

        
    }


    private void DrawInfo(Level level) {

        string lastRewards = JsonConvert.SerializeObject(previousRewards.Skip(Math.Max(0, previousRewards.Count() - 5)).Select(i => $"{i:F3}"));
        string text = $"Current Reward:{distance - timesteps:F3}\nBest x: {bestX}\nCurrent x: {deltaX}\nBest y: {bestY}\nCurrent y: {deltaY}\nPrev: {lastRewards}";
        string[] actions = new []{"Left", "Right", "Up", "Down", "Jump", "Dash", "Grab"};
        string fulltext = text + "\n" + actions.Aggregate((a,b) => a + " " + b);

        if (string.IsNullOrEmpty(text)) {
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

        float maxX = viewWidth - Size.X - margin - padding * 2;
        float maxY = viewHeight - Size.Y - margin - padding * 2;

        Rectangle bgRect = new((int) x, (int) y, (int) (Size.X + padding * 2), (int) (Size.Y + padding * 2));

        Monocle.Draw.SpriteBatch.Begin();

        Draw.Rect(bgRect, Color.Black * alpha);

        Vector2 textPosition = new(x + padding, y + padding);
        Vector2 scale = new(fontSize);

        JetBrainsMonoFont.Draw(text, textPosition, Vector2.Zero, scale, Color.White * infoAlpha);

        float xActions = x + padding;
        float yActions = Size.Y + pixelScale;

        for (int i = 0; i < actions.Length; i++)
            {
            string withSpace = actions[i] + " ";

            try{
                            JetBrainsMonoFont.Draw(actions[i],
            new(xActions, yActions),
            Vector2.Zero,
            scale,
            Color.White * (inputFrame != null && inputFrame.Count == 7 && inputFrame[i] > 0 ? 1f : 0.2f));
            xActions += JetBrainsMonoFont.Measure(withSpace).X * fontSize;
            }
            // not that much of a problem, ignore
            catch(ArgumentOutOfRangeException){

            }


        }
        Draw.SpriteBatch.End();
    }

        private void PlayerUpdate(On.Celeste.Player.orig_Update orig, global::Celeste.Player self)
        {

            ts++;

            deltaX = self.Center.X - playerSpawn.X;
            deltaY = playerSpawn.Y - self.Center.Y;

            if (deltaX > bestX)
            {
                bestX = deltaX;
                xUpdateTs = ts;
            }

            if(deltaY > bestY){
                bestY = deltaY;
                yUpdateTs = ts;
            }

            if(ts - yUpdateTs > 1000 && ts - xUpdateTs > 1000){
                self.Die(Vector2.Zero);
                reward -= 100;
            }

            distance = (deltaX + deltaY)/100;

            timesteps += 0.003;

            orig(self);
        }

        private static void RespawnSpeed(
            On.Monocle.Engine.orig_Update orig,
            Engine self,
            GameTime time
        )
        {
            orig(self, time);

            if (Engine.Scene is not Level level)
            {
                return;
            }

            if (level.Paused)
            {
                return;
            }

            Player player = level.Tracker.GetEntity<Player>();

            // 加速复活过程
            for (
                int i = 1;
                i < respawnSpeed
                    && (player == null || player.StateMachine.State == Player.StIntroRespawn);
                i++
            )
            {
                orig(self, time);
            }

            // 加速章节启动
            for (int i = 1; i < respawnSpeed && RequireFastRestart(level, player); i++)
            {
                orig(self, time);
            }
        }

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
                    && player?.InControl != true;

            if (!result)
            {
                level.Session.SetFlag(StopFastRestartFlag);
            }

            return result;
        }
    }





}
