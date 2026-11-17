using System.Collections.Generic;
using System.Linq;
using CommsTablet.Tablet;
using DV;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// Rearranged's wheel arrangements (decision 13), car first like Paint:
    /// use the tablet at an S282 or S060, and the app lists the arrangements
    /// it can take, each a wheel diagram; a tile asks the loco's mod for it.
    /// The S282's fifty-odd split into RIGID and ARTICULATED. Shown when
    /// either Rearranged mod is on and its radio mode is not turned off.
    /// </summary>
    internal sealed class RearrangePage : TabletPage
    {
        private static readonly string[] S282Sections = { "ALL", "RIGID", "ARTICULATED" };
        private readonly List<RearrangedLink.Arrangement> listed = new List<RearrangedLink.Arrangement>();
        private TrainCar remembered;

        internal RearrangePage(TabletRadio radio) : base(radio) { }

        internal override string Name => "REARRANGE";

        internal override string Tab => "WHEELS";


        internal override ICommsRadioMode Mode => Radio.Rearrange;

        private TabletRearrangeMode Rearrange => Radio.Rearrange;

        internal override bool Available =>
            Radio.Rearrange != null && RearrangedLink.Present
            && Radios.Allow(r => (GameMembers.RadioModes?.Invoke(r) ?? new List<ICommsRadioMode>())
                .FirstOrDefault(m => m != null && m.GetType().Namespace != null && m.GetType().Namespace.StartsWith("Rearranged")));

        private bool S282Chosen => Rearrange?.Car != null && RearrangedLink.IsS282(Rearrange.Car);

        internal override string[] Sections => S282Chosen ? S282Sections : null;

        internal override string Hint => Rearrange?.Car == null ? null : Rearrange.Reason ?? "Use at another loco to switch";

        internal override string EmptyText => Rearrange?.Car != null
            ? $"Rearranged has no arrangements for {Rearrange.Car.ID}.\nUse the tablet at another loco."
            : "Aim the tablet at an S282 or S060 and use it to see its wheel arrangements.";

        internal override int Selected
        {
            get
            {
                var wearing = Rearrange?.Wearing;
                return wearing == null ? -1 : listed.IndexOf(wearing);
            }
        }

        internal override void Refresh()
        {
            tiles.Clear();
            listed.Clear();
            var car = Rearrange?.Car;
            if (car == null)
                return;
            var all = Rearrange.Arrangements;
            var shown = !S282Chosen || section == 0 ? all
                : all.Where(a => a.Articulated == (section == 2));
            foreach (var arrangement in shown)
            {
                listed.Add(arrangement);
                var shay = arrangement.Name.IndexOf("shay", System.StringComparison.OrdinalIgnoreCase) >= 0;
                tiles.Add(new Tile(arrangement.Name, TabletArt.WheelDiagram(arrangement.Wheels, shay)));
            }
        }

        internal override void SelectSection(int index)
        {
            if (index >= 0 && index < S282Sections.Length)
                base.SelectSection(index);
        }

        internal override void Press(int tile)
        {
            if (Rearrange?.Car == null || tile < 0 || tile >= listed.Count)
                return;
            if (!Rearrange.InReach)
            {
                Notice = "Too far; come within 100 m";
                return;
            }
            Notice = Rearrange.Apply(listed[tile]) ? null : Rearrange.Reason;
        }

        internal override void Capture() => remembered = Rearrange?.Car;

        /// <summary>Taken up again near the loco it had chosen, the tablet chooses it again.</summary>
        internal override void Reapply()
        {
            var player = PlayerManager.PlayerTransform;
            if (remembered != null && remembered.gameObject.activeInHierarchy && Rearrange != null && player != null
                && (remembered.transform.position - player.position).sqrMagnitude < TabletCarMode.SignalRange * TabletCarMode.SignalRange)
                Rearrange.Choose(remembered);
            remembered = null;
        }
    }
}
