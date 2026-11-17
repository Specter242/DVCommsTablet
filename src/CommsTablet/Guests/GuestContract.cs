namespace CommsTablet
{
 public static class GuestContract
    {
        /// <summary>The oldest guest contract this tablet takes.</summary>
        public const int Min = 1;

        /// <summary>The newest guest contract this tablet takes.</summary>
        public const int Max = 1;

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
