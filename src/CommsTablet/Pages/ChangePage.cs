using System.Collections.Generic;
using System.Linq;
using CommsTablet.Tablet;
using DV;

namespace CommsTablet.Pages
{
    /// <summary>
    /// DV Car Changer's modifications, car first like Paint and Wheels: use
    /// the tablet at a car Car Changer has modifications for, and the app
    /// lists them by name, those on lit; a tile switches one on or off by Car
    /// Changer's own rules (<see cref="TabletChangeMode"/>). Shown with Car
    /// Changer 1.5.4 where the player's radio has its mode, in single player
    /// and on a Multiplayer host; hidden on a Multiplayer client, where Car
    /// Changer's changes would be the client's alone and unsaved.
    /// </summary>
    internal sealed class ChangePage : TabletPage
    {
        private readonly List<CarChangerLink.Modification> listed = new List<CarChangerLink.Modification>();
        private TrainCar remembered;

        internal ChangePage(TabletRadio radio) : base(radio) { }

        internal override string Name => "CHANGE";

        internal override ICommsRadioMode Mode => Radio.Change;

        private TabletChangeMode Change => Radio.Change;

        internal override bool Available =>
            Radio.Change != null && !Session.IsMultiplayerClient && !CarChangerOwnApp && CarChangerLink.Present
            && Radios.Allow(r => (GameMembers.RadioModes?.Invoke(r) ?? new List<ICommsRadioMode>())
                .FirstOrDefault(m => m != null && m.GetType() == CarChangerLink.RadioMode));

 internal static bool CarChangerOwnApp => StepsAsideFor(Guests.GuestRegistry.All);

        /// <summary>Whether, among <paramref name="guests"/>, Car Changer's own app is bound and working.</summary>
        internal static bool StepsAsideFor(IEnumerable<Guests.GuestApp> guests) =>
            guests.Any(g => g.Assembly == CarChangerLink.GameAssembly && !g.Failed);

        internal override string Hint => Change?.Car == null ? null
            : Change.Reason ?? (Session.IsMultiplayerHost ? "Car Changer's changes show for you only" : "Press a change to switch it on or off");

        internal override string EmptyText => Change?.Car != null
            ? $"Car Changer has no changes for {Change.Car.ID}.\nUse the tablet at another car."
            : "Aim the tablet at a car with Car Changer modifications and use it to see them.";

        /// <summary>The tile the scroll wheel last stepped to.</summary>
        internal override int Selected => Change?.Last == null ? -1 : listed.IndexOf(Change.Last);

        /// <summary>Lit: the changes on the car, any number of them.</summary>
        internal override bool IsOn(int tile) => tile >= 0 && tile < listed.Count && Change != null && Change.IsOn(listed[tile]);

        internal override void Refresh()
        {
            tiles.Clear();
            listed.Clear();
            if (Change?.Car == null)
                return;
            foreach (var modification in Change.Modifications)
            {
                listed.Add(modification);
                var state = Change.Pending == modification ? "SWAP?" : Change.IsOn(modification) ? "ON" : null;
                tiles.Add(new Tile(state == null ? modification.Name : $"{modification.Name}\n{state}", null));
            }
        }

        internal override void Press(int tile)
        {
            if (Change?.Car == null || tile < 0 || tile >= listed.Count)
                return;
            Notice = Change.Toggle(listed[tile]) ? null : Change.Reason;
        }

        internal override void Capture() => remembered = Change?.Car;

        /// <summary>Taken up again near the car it had chosen, the tablet chooses it again.</summary>
        internal override void Reapply()
        {
            var player = PlayerManager.PlayerTransform;
            if (remembered != null && remembered.gameObject.activeInHierarchy && Change != null && player != null
                && (remembered.transform.position - player.position).sqrMagnitude < TabletCarMode.SignalRange * TabletCarMode.SignalRange)
                Change.Choose(remembered);
            remembered = null;
        }
    }
}
