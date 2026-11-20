using System;
using System.Collections.Generic;

namespace CommsTablet.Core.Tracks
{
    /// <summary>
    /// Speed limits from curvature, by the table Derail Valley's sign
    /// placer uses for its speed signs (SignPlacer.GetMaxSpeedForRadius),
    /// so the engineer keeps to the limits the player sees posted.
    /// </summary>
    public static class CurveSpeed
    {
        private static readonly (double Radius, double Kmh)[] Table =
        {
            (50, 10), (70, 20), (95, 30), (130, 40), (170, 50), (230, 60),
            (360, 70), (700, 80), (900, 90), (1200, 100),
        };

        public const double StraightKmh = 120;

        /// <summary>The posted speed for a curve of this radius, km/h.</summary>
        public static double KmhForRadius(double radius)
        {
            foreach (var (r, kmh) in Table)
            {
                if (radius < r)
                    return kmh;
            }
            return StraightKmh;
        }

        /// <summary>
        /// The radius of the circle through three points (infinite for
        /// points in a line), in the horizontal plane.
        /// </summary>
        public static double Radius(MapPoint a, MapPoint b, MapPoint c)
        {
            var ab = a.DistanceTo(b);
            var bc = b.DistanceTo(c);
            var ca = c.DistanceTo(a);
            var cross = (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
            var area2 = Math.Abs(cross);
            if (area2 < 1e-9)
                return double.PositiveInfinity;
            return ab * bc * ca / (2 * area2);
        }

        /// <summary>
        /// A track's limits from points along it (offset and position, in
        /// order): the radius over a chord of about <paramref name="halfChord"/>
        /// meters each side of every point, each limit held over
        /// <paramref name="holdChunk"/> meter chunks at its lowest, as the
        /// signs are, and capped at <paramref name="capKmh"/>.
        /// </summary>
        public static SpeedLimitProfile Profile(IReadOnlyList<double> offsets, IReadOnlyList<MapPoint> points,
            double capKmh = StraightKmh, double halfChord = 12, double holdChunk = 50)
        {
            var n = offsets.Count;
            if (n < 3)
                return Capped(capKmh);
            var length = offsets[n - 1] - offsets[0];
            if (length < 2 * halfChord)
                halfChord = length / 2;
            var sampleOffsets = new List<double>();
            var sampleLimits = new List<double>();
            var lo = 0;
            var hi = 0;
            for (var i = 0; i < n; i++)
            {
                var s = offsets[i];
                while (lo < i && offsets[lo] < s - halfChord)
                    lo++;
                while (hi < n - 1 && offsets[hi] < s + halfChord)
                    hi++;
                var a = lo;
                var c = hi;
                if (a == i && a > 0)
                    a--;
                if (c == i && c < n - 1)
                    c++;
                var kmh = a == i || c == i ? StraightKmh : KmhForRadius(Radius(points[a], points[i], points[c]));
                sampleOffsets.Add(s - offsets[0]);
                sampleLimits.Add(Math.Min(kmh, capKmh) / 3.6);
            }

            // Hold each chunk at its lowest limit, so a limit covers its whole curve.
            var chunkOffsets = new List<double>();
            var chunkLimits = new List<double>();
            var chunkStart = 0.0;
            var chunkMin = double.PositiveInfinity;
            for (var i = 0; i < sampleOffsets.Count; i++)
            {
                if (sampleOffsets[i] >= chunkStart + holdChunk)
                {
                    chunkOffsets.Add(chunkStart);
                    chunkLimits.Add(chunkMin);
                    chunkStart = sampleOffsets[i];
                    chunkMin = double.PositiveInfinity;
                }
                chunkMin = Math.Min(chunkMin, sampleLimits[i]);
            }
            chunkOffsets.Add(chunkStart);
            chunkLimits.Add(chunkMin);
            return SpeedLimitProfile.FromSamples(chunkOffsets, chunkLimits, quantum: 0.5);
        }

        private static SpeedLimitProfile Capped(double capKmh) =>
            new SpeedLimitProfile(new[] { 0.0 }, new[] { Math.Min(capKmh, StraightKmh) / 3.6 });
    }
}
