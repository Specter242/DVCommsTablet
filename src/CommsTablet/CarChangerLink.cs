using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Localization;
using DV.ThingTypes;
using HarmonyLib;
using UnityModManagerNet;
using Object = UnityEngine.Object;

namespace CommsTablet
{
 internal static class CarChangerLink
    {
        internal const string Id = "DVCarChanger";

        /// <summary>The one version of Car Changer the Change page is written and checked against.</summary>
        internal const string Pinned = "1.5.4";

        /// <summary>One modification a car can take: Car Changer's config, its id, and its name in the game's language.</summary>
        internal sealed class Modification
        {
            /// <summary>Car Changer's <c>ModelConfig</c>, the object its rules take.</summary>
            internal readonly object Config;
            internal readonly string ModId;
            internal readonly string Name;

            internal Modification(object config, string id, string name)
            {
                Config = config;
                ModId = id;
                Name = name;
            }
        }

        private static readonly Type Manager = AccessTools.TypeByName("CarChanger.Game.ChangeManager");
        private static readonly Type Applied = AccessTools.TypeByName("CarChanger.Game.Components.AppliedChange");
        private static readonly Type Config = AccessTools.TypeByName("CarChanger.Common.ModelConfig");
        private static readonly Type Extensions = AccessTools.TypeByName("CarChanger.Game.Extensions");

        /// <summary>Car Changer's radio mode, which it adds to every comms radio: the page is shown where the player's radio has it.</summary>
        internal static readonly Type RadioMode = AccessTools.TypeByName("CarChanger.Game.CommsRadioCarChanger");

        private static readonly FieldInfo LoadedConfigs = Manager == null ? null : AccessTools.Field(Manager, "LoadedConfigs");
        private static readonly MethodInfo AppliedChanges = Extensions == null ? null : AccessTools.Method(Extensions, "GetAppliedChanges", new[] { typeof(TrainCar) });
        private static readonly MethodInfo AddChange = Applied == null || Config == null ? null : AccessTools.Method(Applied, "AddChange", new[] { typeof(TrainCar), Config });
        private static readonly FieldInfo AppliedConfig = Applied == null ? null : AccessTools.Field(Applied, "Config");
        private static readonly MethodInfo CanCombine = Config == null ? null : AccessTools.Method(Config, "CanCombine", new[] { Config, Config });
        private static readonly FieldInfo ModificationId = Config == null ? null : AccessTools.Field(Config, "ModificationId");
        private static readonly PropertyInfo LocalizationKey = Config == null ? null : AccessTools.Property(Config, "LocalizationKey");

        /// <summary>Whether every member the page calls resolved.</summary>
        internal static bool Resolves => RadioMode != null && LoadedConfigs != null && AppliedChanges != null && AddChange != null
            && AppliedConfig != null && CanCombine != null && ModificationId != null && LocalizationKey != null;

        /// <summary>The members that did not resolve, for the contract check and the log.</summary>
        internal static IEnumerable<string> Missing()
        {
            var members = new (string name, object member)[]
            {
                ("CommsRadioCarChanger", RadioMode), ("ChangeManager.LoadedConfigs", LoadedConfigs),
                ("Extensions.GetAppliedChanges", AppliedChanges), ("AppliedChange.AddChange", AddChange),
                ("AppliedChange.Config", AppliedConfig), ("ModelConfig.CanCombine", CanCombine),
                ("ModelConfig.ModificationId", ModificationId), ("ModelConfig.LocalizationKey", LocalizationKey),
            };
            return members.Where(m => m.member == null).Select(m => m.name);
        }

        /// <summary>Whether the page is written for this version of Car Changer.</summary>
        internal static bool Accepts(string version) => string.Equals(version?.Trim(), Pinned, StringComparison.Ordinal);

        /// <summary>The installed Car Changer's version if it is on, else null.</summary>
        internal static string Installed
        {
            get
            {
                var mod = UnityModManager.FindMod(Id);
                return mod != null && mod.Active ? mod.Info?.Version : null;
            }
        }

        private static string refused;

        /// <summary>Whether Car Changer 1.5.4 is on and every member resolved.</summary>
        internal static bool Present
        {
            get
            {
                var version = Installed;
                if (version == null)
                    return false;
                var ok = Accepts(version) && Resolves;
                if (!ok && refused != version)
                {
                    // Once per version: the page is hidden, and why.
                    refused = version;
                    Main.Mod?.Logger.Log(Accepts(version)
                        ? $"Car Changer {version}: the Change page is hidden, members missing: {string.Join(", ", Missing())}"
                        : $"Car Changer {version}: the Change page is written for {Pinned} and stays hidden with this version");
                }
                return ok;
            }
        }

