using System.Collections.Generic;
using System.Globalization;
using CommsTablet.Tablet;
using DV;
using DV.InventorySystem;
using DV.ThingTypes;
using DV.Utils;

namespace CommsTablet.Pages
{
    /// <summary>
    /// Custom Car Loader's work trains for sale, each a side view with its
    /// price, as CCL's own radio mode sells them (its CommsRadioCCLWorkTrain:
    /// unlock, take the money, show the popup), in the Vehicles app beside
    /// Crew. A first press chooses one and shows its price; a second buys it,
    /// and it joins the Crew list to summon. Offered where CCL offers its mode:
    /// with Summon, so in Career.
    /// </summary>
    internal sealed class BuyPage : TabletPage
    {
        private readonly List<TrainCarLivery> forSale = new List<TrainCarLivery>();
        private readonly SummonPage summon;
        private TrainCarLivery chosen;

        internal BuyPage(TabletRadio radio, SummonPage summon) : base(radio) => this.summon = summon;

        internal override string Name => "BUY";

        internal override ICommsRadioMode Mode => null;

        internal override bool Available => WorkTrains.Selling && summon.Available;

        internal override string EmptyText => "Every work train is bought.\nSummon them from the Crew tab.";

        internal override string Hint => chosen != null ? "Press it again to buy" : "Work trains for sale";

        private static double Money => SingletonBehaviour<Inventory>.Instance != null ? SingletonBehaviour<Inventory>.Instance.PlayerMoney : 0d;

        private static string Dollars(double amount) => "$" + amount.ToString("N0", CultureInfo.InvariantCulture);

        internal override string Status => chosen == null
            ? $"Choose a work train to buy. You have {Dollars(Money)}."
            : $"{NameOf(chosen)}: {Dollars(WorkTrains.PriceOf(chosen))}. You have {Dollars(Money)}.";

        internal override int Selected => chosen == null ? -1 : forSale.IndexOf(chosen);

        private static string NameOf(TrainCarLivery livery) => DV.Localization.LocalizationAPI.L(livery.localizationKey);

        internal override void Refresh()
        {
            tiles.Clear();
            forSale.Clear();
            forSale.AddRange(WorkTrains.ForSale());
            if (chosen != null && !forSale.Contains(chosen))
                chosen = null;
            foreach (var livery in forSale)
                tiles.Add(new Tile($"{NameOf(livery)}\n{Dollars(WorkTrains.PriceOf(livery))}", livery.icon, livery));
        }

        internal override void Press(int tile)
        {
            if (tile < 0 || tile >= forSale.Count)
                return;
            var livery = forSale[tile];
            if (livery != chosen)
            {
                chosen = livery;
                Notice = null;
                return;
            }
            var inventory = SingletonBehaviour<Inventory>.Instance;
            if (inventory == null)
                return;
            var price = WorkTrains.PriceOf(livery);
            if (Money < price)
            {
                Notice = $"Not enough money: {NameOf(livery)} costs {Dollars(price)}";
                Radio.Sound(Radio.Summon != null ? Radio.Summon.cancelSound : null);
                return;
            }
            if (!WorkTrains.Buy(livery))
            {
                Notice = "Custom Car Loader would not sell it";
                return;
            }
            inventory.RemoveMoney(price);
            if (Radio.Summon != null && Radio.Summon.moneyRemovedSound != null)
                Radio.Summon.moneyRemovedSound.Play2D();
            Notice = $"Bought {NameOf(livery)}: summon it from the Crew tab";
            chosen = null;
        }

        internal override void Capture() { }

        internal override void Reapply() { }
    }
}
