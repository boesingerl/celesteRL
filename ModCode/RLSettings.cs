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
using static Celeste.Mod.RL.SimplifiedGraphicsFeature;

namespace Celeste.Mod.RL
{

    // If no SettingName is applied, it defaults to
    // modoptions_[typename without settings]_title
    // The value is then used to look up the UI text in the dialog files.
    // If no dialog text can be found, Everest shows a prettified mod name instead.
    [SettingName("modoptions_rlmodule_title")]
    public class RLSettings : EverestModuleSettings {

        // SettingName also works on props, defaulting to
        // modoptions_[typename without settings]_[propname]

        // Example ON / OFF property with a default value.

        // Interpolation type to use when resizing observations 
        [SettingRange(0, 3)] 
        public int Downsampling { get; set; } = 0;

        // Distance around Madeline to show when creating observation
        [SettingRange(1, 5)] 
        public int VisionSize { get; set; } = 3;

        // Frame stepping 
        public bool FrameStep { get; set; } = false;

        // Show HUD with reward, inputs, etc
        public bool ShowHUD { get; set; } = false;

        // Whether to TP to first room on death
        public bool RespawnLvl1 { get; set; } = false;

        // Whether to center camera
        public bool CenterCamera { get; set; } = false;

        // Simplified graphics options (todo: set them as submenu instead)
        public bool SimplifiedGraphics { get; set; } = false;
        public int? SimplifiedLighting { get; set; } = 10;
        public int? SimplifiedBloomBase { get; set; } = 0;
        public int? SimplifiedBloomStrength { get; set; } = 1;
        public bool SimplifiedDustSpriteEdge { get; set; } = true;
        public bool SimplifiedScreenWipe { get; set; } = true;
        public bool SimplifiedColorGrade { get; set; } = true;
        public bool SimplifiedBackgroundTiles { get; set; } = false;
        public bool SimplifiedBackdrop { get; set; } = true;
        public bool SimplifiedDecal { get; set; } = true;
        public bool SimplifiedParticle { get; set; } = true;
        public bool SimplifiedDistort { get; set; } = true;
        public bool SimplifiedMiniTextbox { get; set; } = true;
        public bool SimplifiedLightningStrike { get; set; } = true;
        public bool SimplifiedClutteredEntity { get; set; } = true;
        public bool SimplifiedHud { get; set; } = true;
        public bool SimplifiedWavedEdge { get; set; } = true;
        public bool SimplifiedSpikes { get; set; } = true;

        // Tile style: used for replacing all tiles to solid green in custom level 
        public SimplifiedGraphicsFeature.SolidTilesStyle simplifiedSolidTilesStyle;

        public void CreateSimplifiedSolidTilesStyleEntry(TextMenu menu, bool inGame) {
            menu.Add(new TextMenuExt.EnumerableSlider<SolidTilesStyle>("Solid Tiles Style", SolidTilesStyle.All,
                    RLModule.Settings.SimplifiedSolidTilesStyle).Change(value => {
                        RLModule.Settings.simplifiedSolidTilesStyle = value;
                        SimplifiedGraphicsFeature.ReplaceSolidTilesStyle();
                        }
                        ));
        }
        public SimplifiedGraphicsFeature.SolidTilesStyle SimplifiedSolidTilesStyle {
            get => simplifiedSolidTilesStyle;
            set {
                if (simplifiedSolidTilesStyle != value && SimplifiedGraphicsFeature.SolidTilesStyle.All.Any(style => style.Value == value.Value)) {
                    simplifiedSolidTilesStyle = value;
                    if (SimplifiedGraphics) {
                        SimplifiedGraphicsFeature.ReplaceSolidTilesStyle();
                    }
                }
            }
        }

        // Rate at which speed up when respawning, room transitions, etc
        [SettingRange(1, 20)] 
        public int RespawnRate { get; set; } = 20;        

    }


}
