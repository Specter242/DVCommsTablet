using System;
using System.Collections.Generic;
using System.Linq;
using DV.CashRegister;
using DV.Shops;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// Sells the tablet beside the comms radio, at its price and stock, or
    /// beside the clinometer where no shop stocks the radio. Realistic
    /// Firing's <c>FiringTabletShop</c> (d0f162e9).
    /// </summary>
    [HarmonyPatch(typeof(GlobalShopController), "Awake")]
    internal static class TabletShop
    {
        private static readonly string[] Neighbors = { TabletItem.RadioPrefab, "Clinometer" };

        private static InventoryItemSpec Spec => TabletItem.Template != null ? TabletItem.Template.GetComponent<InventoryItemSpec>() : null;

        private static void Prefix(GlobalShopController __instance)
        {
            try
            {
                var spec = Spec;
                if (spec == null || __instance.shopItemsData == null || __instance.shopItemsData.Any(d => d?.item == spec))
                    return;
                var source = Neighbors.Select(n => __instance.shopItemsData.FirstOrDefault(d => d?.item?.ItemPrefabName == n))
                    .FirstOrDefault(d => d != null);
                if (source == null)
                {
                    Main.Mod?.Logger.Warning("No comms radio or clinometer in the shops' items; the comms tablet is not sold");
                    return;
                }
                var data = new ShopItemData
                {
                    item = spec,
                    basePrice = source.basePrice,
                    isGlobal = source.isGlobal,
                    careerOnly = source.careerOnly,
                    soldOnlyAt = new List<Shop>(source.soldOnlyAt ?? new List<Shop>()),
                };
                var amount = AccessTools.Field(typeof(ShopItemData), "amount");
                amount.SetValue(data, amount.GetValue(source));
                __instance.shopItemsData.Add(data);
                Main.Mod?.Logger.Log($"Comms tablet in the shops' items beside the {source.item.ItemPrefabName}, at {source.basePrice:F0}");
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not add the comms tablet to the shops' items", e);
            }
        }

        private static void Postfix(GlobalShopController __instance)
        {
            try
            {
                var spec = Spec;
                if (spec == null || __instance.globalShopList == null)
                    return;
                var count = 0;
                foreach (var shop in __instance.globalShopList)
                {
                    if (shop == null || shop.scanItemResourceModules == null || shop.scanItemResourceModules.Any(m => m?.sellingItemSpec == spec))
                        continue;
                    var source = Neighbors.Select(n => shop.scanItemResourceModules.FirstOrDefault(m => m?.sellingItemSpec?.ItemPrefabName == n))
                        .FirstOrDefault(m => m != null);
                    if (source == null)
                        continue;
                    var holder = new GameObject("[CommsTablet shelf template]");
                    holder.SetActive(false);
                    var copy = Object.Instantiate(source.gameObject, holder.transform, false);
                    copy.name = "CommsTablet_ShelfItem";
                    var tag = copy.GetComponent<ScanItemCashRegisterModule>();
                    tag.sellingItemSpec = spec;
                    ShowTablet(copy);
                    copy.transform.SetParent(source.transform.parent, false);
                    // Where the neighbor's tag is, as Realistic Firing places its tablet.
                    copy.transform.localPosition = source.transform.localPosition;
                    copy.transform.localRotation = source.transform.localRotation;
                    Object.Destroy(holder);
                    shop.scanItemResourceModules = shop.scanItemResourceModules.Append(tag).ToArray();
                    if (shop.cashRegister != null && shop.cashRegister.registerModules != null)
                    {
                        shop.cashRegister.registerModules = shop.cashRegister.registerModules.Append(tag).ToArray();
                        HearTag(shop.cashRegister, tag);
                    }
                    tag.ItemPurchased += __instance.AddItemToInstantiationQueue;
                    count++;
                }
                Main.Mod?.Logger.Log($"Comms tablet on the shelves of {count} shops");
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not put the comms tablet on the shops' shelves", e);
            }
        }

        // The neighbor's display model gives way to a small tablet shell. The
        // tag's name and price are TextMeshPro texts on the same tag
        // (ScanItemCashRegisterModule.itemNameText, itemPriceText): they stay.
        private static void ShowTablet(GameObject shelfItem)
        {
            var renderers = shelfItem.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.GetComponent<TMPro.TMP_Text>() == null).ToArray();
            var anchor = renderers.FirstOrDefault()?.transform;
            foreach (var renderer in renderers)
                renderer.enabled = false;
            if (anchor == null)
                return;
            var shell = TabletBody.Build(anchor);
            var scale = Mathf.Max(1e-4f, anchor.lossyScale.x);
            shell.localScale = Vector3.one * (0.42f / scale);
        }

        // A register hooks up its shelf tags once, when it comes on; one
        // already on would miss the tablet's. Realistic Firing's
        // FireIronShop.HearTag (a player's report, 2026-10-12).
        private static readonly AccessTools.FieldRef<CashRegisterWithModules, DV.CabControls.ButtonBase> BuyButton =
            AccessTools.FieldRefAccess<CashRegisterWithModules, DV.CabControls.ButtonBase>("buyButton");

        private static void HearTag(CashRegisterWithModules register, CashRegisterModule tag)
        {
            if (!register.isActiveAndEnabled || BuyButton(register) == null)
                return;
            var heard = AccessTools.MethodDelegate<Action>(AccessTools.Method(typeof(CashRegisterWithModules), "OnUnitsToBuyChanged"), register);
            tag.OnUnitsToBuyChanged -= heard;
            tag.OnUnitsToBuyChanged += heard;
        }
    }

    /// <summary>
    /// When the game exits, the tablet's template can be destroyed before
    /// the shops, whose teardown reads each entry's item and would stop at
    /// the gone one (a <c>NullReferenceException</c> in
    /// <c>GlobalShopController.OnDestroy</c>). Entries whose item is gone are
    /// dropped first.
    /// </summary>
    [HarmonyPatch(typeof(GlobalShopController), "OnDestroy")]
    internal static class TabletShopTeardown
    {
        private static void Prefix(GlobalShopController __instance) =>
            __instance.shopItemsData?.RemoveAll(d => d == null || d.item == null);
    }
}
