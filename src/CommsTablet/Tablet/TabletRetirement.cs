using System;
using System.Collections.Generic;
using System.Linq;
using DV.Items;
using HarmonyLib;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// A merged guest's own tablet in a save (decision 18): its saved entry is
    /// renamed to the Comms Tablet as the save's items are read, before the
    /// starting kit is checked against them and before any is made. The
    /// entry keeps its place, state, and mounting, so a guest tablet saved on
    /// a gadget mount comes back on it as the Comms Tablet. The item load
    /// wrapper (<see cref="TabletLoading"/>) answers the guest's name too, for
    /// every other path (bought items queued, Multiplayer's saves).
    /// </summary>
    [HarmonyPatch(typeof(StartingItemsController), "CombineAndResolveIllegalDuplicates")]
    internal static class TabletRetirement
    {
        private static void Postfix(List<StorageItemData> __result)
        {
            try
            {
                if (__result == null || !Guests.GuestRegistry.AnyMerged || TabletItem.Template == null)
                    return;
                var renamed = new List<string>();
                foreach (var data in __result)
                    if (data != null && Guests.GuestRegistry.Retires(data.itemPrefabName))
                    {
                        renamed.Add(data.itemPrefabName);
                        data.itemPrefabName = TabletItem.PrefabName;
                    }
                if (renamed.Count > 0)
                    Main.Mod?.Logger.Log($"Saved guest tablets load as the Comms Tablet: {string.Join(", ", renamed)}");
            }
            catch (Exception e)
            {
                // A failure must not interrupt the stock saved-item loader; the load wrapper still answers the names.
                Main.Mod?.Logger.LogException("Could not rename saved guest tablets", e);
            }
        }
    }

 [HarmonyPatch(typeof(StartingItemsController), "GetStartingItemsPrefabs")]
    internal static class TabletStartingItems
    {
        internal const string GivenKey = "CommsTablet_TabletGiven";

        private static void Postfix(SaveGameData data, ref List<StartingItems.StartingItem> __result)
        {
            try
            {
                __result = WithTabletOnce(data, __result);
            }
            catch (Exception e)
            {
                // A tablet failure must not interrupt the stock saved-item loader.
                Main.Mod?.Logger.LogException("Could not add the comms tablet to the starting equipment", e);
            }
        }

        /// <summary>
        /// Whether a save's starting kit holds the tablet: any mode but Career
        /// (Free Roam, the sandbox); a career with the Expanded or Engineer
        /// starting equipment (the save's <c>Starting_items</c> 1 or 2; Basic
        /// is 0, and a career's Auto is stored as Basic). Merged guests do not
        /// change it.
        /// </summary>
        internal static bool Included(string gameMode, int? startingItems) =>
            gameMode != "Career" || (startingItems ?? 0) >= (int)GameParams.StartingItemsType.Expanded;

        internal static List<StartingItems.StartingItem> WithTabletOnce(SaveGameData data, List<StartingItems.StartingItem> items)
        {
            if (data == null || items == null || data.GetBool(GivenKey) == true
                || !Included(data.GetString("Game_mode"), data.GetInt("Starting_items")))
                return items;
            if (items.Any(i => i.item?.ItemPrefabName == TabletItem.PrefabName))
            {
                data.SetBool(GivenKey, true);
                return items;
            }
            var spec = TabletItem.Template != null ? TabletItem.Template.GetComponent<InventoryItemSpec>() : null;
            if (spec?.ItemPrefabName != TabletItem.PrefabName)
                return items;
            data.SetBool(GivenKey, true);
            // GetStartingItems can return the asset's own list: keep it intact.
            return new List<StartingItems.StartingItem>(items)
            {
                new StartingItems.StartingItem { item = spec, preferredRelativeSlot = 0, backpackPriority = true, allowDuplicates = true },
            };
        }
    }
}
