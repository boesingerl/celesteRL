/// <summary>
/// Camera Centering, see https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/CelesteTAS-EverestInterop/Source/EverestInterop/CenterCamera.cs for original file 
/// </summary>

using System;
using System.Collections.Generic;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;


namespace Celeste.Mod.RL;

internal class CameraHitboxEntity : Entity
{
    private static readonly Color color = Color.LightBlue * 0.75f;
    private Vector2 cameraTopLeft;
    private Vector2 cameraBottomRight;
    private Level level;

    private bool DrawCamera => RLModule.Settings.CenterCamera && false;

    public override void Added(Scene scene)
    {
        base.Added(scene);
        Tag = Tags.Global | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate;
        level = scene as Level;
        Add(new PostUpdateHook(UpdateCameraHitbox));
    }

    private void UpdateCameraHitbox()
    {
        cameraTopLeft = level.MouseToWorld(Vector2.Zero);
        cameraBottomRight = level.MouseToWorld(new Vector2(Engine.ViewWidth, Engine.ViewHeight));
    }

    public override void DebugRender(Camera camera)
    {
        if (!DrawCamera)
        {
            return;
        }

        Draw.HollowRect(cameraTopLeft, cameraBottomRight.X - cameraTopLeft.X, cameraBottomRight.Y - cameraTopLeft.Y, color);
    }

    public static void Load()
    {
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
    }

    public static void Unload()
    {
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader)
    {
        orig(self, playerIntro, isFromLoader);
        if (!self.Tracker.Entities.TryGetValue(typeof(CameraHitboxEntity), out var entities) || entities.IsEmpty())
        {
            self.Add(new CameraHitboxEntity());
        }
    }
}

public static class CenterCamera
{
    private static Vector2? savedCameraPosition;
    private static float? savedLevelZoom;
    private static float? savedLevelZoomTarget;
    private static Vector2? savedLevelZoomFocusPoint;
    private static float? savedLevelScreenPadding;
    private static Vector2? lastPlayerPosition;
    private static Vector2 offset;
    private static Vector2 screenOffset;
    private static float viewportScale = 1f;


    // this must be <= 4096 / 320 = 12.8, it's used in FreeCameraHitbox and 4096 is the maximum texture size
    public const float MaximumViewportScale = 12f;

    public static float LevelZoom
    {
        get => 1 / viewportScale;
        private set => viewportScale = 1 / value;
    }

    public static bool LevelZoomOut => LevelZoom < 0.999f;

    public static Camera ScreenCamera { get; private set; } = new();

    public static void Load()
    {
        On.Monocle.Engine.RenderCore += EngineOnRenderCore;
        On.Monocle.Commands.Render += CommandsOnRender;
        On.Celeste.Level.Render += LevelOnRender;
#if DEBUG
        offset = Engine.Instance.GetDynamicDataInstance().Get<Vector2?>("CelesteTAS_Offset") ?? Vector2.Zero;
        screenOffset = Engine.Instance.GetDynamicDataInstance().Get<Vector2?>("CelesteTAS_Screen_Offset") ?? Vector2.Zero;
        LevelZoom = Engine.Instance.GetDynamicDataInstance().Get<float?>("CelesteTAS_LevelZoom") ?? 1f;
#endif
    }

    public static void Unload()
    {
        On.Monocle.Engine.RenderCore -= EngineOnRenderCore;
        On.Monocle.Commands.Render -= CommandsOnRender;
        On.Celeste.Level.Render -= LevelOnRender;
#if DEBUG
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_Offset", offset);
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_Screen_Offset", screenOffset);
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_LevelZoom", LevelZoom);
#endif
    }

    private static void EngineOnRenderCore(On.Monocle.Engine.orig_RenderCore orig, Engine self)
    {
        CenterTheCamera();
        orig(self);
        RestoreTheCamera();
    }

    // fix: clicked entity error when console and center camera are enabled
    private static void CommandsOnRender(On.Monocle.Commands.orig_Render orig, Monocle.Commands self)
    {
        CenterTheCamera();
        orig(self);
        RestoreTheCamera();
    }

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self)
    {
        orig(self);
        MoveCamera(self);
        ZoomCamera();
        LockCamera(self);
    }

    private static void LockCamera(Level level)
    {

    }

    private static void CenterTheCamera()
    {
        if (Engine.Scene is not Level level || !RLModule.Settings.CenterCamera)
        {
            return;
        }

        Camera camera = level.Camera;
        if (Engine.Scene.GetPlayer() is { } player)
        {
            lastPlayerPosition = ((Vector2?)null) ?? player.Position;
        }


        if (lastPlayerPosition != null)
        {
            savedCameraPosition = camera.Position;
            savedLevelZoom = level.Zoom;
            savedLevelZoomTarget = level.ZoomTarget;
            savedLevelZoomFocusPoint = level.ZoomFocusPoint;
            savedLevelScreenPadding = level.ScreenPadding;

            camera.Position = lastPlayerPosition.Value + offset - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);

            level.Zoom = LevelZoom;
            level.ZoomTarget = LevelZoom;
            level.ZoomFocusPoint = new Vector2(320f, 180f) / 2f;
            if (LevelZoomOut)
            {
                level.ZoomFocusPoint += screenOffset;
            }

            level.ScreenPadding = 0;

            ScreenCamera = new((int)Math.Round(320 * viewportScale), (int)Math.Round(180 * viewportScale));
            ScreenCamera.Position = lastPlayerPosition.Value + offset -
                                    new Vector2(ScreenCamera.Viewport.Width / 2f, ScreenCamera.Viewport.Height / 2f);
            if (LevelZoomOut)
            {
                ScreenCamera.Position += screenOffset;
            }
        }
    }

    private static void RestoreTheCamera()
    {
        if (Engine.Scene is not Level level)
        {
            return;
        }

        if (savedCameraPosition != null)
        {
            level.Camera.Position = savedCameraPosition.Value;
            savedCameraPosition = null;
        }

        if (savedLevelZoom != null)
        {
            level.Zoom = savedLevelZoom.Value;
            savedLevelZoom = null;
        }

        if (savedLevelZoomTarget != null)
        {
            level.ZoomTarget = savedLevelZoomTarget.Value;
            savedLevelZoomTarget = null;
        }

        if (savedLevelZoomFocusPoint != null)
        {
            level.ZoomFocusPoint = savedLevelZoomFocusPoint.Value;
            savedLevelZoomFocusPoint = null;
        }

        if (savedLevelScreenPadding != null)
        {
            level.ScreenPadding = savedLevelScreenPadding.Value;
            savedLevelScreenPadding = null;
        }
    }

    private static float ArrowKeySensitivity
    {
        get
        {

            return 1;

        }
    }

    public static void ResetCamera()
    {

    }

    private static void MoveCamera(Level level)
    {

    }

    private static void ZoomCamera()
    {
        
    }
}