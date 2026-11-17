using CommsTablet.Tablet;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// The crew vehicles the radio's Summon mode offers: the unlocked garage
    /// vehicles and Custom Car Loader's bought work trains
    /// (<c>availableVehiclesForSpawn</c>). A choice writes
    /// <c>selectedVehicleIndex</c> and presses twice, EnterSpawnMode →
    /// PickCrewVehicle → PickDestination, where the tablet aims as the radio.
    /// </summary>
    internal sealed class SummonPage : TabletPage
    {
        private TrainCarLivery remembered;

        internal SummonPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "SUMMON";


        internal override string Tab => "CREW";

        internal override ICommsRadioMode Mode => Radio.Summon;

        internal override string EmptyText => "No crew vehicles unlocked yet.";

        internal override bool Available => Radio.Summon != null && GameMembers.SummonResolves
            && Session.Career && Radios.Allow(r => r.crewVehicleControl);

        internal override int Selected
        {
            get
            {
                var summon = Radio.Summon;
                if (summon == null || !GameMembers.SummonResolves || summon.CurrentState == CommsRadioCrewVehicle.State.EnterSpawnMode)
                    return -1;
                return GameMembers.SummonIndex(summon);
            }
        }

        internal override void Refresh()
        {
            tiles.Clear();
            var list = Radio.Summon != null && GameMembers.SummonResolves ? GameMembers.SummonList(Radio.Summon) : null;
            if (list == null)
                return;
            foreach (var livery in list)
                tiles.Add(new Tile(livery != null ? LocalizationAPI.L(livery.localizationKey) : "?", livery != null ? livery.icon : null, livery));
        }

        internal override void Press(int tile)
        {
            var list = GameMembers.SummonList(Radio.Summon);
            if (list == null || tile < 0 || tile >= list.Count)
                return;
            Choose(list[tile]);
        }

        private void Choose(TrainCarLivery livery)
        {
            remembered = livery;
            var summon = Radio.Summon;
            Radio.Drive(this, summon.confirmSound, () =>
            {
                // Resolved afresh: the list changes on a garage unlock or a work-train purchase.
                var index = GameMembers.SummonList(summon).IndexOf(livery);
                if (index < 0)
                    return false;
                GameMembers.SummonIndex(summon) = index;
                summon.OnUse();
                summon.OnUse();
                return summon.CurrentState == CommsRadioCrewVehicle.State.PickDestination && GameMembers.SummonIndex(summon) == index;
            });
        }

        internal override void Capture()
        {
            var summon = Radio.Summon;
            if (summon == null || !GameMembers.SummonResolves)
                return;
            // Back at the start after a summon: nothing to take up again.
            var index = summon.CurrentState == CommsRadioCrewVehicle.State.EnterSpawnMode ? -1 : GameMembers.SummonIndex(summon);
            var list = GameMembers.SummonList(summon);
            remembered = index >= 0 && index < list.Count ? list[index] : null;
        }

        internal override void Reapply()
        {
            if (remembered != null)
                Choose(remembered);
        }
    }
}
