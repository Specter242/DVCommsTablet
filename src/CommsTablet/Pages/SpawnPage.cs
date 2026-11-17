using System.Collections.Generic;
using System.Linq;
using CommsTablet.Tablet;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;

namespace CommsTablet.Pages
{
 internal sealed class SpawnPage : TabletPage
    {
        private const int Locos = 0, Cars = 1;
        private static readonly string[] SectionNames = { "LOCOS", "CARS" };

        // Each tile's index in the spawner's list; for the car types' level, the type's index.
        private readonly List<int> indexes = new List<int>();
        private TrainCarType_v2 openType;
        private (int category, TrainCarType_v2 type, TrainCarLivery livery)? remembered;

        internal SpawnPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "SPAWN";


        internal override ICommsRadioMode Mode => Radio.Spawner;

        internal override bool Available => Radio.Spawner != null && GameMembers.SpawnResolves
            && Session.CheatMode && Radios.Allow(r => r.carSpawnerControl);

        internal override string[] Sections => SectionNames;

        internal override void SelectSection(int section)
        {
            base.SelectSection(section);
            openType = null;
        }

        internal override int Selected
        {
            get
            {
                var spawner = Radio.Spawner;
                if (spawner == null || !GameMembers.SpawnResolves)
                    return -1;
                var state = GameMembers.GetSpawnState(spawner);
                if (state == GameMembers.SpawnPickLoco || (state == GameMembers.SpawnPickDestination && CategoryOf(spawner) == GameMembers.SpawnLoco))
                    return Section == Locos ? indexes.IndexOf(GameMembers.SpawnLocoIndex(spawner)) : -1;
                if (state != GameMembers.SpawnPickCarLivery && state != GameMembers.SpawnPickDestination || Section != Cars)
                    return -1;
                var type = TypeAt(spawner, GameMembers.SpawnCarTypeIndex(spawner));
                if (openType == null)
                    return indexes.IndexOf(GameMembers.SpawnCarTypeIndex(spawner));
                return type == openType ? indexes.IndexOf(GameMembers.SpawnCarLiveryIndex(spawner)) : -1;
            }
        }

        private static int CategoryOf(CommsRadioCarSpawner spawner) => System.Convert.ToInt32(GameMembers.SpawnCategory.GetValue(spawner));

        private static TrainCarType_v2 TypeAt(CommsRadioCarSpawner spawner, int index)
        {
            var types = GameMembers.SpawnCarTypes(spawner);
            return types != null && index >= 0 && index < types.Count ? types[index] : null;
        }

        private static string LiveryName(TrainCarLivery livery) => livery != null ? LocalizationAPI.L(livery.localizationKey) : "?";

        internal override void Refresh()
        {
            tiles.Clear();
            indexes.Clear();
            var spawner = Radio.Spawner;
            if (spawner == null || !GameMembers.SpawnResolves)
                return;
            if (Section == Locos)
            {
                var locos = GameMembers.SpawnLocos(spawner);
                for (var i = 0; i < locos.Count; i++)
                    if (locos[i] != null && GameMembers.SpawnLiveryUnlocked(locos[i]))
                        Add(i, new Tile(LiveryName(locos[i]), locos[i].icon, locos[i]));
                return;
            }
            var types = GameMembers.SpawnCarTypes(spawner);
            if (openType == null)
            {
                for (var i = 0; i < types.Count; i++)
                {
                    var type = types[i];
                    if (type == null || !GameMembers.SpawnCarTypeUnlocked(type))
                        continue;
                    var first = Unlocked(spawner, type).Select(p => p.livery).FirstOrDefault();
                    Add(i, new Tile(LocalizationAPI.L(type.localizationKey), first != null ? first.icon : null, first));
                }
                return;
            }
            Add(-1, new Tile("BACK", null, back: true));
            foreach (var (index, livery) in Unlocked(spawner, openType))
                Add(index, new Tile(LiveryName(livery), livery.icon, livery));
        }

        private void Add(int index, Tile tile)
        {
            indexes.Add(index);
            tiles.Add(tile);
        }

        /// <summary>A car type's unlocked liveries, with their indexes in the spawner's list for it.</summary>
        private static IEnumerable<(int index, TrainCarLivery livery)> Unlocked(CommsRadioCarSpawner spawner, TrainCarType_v2 type)
        {
            if (!GameMembers.SpawnCarLiveries(spawner).TryGetValue(type, out var liveries))
                yield break;
            for (var i = 0; i < liveries.Count; i++)
                if (liveries[i] != null && GameMembers.SpawnLiveryUnlocked(liveries[i]))
                    yield return (i, liveries[i]);
        }

