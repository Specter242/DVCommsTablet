using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace CommsTablet
{
    public static class Main
    {
        public static UnityModManager.ModEntry Mod { get; private set; }

        /// <summary>The mod's patch classes that failed to apply this session, by name.</summary>
        internal static IReadOnlyList<string> FailedPatches { get; private set; } = new string[0];

        /// <summary>
        /// Applies each patch class on its own, as Harmony's PatchAll does, but
        /// goes on past one that fails (a game update, or another mod in the
        /// way), naming it to <paramref name="error"/>. PatchAll stops at the
        /// first class that throws and leaves every class after it unpatched.
        /// From Realistic Firing's <c>Main.PatchEachClass</c> (d0f162e9).
        /// </summary>
        internal static List<string> PatchEachClass(Harmony harmony, IEnumerable<Type> types, Action<string> error)
        {
            var failed = new List<string>();
            foreach (var type in types)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    failed.Add(type.FullName);
                    error?.Invoke($"Patch {type.FullName} failed; the rest still load: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
                }
            }
            return failed;
        }

        private static string giveResult;

        private static void DrawSettings(UnityModManager.ModEntry modEntry)
        {
            if (GUILayout.Button("Give me a comms tablet", GUILayout.Width(240f)))
                giveResult = Tablet.TabletItem.GiveToPlayer();
            if (giveResult != null)
                GUILayout.Label(giveResult);
            GUILayout.Label("The tablet is also sold beside the comms radio in the shops.");
        }

        // Entry method named in Info.json. Patches stay applied until the game
        // restarts, so the mod has no OnToggle handler.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            foreach (var missing in GameMembers.Missing)
                modEntry.Logger.Error($"The game has changed under the tablet ({missing}); the pages that need it stay hidden");
            modEntry.OnGUI = DrawSettings;
            Map.MapWorld.Hook();
            var harmony = new Harmony(modEntry.Info.Id);
            FailedPatches = PatchEachClass(harmony, AccessTools.GetTypesFromAssembly(typeof(Main).Assembly),
                error => modEntry.Logger.Error(error));
            if (FailedPatches.Count > 0)
                modEntry.Logger.Error($"{FailedPatches.Count} of Comms Tablet's patches failed and the rest loaded: {string.Join(", ", FailedPatches)}. " +
                                      "Another mod, or a game update, may be in the way; please send this log with a bug report.");
            modEntry.Logger.Log($"Comms Tablet {modEntry.Info.Version} loaded");
            return true;
        }
    }
}
