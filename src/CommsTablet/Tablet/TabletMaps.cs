using DV;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The game's switch maps (decision 12) as far as the Switches app's map
    /// (<see cref="Map.TabletMap"/>) needs the game's own rules: whether the
    /// level has them, and throwing a switch as the touch maps do. As there,
    /// a switch is thrown from a map only with the Dispatcher license;
    /// Multiplayer sends it as any switch.
    /// </summary>
    internal static class TabletMaps
    {
        private static JunctionGeneratedDataRuntime Data => SingletonBehaviour<LevelInfo>.Instance?.junctionData;

        /// <summary>Whether the level's switch maps are there to show.</summary>
        internal static bool Ready => Data != null && Data.junctionPageDataWorld != null && Data.junctionPageDataWorld.Length > 0
            && Data.junctionPageDataStations != null;

        private static GeneralLicenseType_v2 dispatcher;

        /// <summary>Whether the player holds the Dispatcher license, which the game's touch maps need to throw a switch.</summary>
        internal static bool CanSwitch
        {
            get
            {
                if (dispatcher == null)
                    dispatcher = GeneralLicenseType.Dispatcher1.ToV2();
                // The license manager answers yes for no license at all: a lost lookup must not unlock it.
                return dispatcher != null && SingletonBehaviour<LicenseManager>.Instance != null
                    && SingletonBehaviour<LicenseManager>.Instance.IsGeneralLicenseAcquired(dispatcher);
            }
        }

        /// <summary>
        /// Throws a switch as the game's touch map does (its <c>JunctionSwitcher</c>
        /// thumbpad press: the visual switch, then the junction), with the
        /// switch sound at <paramref name="at"/>. False where switching is not allowed.
        /// </summary>
        internal static bool Flip(Junction junction, UnityEngine.Transform at)
        {
            var manager = SingletonBehaviour<JunctionSwitcherManager>.Instance;
            if (junction == null || manager == null || !manager.IsSwitchingAllowed(junction))
                return false;
            var stand = junction.RemoteControllable();
            if (stand != null && stand.VisualSwitch != null)
                stand.VisualSwitch.Switch();
            else
                junction.Switch(Junction.SwitchMode.REGULAR);
            if (manager.switchSound != null)
                CommsRadioController.PlayAudioFromRadio(manager.switchSound, at);
            return true;
        }
    }
}
