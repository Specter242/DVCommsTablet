using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.UserManagement;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace CommsTablet
{
    /// <summary>What the session allows: the rules the radio offers its modes by.</summary>
    internal static class Session
    {
        /// <summary>"Career", "FreeRoam", ..., or null outside a game.</summary>
        internal static string GameMode
        {
            get
            {
                var users = SingletonBehaviour<UserManager>.Instance;
                return users != null ? users.CurrentUser?.CurrentSession?.GameMode : null;
            }
        }

        internal static bool Career => GameMode == "Career";

        /// <summary>The radio's own rule for Spawn, Cargo, Paint (<c>CommsRadioController.UpdateModesAvailability</c>).</summary>
        internal static bool CheatMode
        {
            get
            {
                var parameters = Globals.G?.GameParams;
                return parameters != null && ((parameters.CommsRadioSandboxCheatMode && GameMode == "FreeRoam") || parameters.CommsRadioCheatMode);
            }
        }

        private static readonly Type Lifecycle = AccessTools.TypeByName("Multiplayer.Components.Networking.NetworkLifecycle");
        private static readonly PropertyInfo ClientRunning = Lifecycle == null ? null : AccessTools.Property(Lifecycle, "IsClientRunning");
        private static readonly PropertyInfo ServerRunning = Lifecycle == null ? null : AccessTools.Property(Lifecycle, "IsServerRunning");
        private static UnityEngine.Object lifecycle;

        /// <summary>
        /// Whether this game is a Multiplayer client: its client runs without
        /// the host's server. False without the Multiplayer mod.
        /// </summary>
        internal static bool IsMultiplayerClient
        {
            get
            {
                if (ClientRunning == null || ServerRunning == null)
                    return false;
                if (lifecycle == null)
                    lifecycle = UnityEngine.Object.FindObjectOfType(Lifecycle);
                if (lifecycle == null)
                    return false;
                try
                {
                    return (bool)ClientRunning.GetValue(lifecycle) && !(bool)ServerRunning.GetValue(lifecycle);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// The live comms radios, so a page stays hidden where the player's radio
    /// has that mode off (DisableRadioFunctions, for one): the tablet is no
    /// way around another mod's rules.
    /// </summary>
    [HarmonyPatch(typeof(CommsRadioController))]
    internal static class Radios
    {
        private static readonly HashSet<CommsRadioController> live = new HashSet<CommsRadioController>();

        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        private static void Awake(CommsRadioController __instance) => live.Add(__instance);

        [HarmonyPrefix]
        [HarmonyPatch("OnDestroy")]
        private static void OnDestroy(CommsRadioController __instance) => live.Remove(__instance);

        /// <summary>
        /// Whether a radio would offer <paramref name="mode"/>: true with no
        /// radio about, or when any live radio has it on.
        /// </summary>
        internal static bool Allow(Func<CommsRadioController, ICommsRadioMode> mode)
        {
            live.RemoveWhere(r => r == null);
            if (live.Count == 0 || GameMembers.RadioModes == null || GameMembers.RadioDisabled == null)
                return true;
            return live.Any(radio =>
            {
                var modes = GameMembers.RadioModes(radio);
                var index = modes?.IndexOf(mode(radio)) ?? -1;
                return index >= 0 && !(GameMembers.RadioDisabled(radio)?.Contains(index) ?? false);
            });
        }
    }

    /// <summary>Silences the radio's sounds while the tablet steps a mode through several states at once.</summary>
    [HarmonyPatch(typeof(CommsRadioController), nameof(CommsRadioController.PlayAudioFromRadio))]
    internal static class QuietSteps
    {
        internal static bool Active;

        private static bool Prefix() => !Active;
    }
}