        private static IList ConfigsOf(TrainCarLivery livery)
        {
            if (livery == null || !(LoadedConfigs?.GetValue(null) is IDictionary loaded))
                return null;
            return loaded.Contains(livery) ? loaded[livery] as IList : null;
        }

        /// <summary>Whether Car Changer has modifications for <paramref name="car"/>'s livery.</summary>
        internal static bool Has(TrainCar car)
        {
            try
            {
                return Present && car != null && !car.isExploded && ConfigsOf(car.carLivery)?.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The modifications <paramref name="car"/> can take, by name.</summary>
        internal static List<Modification> For(TrainCar car) => For(car != null ? car.carLivery : null);

        /// <summary>The modifications a car of <paramref name="livery"/> can take, by name.</summary>
        internal static List<Modification> For(TrainCarLivery livery)
        {
            var list = new List<Modification>();
            try
            {
                var configs = ConfigsOf(livery);
                if (configs == null)
                    return list;
                foreach (var config in configs)
                    if (config != null)
                        list.Add(new Modification(config, (string)ModificationId.GetValue(config), NameOf(config)));
                // Car Changer lists them in the order its packs load; by name, a pack's kinds sit together.
                var order = StringComparer.Create(LocalizationAPI.CC, true);
                list.Sort((a, b) =>
                {
                    var byName = order.Compare(a.Name, b.Name);
                    return byName != 0 ? byName : string.CompareOrdinal(a.ModId, b.ModId);
                });
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not list Car Changer's modifications", e);
                list.Clear();
            }
            return list;
        }

        private static string NameOf(object config)
        {
            var id = (string)ModificationId.GetValue(config);
            var name = LocalizationAPI.L((string)LocalizationKey.GetValue(config));
            return string.IsNullOrWhiteSpace(name) || name.StartsWith("[ MISSING", StringComparison.Ordinal) ? id : name;
        }

        /// <summary>
        /// The configs of the changes on <paramref name="car"/>, as Car
        /// Changer reads them (and saves them), less those in
        /// <paramref name="leaving"/>: a destroyed component stays until the
        /// frame ends.
        /// </summary>
        internal static List<object> On(TrainCar car, ICollection<Object> leaving = null) =>
            AppliedOn(car).Where(c => leaving == null || !leaving.Contains(c)).Select(c => AppliedConfig.GetValue(c)).Where(c => c != null).ToList();

        private static List<Object> AppliedOn(TrainCar car)
        {
            var result = new List<Object>();
            try
            {
                if (car != null && AppliedChanges?.Invoke(null, new object[] { car }) is IList changes)
                    foreach (var change in changes)
                        if (change is Object component && component != null)
                            result.Add(component);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not read a car's changes from Car Changer", e);
            }
            return result;
        }

        /// <summary>
        /// Whether <paramref name="a"/> can go on with <paramref name="b"/>
        /// (<c>ModelConfig.CanCombine</c>; never with itself). The radio asks
        /// it with the new change first to switch one on, and with the one on
        /// first to take a clashing one off: so does the page.
        /// </summary>
        internal static bool Combine(object a, object b)
        {
            try
            {
                return (bool)CanCombine.Invoke(null, new[] { a, b });
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Car Changer could not compare two modifications", e);
                return false;
            }
        }

        /// <summary>The changes on the car that <paramref name="config"/> cannot go on with, as the radio finds them.</summary>
        internal static List<object> Clashes(object config, IEnumerable<object> on) => on.Where(c => !Combine(config, c)).ToList();

        /// <summary>Puts <paramref name="config"/> on <paramref name="car"/> as the radio does (<c>AppliedChange.AddChange</c>); Car Changer applies it the next frame.</summary>
        internal static bool Add(TrainCar car, object config)
        {
            try
            {
                return AddChange.Invoke(null, new[] { car, config }) != null;
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Car Changer could not add a modification", e);
                return false;
            }
        }

        /// <summary>Takes the changes <paramref name="which"/> picks off <paramref name="car"/>, as the radio does (each change's component destroyed); the returned components are going.</summary>
        internal static List<Object> Remove(TrainCar car, Func<object, bool> which)
        {
            var gone = new List<Object>();
            foreach (var change in AppliedOn(car))
            {
                var config = AppliedConfig.GetValue(change);
                if (config == null || !which(config))
                    continue;
                Object.Destroy(change);
                gone.Add(change);
            }
            return gone;
        }
    }
}
