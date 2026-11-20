namespace CommsTablet
{
 public static class GuestContract
    {
        /// <summary>The oldest guest contract this tablet takes.</summary>
        public const int Min = 1;

        /// <summary>
        /// The newest guest contract this tablet takes. Contract 2 (Comms
        /// Tablet 0.1.3) adds the optional car-first members: <c>Fits</c>,
        /// <c>Chosen</c>, and <c>BeamColor</c>. Contract 3 (Comms Tablet
        /// 0.1.4) adds the Settings app's one-time actions: a mod's own
        /// <c>&lt;assembly name&gt;.CommsTabletSettings</c> with
        /// <c>Actions</c>, <c>Act</c>, and optional <c>ActionHelp</c>, read
        /// whatever contract its app declares (an app's members are
        /// unchanged), so <c>Max &gt;= 3</c> says this tablet draws them.
        /// </summary>
        public const int Max = 3;

        /// <summary>
        /// Whether the tablet has bound the guest with this <c>Id</c>: its
        /// contract taken and every member bound. A merged guest's app is on
        /// the tablet's home screen and its own tablet is retired (not given,
        /// not sold; a saved one loads as the Comms Tablet). It stays merged
        /// for the session even if its app later throws and is hidden. False
        /// if the Comms Tablet did not load or cannot build its own tablet, so
        /// a guest never retires its tablet without one to take its place.
        /// </summary>
        public static bool Merged(string id) =>
            Main.Mod != null && Guests.GuestRegistry.IsMerged(id) && Tablet.TabletItem.Template != null;
    }
}
