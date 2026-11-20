using System;
using System.Collections.Generic;

namespace CommsTablet.Core.Ahead
{
    /// <summary>
    /// How long the front of a train takes to reach points ahead, if it runs
    /// at track speed (decision 21, its first two steps): it accelerates and
    /// brakes at set rates, brakes in time for each lower limit, and speeds up
    /// only once its rear has cleared a limit. The train's own pulling power
    /// and the grades are not in this yet.
    /// </summary>
    public sealed class RunEstimate
    {
        public const double DefaultAcceleration = 0.2, DefaultBraking = 0.35;

        /// <summary>Spacing of the points the run is worked out at, in meters.</summary>
        private const double Step = 10;

        /// <summary>The least speed a stretch is timed at, so a stop on the way cannot take forever.</summary>
        private const double SlowestTimed = 0.5;

        private readonly double[] distances;
        private readonly double[] seconds;
        private readonly double[] speeds;

        private RunEstimate(double[] distances, double[] speeds, double[] seconds)
        {
            this.distances = distances;
            this.speeds = speeds;
            this.seconds = seconds;
        }

        /// <summary>Works out the run of a train whose front is at <paramref name="nose"/> on the profile's path, going at <paramref name="speedNow"/> m/s.</summary>
        public static RunEstimate Compute(AheadProfile profile, double nose, double trainLength, double speedNow,
            double acceleration = DefaultAcceleration, double braking = DefaultBraking)
        {
            var end = profile.Path.Length;
            var points = new List<double>();
            var steps = Math.Max(2, (int)Math.Ceiling((end - nose) / Step) + 1);
            for (var i = 0; i < steps; i++)
                points.Add(Math.Min(end, nose + i * Step));
            // Each stop on the way is a point of its own, so the train is at a stand exactly there.
            var stops = new List<double>();
            foreach (var stop in profile.Path.Stops)
            {
                if (stop.Distance > nose && stop.Distance < end)
                {
                    points.Add(stop.Distance);
                    stops.Add(stop.Distance);
                }
            }
            points.Sort();
            var d = points.ToArray();
            var count = d.Length;
            var v = new double[count];
            for (var i = 0; i < count; i++)
            {
                // The whole train is under a limit while any of it is: from its rear to its front.
                v[i] = profile.LowestKmh(Math.Max(0, d[i] - trainLength), d[i]) / 3.6;
            }
            // The track ending is a stop, as is a route's end; a path that merely reaches its length runs on past it.
            if (profile.Path.End != AheadEnd.Horizon)
                v[count - 1] = 0;
            foreach (var stop in stops)
                v[Array.BinarySearch(d, stop)] = 0;
            for (var i = count - 2; i >= 0; i--)
                v[i] = Math.Min(v[i], Math.Sqrt(v[i + 1] * v[i + 1] + 2 * braking * (d[i + 1] - d[i])));
            v[0] = Math.Max(0, speedNow);
            var t = new double[count];
            for (var i = 1; i < count; i++)
            {
                var gap = d[i] - d[i - 1];
                v[i] = Math.Min(v[i], Math.Sqrt(v[i - 1] * v[i - 1] + 2 * acceleration * gap));
                t[i] = t[i - 1] + (gap > 0 ? gap / Math.Max(SlowestTimed, (v[i] + v[i - 1]) / 2) : 0);
            }
            return new RunEstimate(d, v, t);
        }

        /// <summary>Seconds until the front of the train reaches a distance along the path.</summary>
        public double SecondsTo(double distance) => Read(seconds, distance);

        /// <summary>The train's speed there, in m/s.</summary>
        public double SpeedAt(double distance) => Read(speeds, distance);

        private double Read(double[] values, double distance)
        {
            var i = Array.BinarySearch(distances, distance);
            if (i >= 0)
                return values[i];
            i = ~i;
            if (i <= 0)
                return values[0];
            if (i >= distances.Length)
                return values[values.Length - 1];
            var t = (distance - distances[i - 1]) / (distances[i] - distances[i - 1]);
            return values[i - 1] + t * (values[i] - values[i - 1]);
        }
    }

    /// <summary>The kinds of thing the app lists ahead.</summary>
    public enum WaypointKind : byte
    {
        Switch,
        LowerLimit,
        Grade,
        Yard,
        TrackEnd,
        Reversal,
        Destination,
    }

