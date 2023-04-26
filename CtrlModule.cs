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
using Celeste.Mod;
using static Celeste.Player.ChaserStateSound;

namespace Celeste.Mod.Ctrl
{

    public class IgnorePropertiesResolver : DefaultContractResolver
    {
        private readonly HashSet<string> ignoreProps;
        public IgnorePropertiesResolver(IEnumerable<string> propNamesToIgnore)
        {
            this.ignoreProps = new HashSet<string>(propNamesToIgnore);
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
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
        private List<int> inputFrame;

        private float reward;
        private float bestX;
        private Vector2 playerSpawn;
        private bool terminated;
        private bool spawned;
        public static int respawnSpeed = 20;
        private const string StopFastRestartFlag = nameof(StopFastRestartFlag);

        public CtrlModule()
        {
            instance = this;
            runningThread = null;
            server = new ResponseSocket();
            server.Bind("tcp://*:7777");
            inputFrame = null;
            reward = 0;
            playerSpawn = Vector2.Zero;
            terminated = true;
            spawned = true;
            bestX = 0;
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

        }

        public override void Unload()
        {
            On.Celeste.Celeste.Update -= GameUpdate;
            On.Monocle.MInput.GamePadData.Update -= GpUpdate;
            Everest.Events.Player.OnSpawn -= OnSpawn;
            Everest.Events.Player.OnDie -= OnDie;
            On.Celeste.Player.Update -= PlayerUpdate;
            On.Monocle.Engine.Update -= RespawnSpeed;
            IL.Monocle.MInput.Update -= MInputOnUpdate;

            server?.Dispose();

        }

        private void GpUpdate(On.Monocle.MInput.GamePadData.orig_Update orig, MInput.GamePadData self)
        {
            orig(self);

            if (!terminated && inputFrame != null && inputFrame.Count == 7)
            {


                // Convert agent action to keys pressed.
                GamePadButtons buttons = new GamePadButtons(
                    (inputFrame[0] == 1 ? Buttons.DPadLeft : 0)
                    | (inputFrame[1] == 1 ? Buttons.DPadRight : 0)
                    | (inputFrame[2] == 1 ? Buttons.DPadUp : 0)
                    | (inputFrame[3] == 1 ? Buttons.DPadDown : 0)
                    | (inputFrame[4] == 1 ? Buttons.A : 0)
                    | (inputFrame[5] == 1 ? Buttons.X : 0)
                    | (inputFrame[6] == 1 ? Buttons.RightTrigger : 0)
                    );
                GamePadDPad pad = new GamePadDPad(
                    inputFrame[2] == 1 ? ButtonState.Pressed : ButtonState.Released,
                    inputFrame[3] == 1 ? ButtonState.Pressed : ButtonState.Released,
                    inputFrame[0] == 1 ? ButtonState.Pressed : ButtonState.Released,
                    inputFrame[1] == 1 ? ButtonState.Pressed : ButtonState.Released
                );

                GamePadState state = new GamePadState(
                    new GamePadThumbSticks(new Vector2(0, 0), new Vector2(0, 0)),
                    new GamePadTriggers(0, inputFrame[6] == 1 ? 1 : 0),
                    buttons,
                    pad
                );

                MInput.GamePads[0].PreviousState = MInput.GamePads[0].CurrentState;
                MInput.GamePads[0].CurrentState = state;
                MInput.ControllerHasFocus = true;

                MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethod("UpdateVirtualInputs", BindingFlags.Static | BindingFlags.NonPublic);
                UpdateVirtualInputs.Invoke(null, new object[] { });


#if false
                if (Engine.Commands.Open)
                {
                    MethodInfo UpdateKeyboard = typeof(MInput.KeyboardData).GetMethod("UpdateNull", BindingFlags.Instance | BindingFlags.NonPublic);
                    UpdateKeyboard.Invoke(MInput.Keyboard, new object[] { });
                }
                else
                {
                    MethodInfo UpdateKeyboard = typeof(MInput.KeyboardData).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    UpdateKeyboard.Invoke(MInput.Keyboard, new object[] { });
                }

                MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethod("UpdateVirtualInputs", BindingFlags.Static | BindingFlags.NonPublic);
                UpdateVirtualInputs.Invoke(null, new object[] { });

                MInput.ControllerHasFocus = false;
#endif


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
        }


        private void GameUpdate(On.Celeste.Celeste.orig_Update orig, Celeste self, GameTime gameTime)
        {

            if (runningThread == null || !runningThread.IsAlive)
            {
                runningThread = new Thread(() =>
                {

                    if (Celeste.Scene == null || Celeste.Scene.GetType() != typeof(Level))
                        return;

                    Level celesteLevel = (Level)Celeste.Scene;

                    SolidTiles tiles = celesteLevel.SolidTiles;
                    var mTextures = tiles.Tiles.Tiles.ToArray();
                    var leveldata = celesteLevel.Session.LevelData;
                    Player player = Celeste.Scene.Tracker.GetEntity<Player>();

                    if (player == null)
                        return;

                    Entity[] entits = Monocle.Engine.Scene.Entities.ToArray();
                    List<Dictionary<string, string>> entities_ser = new List<Dictionary<string, string>>();

                    bool playerPresent = false;

                    foreach (Entity ent in entits)
                    {
                        Dictionary<string, string> attrs = new Dictionary<string, string>();

                        attrs["Name"] = ent.GetType().ToString();

                        if (attrs["Name"] == "Celeste.Player")
                        {
                            playerPresent = true;
                        }

                        foreach (string attrname in new[] { "X", "Y", "Width", "Height", "Left", "Right", "Bottom", "Top", "Direction" })
                        {
                            var val = GetProperty(ent, attrname);
                            if (val != null)
                            {
                                attrs[attrname] = val.ToString();

                            }
                    }


                        entities_ser.Add(attrs);
                    }

                    if (playerPresent || terminated)
                    {
                        Dictionary<string, object> dic = new Dictionary<string, object>
                        {
                            ["solids"] = leveldata.Solids,
                            ["bounds"] = leveldata.Bounds,
                            ["player"] = player.Position,
                            ["entities"] = entities_ser

                        };

                        string payload = JsonConvert.SerializeObject(new List<object>() { dic, reward, terminated });

                        string clpay = server.ReceiveFrameString();
                        inputFrame = JsonConvert.DeserializeObject<List<int>>(clpay);

                        server.SendFrame(payload);

                        if (inputFrame != null && inputFrame.Count == 1)
                        {
                            terminated = false;
                            reward = 0;
                            bestX = 0;

                            DynamicData cmd = new DynamicData(typeof(Commands));
                            cmd.Invoke("CmdLoadIDorSID", "1", "1");
                        }
                    }

                });
                runningThread.Start();
            }



            orig(self, gameTime);



        }

            // update controller even the game is lose focus 
    private static void MInputOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        ilCursor.Goto(il.Instrs.Count - 1);

        if (ilCursor.TryGotoPrev(MoveType.After, i => i.MatchCallvirt<MInput.MouseData>("UpdateNull"))) {
            ilCursor.EmitDelegate<Action>(UpdateGamePads);
        }

        // skip the orig GamePads[j].UpdateNull();
        if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(0))) {
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


        private void PlayerUpdate(On.Celeste.Player.orig_Update orig, global::Celeste.Player self)
        {

            float deltaX = Convert.ToInt32(self.Center.X - playerSpawn.X);

            if (deltaX > bestX)
            {
                reward = ((deltaX - bestX)/100);
                bestX = deltaX;
            }
            

            orig(self);
        }

        public void InputUpdate(On.Monocle.MInput.KeyboardData.orig_Update orig, global::Monocle.MInput.KeyboardData self)
        {
            // orig(self);




        }

        private static void RespawnSpeed(On.Monocle.Engine.orig_Update orig, Engine self, GameTime time)
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
            for (int i = 1; i < respawnSpeed && (player == null || player.StateMachine.State == Player.StIntroRespawn); i++)
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

            bool result = !level.TimerStarted && level.Session.Area.ID != 8 && !level.SkippingCutscene &&
                          player?.StateMachine.State != Player.StIntroRespawn ||
                          level.TimerStarted && !level.InCutscene && level.Session.FirstLevel && player?.InControl != true;

            if (!result)
            {
                level.Session.SetFlag(StopFastRestartFlag);
            }

            return result;
        }

    }



#if false
        /// <summary>
        /// 
        /// </summary>
        /// <param name="self"></param>
        public delegate void orig_Tick(Game self);
        public static void GameTick(orig_Tick orig, Game self)
        {
            DynamicData game = new DynamicData(self);
            game.Set("IsFixedTimeStep", false);
            orig(self);
        }

