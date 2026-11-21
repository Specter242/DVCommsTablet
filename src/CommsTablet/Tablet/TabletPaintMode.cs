using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Customization.Paint;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The tablet's Paint mode, its own, car first (<see cref="TabletCarMode"/>):
    /// the Paint app shows the chosen car's paints, each as a side view of it
    /// in that paint, and a tile paints it at once. Used at the chosen car
    /// again, the mode repaints it with the last paint (after a change of
    /// area).
    /// <list type="bullet">
    /// <item>Without Skin Manager: the stock Paint mode's themes, applied as
    /// it applies them (<c>TrainCarPaint.CurrentTheme</c> where
    /// <c>IsSupported</c>), its highlighter, sounds, and themes taken over
    /// when the tablet is built.</item>
    /// <item>With it: its skins through <see cref="SkinManagerLink"/>.</item>
    /// </list>
    /// Multiplayer sends every theme change as it does the radio's.
    /// </summary>
    internal sealed class TabletPaintMode : TabletCarMode
    {
        internal const int All = 0, Exterior = 1, Interior = 2;
        private static readonly Color Laser = new Color(1f, 1f, 1f, 1f);
        private static readonly Color SkinLaser = new Color(1f, 0.5f, 0f);

        private const float PollInterval = 0.25f;

        // From the stock Paint mode, set when the tablet is built (TabletItem).
        public PaintTheme[] themes;

        private readonly List<PaintTheme> paints = new List<PaintTheme>();
        private (string title, string content, string action) shown;
        private (TrainCar car, TrainCar pointed, PaintTheme last, int area, int version) shownKey;
        private bool displayValid;
        private float nextPollAt;
        private int stateVersion;
        private TrainCar wearingCar;
        private int wearingArea;
        private PaintTheme cachedWearing;
        private float wearingPollAt = -1f;
        private int area;

        /// <summary>The chosen car's paints, in the page's order.</summary>
        internal IReadOnlyList<PaintTheme> Paints => paints;

        /// <summary>The last paint chosen on the screen or with the wheel, kept from car to car.</summary>
        internal PaintTheme Last { get; private set; }

        /// <summary>All, Exterior, or Interior: what a paint covers on a car with an inside.</summary>
        internal int Area
        {
            get => area;
            set
            {
                if (area == value)
                    return;
                area = value;
                wearingPollAt = -1f;
                stateVersion++;
            }
        }

        public override Color GetLaserBeamColor() => SkinManagerLink.Present ? SkinLaser : Laser;

        protected override void OnChosen()
        {
            paints.Clear();
            paints.AddRange(PaintsFor(Car));
            if (Car.PaintInterior == null) Area = All;
            wearingCar = null;
            cachedWearing = null;
            wearingPollAt = -1f;
            stateVersion++;
        }

        protected override void OnReleased()
        {
            paints.Clear();
            wearingCar = null;
            cachedWearing = null;
            wearingPollAt = -1f;
            stateVersion++;
        }

        protected override void ApplyLast()
        {
            if (Last != null && paints.Contains(Last))
                Paint(Last);
        }

        // The scroll wheel steps through the chosen car's paints, as the radio
        // steps; using the tablet at the car paints it. A wheel turned through
        // dozens of skins should not repaint (and resend) each.
        protected override bool Step(int by)
        {
            if (paints.Count == 0)
                return false;
            var from = Last != null && paints.Contains(Last) ? Last : Wearing;
            var index = from != null ? paints.IndexOf(from) : -1;
            index = index < 0 ? (by > 0 ? 0 : paints.Count - 1) : (index + by + paints.Count) % paints.Count;
            Last = paints[index];
            Redisplay();
            return true;
        }

        /// <summary>The paints that fit a car: Skin Manager's skins for it, or the stock themes it supports.</summary>
        internal List<PaintTheme> PaintsFor(TrainCar car)
        {
            if (car == null)
                return new List<PaintTheme>();
            if (SkinManagerLink.Present)
                return SkinManagerLink.SkinsFor(car.carLivery);
            return themes == null ? new List<PaintTheme>() : themes.Where(t => t != null && Supports(car, t, All)).ToList();
        }

        /// <summary>
        /// Paints the chosen car with <paramref name="theme"/> over <see cref="Area"/>,
        /// as the stock mode or Skin Manager's switcher would. False if it does not fit.
        /// </summary>
        internal bool Paint(PaintTheme theme)
        {
            if (Car == null || theme == null || !InReach)
                return false;
            Last = theme;
            var painted = false;
            if (SkinManagerLink.Present)
            {
                if (paints.Contains(theme))
                {
                    SkinManagerLink.Apply(Car, theme, Area == Exterior ? SkinManagerLink.Exterior
                        : Area == Interior ? SkinManagerLink.Interior : SkinManagerLink.All);
                    painted = true;
                }
            }
            else
            {
                if (Area != Exterior && Car.PaintInterior != null && Car.PaintInterior.IsSupported(theme))
                {
                    Car.PaintInterior.CurrentTheme = theme;
                    painted = true;
                }
                if (Area != Interior && Car.PaintExterior != null && Car.PaintExterior.IsSupported(theme))
                {
                    Car.PaintExterior.CurrentTheme = theme;
                    painted = true;
                }
            }
            CommsRadioController.PlayAudioFromRadio(painted ? confirmSound : cancelSound, transform);
            Redisplay();
            return painted;
        }

        private static bool Supports(TrainCar car, PaintTheme theme, int area) =>
            (area != Exterior && car.PaintInterior != null && car.PaintInterior.IsSupported(theme))
            || (area != Interior && car.PaintExterior != null && car.PaintExterior.IsSupported(theme));

        /// <summary>The paint the chosen car wears over <see cref="Area"/> (its outside for All).</summary>
        internal PaintTheme Wearing
        {
            get
            {
                if (Car == null)
                    return null;
                if (SkinManagerLink.Present)
                {
                    var now = Time.unscaledTime;
                    if (wearingCar == Car && wearingArea == area && now < wearingPollAt)
                        return cachedWearing;
                    var (exterior, interior) = SkinManagerLink.Wearing(Car);
                    var name = area == Interior ? interior : exterior;
                    PaintTheme match = null;
                    for (var i = 0; i < paints.Count; i++)
                        if (paints[i].name == name)
                        {
                            match = paints[i];
                            break;
                        }
                    wearingCar = Car;
                    wearingArea = area;
                    cachedWearing = match;
                    wearingPollAt = now + PollInterval;
                    return match;
                }
                var paint = area == Interior ? Car.PaintInterior : Car.PaintExterior;
                return paint != null ? paint.CurrentTheme : null;
            }
        }

        internal static string NameOf(PaintTheme theme)
        {
            if (theme == null)
                return "";
            var name = theme.LocalizedName;
            return string.IsNullOrEmpty(name) || name.StartsWith("[") ? (string.IsNullOrEmpty(theme.AssetName) ? theme.name : theme.AssetName) : name;
        }

        protected override void Redisplay()
        {
            shown = default;
            displayValid = false;
            wearingPollAt = -1f;
            stateVersion++;
        }

        protected override void ShowDisplay()
        {
            if (display == null)
                return;
            var key = (Car, Pointed, Last, area, stateVersion);
            var now = Time.unscaledTime;
            var pollDue = now >= nextPollAt;
            if (displayValid && key == shownKey && !pollDue)
                return;
            shownKey = key;
            displayValid = true;
            if (pollDue && Car != null)
                wearingPollAt = -1f;
            nextPollAt = now + PollInterval;
            string content;
            if (Car == null)
                content = "Point at a car";
            else
            {
                var wearing = Wearing;
                content = Last != null && Last != wearing && paints.Contains(Last)
                    ? $"{Car.ID}: {NameOf(wearing)} > {NameOf(Last)}"
                    : $"{Car.ID}: {NameOf(wearing)}";
            }
            var action = ActionWord(Last != null && paints.Contains(Last));
            var next = (CommsRadioLocalization.MODE_PAINTJOB, content, action);
            if (next == shown)
                return;
            shown = next;
            display.SetDisplay(next.Item1, content, action);
        }
    }
}
