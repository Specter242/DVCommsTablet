using System;
using System.Collections;
using CommsTablet.Core.Booklet;
using DV.Utils;
using UnityEngine;

namespace CommsTablet.Map
{
    /// <summary>
    /// The map's view of the loaded world: its track network and station
    /// booklet, built once per world and let go at unload. They are read a
    /// few seconds after a world loads, ahead of the map (as AI Engineer
    /// reads its own), or else the first time the map is drawn.
    /// </summary>
    internal static class MapWorld
    {
        /// <summary>Seconds after loading before the booklet is read: AI Engineer reads its own at 5, so the two are not read in one frame.</summary>
        private const float PrepareDelay = 7f;

        private static TrackNetwork network;
        private static float nextBuildAttempt;
        private static bool hooked;
        private static Coroutine preparing;

        internal static void Hook()
        {
            if (hooked)
                return;
            hooked = true;
            WorldStreamingInit.LoadingFinished += Loaded;
            UnloadWatcher.UnloadRequested += Forget;
        }

        /// <summary>Whether the network has been built for this world.</summary>
        internal static bool HasNetwork => network != null;

        /// <summary>The loaded world's network, built the first time it is wanted; null while no world is loaded or if it could not be read.</summary>
        internal static TrackNetwork Network
        {
            get
            {
                if (network != null)
                    return network;
                if (UnloadWatcher.isUnloading || !WorldStreamingInit.IsLoaded || RailTrackRegistryBase.Instance == null || Time.unscaledTime < nextBuildAttempt)
                    return null;
                try
                {
                    network = TrackNetwork.Build();
                }
                catch (Exception e)
                {
                    // Not again every frame: once in a while, in case the world was still settling.
                    nextBuildAttempt = Time.unscaledTime + 15f;
                    Main.Mod?.Logger.LogException("Map: the track network could not be read", e);
                    network = null;
                }
                return network;
            }
        }

        /// <summary>The loaded world's station booklet, read the first time it is wanted; null if it could not be.</summary>
        internal static Booklet Booklet => BookletReader.Get(Network);

        private static void Loaded()
        {
            // The game calls this inside its loading coroutine: nothing thrown here may reach it.
            try
            {
                Forget();
                var runner = SingletonBehaviour<CoroutineManager>.Instance;
                if (runner != null)
                    preparing = runner.Run(PrepareLater());
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Map: could not set up for the loaded world", e);
            }
        }

        private static IEnumerator PrepareLater()
        {
            yield return new WaitForSecondsRealtime(PrepareDelay);
            preparing = null;
            if (UnloadWatcher.isUnloading)
                yield break;
            try
            {
                _ = Booklet;
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Map: could not read the station booklet ahead of the map", e);
            }
        }

        /// <summary>Lets the world's network, booklet, and the map's place go: at unload, and before a new world.</summary>
        internal static void Forget()
        {
            if (preparing != null && SingletonBehaviour<CoroutineManager>.Instance != null)
                SingletonBehaviour<CoroutineManager>.Instance.Stop(preparing);
            preparing = null;
            network = null;
            nextBuildAttempt = 0f;
            BookletReader.Forget();
            TabletMap.Forget();
        }
    }
}
