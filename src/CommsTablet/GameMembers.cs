using System;
using System.Collections.Generic;
using System.Reflection;
using DV;
using DV.ThingTypes;
using HarmonyLib;

namespace CommsTablet
{
 internal static class GameMembers
    {
        private static readonly List<string> missing = new List<string>();

        /// <summary>Each member that did not resolve, as "Type.member: why".</summary>
        internal static IReadOnlyList<string> Missing => missing;

        // CommsRadioController: finding a mode and whether the radio has it off.
        internal static readonly AccessTools.FieldRef<CommsRadioController, List<ICommsRadioMode>> RadioModes =
            Field<CommsRadioController, List<ICommsRadioMode>>("allModes");
        internal static readonly AccessTools.FieldRef<CommsRadioController, HashSet<int>> RadioDisabled =
            Field<CommsRadioController, HashSet<int>>("disabledModeIndices");

        // CommsRadioCrewVehicle (Summon).
        internal static readonly AccessTools.FieldRef<CommsRadioCrewVehicle, List<TrainCarLivery>> SummonList =
            Field<CommsRadioCrewVehicle, List<TrainCarLivery>>("availableVehiclesForSpawn");
        internal static readonly AccessTools.FieldRef<CommsRadioCrewVehicle, int> SummonIndex =
            Field<CommsRadioCrewVehicle, int>("selectedVehicleIndex");
        /// <summary>Rebuilds the summon list from the garages and the work trains without a garage.</summary>
        internal static readonly Action<CommsRadioCrewVehicle> SummonRefresh =
            InstanceAction<CommsRadioCrewVehicle>("UpdateAvailableVehicles");

        // CommsRadioCarSpawner (Spawn).
        internal static readonly AccessTools.FieldRef<CommsRadioCarSpawner, List<TrainCarLivery>> SpawnLocos =
            Field<CommsRadioCarSpawner, List<TrainCarLivery>>("locoLiveries");
        internal static readonly AccessTools.FieldRef<CommsRadioCarSpawner, List<TrainCarType_v2>> SpawnCarTypes =
            Field<CommsRadioCarSpawner, List<TrainCarType_v2>>("carTypesToSpawn");
        internal static readonly AccessTools.FieldRef<CommsRadioCarSpawner, Dictionary<TrainCarType_v2, List<TrainCarLivery>>> SpawnCarLiveries =
            Field<CommsRadioCarSpawner, Dictionary<TrainCarType_v2, List<TrainCarLivery>>>("carLiveriesToSpawn");
        internal static readonly AccessTools.FieldRef<CommsRadioCarSpawner, int> SpawnLocoIndex =
            Field<CommsRadioCarSpawner, int>("selectedLocoIndex");
        internal static readonly AccessTools.FieldRef<CommsRadioCarSpawner, int> SpawnCarTypeIndex =
            Field<CommsRadioCarSpawner, int>("selectedCarTypeIndex");
        internal static readonly AccessTools.FieldRef<CommsRadioCarSpawner, int> SpawnCarLiveryIndex =
            Field<CommsRadioCarSpawner, int>("selectedCarLiveryIndex");
        // Private nested enums: Category { Loco, Car }; State { EnterSpawnMode,
        // PickCategory, PickLoco, PickCarType, PickCarLivery, PickDestination }.
        internal static readonly FieldInfo SpawnCategory = EnumField<CommsRadioCarSpawner>("category", 2);
        internal static readonly FieldInfo SpawnState = EnumField<CommsRadioCarSpawner>("state", 6);
        internal static readonly Func<TrainCarLivery, bool> SpawnLiveryUnlocked =
            StaticPredicate<CommsRadioCarSpawner, TrainCarLivery>("IsCarLiveryUnlocked");
        internal static readonly Func<TrainCarType_v2, bool> SpawnCarTypeUnlocked =
            StaticPredicate<CommsRadioCarSpawner, TrainCarType_v2>("IsCarTypeUnlocked");

        internal const int SpawnLoco = 0, SpawnCar = 1;
        internal const int SpawnPickLoco = 2, SpawnPickCarLivery = 4, SpawnPickDestination = 5;

        // CommsRadioCargoLoader (Cargo): the cargo to load, null to unload; private
        // enum State { EnterLoadMode, PickCargoAndLoad }.
        internal static readonly AccessTools.FieldRef<CommsRadioCargoLoader, CargoType_v2> CargoSelected =
            Field<CommsRadioCargoLoader, CargoType_v2>("selectedCargoType");
        internal static readonly FieldInfo CargoState = EnumField<CommsRadioCargoLoader>("state", 2);
        internal const int CargoPickAndLoad = 1;