        private void OnSpawn(Player player)
        {
            prevCenter = player.Center;
            playerReady = true;
        }

        private void OnDie(Player player)
        {
            playerReady = false;
            roomsVisited.Clear();
            reward -= 10;
            terminated = true;

            // Reset to first room on death.
            DynamicData cmd = new DynamicData(typeof(Commands));
            cmd.Invoke("CmdLoadIDorSID", "1", "1");
        }

        private void OnTransitionTo(Level level, LevelData next, Vector2 direction)
        {
            // Don't reward revisit of rooms.
            if (!roomsVisited.Contains(next.Name))
            {
                roomsVisited.Add(next.Name);
                reward += 200;
            }
        }

        private void GameRun(On.Monocle.Engine.orig_RunWithLogging orig, global::Monocle.Engine self)
        {
            try
            {
                // Create ZMQ connection for RL agent.
                server = new ResponseSocket();
                server.Bind("tcp://*:7777");
                orig(self);
            }
            finally
            {
                server?.Dispose();
            }
        }

        private void GameUpdate(On.Celeste.Celeste.orig_Update orig, Celeste self, GameTime gameTime)
        {
            // Custom stepping.
            if (playerReady)
            {
                // Read agent command.
                string payload = server.ReceiveFrameString();
                inputFrame = JsonConvert.DeserializeObject<List<int>>(payload);

                // Run update.
                orig(self, gameTime);

                // Send back reward.
                payload = JsonConvert.SerializeObject(new List<int> { reward, terminated ? 1 : 0 });
                server.SendFrame(payload);

                // Reset reward and terminated status.
                reward = -1;
                terminated = false;
            }
            else
            {
                // Regular updates.
                orig(self, gameTime);
            }
        }

