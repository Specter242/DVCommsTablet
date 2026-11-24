using System.Linq;
using CommsTablet.Map;
using CommsTablet.Tablet;
using DV;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// The Track Ahead app (decision 21): the grades, limits, switches, and
    /// waypoints ahead of the player's train, drawn by <see cref="TabletAhead"/>
    /// on the screen's layer for pages that draw themselves. It only reads.
    /// </summary>
    internal sealed class AheadPage : TabletPage
    {
        internal AheadPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "TRACK AHEAD";

        internal override string Label => "Track Ahead";

        internal override ICommsRadioMode Mode => null;

        internal override bool Available => WorldStreamingInit.IsLoaded && !UnloadWatcher.isUnloading && RailTrackRegistryBase.Instance != null;

        internal override int Selected => -1;

        internal override bool DrawsItself => true;

        internal override void Draw() => TabletAhead.Draw(this);

        /// <summary>Whether the Switches map is there to pick a destination on.</summary>
        internal bool MapReady => Radio.Pages.OfType<MapPage>().Any(p => p.Available);

        /// <summary>Opens the Switches map to pick a destination; its ROUTE brings the player back here.</summary>
        internal void PickDestination()
        {
            // The home screen's Map app, so the player learns where the map is.
            var map = Radio.Pages.OfType<MapPage>().Where(p => p.Available).OrderByDescending(p => p.OwnApp).FirstOrDefault();
            if (map == null)
                return;
            TabletMap.PickForAhead();
            Radio.Show(map);
        }

        internal override string Status => TabletAhead.Status;

        /// <summary>Where the switch sounds play when SET SWITCHES throws, as for the map's.</summary>
        internal Transform Where => Radio.Where;

        internal override void Refresh() { }

        internal override void Press(int tile) { }

        internal override void Capture() { }

        internal override void Reapply() { }
    }
}
