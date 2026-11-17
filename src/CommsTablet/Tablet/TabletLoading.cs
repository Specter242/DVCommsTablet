using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DV.Items;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// Saved and bought items are made from their prefab name with
    /// <c>Resources.Load</c>, which cannot find a mod's item. As Realistic
    /// Firing does for its tablet (<c>FireIronLoading</c>, d0f162e9), only the
    /// game's saved-item and purchased-item load sites are redirected, never
    /// <c>Resources.Load</c> itself.
    /// <para>
    /// Realistic Firing rewrites the same two sites, replacing each load
    /// with its own <c>LoadPrefab</c>, which falls back to
    /// <c>Resources.Load</c>. Whichever mod rewrote first would leave the
    /// other nothing to find, so this one runs last and wraps whatever loader
    /// it finds there: <c>Resources.Load(string)</c>, or another mod's
    /// <c>static Object …Load…(string)</c>. Its tablet is answered here; any
    /// other name goes on to that loader.
    /// </para>
    /// </summary>
    [HarmonyPatch]
    internal static class TabletLoading
    {
        private static readonly List<Func<string, Object>> loaders = new List<Func<string, Object>>();

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(StartingItemsController), "InstantiateItem");
            yield return AccessTools.EnumeratorMoveNext(
                AccessTools.DeclaredMethod(typeof(DV.Shops.GlobalShopController), "InstantiatePurchasedItems"));
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RealisticFiring")]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            Redirect(instructions, original);

        /// <summary>
        /// The tablet for its own name, and for a merged guest's own tablet
        /// (decision 18: a saved one becomes the Comms Tablet); any other name,
        /// or a guest's tablet when this one cannot be built, goes on to the
        /// loader that was there.
        /// </summary>
        internal static Object Load(string path, int loader)
        {
            if (path == TabletItem.PrefabName)
                return TabletItem.Template;
            if (Guests.GuestRegistry.Retires(path) && TabletItem.Template != null)
                return TabletItem.Template;
            return loaders[loader](path);
        }

        /// <summary>Whether <paramref name="method"/> loads a prefab by name: the stock load, or another mod's redirect of it.</summary>
        internal static bool IsLoader(MethodInfo method)
        {
            if (method == null || !method.IsStatic || method.IsGenericMethodDefinition || method.ReturnType != typeof(Object))
                return false;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                return false;
            if (method.DeclaringType == typeof(Resources))
                return method.Name == nameof(Resources.Load);
            return method.Name.Contains("Load") && method.DeclaringType?.Assembly != typeof(TabletLoading).Assembly
                && method.DeclaringType?.Assembly != typeof(Object).Assembly;
        }

        internal static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var load = AccessTools.DeclaredMethod(typeof(TabletLoading), nameof(Load));
            var redirected = 0;
            foreach (var instruction in instructions)
            {
                if (!(instruction.operand is MethodInfo called) || (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                    || !IsLoader(called))
                {
                    yield return instruction;
                    continue;
                }
                loaders.Add(AccessTools.MethodDelegate<Func<string, Object>>(called));
                redirected++;
                // The name is on the stack; add which loader to fall back to.
                yield return new CodeInstruction(OpCodes.Ldc_I4, loaders.Count - 1).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, load);
            }
            var where = $"{original?.DeclaringType?.Name}.{original?.Name}";
            if (redirected > 0)
                yield break;
            // Skin Manager replaces both game sites' loads with its own helpers, which are wrapped instead.
            if (SkinManagerTabletLoading.Prepare() && original?.DeclaringType?.Assembly != SkinManagerTabletLoading.Assembly)
                Main.Mod?.Logger.Log($"No prefab load left in {where}: Skin Manager makes these items, through its loaders, wrapped instead");
            else
                Main.Mod?.Logger.Warning($"The comms tablet's lookup found no prefab load in {where}: saved or bought tablets will not appear there");
        }
    }

    /// <summary>
    /// Skin Manager (4.4.1) makes saved and bought items through its own
    /// helpers instead of the game's; their loads are wrapped the same way.
    /// Realistic Firing's <c>SkinManagerFireIronLoading</c> (d0f162e9).
    /// </summary>
    [HarmonyPatch]
    internal static class SkinManagerTabletLoading
    {
        private static readonly (string Type, string Method)[] Loaders =
        {
            ("SkinManagerMod.Patches.ShopPatches", "InstantiatePaintCan"),
            ("SkinManagerMod.Patches.InventoryPatches", "CustomInstantiateItem"),
        };

        internal static System.Reflection.Assembly Assembly =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SkinManagerMod");

        private static List<MethodBase> Targets()
        {
            var assembly = Assembly;
            if (assembly == null)
                return new List<MethodBase>();
            return Loaders.Select(l => assembly.GetType(l.Type, false))
                .Select((type, i) => type == null ? null : (MethodBase)AccessTools.DeclaredMethod(type, Loaders[i].Method))
                .Where(m => m != null).ToList();
        }

        internal static bool Prepare() => Targets().Count > 0;

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = Targets();
            if (targets.Count < Loaders.Length)
                Main.Mod?.Logger.Warning("Skin Manager's item loaders have changed: the comms tablet may not be found in its shops or saves");
            return targets;
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RealisticFiring")]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            TabletLoading.Redirect(instructions, original);
    }

    /// <summary>
    /// Multiplayer makes items on clients from the game's item catalog
    /// (<c>NetworkedItemManager.BuildPrefabLookup</c> reads
    /// <c>Globals.G.Items.items</c>), so the tablet joins the catalog just
    /// before that lookup is built, on the host and on clients.
    /// </summary>
    [HarmonyPatch]
    internal static class MultiplayerItems
    {
        private static MethodBase Target() =>
            AccessTools.TypeByName("Multiplayer.Components.Networking.World.NetworkedItemManager") is Type manager
                ? AccessTools.DeclaredMethod(manager, "BuildPrefabLookup") : null;

        internal static bool Prepare() => Target() != null;

        internal static MethodBase TargetMethod() => Target();

        private static void Prefix()
        {
            if (!TabletItem.RegisterAsGameItem())
                Main.Mod?.Logger.Warning("The comms tablet could not join the item catalog; Multiplayer cannot make it on clients");
        }
    }
}