        internal override void Press(int tile)
        {
            var spawner = Radio.Spawner;
            if (tile < 0 || tile >= tiles.Count || spawner == null)
                return;
            Notice = null;
            if (Section == Locos)
            {
                var locos = GameMembers.SpawnLocos(spawner);
                Choose(GameMembers.SpawnLoco, null, locos[indexes[tile]]);
                return;
            }
            if (openType == null)
            {
                var type = TypeAt(spawner, indexes[tile]);
                var liveries = Unlocked(spawner, type).ToList();
                if (liveries.Count == 1)
                {
                    Choose(GameMembers.SpawnCar, type, liveries[0].livery);
                    return;
                }
                openType = type;
                Scroll = 0;
                Refresh();
                return;
            }
            if (tiles[tile].Back)
            {
                openType = null;
                Scroll = 0;
                Refresh();
                return;
            }
            Choose(GameMembers.SpawnCar, openType, GameMembers.SpawnCarLiveries(spawner)[openType][indexes[tile]]);
        }

        private void Choose(int category, TrainCarType_v2 type, TrainCarLivery livery)
        {
            remembered = (category, type, livery);
            var spawner = Radio.Spawner;
            Radio.Drive(this, spawner.confirmSound, () =>
            {
                // Indexes resolved afresh in the spawner's lists, which the tablet does not own.
                if (category == GameMembers.SpawnLoco)
                {
                    var index = GameMembers.SpawnLocos(spawner).IndexOf(livery);
                    if (index < 0)
                        return false;
                    GameMembers.SetSpawnCategory(spawner, GameMembers.SpawnLoco);
                    spawner.OnUse();                                  // → PickCategory
                    GameMembers.SpawnLocoIndex(spawner) = index;
                    spawner.OnUse();                                  // → PickLoco
                    return GameMembers.GetSpawnState(spawner) == GameMembers.SpawnPickLoco && GameMembers.SpawnLocoIndex(spawner) == index;
                }
                var typeIndex = GameMembers.SpawnCarTypes(spawner).IndexOf(type);
                if (typeIndex < 0 || !GameMembers.SpawnCarLiveries(spawner).TryGetValue(type, out var liveries))
                    return false;
                var liveryIndex = liveries.IndexOf(livery);
                if (liveryIndex < 0)
                    return false;
                GameMembers.SetSpawnCategory(spawner, GameMembers.SpawnCar);
                spawner.OnUse();                                      // → PickCategory
                GameMembers.SpawnCarTypeIndex(spawner) = typeIndex;
                spawner.OnUse();                                      // → PickCarType
                GameMembers.SpawnCarLiveryIndex(spawner) = liveryIndex;
                spawner.OnUse();                                      // → PickCarLivery, or PickDestination for a type with one livery
                var state = GameMembers.GetSpawnState(spawner);
                return (state == GameMembers.SpawnPickCarLivery || state == GameMembers.SpawnPickDestination)
                       && GameMembers.SpawnCarTypeIndex(spawner) == typeIndex && GameMembers.SpawnCarLiveryIndex(spawner) == liveryIndex;
            });
        }

        internal override void Capture()
        {
            var spawner = Radio.Spawner;
            if (spawner == null || !GameMembers.SpawnResolves)
                return;
            var state = GameMembers.GetSpawnState(spawner);
            if (state != GameMembers.SpawnPickLoco && state != GameMembers.SpawnPickCarLivery && state != GameMembers.SpawnPickDestination)
            {
                remembered = null;
                return;
            }
            if (CategoryOf(spawner) == GameMembers.SpawnLoco)
            {
                var locos = GameMembers.SpawnLocos(spawner);
                var index = GameMembers.SpawnLocoIndex(spawner);
                remembered = index >= 0 && index < locos.Count ? (GameMembers.SpawnLoco, null, locos[index]) : ((int, TrainCarType_v2, TrainCarLivery)?)null;
                return;
            }
            var type = TypeAt(spawner, GameMembers.SpawnCarTypeIndex(spawner));
            var liveryIndex = GameMembers.SpawnCarLiveryIndex(spawner);
            remembered = type != null && GameMembers.SpawnCarLiveries(spawner).TryGetValue(type, out var liveries) && liveryIndex >= 0 && liveryIndex < liveries.Count
                ? (GameMembers.SpawnCar, type, liveries[liveryIndex]) : ((int, TrainCarType_v2, TrainCarLivery)?)null;
        }

        internal override void Reapply()
        {
            if (remembered.HasValue)
                Choose(remembered.Value.category, remembered.Value.type, remembered.Value.livery);
        }
    }
}
