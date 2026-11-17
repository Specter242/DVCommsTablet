using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace CommsTablet
{
    /// <summary>
    /// JimTaplin's Rearranged mods (decision 13), by reflection: Rearranged
    /// S282 Re-Arranged (2.1.1) and Rearranged S060 (0.3.6-mp). Both change a
    /// stock steam loco's wheel arrangement in place, keeping its livery,
    /// and add their own radio mode. The tablet asks the same request each
    /// radio mode makes, which checks the loco, saves the arrangement, and
    /// goes through the host in Multiplayer. Every member is internal to the
    /// mods, so each one is resolved by name and the app stays hidden if any
    /// is missing.
    /// </summary>
    internal static class RearrangedLink
    {
        internal const string S282Id = "RearrangedS282", S060Id = "RearrangedS060";

        /// <summary>One wheel arrangement a loco can take: the mod's id for it and its name.</summary>
        internal sealed class Arrangement
        {
            internal readonly int Id;
            internal readonly string Name;
            /// <summary>Whether it is articulated: more than one group of driving wheels.</summary>
            internal readonly bool Articulated;

            internal Arrangement(int id, string name, bool articulated)
            {
                Id = id;
                Name = name;
                Articulated = articulated;
            }

            /// <summary>The Whyte groups of wheels, leading to trailing, from the name ("2-8-2 Mikado"), or none.</summary>
            internal int[] Wheels
            {
                get
                {
                    // "0-4+4-0T": a plus joins two engines' drivers, a T marks a tank engine.
                    var whyte = Name.Split(' ')[0].TrimEnd('T', 't').Split('-', '+');
                    var groups = new int[whyte.Length];
                    for (var i = 0; i < whyte.Length; i++)
                        if (!int.TryParse(whyte[i], out groups[i]))
                            return KnownWheels();
                    return whyte.Length >= 3 ? groups : KnownWheels();
                }
            }

            // The S060's arrangements named for their builders, not their wheels.
            private int[] KnownWheels()
            {
                var name = Name.ToLowerInvariant();
                if (name.Contains("meyer")) return new[] { 0, 4, 4, 0 };
                if (name.Contains("shay")) return new[] { 0, 4, 4, 0 };
                if (name.Contains("dockside")) return new[] { 0, 4, 0 };
                return null;
            }
        }

        // ---- Rearranged S282 ---------------------------------------------------
        private static readonly Type S282Target = AccessTools.TypeByName("RearrangedS282.S282Target");
        private static readonly Type S282Arrangements = AccessTools.TypeByName("RearrangedS282.WheelArrangement");
        private static readonly Type S282Sync = AccessTools.TypeByName("RearrangedS282.MultiplayerSync");
        private static readonly Type S282Rearranger = AccessTools.TypeByName("RearrangedS282.WheelRearranger");
        private static readonly MethodInfo S282CanModify = Method(S282Target, "CanModify", typeof(TrainCar));
        private static readonly FieldInfo S282Names = S282Arrangements != null ? AccessTools.Field(S282Arrangements, "WheelArrangementNames") : null;
        private static readonly MethodInfo S282Runtime = Method(S282Arrangements, "IsRuntimeId", typeof(int));
        private static readonly MethodInfo S282Articulated = Method(S282Arrangements, "IsArticulatedId", typeof(int));
        private static readonly MethodInfo S282Request = Method(S282Sync, "RequestChange", typeof(TrainCar), typeof(int), typeof(string).MakeByRefType());
        private static readonly PropertyInfo S282Current = S282Rearranger != null ? AccessTools.Property(S282Rearranger, "currentWA") : null;
        /// <summary>The S282's own arrangement, which a loco without the mod's component has.</summary>
        private const int S282Stock = 10;

        // ---- Rearranged S060 ---------------------------------------------------
        private static readonly Type S060Parts = AccessTools.TypeByName("RearrangedS060.S060Parts");
        private static readonly Type S060Network = AccessTools.TypeByName("RearrangedS060.S060NetworkSelection");
        private static readonly Type S060Selection = AccessTools.TypeByName("RearrangedS060.S060Selection");
        private static readonly Type S060Lifecycle = AccessTools.TypeByName("RearrangedS060.S060Lifecycle");
        private static readonly Type S060Conversion = AccessTools.TypeByName("RearrangedS060.S060Conversion");
        private static readonly MethodInfo S060IsS060 = Method(S060Parts, "IsS060", typeof(TrainCar));
        private static readonly MethodInfo S060Supported = Method(S060Network, "IsSupported", typeof(int));
        private static readonly MethodInfo S060Decode = Method(S060Network, "Decode", typeof(int));
        private static readonly MethodInfo S060Encode = S060Selection != null ? Method(S060Network, "Encode", S060Selection) : null;
        private static readonly PropertyInfo S060Name = S060Selection != null ? AccessTools.Property(S060Selection, "Name") : null;
        private static readonly MethodInfo S060Apply = S060Selection != null ? Method(S060Lifecycle, "TryApply", typeof(TrainCar), S060Selection, typeof(string).MakeByRefType()) : null;
        private static readonly MethodInfo S060Current = Method(S060Conversion, "Current", typeof(TrainCar));
        private static readonly PropertyInfo S060CurrentSelection = S060Conversion != null ? AccessTools.Property(S060Conversion, "Selection") : null;
        /// <summary>The ids the S060's network selection takes: its eight arrangements and the short-wheelbase 0-6-0.</summary>
        private const int S060Ids = 9;

        private static MethodInfo Method(Type type, string name, params Type[] parameters) =>
            type == null ? null : AccessTools.Method(type, name, parameters);

        private static bool Active(string id) => UnityModManager.FindMod(id)?.Active == true;

        /// <summary>Whether Rearranged S282 is on and every member the tablet calls resolved.</summary>
        internal static bool S282Present => Active(S282Id) && S282Resolves;

        internal static bool S282Resolves => S282CanModify != null && S282Names != null && S282Runtime != null && S282Articulated != null
            && S282Request != null && S282Current != null;

        /// <summary>Whether Rearranged S060 is on and every member the tablet calls resolved.</summary>
        internal static bool S060Present => Active(S060Id) && S060Resolves;

        internal static bool S060Resolves => S060IsS060 != null && S060Supported != null && S060Decode != null && S060Encode != null
            && S060Name != null && S060Apply != null && S060Current != null && S060CurrentSelection != null;

        internal static bool Present => S282Present || S060Present;

        /// <summary>The members that did not resolve, for the contract check and the log.</summary>
        internal static IEnumerable<string> Missing()
        {
            var members = new (string name, object member)[]
            {
                ("S282Target.CanModify", S282CanModify), ("WheelArrangement.WheelArrangementNames", S282Names),
                ("WheelArrangement.IsRuntimeId", S282Runtime), ("WheelArrangement.IsArticulatedId", S282Articulated),
                ("MultiplayerSync.RequestChange", S282Request), ("WheelRearranger.currentWA", S282Current),
                ("S060Parts.IsS060", S060IsS060), ("S060NetworkSelection.IsSupported", S060Supported),
                ("S060NetworkSelection.Decode", S060Decode), ("S060NetworkSelection.Encode", S060Encode),
                ("S060Selection.Name", S060Name), ("S060Lifecycle.TryApply", S060Apply),
                ("S060Conversion.Current", S060Current), ("S060Conversion.Selection", S060CurrentSelection),
            };
            return members.Where(m => m.member == null).Select(m => m.name);
        }

        /// <summary>Whether <paramref name="car"/> is an S282 the S282 mod works on (the stock livery).</summary>
        internal static bool IsS282(TrainCar car) => S282Present && car != null && car.carLivery != null && car.carLivery.id == "LocoS282A";

        internal static bool IsS060(TrainCar car) => S060Present && car != null && !car.isExploded && Invoke<bool>(S060IsS060, car);

        /// <summary>Whether either mod rearranges <paramref name="car"/>.</summary>
        internal static bool Fits(TrainCar car) => IsS282(car) || IsS060(car);

        /// <summary>The arrangements <paramref name="car"/> can take, in each mod's own order.</summary>
        internal static List<Arrangement> For(TrainCar car)
        {
            var list = new List<Arrangement>();
            try
            {
                if (IsS282(car) && S282Names.GetValue(null) is string[] names)
                {
                    for (var id = 0; id < names.Length; id++)
                        if (Invoke<bool>(S282Runtime, id))
                            list.Add(new Arrangement(id, names[id], Invoke<bool>(S282Articulated, id)));
                }
                else if (IsS060(car))
                {
                    for (var id = 0; id < S060Ids; id++)
                        if (Invoke<bool>(S060Supported, id))
                        {
                            var name = (string)S060Name.GetValue(S060Decode.Invoke(null, new object[] { id }));
                            list.Add(new Arrangement(id, name, name.IndexOf("meyer", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("shay", StringComparison.OrdinalIgnoreCase) >= 0));
                        }
                }
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not list Rearranged's arrangements", e);
                list.Clear();
            }
            return list;
        }

        /// <summary>The id of the arrangement <paramref name="car"/> has now, or -1.</summary>
        internal static int Current(TrainCar car)
        {
            try
            {
                if (IsS282(car))
                {
                    var rearranger = car.GetComponent(S282Rearranger);
                    return rearranger == null ? S282Stock : Convert.ToInt32(S282Current.GetValue(rearranger));
                }
                if (IsS060(car))
                {
                    var conversion = S060Current.Invoke(null, new object[] { car });
                    // No conversion: the stock 0-6-0.
                    var selection = conversion == null ? S060Decode.Invoke(null, new object[] { 0 }) : S060CurrentSelection.GetValue(conversion);
                    return Convert.ToInt32(S060Encode.Invoke(null, new[] { selection }));
                }
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Could not read a loco's arrangement from Rearranged", e);
            }
            return -1;
        }

        /// <summary>
        /// Asks the loco's mod to change it to arrangement <paramref name="id"/>,
        /// as its radio mode does. On a Multiplayer client, true means the
        /// host was asked. <paramref name="reason"/> says why not.
        /// </summary>
        internal static bool Apply(TrainCar car, int id, out string reason)
        {
            reason = null;
            try
            {
                object[] args;
                bool done;
                if (IsS282(car))
                {
                    args = new object[] { car, id, null };
                    done = (bool)S282Request.Invoke(null, args);
                    reason = args[2] as string;
                    return done;
                }
                if (IsS060(car))
                {
                    args = new[] { car, S060Decode.Invoke(null, new object[] { id }), null };
                    done = (bool)S060Apply.Invoke(null, args);
                    reason = args[2] as string;
                    return done;
                }
                reason = "Not an S282 or S060";
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Rearranged could not change the loco", e);
                reason = "Rearranged could not change it; see the log";
            }
            return false;
        }

        private static T Invoke<T>(MethodInfo method, params object[] args)
        {
            try
            {
                return method != null && method.Invoke(null, args) is T result ? result : default;
            }
            catch
            {
                return default;
            }
        }
    }
}
