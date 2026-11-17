using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The tablet's Rearrange mode (decision 13), car first
    /// (<see cref="TabletCarMode"/>): use the tablet at an S282 or S060 that
    /// Rearranged works on, and its app lists the wheel arrangements it can
    /// take; a tile asks the mod for one. Used at the chosen loco again, the
    /// mode applies the last arrangement stepped to with the scroll wheel.
    /// The change itself is the mod's (<see cref="RearrangedLink"/>).
    /// </summary>
    internal sealed class TabletRearrangeMode : TabletCarMode
    {
        private static readonly Color Laser = new Color(0.55f, 0.75f, 1f);

        private readonly List<RearrangedLink.Arrangement> arrangements = new List<RearrangedLink.Arrangement>();
        private (string content, string action) shown;

        /// <summary>The chosen loco's arrangements, in the mod's order.</summary>
        internal IReadOnlyList<RearrangedLink.Arrangement> Arrangements => arrangements;

        /// <summary>The arrangement last stepped to with the wheel or asked for, if any.</summary>
        internal RearrangedLink.Arrangement Last { get; private set; }

        /// <summary>Why the last request was refused, or what a Multiplayer client is waiting for.</summary>
        internal string Reason { get; private set; }

        public override Color GetLaserBeamColor() => Laser;

        protected override bool Fits(TrainCar car) => RearrangedLink.Fits(car);

        protected override void OnChosen()
        {
            arrangements.Clear();
            arrangements.AddRange(RearrangedLink.For(Car));
            Last = null;
            Reason = null;
        }

        protected override void OnReleased()
        {
            arrangements.Clear();
            Last = null;
            Reason = null;
        }

        protected override void ApplyLast()
        {
            if (Last != null)
                Apply(Last);
        }

        protected override bool Step(int by)
        {
            if (arrangements.Count == 0)
                return false;
            var from = Last ?? Wearing;
            var index = from != null ? arrangements.IndexOf(from) : -1;
            index = index < 0 ? (by > 0 ? 0 : arrangements.Count - 1) : (index + by + arrangements.Count) % arrangements.Count;
            Last = arrangements[index];
            Redisplay();
            return true;
        }

        /// <summary>The arrangement the chosen loco has now, if it is one of its list.</summary>
        internal RearrangedLink.Arrangement Wearing
        {
            get
            {
                if (Car == null)
                    return null;
                var id = RearrangedLink.Current(Car);
                return arrangements.FirstOrDefault(a => a.Id == id);
            }
        }

        /// <summary>Asks the loco's mod for <paramref name="arrangement"/>; false, with <see cref="Reason"/>, if refused.</summary>
        internal bool Apply(RearrangedLink.Arrangement arrangement)
        {
            if (Car == null || arrangement == null || !InReach)
                return false;
            Last = arrangement;
            var done = RearrangedLink.Apply(Car, arrangement.Id, out var reason);
            Reason = done ? null : reason ?? "Refused";
            DV.CommsRadioController.PlayAudioFromRadio(done ? confirmSound : cancelSound, transform);
            Redisplay();
            return done;
        }

        protected override void Redisplay() => shown = default;

        protected override void ShowDisplay()
        {
            if (display == null)
                return;
            string content;
            if (Car == null)
                content = "Point at an S282 or S060";
            else
            {
                var wearing = Wearing;
                content = Last != null && Last != wearing
                    ? $"{Car.ID}: {wearing?.Name ?? "?"} > {Last.Name}"
                    : $"{Car.ID}: {wearing?.Name ?? "?"}";
            }
            var next = (content, ActionWord(Last != null));
            if (next == shown)
                return;
            shown = next;
            display.SetDisplay("REARRANGE", content, next.Item2);
        }
    }
}
