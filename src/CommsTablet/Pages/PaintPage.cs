using System.Linq;
using CommsTablet.Tablet;
using DV;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// The chosen car's paints, each tile a side view of that car in it: the
    /// stock themes, or Skin Manager's skins when it is installed (decision
    /// 7). Car first (<see cref="TabletPaintMode"/>): aim and use the tablet
    /// at a car, then press a paint to paint it. A car with an inside gets
    /// ALL, EXTERIOR, and INTERIOR sections for what a paint covers.
    /// </summary>
    internal sealed class PaintPage : TabletPage
    {
        private static readonly string[] AreaNames = { "ALL", "EXTERIOR", "INTERIOR" };
        private TrainCar remembered;

        internal PaintPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "PAINT";


        internal override ICommsRadioMode Mode => Radio.Paint;

        /// <summary>
        /// As the radio offers Paint: with its cheat rule, or with Skin
        /// Manager, also where Skin Manager's own setting allows reskinning;
        /// hidden where the player's radio has the mode off.
        /// </summary>
        internal override bool Available
        {
            get
            {
                if (Radio.Paint == null)
                    return false;
                if (SkinManagerLink.Present)
                    return (Session.CheatMode || SkinManagerLink.AlwaysAllowed) && Radios.Allow(SkinManagerLink.SwitcherOn);
                return Session.CheatMode && Radios.Allow(r => r.carPaintjobControl);
            }
        }

        private TabletPaintMode Paint => Radio.Paint;

        internal override string[] Sections => Paint?.Car != null && Paint.Car.PaintInterior != null ? AreaNames : null;

        internal override int Section => Paint != null ? Paint.Area : 0;

        internal override void SelectSection(int index)
        {
            Notice = null;
            if (Paint != null && index >= 0 && index < AreaNames.Length)
                Paint.Area = index;
        }

        internal override string Hint => Paint?.Car != null ? "Use at another car to switch" : null;

        internal override string EmptyText => Paint?.Car != null
            ? $"No {(SkinManagerLink.Present ? "skins" : "paints")} fit {Paint.Car.ID}.\nUse the tablet at another car."
            : SkinManagerLink.Present
                ? "Aim the tablet at a car and use it to see its skins."
                : "Aim the tablet at a car and use it to see its paints.";

        internal override int Selected
        {
            get
            {
                var wearing = Paint?.Wearing;
                return wearing == null ? -1 : Paint.Paints.ToList().IndexOf(wearing);
            }
        }

        internal override void Refresh()
        {
            tiles.Clear();
            var car = Paint?.Car;
            if (car == null)
                return;
            foreach (var paint in Paint.Paints)
                tiles.Add(new Tile(TabletPaintMode.NameOf(paint), null, car.carLivery, theme: paint, car: car));
        }

        internal override void Press(int tile)
        {
            if (Paint?.Car == null || tile < 0 || tile >= Paint.Paints.Count)
                return;
            var paint = Paint.Paints[tile];
            if (!Paint.InReach)
            {
                Notice = "Too far; come within 100 m";
                return;
            }
            Notice = Paint.Paint(paint) ? null
                : Paint.Area == TabletPaintMode.Interior ? "Not for this car's inside"
                : Paint.Area == TabletPaintMode.Exterior ? "Not for this car's outside"
                : "Does not fit this car";
        }

        internal override void Capture() => remembered = Paint?.Car;

        /// <summary>Taken up again near the car it had chosen, the tablet chooses it again.</summary>
        internal override void Reapply()
        {
            var player = PlayerManager.PlayerTransform;
            if (remembered != null && remembered.gameObject.activeInHierarchy && Paint != null && player != null
                && (remembered.transform.position - player.position).sqrMagnitude < 100f * 100f)
                Paint.Choose(remembered);
            remembered = null;
        }
    }
}
