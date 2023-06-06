/// <summary>
/// Simplified Graphics from CelesteTAS-EverestInterop, see original at https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/CelesteTAS-EverestInterop/Source/EverestInterop/SimplifiedGraphicsFeature.cs
/// </summary>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;


namespace  Celeste.Mod.RL;

public static class SimplifiedGraphicsFeature {
    private static readonly List<string> SolidDecals = new() {
        "3-resort/bridgecolumn",
        "3-resort/bridgecolumntop",
        "3-resort/brokenelevator",
        "3-resort/roofcenter",
        "3-resort/roofcenter_b",
        "3-resort/roofcenter_c",
        "3-resort/roofcenter_d",
        "3-resort/roofedge",
        "3-resort/roofedge_b",
        "3-resort/roofedge_c",
        "3-resort/roofedge_d",
        "4-cliffside/bridge_a",
    };

    private static bool lastSimplifiedGraphics = RLModule.Settings.SimplifiedGraphics;
    private static SolidTilesStyle currentSolidTilesStyle;
    private static bool creatingSolidTiles;

    public static void Initialize() {
        // Optional: Various graphical simplifications to cut down on visual noise.
        On.Celeste.Level.Update += Level_Update;

        Type t = typeof(SimplifiedGraphicsFeature);

        /* On.Celeste.CrystalStaticSpinner.CreateSprites += CrystalStaticSpinner_CreateSprites;
        IL.Celeste.CrystalStaticSpinner.GetHue += CrystalStaticSpinnerOnGetHue; */

        On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
        On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
        On.Celeste.MirrorSurfaces.Render += MirrorSurfacesOnRender;

        IL.Celeste.LightingRenderer.Render += LightingRenderer_Render;
        On.Celeste.ColorGrade.Set_MTexture_MTexture_float += ColorGradeOnSet_MTexture_MTexture_float;
        IL.Celeste.BloomRenderer.Apply += BloomRendererOnApply;

        On.Celeste.Decal.Render += Decal_Render;
        HookHelper.SkipMethod(t, nameof(IsSimplifiedDecal), "Render", typeof(CliffsideWindFlag), typeof(Flagline), typeof(FakeWall));

        HookHelper.SkipMethod(t, nameof(IsSimplifiedParticle),
            typeof(ParticleSystem).GetMethod("Render", new Type[] { }),
            typeof(ParticleSystem).GetMethod("Render", new[] {typeof(float)})
        );
        HookHelper.SkipMethod(t, nameof(IsSimplifiedDistort), "Apply", typeof(Glitch));
        HookHelper.SkipMethod(t, nameof(IsSimplifiedMiniTextbox), "Render", typeof(MiniTextbox));

        IL.Celeste.Distort.Render += DistortOnRender;
        On.Celeste.SolidTiles.ctor += SolidTilesOnCtor;
        On.Celeste.Autotiler.GetTile += AutotilerOnGetTile;
        On.Monocle.Entity.Render += BackgroundTilesOnRender;
        IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
        On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;

        IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;

        HookHelper.ReturnZeroMethod(t, nameof(SimplifiedWavedBlock),
            typeof(DreamBlock).GetMethodInfo("Lerp"),
            typeof(LavaRect).GetMethodInfo("Wave")
        );
        HookHelper.ReturnZeroMethod(
            t,
            nameof(SimplifiedWavedBlock),
            ModUtils.GetTypes().Where(type => type.FullName?.EndsWith("Renderer+Edge") == true)
                .Select(type => type.GetMethodInfo("GetWaveAt")).ToArray()
        );
        On.Celeste.LightningRenderer.Bolt.Render += BoltOnRender;

        IL.Celeste.Level.Render += LevelOnRender;

        On.Celeste.Audio.Play_string += AudioOnPlay_string;
        HookHelper.SkipMethod(t, nameof(IsSimplifiedLightningStrike), "Render",
            typeof(LightningStrike),
            ModUtils.GetType("ContortHelper", "ContortHelper.BetterLightningStrike")
        );

        HookHelper.SkipMethod(t, nameof(IsSimplifiedClutteredEntity), "Render",
            typeof(ReflectionTentacles), typeof(SummitCloud), typeof(TempleEye), typeof(Wire),
            typeof(DustGraphic).GetNestedType("Eyeballs", BindingFlags.NonPublic)
        );

        HookHelper.SkipMethod(
            t,
            nameof(IsSimplifiedHud),
            "Render",
            typeof(HeightDisplay), typeof(TalkComponent.TalkComponentUI), typeof(BirdTutorialGui), typeof(CoreMessage), typeof(MemorialText),
            typeof(Player).Assembly.GetType("Celeste.Mod.Entities.CustomHeightDisplay"),
            ModUtils.GetType("Monika's D-Sides", "Celeste.Mod.RubysEntities.AltHeightDisplay")
        );

        On.Celeste.Spikes.ctor_Vector2_int_Directions_string += SpikesOnCtor_Vector2_int_Directions_string;
    }

