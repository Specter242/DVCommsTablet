using System;
using CommsTablet.Guests;
using UnityEngine;

namespace CommsTablet.Tablet
{
 internal sealed class TabletGuestCarMode : TabletCarMode
    {
        private static readonly Color Laser = new Color(1f, 1f, 1f, 1f);

        /// <summary>The guest the mode chooses cars for.</summary>
        internal GuestApp Guest;

        /// <summary>The use at the chosen car, given to the guest's shown view: whether it took it.</summary>
        internal Func<bool> UseAtCar;

        /// <summary>Raised as a car is chosen (or let go: null), for the screens to tell the guest's views.</summary>
        internal event Action<GuestApp, TrainCar> Told;

        public override Color GetLaserBeamColor() => Guest?.BeamColor() ?? Laser;

        protected override bool Fits(TrainCar car) => Guest != null && Guest.Fits(car);

        protected override void OnChosen() => Told?.Invoke(Guest, Car);

        protected override void OnReleased() => Told?.Invoke(Guest, null);

        protected override void ApplyLast() => UseAtCar?.Invoke();

        // The wheel goes to the guest's Scroll by the radio, not through the mode.
        protected override bool Step(int by) => false;

        // A guest's app has the whole screen, with no status strip to show the display on.
        protected override void ShowDisplay() { }

        protected override void Redisplay() { }
    }
}
