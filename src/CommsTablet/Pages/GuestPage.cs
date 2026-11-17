using CommsTablet.Guests;
using CommsTablet.Tablet;
using DV;

namespace CommsTablet.Pages
{
 internal sealed class GuestPage : TabletPage
    {
        internal readonly GuestApp Guest;

        internal GuestPage(TabletRadio radio, GuestApp guest) : base(radio) => Guest = guest;

        internal override string Name => Guest.Name;

        internal override string Label => Guest.Name;

        internal override ICommsRadioMode Mode => null;

        internal override bool Available => Guest.Available;

        internal override int Selected => -1;

        internal override string EmptyText => "";

        internal override void Refresh() { }

        internal override void Press(int tile) { }

        internal override void Capture() { }

        internal override void Reapply() { }
    }
}