        // StaticTextureRenderBase: the step where the game, and other mods' patches (Double Track
        // Maps, Interchange Yards), fill a render's pages; run on the map's copies of the station
        // booklet and route map renders before their pages are read (Map/BookletReader).
        internal static readonly MethodInfo RenderPageStep =
            InstanceMethod<DV.RenderTextureSystem.BookletRender.StaticTextureRenderBase>("GetStaticTemplatePaperData",
                typeof(DV.RenderTextureSystem.BookletRender.TemplatePaperData[]));

        // GadgetItem: the placement's state (its aim's reach and VR pointer, its press), and the
        // flag a use sets for its next placement, cleared where the tablet's use is the radio's.
        internal static readonly AccessTools.FieldRef<DV.Customization.Gadgets.GadgetItem, DV.Customization.Gadgets.GadgetItem.GadgetPlacingContext> GadgetContext =
            Field<DV.Customization.Gadgets.GadgetItem, DV.Customization.Gadgets.GadgetItem.GadgetPlacingContext>("context");
        internal static readonly AccessTools.FieldRef<DV.Customization.Gadgets.GadgetItem, bool> GadgetPlace =
            Field<DV.Customization.Gadgets.GadgetItem, bool>("place");

        internal static bool SummonResolves => SummonList != null && SummonIndex != null;

        internal static bool SpawnResolves => SpawnLocos != null && SpawnCarTypes != null && SpawnCarLiveries != null
            && SpawnLocoIndex != null && SpawnCarTypeIndex != null && SpawnCarLiveryIndex != null
            && SpawnCategory != null && SpawnState != null && SpawnLiveryUnlocked != null && SpawnCarTypeUnlocked != null;

        internal static bool CargoResolves => CargoSelected != null && CargoState != null;

        internal static int GetCargoState(CommsRadioCargoLoader loader) => Convert.ToInt32(CargoState.GetValue(loader));

        internal static int GetSpawnState(CommsRadioCarSpawner spawner) => Convert.ToInt32(SpawnState.GetValue(spawner));

        internal static void SetSpawnCategory(CommsRadioCarSpawner spawner, int category) =>
            SpawnCategory.SetValue(spawner, Enum.ToObject(SpawnCategory.FieldType, category));

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name)
        {
            var field = AccessTools.Field(typeof(T), name);
            if (field == null || field.IsStatic || field.FieldType != typeof(F))
            {
                missing.Add($"{typeof(T).Name}.{name}: " + (field == null ? "not found" : field.IsStatic ? "static" : $"is {field.FieldType.Name}, not {typeof(F).Name}"));
                return null;
            }
            return AccessTools.FieldRefAccess<T, F>(field);
        }

        private static FieldInfo EnumField<T>(string name, int values)
        {
            var field = AccessTools.Field(typeof(T), name);
            if (field == null || field.IsStatic || !field.FieldType.IsEnum || Enum.GetValues(field.FieldType).Length != values)
            {
                missing.Add($"{typeof(T).Name}.{name}: " + (field == null ? "not found" : $"is not the {values}-value enum it was"));
                return null;
            }
            return field;
        }

        private static Action<T> InstanceAction<T>(string name)
        {
            var method = AccessTools.Method(typeof(T), name, Type.EmptyTypes);
            if (method == null || method.IsStatic || method.ReturnType != typeof(void))
            {
                missing.Add($"{typeof(T).Name}.{name}(): " + (method == null ? "not found" : "changed signature"));
                return null;
            }
            return AccessTools.MethodDelegate<Action<T>>(method);
        }

        private static MethodInfo InstanceMethod<T>(string name, Type returns)
        {
            var method = AccessTools.Method(typeof(T), name, Type.EmptyTypes);
            if (method == null || method.IsStatic || method.ReturnType != returns)
            {
                missing.Add($"{typeof(T).Name}.{name}(): " + (method == null ? "not found" : "changed signature"));
                return null;
            }
            return method;
        }

        private static Func<TArg, bool> StaticPredicate<T, TArg>(string name)
        {
            var method = AccessTools.Method(typeof(T), name, new[] { typeof(TArg) });
            if (method == null || !method.IsStatic || method.ReturnType != typeof(bool))
            {
                missing.Add($"{typeof(T).Name}.{name}({typeof(TArg).Name}): " + (method == null ? "not found" : "changed signature"));
                return null;
            }
            return AccessTools.MethodDelegate<Func<TArg, bool>>(method);
        }
    }
}
