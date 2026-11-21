using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.Customization.Paint;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace CommsTablet
{
 internal static class SkinManagerLink
    {
        private static readonly Type Provider = AccessTools.TypeByName("SkinManagerMod.SkinProvider");
        private static readonly Type Manager = AccessTools.TypeByName("SkinManagerMod.SkinManager");
        private static readonly Type Theme = AccessTools.TypeByName("SkinManagerMod.CustomPaintTheme");
        private static readonly Type Area = AccessTools.TypeByName("SkinManagerMod.PaintArea");
        private static readonly Type ModMain = AccessTools.TypeByName("SkinManagerMod.Main");
        internal static readonly Type Switcher = AccessTools.TypeByName("SkinManagerMod.CommsRadioSkinSwitcher");

        private static readonly MethodInfo SkinsForType = Method(Provider, "GetSkinsForType", typeof(TrainCarLivery), typeof(bool), typeof(bool));
        private static readonly MethodInfo ApplySkinMethod = Area == null ? null : Method(Manager, "ApplySkin", typeof(TrainCar), Theme, Area);
        private static readonly MethodInfo CurrentSkin = Method(Manager, "GetCurrentCarSkin", typeof(TrainCar), typeof(bool));
        private static readonly MethodInfo Supports = Method(Theme, "SupportsVehicle", typeof(TrainCarLivery));
        private static readonly MethodInfo ApplyToObject = Method(Theme, "Apply", typeof(GameObject), typeof(TrainCar));
        private static readonly MethodInfo ApplyPaxInterior = Method(Theme, "ApplyPaxInterior", typeof(GameObject), typeof(TrainCar));
        // CommsRadioSkinSwitcher.RadioSkinApplied, which Skin Manager's Custom Car Loader module
        // listens to, to paint the rest of a CCL set (TrainsetPropagator).
        private static readonly FieldInfo RadioSkinApplied = Switcher == null ? null : AccessTools.Field(Switcher, "RadioSkinApplied");
        private static readonly MethodInfo CclSubstitute = Method(Theme, "TryGetCCLSubstitution", typeof(Material), typeof(Material).MakeByRefType());
        private static readonly PropertyInfo Settings = ModMain == null ? null : AccessTools.Property(ModMain, "Settings");
        private static readonly FieldInfo AlwaysAllowRadioReskin = Settings == null ? null : AccessTools.Field(Settings.PropertyType, "alwaysAllowRadioReskin");
        private static Type alwaysAllowFallbackType;
        private static FieldInfo alwaysAllowFallbackField;

        // Skin Manager's PaintArea flags: Exterior 1, Interior 2, All 3.
        internal const int Exterior = 1, Interior = 2, All = 3;

        // SMShared.Constants.DE6_LIVERY_ID and PASSENGER_TYPE_ID.
        private const string De6 = "LocoDE6", Passenger = "Passenger";

        /// <summary>Whether Skin Manager is loaded with the members the Paint page uses.</summary>
        internal static bool Present => SkinsForType != null && ApplySkinMethod != null && Theme != null;

        private static MethodInfo Method(Type type, string name, params Type[] parameters) =>
            type == null || parameters.Any(p => p == null) ? null : AccessTools.Method(type, name, parameters);

        /// <summary>Skin Manager's own setting to offer reskinning outside the radio's cheat rule (on by default).</summary>
        internal static bool AlwaysAllowed
        {
            get
            {
                try
                {
                    var settings = Settings?.GetValue(null);
                    if (settings == null)
                        return false;
                    var always = AlwaysAllowRadioReskin;
                    if (always == null)
                    {
                        var type = settings.GetType();
                        if (type != alwaysAllowFallbackType)
                        {
                            alwaysAllowFallbackField = AccessTools.Field(type, "alwaysAllowRadioReskin");
                            alwaysAllowFallbackType = type;
                        }
                        always = alwaysAllowFallbackField;
                    }
                    return always != null && always.GetValue(settings) is bool on && on;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The skin switcher Skin Manager put in a radio's mode list in Paint's place, if any.</summary>
        internal static ICommsRadioMode SwitcherOn(CommsRadioController radio) =>
            Switcher != null && radio != null ? radio.GetComponent(Switcher) as ICommsRadioMode : null;

        internal static bool IsSkin(PaintTheme theme) => theme != null && Theme != null && Theme.IsInstanceOfType(theme);

        /// <summary>The skins for a car's livery, built-in themes included, sorted as Skin Manager shows them.</summary>
        internal static List<PaintTheme> SkinsFor(TrainCarLivery livery)
        {
            if (!Present || livery == null)
                return new List<PaintTheme>();
            return ((IEnumerable)SkinsForType.Invoke(null, new object[] { livery, true, true })).OfType<PaintTheme>().ToList();
        }

        /// <summary>The names of the skins a car wears, outside and in.</summary>
        internal static (string exterior, string interior) Wearing(TrainCar car)
        {
            if (CurrentSkin == null || car == null)
                return (null, null);
            var worn = CurrentSkin.Invoke(null, new object[] { car, false });
            return worn is ValueTuple<string, string> pair ? pair : (null, null);
        }

        /// <summary>Paints a car, and the tender or slug behind it as Skin Manager's switcher does.</summary>
        internal static void Apply(TrainCar car, PaintTheme skin, int area)
        {
            var flags = Enum.ToObject(Area, area);
            ApplySkinMethod.Invoke(null, new object[] { car, skin, flags });
            var rear = car.rearCoupler;
            var attached = rear != null && rear.IsCoupled() ? rear.coupledTo?.train : null;
            if (attached != null && SupportsVehicle(skin, attached.carLivery)
                && ((CarTypes.IsMUSteamLocomotive(car.carType) && CarTypes.IsTender(attached.carLivery))
                    || (car.carLivery.id == De6 && CarTypes.IsSlug(attached.carLivery))))
                ApplySkinMethod.Invoke(null, new object[] { attached, skin, flags });
            // As the switcher announces its paints, so the rest of a Custom Car Loader set follows.
            try
            {
                (RadioSkinApplied?.GetValue(null) as Delegate)?.DynamicInvoke(car, skin, flags);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("A listener to Skin Manager's RadioSkinApplied failed on a tablet paint", e.GetBaseException());
            }
        }

        private static bool SupportsVehicle(PaintTheme skin, TrainCarLivery livery) =>
            Supports != null && livery != null && Supports.Invoke(skin, new object[] { livery }) is bool yes && yes;

        /// <summary>
        /// Paints a script-free copy of <paramref name="car"/>'s model for a side
        /// view: Custom Car Loader substitutions, then the skin's textures, as
        /// Skin Manager's <c>ReapplyThemes</c> does (a passenger car's body goes
        /// through <c>ApplyPaxInterior</c>). The textures go onto material
        /// instances, returned for the caller to destroy with the copy. False
        /// if it could not paint (the car is gone).
        /// </summary>
        internal static bool PaintCopy(GameObject copy, PaintTheme skin, TrainCar car, ICollection<Material> made)
        {
            if (ApplyToObject == null || car == null)
                return false;
            foreach (var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || CclSubstitute == null)
                        continue;
                    var args = new object[] { materials[i], null };
                    if (CclSubstitute.Invoke(skin, args) is bool found && found && args[1] is Material substitute && substitute != null)
                    {
                        materials[i] = substitute;
                        changed = true;
                    }
                }
                if (changed)
                    renderer.sharedMaterials = materials;
            }
            var before = new HashSet<Material>(copy.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null));
            try
            {
                ApplyToObject.Invoke(skin, new object[] { copy, car });
                if (ApplyPaxInterior != null && car.carLivery.parentType != null && car.carLivery.parentType.id == Passenger)
                    ApplyPaxInterior.Invoke(skin, new object[] { copy, car });
            }
            finally
            {
                foreach (var material in copy.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials))
                    if (material != null && !before.Contains(material))
                        made.Add(material);
            }
            return true;
        }
    }
}
