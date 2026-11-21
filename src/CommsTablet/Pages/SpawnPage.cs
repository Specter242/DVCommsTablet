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

        private readonly struct Entry
        {
            internal readonly TrainCarType_v2 Type;
            internal readonly TrainCarLivery Livery;

            internal Entry(TrainCarType_v2 type, TrainCarLivery livery)
            {
                Type = type;
                Livery = livery;
            }
        }

        // Each tile's livery or car type identity, instead of live spawner indices.
        private readonly List<Entry> entries = new List<Entry>();
        private TrainCarType_v2 openType;
        private (int category, TrainCarType_v2 type, TrainCarLivery livery)? remembered;
        // What the list left out on its last refresh, each "name (why)".
        private readonly List<string> locked = new List<string>();

        internal SpawnPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "SPAWN";


        internal override ICommsRadioMode Mode => Radio.Spawner;

        internal override bool Available => Radio.Spawner != null && GameMembers.SpawnResolves
            && Session.CheatMode && Radios.Allow(r => r.carSpawnerControl);

        internal override string[] Sections => SectionNames;

        internal override string Hint => locked.Count == 0 ? null
            : locked.Count == 1 ? "Locked: " + locked[0]
            : $"Locked: {locked[0]} and {locked.Count - 1} more";

        /// <summary>Why the spawner leaves a livery out: the license it needs, else its garage (the game's IsCarLiveryUnlocked, in its order).</summary>
        private static string WhyLocked(TrainCarLivery livery)
        {
            try
            {
                var licenses = DV.Utils.SingletonBehaviour<LicenseManager>.Instance;
                if (livery.requiredLicense != null && licenses != null && !licenses.IsGeneralLicenseAcquired(livery.requiredLicense))
                    return "needs its license";
            }
            catch (System.Exception)
            {
                // Read as a garage below.
            }
            return "garage locked";
        }

        private void Locked(string name, TrainCarLivery livery) => locked.Add($"{name} ({WhyLocked(livery)})");

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
                {
                    if (Section != Locos)
                        return -1;
                    var locos = GameMembers.SpawnLocos(spawner);
                    var index = GameMembers.SpawnLocoIndex(spawner);
                    var current = locos != null && index >= 0 && index < locos.Count ? locos[index] : null;
                    return current == null ? -1 : entries.FindIndex(e => e.Livery == current);
                }
                if (state != GameMembers.SpawnPickCarLivery && state != GameMembers.SpawnPickDestination || Section != Cars)
                    return -1;
                var type = TypeAt(spawner, GameMembers.SpawnCarTypeIndex(spawner));
                if (openType == null)
                    return type == null ? -1 : entries.FindIndex(e => e.Type == type);
                if (type != openType || type == null)
                    return -1;
                var map = GameMembers.SpawnCarLiveries(spawner);
                if (map == null || !map.TryGetValue(type, out var liveries) || liveries == null)
                    return -1;
                var liveryIndex = GameMembers.SpawnCarLiveryIndex(spawner);
                var currentLivery = liveryIndex >= 0 && liveryIndex < liveries.Count ? liveries[liveryIndex] : null;
                return currentLivery == null ? -1 : entries.FindIndex(e => e.Livery == currentLivery);
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
            entries.Clear();
            locked.Clear();
            var spawner = Radio.Spawner;
            if (spawner == null || !GameMembers.SpawnResolves)
                return;
            if (Section == Locos)
            {
                var locos = GameMembers.SpawnLocos(spawner);
                if (locos == null)
                    return;
                for (var i = 0; i < locos.Count; i++)
                {
                    if (locos[i] == null)
                        continue;
                    if (GameMembers.SpawnLiveryUnlocked(locos[i]))
                        Add(new Entry(null, locos[i]), new Tile(LiveryName(locos[i]), locos[i].icon, locos[i]));
                    else
                        Locked(LiveryName(locos[i]), locos[i]);
                }
                return;
            }
            var types = GameMembers.SpawnCarTypes(spawner);
            if (openType == null)
            {
                if (types == null)
                    return;
                for (var i = 0; i < types.Count; i++)
                {
                    var type = types[i];
                    if (type == null)
                        continue;
                    if (!GameMembers.SpawnCarTypeUnlocked(type))
                    {
                        var sample = type.liveries?.FirstOrDefault(l => l != null);
                        if (sample != null)
                            Locked(LocalizationAPI.L(type.localizationKey), sample);
                        continue;
                    }
                    var first = Unlocked(spawner, type).FirstOrDefault();
                    Add(new Entry(type, null), new Tile(LocalizationAPI.L(type.localizationKey), first != null ? first.icon : null, first));
                }
                return;
            }
            Add(default, new Tile("BACK", null, back: true));
            foreach (var livery in Unlocked(spawner, openType))
                Add(new Entry(openType, livery), new Tile(LiveryName(livery), livery.icon, livery));
            var map = GameMembers.SpawnCarLiveries(spawner);
            if (map != null && map.TryGetValue(openType, out var all) && all != null)
                foreach (var livery in all)
                    if (livery != null && !GameMembers.SpawnLiveryUnlocked(livery))
                        Locked(LiveryName(livery), livery);
        }

        private void Add(Entry entry, Tile tile)
        {
            entries.Add(entry);
            tiles.Add(tile);
        }

        /// <summary>A car type's unlocked liveries.</summary>
        private static IEnumerable<TrainCarLivery> Unlocked(CommsRadioCarSpawner spawner, TrainCarType_v2 type)
        {
            var map = GameMembers.SpawnCarLiveries(spawner);
            if (type == null || map == null || !map.TryGetValue(type, out var liveries) || liveries == null)
                yield break;
            for (var i = 0; i < liveries.Count; i++)
                if (liveries[i] != null && GameMembers.SpawnLiveryUnlocked(liveries[i]))
                    yield return liveries[i];
        }

        internal override void Press(int tile)
        {
            var spawner = Radio.Spawner;
            if (tile < 0 || tile >= entries.Count || spawner == null || !GameMembers.SpawnResolves)
                return;
            Notice = null;
            if (Section == Locos)
            {
                var livery = entries[tile].Livery;
                if (livery != null)
                    Choose(GameMembers.SpawnLoco, null, livery);
                return;
            }
            if (openType == null)
            {
                var type = entries[tile].Type;
                if (type == null)
                    return;
                var liveries = Unlocked(spawner, type).ToList();
                if (liveries.Count == 1)
                {
                    Choose(GameMembers.SpawnCar, type, liveries[0]);
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
            var chosen = entries[tile].Livery;
            if (chosen != null)
                Choose(GameMembers.SpawnCar, openType, chosen);
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
                    var locos = GameMembers.SpawnLocos(spawner);
                    var index = locos != null ? locos.IndexOf(livery) : -1;
                    if (index < 0)
                        return false;
                    GameMembers.SetSpawnCategory(spawner, GameMembers.SpawnLoco);
                    spawner.OnUse();                                  // → PickCategory
                    GameMembers.SpawnLocoIndex(spawner) = index;
                    spawner.OnUse();                                  // → PickLoco
                    return GameMembers.GetSpawnState(spawner) == GameMembers.SpawnPickLoco && GameMembers.SpawnLocoIndex(spawner) == index;
                }
                var types = GameMembers.SpawnCarTypes(spawner);
                var map = GameMembers.SpawnCarLiveries(spawner);
                var typeIndex = type != null && types != null ? types.IndexOf(type) : -1;
                if (typeIndex < 0 || map == null || !map.TryGetValue(type, out var liveries) || liveries == null)
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
                remembered = locos != null && index >= 0 && index < locos.Count ? (GameMembers.SpawnLoco, null, locos[index]) : ((int, TrainCarType_v2, TrainCarLivery)?)null;
                return;
            }
            var type = TypeAt(spawner, GameMembers.SpawnCarTypeIndex(spawner));
            var liveryIndex = GameMembers.SpawnCarLiveryIndex(spawner);
            var map = GameMembers.SpawnCarLiveries(spawner);
            remembered = type != null && map != null && map.TryGetValue(type, out var liveries) && liveries != null && liveryIndex >= 0 && liveryIndex < liveries.Count
                ? (GameMembers.SpawnCar, type, liveries[liveryIndex]) : ((int, TrainCarType_v2, TrainCarLivery)?)null;
        }

        internal override void Reapply()
        {
            if (remembered.HasValue)
                Choose(remembered.Value.category, remembered.Value.type, remembered.Value.livery);
        }
    }
}