    /// <summary>Something ahead worth naming, at a distance along the path.</summary>
    public readonly struct AheadWaypoint
    {
        public readonly WaypointKind Kind;
        public readonly double Distance;

        /// <summary>The switch, for <see cref="WaypointKind.Switch"/>; its index among the path's, else -1.</summary>
        public readonly int SwitchIndex;

        /// <summary>The limit, in km/h, for <see cref="WaypointKind.LowerLimit"/>.</summary>
        public readonly int Kmh;

        /// <summary>The grade in percent and its length, for <see cref="WaypointKind.Grade"/>.</summary>
        public readonly double Percent, Length;

        /// <summary>The yard, for <see cref="WaypointKind.Yard"/>.</summary>
        public readonly string Yard;

        public AheadWaypoint(WaypointKind kind, double distance, int switchIndex = -1, int kmh = 0, double percent = 0, double length = 0, string yard = null)
        {
            Kind = kind;
            Distance = distance;
            SwitchIndex = switchIndex;
            Kmh = kmh;
            Percent = percent;
            Length = length;
            Yard = yard;
        }
    }

    /// <summary>Picks what to list ahead of the train: the nearest of each kind, nearest first.</summary>
    public static class AheadWaypoints
    {
        /// <summary>A grade worth calling out, and how long it must last.</summary>
        public const double SteepPercent = 1.5, SteepLength = 150;

        /// <summary>
        /// The waypoints to give the rows there are, in order of distance: first
        /// what the driver must act on (a route's end, its next reversal, a switch
        /// to throw, as <paramref name="urgent"/> says), then the nearest of the rest.
        /// </summary>
        public static List<AheadWaypoint> Shortlist(IReadOnlyList<AheadWaypoint> all, int rows, Func<AheadWaypoint, bool> urgent)
        {
            var kept = new List<AheadWaypoint>();
            foreach (var w in all)
            {
                if (kept.Count < rows && urgent(w))
                    kept.Add(w);
            }
            foreach (var w in all)
            {
                if (kept.Count < rows && !urgent(w))
                    kept.Add(w);
            }
            kept.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return kept;
        }

        /// <param name="profile">The way ahead.</param>
        /// <param name="nose">The front of the train, along the path.</param>
        /// <param name="shapes">The tracks, for their yards.</param>
        /// <param name="notable">Which switch to name besides the next one (one set off the main line, say).</param>
        public static List<AheadWaypoint> Pick(AheadProfile profile, double nose, Func<int, TrackShape> shapes, Func<AheadSwitch, bool> notable)
        {
            var found = new List<AheadWaypoint>();
            var path = profile.Path;

            var next = -1;
            var marked = -1;
            for (var i = 0; i < path.Switches.Count; i++)
            {
                var s = path.Switches[i];
                if (s.Distance <= nose || s.Meeting != SwitchMeeting.Facing)
                    continue;
                if (next < 0)
                    next = i;
                if (notable(s))
                {
                    marked = i;
                    break;
                }
            }
            if (next >= 0)
                found.Add(new AheadWaypoint(WaypointKind.Switch, path.Switches[next].Distance, next));
            if (marked >= 0 && marked != next)
                found.Add(new AheadWaypoint(WaypointKind.Switch, path.Switches[marked].Distance, marked));

            var now = profile.KmhAt(nose);
            foreach (var run in profile.Limits)
            {
                if (run.From > nose && run.Kmh < now)
                {
                    found.Add(new AheadWaypoint(WaypointKind.LowerLimit, run.From, kmh: run.Kmh));
                    break;
                }
            }

            foreach (var run in profile.Grades)
            {
                if (run.From > nose && Math.Abs(run.Percent) >= SteepPercent && run.Length >= SteepLength)
                {
                    found.Add(new AheadWaypoint(WaypointKind.Grade, run.From, percent: run.Percent, length: run.Length));
                    break;
                }
            }

            var yardNow = shapes(path.LegAt(nose).Track).Yard;
            foreach (var leg in path.Legs)
            {
                if (leg.Start <= nose)
                    continue;
                var yard = shapes(leg.Track).Yard;
                if (yard != null && yard != yardNow)
                {
                    found.Add(new AheadWaypoint(WaypointKind.Yard, leg.Start, yard: yard));
                    break;
                }
            }

            if (path.End == AheadEnd.DeadEnd)
                found.Add(new AheadWaypoint(WaypointKind.TrackEnd, path.Length));
            foreach (var stop in path.Stops)
            {
                if (stop.Kind == AheadStopKind.Reversal && stop.Distance > nose)
                {
                    found.Add(new AheadWaypoint(WaypointKind.Reversal, stop.Distance));
                    break;
                }
            }
            // A route's end is listed even when the part read so far stops short of it.
            if (path.RouteStart > 0 || path.Stops.Count > 0)
                found.Add(new AheadWaypoint(WaypointKind.Destination, path.WholeLength));
            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return found;
        }
    }
}
