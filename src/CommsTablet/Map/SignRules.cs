using System;
using System.Runtime.CompilerServices;
using CommsTablet.Core.Ahead;
using UnityEngine;

namespace CommsTablet.Map
{
    /// <summary>
    /// The game's side of the posted limits: which tracks carry signs, and a
    /// copy of a track's curve for <see cref="SignLimits"/> to work from off
    /// the main thread. Signs themselves stand only near the player, so they
    /// cannot be read from the world. If a game update breaks any of this, the
    /// app keeps its limits from the track's curves (<see cref="TrackShape"/>).
    /// </summary>
    internal static class SignRules
    {
        private const double LeastSignedLength = 100;

        private static bool warned;

        /// <summary>Whether the game puts signs along this track: not yards, not its marked or short tracks.</summary>
        internal static bool Signed(RailTrack track, double length) =>
            length >= LeastSignedLength && !track.name.StartsWith("[Y]", StringComparison.Ordinal) && !track.name.StartsWith("[#]", StringComparison.Ordinal);

        /// <summary>A copy of the track's curve, or null if it could not be read. Main thread only.</summary>
        internal static SignCurve Copy(RailTrack track)
        {
            try
            {
                return ReadCurve(track);
            }
            catch (Exception e)
            {
                Warn(e);
                return null;
            }
        }

        /// <summary>Says once that the limits could not be worked out.</summary>
        internal static void Warn(Exception e)
        {
            if (!warned)
                Main.Mod?.Logger.LogException("Track Ahead: the game's sign rules could not be followed; limits come from the curves", e);
            warned = true;
        }

        // Kept apart so that a curve type a game update changed fails here, inside the catch above.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static SignCurve ReadCurve(RailTrack track)
        {
            var curve = track.curve;
            var count = curve.pointCount;
            var knots = new float[count * SignCurve.FloatsPerKnot];
            var at = 0;
            for (var i = 0; i < count; i++)
            {
                var knot = curve[i];
                at = Put(knots, at, knot.position);
                at = Put(knots, at, knot.globalHandle1);
                at = Put(knots, at, knot.globalHandle2);
            }
            return new SignCurve(knots, curve.close, curve.resolution);
        }

        private static int Put(float[] knots, int at, Vector3 v)
        {
            knots[at] = v.x;
            knots[at + 1] = v.y;
            knots[at + 2] = v.z;
            return at + 3;
        }
    }
}
