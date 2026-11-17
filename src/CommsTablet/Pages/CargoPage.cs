using System.Collections.Generic;
using System.Linq;
using CommsTablet.Tablet;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// The radio's Cargo choices: unload, or a cargo to load, found by the
    /// car type it loads on (BY CAR, each type with a side view) or in one
    /// list (ALL). A choice writes the loader's <c>selectedCargoType</c> and
    /// presses once, EnterLoadMode → PickCargoAndLoad, where the tablet aims
    /// at cars and loads as the radio, and keeps loading car after car. The
    /// stock loader loads only cars the player spawned. Not offered to a
    /// Multiplayer client: a client's load stays on that client (decision 8).
    /// </summary>
    internal sealed class CargoPage : TabletPage
    {
        private const int ByCar = 0, All = 1;
        private static readonly string[] SectionNames = { "BY CAR", "ALL" };

        // Each tile's choice: a car type to open, or a cargo (null with unload).
        private readonly List<TrainCarType_v2> types = new List<TrainCarType_v2>();
        private readonly List<CargoType_v2> cargos = new List<CargoType_v2>();
        private TrainCarType_v2 openType;
        private bool armed;
        private CargoType_v2 remembered;

        internal CargoPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "CARGO";


        internal override ICommsRadioMode Mode => Radio.Cargo;

        internal override bool Available => Radio.Cargo != null && GameMembers.CargoResolves
            && Session.CheatMode && !Session.IsMultiplayerClient && Radios.Allow(r => r.cargoLoaderControl);

        internal override string[] Sections => SectionNames;

        internal override string Hint => "Loads cars you spawned";

        internal override void SelectSection(int index)
        {
            base.SelectSection(index);
            openType = null;
        }

        internal override int Selected
        {
            get
            {
                var loader = Radio.Cargo;
                if (loader == null || !GameMembers.CargoResolves || GameMembers.GetCargoState(loader) != GameMembers.CargoPickAndLoad)
                    return -1;
                var cargo = GameMembers.CargoSelected(loader);
                for (var i = 0; i < tiles.Count; i++)
                    if (types[i] == null && cargos[i] == cargo && !tiles[i].Back)
                        return i;
                return -1;
            }
        }

        private static string CargoName(CargoType_v2 cargo) => LocalizationAPI.L(cargo.localizationKeyShort);

        private static List<CargoType_v2> Sorted(IEnumerable<CargoType_v2> list) =>
            // A copy: the loader sorts the game's own lists in place.
            list.Where(c => c != null).OrderBy(CargoName, System.StringComparer.Create(LocalizationAPI.CC, true)).ToList();

        internal override void Refresh()
        {
            tiles.Clear();
            types.Clear();
            cargos.Clear();
            var model = Globals.G?.Types;
            if (model == null)
                return;
            if (Section == ByCar && openType != null)
            {
                Add(new Tile("BACK", null, back: true), null, null);
                if (model.CarTypeToLoadableCargo.TryGetValue(openType, out var fits))
                    foreach (var cargo in Sorted(fits))
                        Add(new Tile(CargoName(cargo), cargo.icon), null, cargo);
                return;
            }
            Add(new Tile(CommsRadioLocalization.CARGO_UNLOAD, null), null, null);
            if (Section == All)
            {
                foreach (var cargo in Sorted(model.cargos))
                    Add(new Tile(CargoName(cargo), cargo.icon), null, cargo);
                return;
            }
            var withCargo = model.CarTypeToLoadableCargo.Where(p => p.Key != null && p.Value != null && p.Value.Count > 0)
                .Select(p => p.Key).OrderBy(t => LocalizationAPI.L(t.localizationKey), System.StringComparer.Create(LocalizationAPI.CC, true));
            foreach (var type in withCargo)
            {
                var first = type.liveries?.FirstOrDefault(l => l != null && !l.isHidden) ?? type.liveries?.FirstOrDefault(l => l != null);
                Add(new Tile(LocalizationAPI.L(type.localizationKey), first != null ? first.icon : null, first), type, null);
            }
        }

        private void Add(Tile tile, TrainCarType_v2 type, CargoType_v2 cargo)
        {
            tiles.Add(tile);
            types.Add(type);
            cargos.Add(cargo);
        }

        internal override void Press(int tile)
        {
            if (tile < 0 || tile >= tiles.Count || Radio.Cargo == null)
                return;
            Notice = null;
            if (tiles[tile].Back)
            {
                openType = null;
                Scroll = 0;
                Refresh();
                return;
            }
            if (types[tile] != null)
            {
                openType = types[tile];
                Scroll = 0;
                Refresh();
                return;
            }
            Choose(cargos[tile]);
        }

        private void Choose(CargoType_v2 cargo)
        {
            armed = true;
            remembered = cargo;
            var loader = Radio.Cargo;
            Radio.Drive(this, loader.confirmSound, () =>
            {
                GameMembers.CargoSelected(loader) = cargo;
                loader.OnUse();                                       // → PickCargoAndLoad
                return GameMembers.GetCargoState(loader) == GameMembers.CargoPickAndLoad && GameMembers.CargoSelected(loader) == cargo;
            });
        }

        internal override void Capture()
        {
            var loader = Radio.Cargo;
            if (loader == null || !GameMembers.CargoResolves)
                return;
            // Used away from a car, the loader cancels back to its start: nothing to take up again.
            armed = GameMembers.GetCargoState(loader) == GameMembers.CargoPickAndLoad;
            remembered = armed ? GameMembers.CargoSelected(loader) : null;
        }

        internal override void Reapply()
        {
            if (armed)
                Choose(remembered);
        }
    }
}
