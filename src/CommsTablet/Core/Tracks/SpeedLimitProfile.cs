using System;
using System.Collections.Generic;

namespace CommsTablet.Core.Tracks
{
    /// <summary>
    /// A track's speed limits, piecewise constant: limit i holds from
    /// offset i up to offset i + 1 (or the track's end), in m/s.
    /// </summary>
    public sealed class SpeedLimitProfile
    {
        public static readonly SpeedLimitProfile Unlimited = new SpeedLimitProfile(new[] { 0.0 }, new[] { double.PositiveInfinity });

        private readonly double[] offsets;
        private readonly double[] speeds;

        public SpeedLimitProfile(double[] offsets, double[] speeds)
        {
            if (offsets == null || speeds == null || offsets.Length == 0 || offsets.Length != speeds.Length)
                throw new ArgumentException("A profile needs matching, nonempty offsets and limits.");
            if (offsets[0] != 0)
                throw new ArgumentException("A profile's first limit starts at offset 0.");
            for (var i = 1; i < offsets.Length; i++)
            {
                if (!(offsets[i] > offsets[i - 1]))
                    throw new ArgumentException("Profile offsets must increase.");
            }
            foreach (var s in speeds)
            {
                if (!(s > 0))
                    throw new ArgumentException("Limits must be positive.");
            }
            this.offsets = (double[])offsets.Clone();
            this.speeds = (double[])speeds.Clone();
        }

        public int Count => offsets.Length;

        public double OffsetAt(int index) => offsets[index];

        public double SpeedAt(int index) => speeds[index];

        public double LimitAt(double offset)
        {
            var i = Array.BinarySearch(offsets, offset);
            if (i < 0)
                i = ~i - 1;
            return speeds[Math.Max(0, i)];
        }

        /// <summary>The lowest limit anywhere between two offsets, in either order.</summary>
        public double MinLimit(double from, double to)
        {
            if (from > to)
            {
                var t = from;
                from = to;
                to = t;
            }
            var min = LimitAt(from);
            for (var i = 0; i < offsets.Length; i++)
            {
                if (offsets[i] > from && offsets[i] < to)
                    min = Math.Min(min, speeds[i]);
            }
            return min;
        }

        /// <summary>
        /// Builds a profile from per-sample limits along a track, merging
        /// runs within <paramref name="quantum"/> m/s of each other into
        /// their lowest, so the profile stays short.
        /// </summary>
        public static SpeedLimitProfile FromSamples(IReadOnlyList<double> sampleOffsets, IReadOnlyList<double> sampleLimits, double quantum = 1.0)
        {
            if (sampleOffsets.Count == 0)
                return Unlimited;
            var offs = new List<double> { 0 };
            var spds = new List<double> { sampleLimits[0] };
            for (var i = 1; i < sampleOffsets.Count; i++)
            {
                var last = spds[spds.Count - 1];
                var s = sampleLimits[i];
                if (Math.Abs(s - last) < quantum || (double.IsPositiveInfinity(s) && double.IsPositiveInfinity(last)))
                {
                    spds[spds.Count - 1] = Math.Min(last, s);
                    continue;
                }
                if (!(sampleOffsets[i] > offs[offs.Count - 1]))
                {
                    spds[spds.Count - 1] = Math.Min(last, s);
                    continue;
                }
                offs.Add(sampleOffsets[i]);
                spds.Add(s);
            }
            return new SpeedLimitProfile(offs.ToArray(), spds.ToArray());
        }
    }
}