    public static void Unload() {
        On.Celeste.Level.Update -= Level_Update;

        On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
        On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
        On.Celeste.MirrorSurfaces.Render -= MirrorSurfacesOnRender;
        IL.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
        On.Celeste.LightningRenderer.Bolt.Render -= BoltOnRender;
        IL.Celeste.Level.Render -= LevelOnRender;
        On.Celeste.ColorGrade.Set_MTexture_MTexture_float -= ColorGradeOnSet_MTexture_MTexture_float;
        IL.Celeste.BloomRenderer.Apply -= BloomRendererOnApply;
        On.Celeste.Decal.Render -= Decal_Render;
        IL.Celeste.Distort.Render -= DistortOnRender;
        On.Celeste.SolidTiles.ctor -= SolidTilesOnCtor;
        On.Celeste.Autotiler.GetTile -= AutotilerOnGetTile;
        On.Monocle.Entity.Render -= BackgroundTilesOnRender;
        IL.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
        On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
        IL.Celeste.LightningRenderer.Render -= LightningRenderer_RenderIL;
        On.Celeste.Audio.Play_string -= AudioOnPlay_string;
        On.Celeste.Spikes.ctor_Vector2_int_Directions_string -= SpikesOnCtor_Vector2_int_Directions_string;
    }

    private static bool IsSimplifiedParticle() => RLModule.Settings.SimplifiedGraphics;

    private static bool IsSimplifiedDistort() => RLModule.Settings.SimplifiedGraphics;

    private static bool IsSimplifiedDecal() => RLModule.Settings.SimplifiedGraphics;

    private static bool IsSimplifiedMiniTextbox() => RLModule.Settings.SimplifiedGraphics;

    private static bool SimplifiedWavedBlock() => RLModule.Settings.SimplifiedGraphics;

    private static ScreenWipe SimplifiedScreenWipe(ScreenWipe wipe) =>
        RLModule.Settings.SimplifiedGraphics ? null : wipe;

    private static bool IsSimplifiedLightningStrike() => RLModule.Settings.SimplifiedGraphics;

    private static bool IsSimplifiedClutteredEntity() => RLModule.Settings.SimplifiedGraphics;

    private static bool IsSimplifiedHud() {
        return RLModule.Settings.SimplifiedGraphics;
    }

    private static void OnSimplifiedGraphicsChanged(bool simplifiedGraphics) {
        if (Engine.Scene is not Level level) {
            return;
        }

        if (simplifiedGraphics) {
            level.Tracker.GetEntities<FloatingDebris>().ForEach(debris => debris.RemoveSelf());
            level.Entities.FindAll<MoonCreature>().ForEach(creature => creature.RemoveSelf());
        }

        if (simplifiedGraphics && currentSolidTilesStyle != RLModule.Settings.SimplifiedSolidTilesStyle ||
            !simplifiedGraphics && currentSolidTilesStyle != default) {
            ReplaceSolidTilesStyle();
        }
    }

    public static void ReplaceSolidTilesStyle() {
        if (Engine.Scene is not Level {SolidTiles: { } solidTiles} level) {
            return;
        }

        Calc.PushRandom();

        SolidTiles newSolidTiles = new(new Vector2(level.TileBounds.X, level.TileBounds.Y) * 8f, level.SolidsData);

        if (solidTiles.Tiles is { } tiles) {
            tiles.RemoveSelf();
            newSolidTiles.Tiles.VisualExtend = tiles.VisualExtend;
            newSolidTiles.Tiles.ClipCamera = tiles.ClipCamera;
        }

        if (solidTiles.AnimatedTiles is { } animatedTiles) {
            animatedTiles.RemoveSelf();
            newSolidTiles.AnimatedTiles.ClipCamera = animatedTiles.ClipCamera;
        }

        solidTiles.Add(solidTiles.Tiles = newSolidTiles.Tiles);
        solidTiles.Add(solidTiles.AnimatedTiles = newSolidTiles.AnimatedTiles);

        Calc.PopRandom();
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        // Seems modified the Settings.SimplifiedGraphics property will mess key config.
        if (lastSimplifiedGraphics != RLModule.Settings.SimplifiedGraphics) {
            OnSimplifiedGraphicsChanged(RLModule.Settings.SimplifiedGraphics);
            lastSimplifiedGraphics = RLModule.Settings.SimplifiedGraphics;
        }
    }

