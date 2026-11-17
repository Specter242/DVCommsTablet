using CommsTablet.Guests;
using CommsTablet.Tablet;
using DV;

namespace CommsTablet.Pages
{
 internal sealed class GuestPage : TabletPage
    {
        internal readonly GuestApp Guest;

        /// <summary>The car-first guest's mode; null for another guest.</summary>
        internal readonly TabletGuestCarMode CarMode;

        private TrainCar remembered;

        internal GuestPage(TabletRadio radio, GuestApp guest, TabletGuestCarMode carMode = null) : base(radio)
        {
            Guest = guest;
            CarMode = carMode;
            if (carMode != null)
                carMode.UseAtCar = () => radio.UseGuest(this);
        }

        internal override string Name => Guest.Name;

        internal override string Label => Guest.Name;

        internal override ICommsRadioMode Mode => CarMode;

        internal override bool Available => Guest.Available;

        internal override int Selected => -1;

        internal override string EmptyText => "";

        internal override void Refresh() { }

        internal override void Press(int tile) { }

        internal override void Capture() => remembered = CarMode?.Car;

        /// <summary>Taken up again near the car it had chosen, a car-first guest's tablet chooses it again, as Paint and Change do.</summary>
        internal override void Reapply()
        {
            var player = PlayerManager.PlayerTransform;
            if (CarMode != null && remembered != null && remembered.gameObject.activeInHierarchy && player != null
                && (remembered.transform.position - player.position).sqrMagnitude < TabletCarMode.SignalRange * TabletCarMode.SignalRange)
                CarMode.Choose(remembered);
            remembered = null;
        }
    }
}