        private void PlayerUpdate(On.Celeste.Player.orig_Update orig, global::Celeste.Player self)
        {
            // Naive reward of how much the agent moved toward the top right.
            float deltaX = Convert.ToInt32(self.Center.X - prevCenter.X);
            float deltaY = -Convert.ToInt32(self.Center.Y - prevCenter.Y); // Up is negative.
            int dist = Convert.ToInt32(Math.Sqrt(deltaX * deltaX + deltaY * deltaY)); // Dist is positive.
            if (deltaX < 0 || deltaY < 0)
            {
                // Only give positive rewards for moving up and/or to the right.
                dist *= -1;
            }
            reward += dist;
            prevCenter = self.Center;

            // If agent doesn't move for 1000 timesteps, terminate.
            if (deltaX == 0 && deltaY == 0)
            {
                if (++idleCount >= 150)
                {
                    idleCount = 0;
                    reward -= 100;
                    terminated = true;
                }
            }

            orig(self);
        }

        private void InputUpdate(On.Monocle.MInput.KeyboardData.orig_Update orig, global::Monocle.MInput.KeyboardData self)
        {
            orig(self);
            if (playerReady && inputFrame != null && inputFrame.Count == 7)
            {
                // Convert agent action to keys pressed.
                List<Keys> keys = new List<Keys>();
                if (inputFrame[0] == 1)
                {
                    keys.Add(Keys.Left);
                }
                if (inputFrame[1] == 1)
                {
                    keys.Add(Keys.Right);
                }
                if (inputFrame[2] == 1)
                {
                    keys.Add(Keys.Up);
                }
                if (inputFrame[3] == 1)
                {
                    keys.Add(Keys.Down);
                }
                if (inputFrame[4] == 1)
                {
                    // Jump
                    keys.Add(Keys.C);
                }
                if (inputFrame[5] == 1)
                {
                    // Dash
                    keys.Add(Keys.X);
                }
                if (inputFrame[6] == 1)
                {
                    // Grab
                    keys.Add(Keys.Z);
                }

                // Press desired keys.
                KeyboardState state = new KeyboardState(keys.ToArray());
                Monocle.MInput.Keyboard.CurrentState = state;

            }
            else if (inputFrame != null && inputFrame.Count == 1)
            {
                // Reset not really needed as this is done on death and next update.
                roomsVisited.Clear();
                terminated = false;

                // Reset to first room on death.
                DynamicData cmd = new DynamicData(typeof(Commands));
                cmd.Invoke("CmdLoadIDorSID", "1", "1");
            }
            // Reset inputFrame.
            inputFrame = null;
        }

#endif
}


