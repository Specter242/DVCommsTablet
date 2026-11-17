using System;
using System.Collections.Generic;
using System.Reflection;
using DV.ThingTypes;
using HarmonyLib;

namespace CommsTablet
{
    /// <summary>
    /// Custom Car Loader's work trains, where the radio's Summon mode and
    /// CCL talk only to each other (CCL 3.1.11). The tablet's summon mode is
    /// a copy, so it joins both conversations itself, through CCL's public
    /// <c>WorkTrainPurchaseHandler</c>. Without CCL this does nothing.
    /// <list type="bullet">
    /// <item>A summon: CCL makes a work train's first summon free and learns
    /// of one only from the radio's <c>crewVehicleControl.CarSummoned</c>
    /// (<c>CommsRadioControllerPatches.OnCarSummoned</c>); the tablet calls
    /// <c>SetAsSummoned(CCL_CarVariant)</c>.</item>
    /// <item>A purchase: CCL refreshes only the radio's summon list
    /// (<c>CommsRadioCCLWorkTrain.LiveryUnlocked</c>); the tablet listens to
    /// <c>LiveryUnlocked</c> and refreshes its own.</item>
    /// </list>
    /// </summary>
    internal static class WorkTrains
    {
        private static readonly Type Handler = AccessTools.TypeByName("CCL.Importer.WorkTrains.WorkTrainPurchaseHandler");
        private static readonly Type Variant = AccessTools.TypeByName("CCL.Importer.Types.CCL_CarVariant");
        private static readonly MethodInfo SetAsSummoned = Handler == null || Variant == null ? null
            : AccessTools.Method(Handler, "SetAsSummoned", new[] { Variant });
        private static readonly FieldInfo Unlocked = Handler == null ? null : AccessTools.Field(Handler, "LiveryUnlocked");

        internal static void Summoned(TrainCar car)
        {
            var livery = car != null ? car.carLivery : null;
            if (SetAsSummoned == null || livery == null || !Variant.IsInstanceOfType(livery))
                return;
            try
            {
                SetAsSummoned.Invoke(null, new object[] { livery });
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not tell Custom Car Loader of a work train summoned from the tablet", e);
            }
        }

        /// <summary>
        /// Calls <paramref name="handler"/> whenever CCL unlocks a work train.
        /// Returns the subscription for <see cref="Unfollow"/>; null without CCL.
        /// </summary>
        internal static Delegate Follow(Action<TrainCarLivery> handler)
        {
            if (Unlocked == null || !Unlocked.IsStatic || !typeof(Delegate).IsAssignableFrom(Unlocked.FieldType))
                return null;
            try
            {
                // Action<CCL_CarVariant>, bound to a handler of the base livery type.
                var typed = Delegate.CreateDelegate(Unlocked.FieldType, handler.Target, handler.Method);
                Unlocked.SetValue(null, Delegate.Combine((Delegate)Unlocked.GetValue(null), typed));
                return typed;
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not follow Custom Car Loader's work-train purchases", e);
                return null;
            }
        }

        // ---- Buying (CCL's CommsRadioCCLWorkTrain, done by the tablet's Buy page) ----

        private static readonly PropertyInfo Locked = Handler == null ? null : AccessTools.Property(Handler, "LockedLiveries");
        private static readonly MethodInfo Unlock = Handler == null || Variant == null ? null : AccessTools.Method(Handler, "Unlock", new[] { Variant });
        private static readonly MethodInfo Popup = Handler == null ? null : AccessTools.Method(Handler, "ShowPopupIfNeeded");
        private static readonly FieldInfo Price = Variant == null ? null : AccessTools.Field(Variant, "UnlockPrice");

        /// <summary>Whether Custom Car Loader is there to sell work trains, and every member the Buy page calls resolved.</summary>
        internal static bool Selling => Locked != null && Unlock != null && Popup != null && Price != null;

        /// <summary>The work trains still for sale, as CCL's radio mode lists them.</summary>
        internal static List<TrainCarLivery> ForSale()
        {
            var list = new List<TrainCarLivery>();
            if (!Selling)
                return list;
            try
            {
                if (Locked.GetValue(null) is System.Collections.IEnumerable liveries)
                    foreach (var livery in liveries)
                        if (livery is TrainCarLivery train)
                            list.Add(train);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not list Custom Car Loader's work trains for sale", e);
            }
            return list;
        }

        internal static float PriceOf(TrainCarLivery livery) =>
            Price != null && livery != null && Variant.IsInstanceOfType(livery) ? Convert.ToSingle(Price.GetValue(livery)) : 0f;

        /// <summary>
        /// Buys a work train as CCL's radio mode does: unlocked (so it joins the
        /// summon lists), the money taken, the popup shown. False if CCL refused.
        /// </summary>
        internal static bool Buy(TrainCarLivery livery)
        {
            if (!Selling || livery == null || !Variant.IsInstanceOfType(livery))
                return false;
            try
            {
                if (!(Unlock.Invoke(null, new object[] { livery }) is bool unlocked) || !unlocked)
                    return false;
                Popup.Invoke(null, null);
                return true;
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Custom Car Loader could not sell the work train", e);
                return false;
            }
        }

        internal static void Unfollow(Delegate subscription)
        {
            if (subscription == null || Unlocked == null)
                return;
            try
            {
                Unlocked.SetValue(null, Delegate.Remove((Delegate)Unlocked.GetValue(null), subscription));
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not stop following Custom Car Loader's work-train purchases", e);
            }
        }
    }
}