    private static void LightingRenderer_Render(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall(typeof(MathHelper), "Clamp")
            )) {
            ilCursor.EmitDelegate<Func<float, float>>(IsSimplifiedLighting);
        }
    }

    private static float IsSimplifiedLighting(float alpha) {
        return RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedLighting != null
            ? (10 - RLModule.Settings.SimplifiedLighting.Value) / 10f
            : alpha;
    }

    private static void ColorGradeOnSet_MTexture_MTexture_float(On.Celeste.ColorGrade.orig_Set_MTexture_MTexture_float orig, MTexture fromTex,
        MTexture toTex, float p) {
        bool? origEnabled = null;
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedColorGrade) {
            origEnabled = ColorGrade.Enabled;
            ColorGrade.Enabled = false;
        }

        orig(fromTex, toTex, p);
        if (origEnabled.HasValue) {
            ColorGrade.Enabled = origEnabled.Value;
        }
    }

    private static void BloomRendererOnApply(ILContext il) {
        ILCursor ilCursor = new(il);
        while (ilCursor.TryGotoNext(
                   MoveType.After,
                   ins => ins.OpCode == OpCodes.Ldarg_0,
                   ins => ins.MatchLdfld<BloomRenderer>("Base")
               )) {
            ilCursor.EmitDelegate<Func<float, float>>(IsSimplifiedBloomBase);
        }

        while (ilCursor.TryGotoNext(
                   MoveType.After,
                   ins => ins.OpCode == OpCodes.Ldarg_0,
                   ins => ins.MatchLdfld<BloomRenderer>("Strength")
               )) {
            ilCursor.EmitDelegate<Func<float, float>>(IsSimplifiedBloomStrength);
        }
    }

    private static float IsSimplifiedBloomBase(float bloomValue) {
        return RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedBloomBase.HasValue
            ? RLModule.Settings.SimplifiedBloomBase.Value / 10f
            : bloomValue;
    }

    private static float IsSimplifiedBloomStrength(float bloomValue) {
        return RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedBloomStrength.HasValue
            ? RLModule.Settings.SimplifiedBloomStrength.Value / 10f
            : bloomValue;
    }

    private static void Decal_Render(On.Celeste.Decal.orig_Render orig, Decal self) {
        if (IsSimplifiedDecal()) {
            string decalName = self.Name.ToLower().Replace("decals/", "");
            if (!SolidDecals.Contains(decalName)) {
                if (!DecalRegistry.RegisteredDecals.TryGetValue(decalName, out DecalRegistry.DecalInfo decalInfo)) {
                    return;
                }

                if (decalInfo.CustomProperties.All(pair => pair.Key != "solid")) {
                    return;
                }
            }
        }

        orig(self);
    }

    private static void DistortOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(typeof(GFX), "FxDistort"))) {
            ilCursor.EmitDelegate<Func<Effect, Effect>>(IsSimplifiedDistort);
        }
    }

    private static Effect IsSimplifiedDistort(Effect effect) {
        return RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedDistort ? null : effect;
    }

    private static void SolidTilesOnCtor(On.Celeste.SolidTiles.orig_ctor orig, SolidTiles self, Vector2 position, VirtualMap<char> data) {
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedSolidTilesStyle != default) {
            currentSolidTilesStyle = RLModule.Settings.SimplifiedSolidTilesStyle;
        } else {
            currentSolidTilesStyle = SolidTilesStyle.All[0];
        }

        creatingSolidTiles = true;
        orig(self, position, data);
        creatingSolidTiles = false;
    }

    private static char AutotilerOnGetTile(On.Celeste.Autotiler.orig_GetTile orig, Autotiler self, VirtualMap<char> mapData, int x, int y,
        Rectangle forceFill, char forceId, Autotiler.Behaviour behaviour) {
        char tile = orig(self, mapData, x, y, forceFill, forceId, behaviour);
        if (creatingSolidTiles && RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedSolidTilesStyle != default && !default(char).Equals(tile) &&
            tile != '0') {
            return RLModule.Settings.SimplifiedSolidTilesStyle.Value;
        } else {
            return tile;
        }
    }

    private static void ModTileGlitcher(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("Monocle.MTexture>::set_Item"))) {
            if (ilCursor.TryFindPrev(out var cursors, ins => ins.OpCode == OpCodes.Ldarg_0,
                    ins => ins.OpCode == OpCodes.Ldfld && ins.Operand.ToString().Contains("<fgTexes>"),
                    ins => ins.OpCode == OpCodes.Ldarg_0, ins => ins.OpCode == OpCodes.Ldfld,
                    ins => ins.OpCode == OpCodes.Ldarg_0, ins => ins.OpCode == OpCodes.Ldfld
                )) {
                for (int i = 0; i < 6; i++) {
                    ilCursor.Emit(cursors[0].Next.OpCode, cursors[0].Next.Operand);
                    cursors[0].Index++;
                }

                ilCursor.EmitDelegate<Func<MTexture, VirtualMap<MTexture>, int, int, MTexture>>(IgnoreNewTileTexture);
            }
        }
    }

    private static MTexture IgnoreNewTileTexture(MTexture newTexture, VirtualMap<MTexture> fgTiles, int x, int y) {
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedSolidTilesStyle != default) {
            if (fgTiles[x, y] is { } texture && newTexture != null) {
                return texture;
            }
        }

        return newTexture;
    }

    private static void BackgroundTilesOnRender(On.Monocle.Entity.orig_Render orig, Entity self) {
        if (self is BackgroundTiles && RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedBackgroundTiles) {
            return;
        }

        orig(self);
    }

    private static void BackdropRenderer_Render(ILContext il) {
        ILCursor c = new(il);

        Instruction methodStart = c.Next;
        c.EmitDelegate(IsNotSimplifiedBackdrop);
        c.Emit(OpCodes.Brtrue, methodStart);
        c.Emit(OpCodes.Ret);
        if (c.TryGotoNext(ins => ins.MatchLdloc(out int _), ins => ins.MatchLdfld<Backdrop>("Visible"))) {
            Instruction ldloc = c.Next;
            c.Index += 2;
            c.Emit(ldloc.OpCode, ldloc.Operand).EmitDelegate(IsShow9DBlackBackdrop);
        }
    }

    private static bool IsNotSimplifiedBackdrop() {
        return !RLModule.Settings.SimplifiedGraphics || !RLModule.Settings.SimplifiedBackdrop;
    }

    private static bool IsShow9DBlackBackdrop(bool visible, Backdrop backdrop) {
        if (backdrop.Visible && Engine.Scene is Level level) {
            bool hideBackdrop =
                backdrop.Name?.StartsWith("bgs/nameguysdsides") == true &&
                (level.Session.Level.StartsWith("g") || level.Session.Level.StartsWith("h")) &&
                level.Session.Level != "hh-08";
            return !hideBackdrop;
        }

        return visible;
    }


    private static DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedDustSpriteEdge) {
            Color color = Color.Transparent;
            return new DustStyles.DustStyle {
                EdgeColors = new[] {color.ToVector3(), color.ToVector3(), color.ToVector3()},
                EyeColor = color,
                EyeTextures = "danger/dustcreature/eyes"
            };
        }

        return orig(session);
    }

    private static void FloatingDebris_ctor(On.Celeste.FloatingDebris.orig_ctor_Vector2 orig, FloatingDebris self, Vector2 position) {
        orig(self, position);
        if (RLModule.Settings.SimplifiedGraphics) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void MoonCreature_ctor(On.Celeste.MoonCreature.orig_ctor_Vector2 orig, MoonCreature self, Vector2 position) {
        orig(self, position);
        if (RLModule.Settings.SimplifiedGraphics) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void MirrorSurfacesOnRender(On.Celeste.MirrorSurfaces.orig_Render orig, MirrorSurfaces self) {
        if (!RLModule.Settings.SimplifiedGraphics) {
            orig(self);
        }
    }

    private static void LightningRenderer_RenderIL(ILContext il) {
        ILCursor c = new(il);
        if (c.TryGotoNext(i => i.MatchLdfld<Entity>("Visible"))) {
            Instruction lightningIns = c.Prev;
            c.Index++;
            c.Emit(lightningIns.OpCode, lightningIns.Operand).EmitDelegate<Func<bool, Lightning, bool>>(IsSimplifiedLightning);
        }

        if (c.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<LightningRenderer>("DrawEdges")
            )) {
            c.EmitDelegate<Func<bool, bool>>(drawEdges => (!RLModule.Settings.SimplifiedGraphics || !RLModule.Settings.SimplifiedWavedEdge) && drawEdges);
        }
    }

    private static bool IsSimplifiedLightning(bool visible, Lightning item) {
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedWavedEdge) {
            Rectangle rectangle = new((int) item.X + 1, (int) item.Y + 1, (int) item.Width, (int) item.Height);
            Draw.SpriteBatch.Draw(GameplayBuffers.Lightning, item.Position + Vector2.One, rectangle, Color.Yellow);
            if (visible) {
                Draw.HollowRect(rectangle, Color.LightGoldenrodYellow);
            }

            return false;
        }

        return visible;
    }

    private static void BoltOnRender(On.Celeste.LightningRenderer.Bolt.orig_Render orig, object self) {
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedWavedEdge) {
            return;
        }

        orig(self);
    }

    private static void LevelOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(i => i.MatchLdarg(0), i => i.MatchLdfld<Level>("Wipe"), i => i.OpCode == OpCodes.Brfalse_S)) {
            ilCursor.Index += 2;
            ilCursor.EmitDelegate(SimplifiedScreenWipe);
        }
    }

    private static EventInstance AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
        EventInstance result = orig(path);
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedLightningStrike &&
            path == "event:/new_content/game/10_farewell/lightning_strike") {
            result?.setVolume(0);
        }

        return result;
    }

    private static void SpikesOnCtor_Vector2_int_Directions_string(On.Celeste.Spikes.orig_ctor_Vector2_int_Directions_string orig, Spikes self,
        Vector2 position, int size, Spikes.Directions direction, string type) {
        if (RLModule.Settings.SimplifiedGraphics && RLModule.Settings.SimplifiedSpikes) {
            if (self.GetType().FullName != "VivHelper.Entities.AnimatedSpikes") {
                type = "outline";
            }
        }

        orig(self, position, size, direction, type);
    }


    private static Color GetTransparentColor() {
        return Color.Transparent;
    }


    internal static string ToDialogText(this string input) => Dialog.Clean("TAS_" + input.Replace(" ", "_"));

    public record struct SolidTilesStyle(string Name, char Value) {
        public static readonly List<SolidTilesStyle> All = new() {
            default,
            new SolidTilesStyle("Dirt", '1'),
            new SolidTilesStyle("Snow", '3'),
            new SolidTilesStyle("Girder", '4'),
            new SolidTilesStyle("Tower", '5'),
            new SolidTilesStyle("Stone", '6'),
            new SolidTilesStyle("Cement", '7'),
            new SolidTilesStyle("Rock", '8'),
            new SolidTilesStyle("Wood", '9'),
            new SolidTilesStyle("Wood Stone", 'a'),
            new SolidTilesStyle("Cliffside", 'b'),
            new SolidTilesStyle("Pool Edges", 'c'),
            new SolidTilesStyle("Temple A", 'd'),
            new SolidTilesStyle("Temple B", 'e'),
            new SolidTilesStyle("Cliffside Alt", 'f'),
            new SolidTilesStyle("Reflection", 'g'),
            new SolidTilesStyle("Reflection Alt", 'G'),
            new SolidTilesStyle("Grass", 'h'),
            new SolidTilesStyle("Summit", 'i'),
            new SolidTilesStyle("Summit No Snow", 'j'),
            new SolidTilesStyle("Core", 'k'),
            new SolidTilesStyle("Deadgrass", 'l'),
            new SolidTilesStyle("Lost Levels", 'm'),
            new SolidTilesStyle("Scifi", 'n'),
            new SolidTilesStyle("Solid Green", 'r')
        };

        public string Name = Name;
        public char Value = Value;

        public override string ToString() {
            return this == default ? "Default": Name;
        }
    }

    // ReSharper restore FieldCanBeMadeReadOnly.Global
}

internal class RemoveSelfComponent : Component {
    public RemoveSelfComponent() : base(true, false) { }

    public override void Added(Entity entity) {
        base.Added(entity);
        entity.Visible = false;
        entity.Collidable = false;
        entity.Collider = null;
    }

    public override void Update() {
        Entity?.RemoveSelf();
        RemoveSelf();
    }
}