using CommsTablet.Map;
using CommsTablet.Tablet;
using DV;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// The Switches app's MAP tab (decisions 12 and 17): the game's station
    /// booklet and route map as AI Engineer's tablet draws and navigates them
    /// (<see cref="TabletMap"/>), without its orders. With the Dispatcher
    /// license a press on a switch throws it, as on the game's touch maps;
    /// without it the map is to look at, as the map items are. The page
    /// draws its own screen on the screen's map layer.
    /// </summary>
    internal sealed class MapPage : TabletPage
    {
        internal MapPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "MAP";

        internal override ICommsRadioMode Mode => null;

        internal override bool Available => TabletMaps.Ready;

        internal override int Selected => -1;

        internal override bool DrawsItself => true;

        internal override void Draw() => TabletMap.Draw(this);

        internal override Rect PressArea => TabletMap.Plates ? Rect.zero : TabletMap.PressArea;

        internal override void PressAt(Vector2 canvas) => TabletMap.PressAt(this, canvas);

        internal override string Status => TabletMap.Status;

        /// <summary>Where a thrown switch's sound plays from: the tablet.</summary>
        internal Transform Where => Radio.Where;

        internal override void Refresh() { }

        internal override void Press(int tile) { }

        internal override void Capture() { }

        internal override void Reapply() { }
    }
}
