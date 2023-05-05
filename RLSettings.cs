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

namespace Celeste.Mod.RL
{

    // If no SettingName is applied, it defaults to
    // modoptions_[typename without settings]_title
    // The value is then used to look up the UI text in the dialog files.
    // If no dialog text can be found, Everest shows a prettified mod name instead.
    [SettingName("modoptions_examplemodule_title")]
    public class RLSettings : EverestModuleSettings {

        // SettingName also works on props, defaulting to
        // modoptions_[typename without settings]_[propname]

        // Example ON / OFF property with a default value.
        public bool FrameStep { get; set; } = false;


        [SettingRange(1, 20)] // Allow choosing a value from 0 (inclusive) to 10 (inclusive).
        public int RespawnRate { get; set; } = 20;

        [SettingRange(1, 70, true)] // Allow choosing a value from 0 (inclusive) to 10 (inclusive).
        public int RewardRate{ get; set; } = 70;

        

    }


}
