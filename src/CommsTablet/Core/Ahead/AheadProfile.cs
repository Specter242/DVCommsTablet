using System;
using System.Collections.Generic;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Ahead
{
    /// <summary>A stretch of steady grade: percent, positive climbing in the direction it is read.</summary>
    public readonly struct GradeRun
    {
        public readonly double From, To, Percent;

        public GradeRun(double from, double to, double percent)
        {
            From = from;
            To = to;
            Percent = percent;
        }

        public double Length => To - From;
    }

    /// <summary>A stretch under one speed limit.</summary>
    public readonly struct LimitRun
    {
        public readonly double From, To;
        public readonly int Kmh;

        public LimitRun(double from, double to, int kmh)
        {
            From = from;
            To = to;
            Kmh = kmh;
        }
    }

    /// <summary>A stretch of a track under one posted limit: offsets in the order a train meets them.</summary>
    public readonly struct PostedSegment
    {
        public readonly double From, To, Kmh;

        public PostedSegment(double from, double to, double kmh)
        {
            From = from;
            To = to;
            Kmh = kmh;
        }

        public double Length => Math.Abs(To - From);
    }

    /// <summary>
    /// A track's posted limits, each way: the stretches as the game's rules
    /// give them, each list in the order a train meets them, and the limits a
    /// train runs under, by offset from the track's Start. Once made it does
    /// not change, so it may be made on one thread and read on another.
    /// </summary>
    public sealed class PostedLimits
    {
        /// <summary>The game's sign placer: a stretch this short before a lower limit takes that limit.</summary>
        private const double ShortBeforeLower = 200;

        public PostedLimits(IReadOnlyList<PostedSegment> forward, IReadOnlyList<PostedSegment> backward)
        {
            Forward = forward;
            Backward = backward;
            ForwardLimits = Profile(forward);
            BackwardLimits = Profile(backward);
        }

        public IReadOnlyList<PostedSegment> Forward { get; }

        public IReadOnlyList<PostedSegment> Backward { get; }

        public SpeedLimitProfile ForwardLimits { get; }

        public SpeedLimitProfile BackwardLimits { get; }

        private static SpeedLimitProfile Profile(IReadOnlyList<PostedSegment> segments)
        {
            var kmh = new double[segments.Count];
            for (var i = 0; i < kmh.Length; i++)
                kmh[i] = segments[i].Kmh;
            for (var i = 0; i + 1 < kmh.Length; i++)
            {
                if (kmh[i] > kmh[i + 1] && segments[i].Length <= ShortBeforeLower)
                    kmh[i] = kmh[i + 1];
            }
            var order = new int[kmh.Length];
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => Math.Min(segments[a].From, segments[a].To).CompareTo(Math.Min(segments[b].From, segments[b].To)));
            var offsets = new List<double>();
            var speeds = new List<double>();
            foreach (var i in order)
            {
                var start = offsets.Count == 0 ? 0 : Math.Min(segments[i].From, segments[i].To);
                var speed = kmh[i] / 3.6;
                if (offsets.Count > 0 && start <= offsets[offsets.Count - 1])
                    speeds[speeds.Count - 1] = Math.Min(speeds[speeds.Count - 1], speed);
                else if (speeds.Count == 0 || speeds[speeds.Count - 1] != speed)
                {
                    offsets.Add(start);
                    speeds.Add(speed);
                }
            }
            return new SpeedLimitProfile(offsets.ToArray(), speeds.ToArray());
        }
    }

    /// <summary>
    /// One track as the app reads it: its heights, its steady grades, its
    /// speed limits, and its yard. Grades and limits belong to the track and
    /// not to the train's place, so they stay put as the train moves. Its
    /// limits start out estimated from its curves, and are replaced by the
    /// posted ones, each way, once those have been worked out.
    /// </summary>
    public sealed class TrackShape
    {
        /// <summary>How far a height may stray from a straight grade before the grade counts as changed, in meters.</summary>
        private const double HeightTolerance = 0.4;

        /// <summary>The stretch the game's sign placer holds a limit over at the least.</summary>
        private const double LimitHold = 300;

        /// <summary>The game's sign placer: yard and short tracks after a switch.</summary>
        private const double YardKmh = 50;

        private PostedLimits posted;

        private readonly double[] offsets;
        private readonly double[] heights;
        private readonly GradeRun[] grades;

        private TrackShape(double length, double[] offsets, double[] heights, GradeRun[] grades, SpeedLimitProfile limits, string yard)
        {
            Length = length;
            this.offsets = offsets;
            this.heights = heights;
            this.grades = grades;
            Limits = limits;
            Yard = yard;
        }

        public double Length { get; }

        public SpeedLimitProfile Limits { get; }

        /// <summary>The yard the track belongs to, or null for a main line.</summary>
        public string Yard { get; }

        /// <summary>Its grades from Start to End.</summary>
        public IReadOnlyList<GradeRun> Grades => grades;

        /// <summary>Whether the posted limits are in.</summary>
        public bool Posted => posted != null;

        /// <summary>The limits a train meets running this way along the track, by offset from Start.</summary>
        public SpeedLimitProfile LimitsFor(bool forward) => posted == null ? Limits : forward ? posted.ForwardLimits : posted.BackwardLimits;

        /// <summary>Takes the posted limits for each way.</summary>
        public void SetPosted(PostedLimits limits) => posted = limits;

        /// <summary>A track from points along it: offsets increasing from 0 to its length, with each point's height and place.</summary>
        public static TrackShape From(double[] offsets, double[] heights, MapPoint[] points, string yard)
        {
            if (offsets == null || heights == null || points == null || offsets.Length < 2 || heights.Length != offsets.Length || points.Length != offsets.Length)
                throw new ArgumentException("A track shape needs at least two points, each with an offset, a height, and a place.");
            var cap = yard != null ? YardKmh : CurveSpeed.StraightKmh;
            var limits = CurveSpeed.Profile(offsets, points, cap, holdChunk: LimitHold);
            return new TrackShape(offsets[offsets.Length - 1], offsets, heights, SteadyGrades(offsets, heights), limits, yard);
        }

        public double HeightAt(double offset)
        {
            var i = Array.BinarySearch(offsets, offset);
            if (i >= 0)
                return heights[i];
            i = ~i;
            if (i <= 0)
                return heights[0];
            if (i >= offsets.Length)
                return heights[heights.Length - 1];
            var t = (offset - offsets[i - 1]) / (offsets[i] - offsets[i - 1]);
            return heights[i - 1] + t * (heights[i] - heights[i - 1]);
        }

        // The height line simplified (Douglas-Peucker), each of its pieces a steady grade.
        private static GradeRun[] SteadyGrades(double[] d, double[] h)
        {
            var keep = new bool[d.Length];
            keep[0] = keep[d.Length - 1] = true;
            var stack = new Stack<(int A, int B)>();
            stack.Push((0, d.Length - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                var slope = (h[b] - h[a]) / Math.Max(1e-6, d[b] - d[a]);
                double worst = 0;
                var at = -1;
                for (var i = a + 1; i < b; i++)
                {
                    var off = Math.Abs(h[a] + slope * (d[i] - d[a]) - h[i]);
                    if (off > worst)
                    {
                        worst = off;
                        at = i;
                    }
                }
                if (at < 0 || worst <= HeightTolerance)
                    continue;
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
            var runs = new List<GradeRun>();
            var last = 0;
            for (var i = 1; i < d.Length; i++)
            {
                if (!keep[i])
                    continue;
                runs.Add(new GradeRun(d[last], d[i], (h[i] - h[last]) / Math.Max(1e-6, d[i] - d[last]) * 100));
                last = i;
            }
            return runs.ToArray();
        }
    }

    /// <summary>
    /// What lies along a path: its heights, its grades in the direction of
    /// travel, and its speed limits, by distance along the path.
    /// </summary>
    public sealed class AheadProfile
    {
        /// <summary>The game's sign placer: a track's last stretch before a switch, and before the end of the track.</summary>
        private const double ApproachLength = 250;
        private const int BeforeSwitchKmh = 60, BeforeEndKmh = 20;

        /// <summary>Grades closer than this read as one, and runs shorter than <see cref="ShortRun"/> join a neighbor, so short switch tracks do not chop the line up.</summary>
        private const double SameGrade = 0.25, ShortRun = 40;

        private readonly Func<int, TrackShape> shapes;
        private readonly List<GradeRun> grades = new List<GradeRun>();
        private readonly List<LimitRun> limits = new List<LimitRun>();

        private AheadProfile(AheadPath path, Func<int, TrackShape> shapes)
        {
            Path = path;
            this.shapes = shapes;
        }

        public AheadPath Path { get; }

        public IReadOnlyList<GradeRun> Grades => grades;

        public IReadOnlyList<LimitRun> Limits => limits;

        public static AheadProfile Build(AheadPath path, Func<int, TrackShape> shapes)
        {
            var profile = new AheadProfile(path, shapes);
            for (var i = 0; i < path.Legs.Count; i++)
            {
                var leg = path.Legs[i];
                if (leg.Length <= 0)
                    continue;
                var shape = shapes(leg.Track);
                profile.AddGrades(leg, shape);
                var dead = i == path.Legs.Count - 1 && path.End == AheadEnd.DeadEnd;
                profile.AddLimits(leg, shape, dead ? BeforeEndKmh : leg.EndsAtSwitch ? BeforeSwitchKmh : int.MaxValue);
            }
            return profile;
        }

        public double HeightAt(double distance)
        {
            var leg = Path.LegAt(distance);
            return shapes(leg.Track).HeightAt(leg.OffsetAt(distance));
        }

        /// <summary>The grade at a distance, in percent, positive climbing.</summary>
        public double GradeAt(double distance)
        {
            var i = Find(grades.Count, n => grades[n].From, distance);
            return i < 0 ? 0 : grades[i].Percent;
        }

        /// <summary>The limit at a distance, in km/h; 0 where there is no track.</summary>
        public int KmhAt(double distance)
        {
            var i = Find(limits.Count, n => limits[n].From, distance);
            return i < 0 ? 0 : limits[i].Kmh;
        }

        /// <summary>The lowest limit anywhere between two distances, in km/h.</summary>
        public int LowestKmh(double from, double to)
        {
            var i = Math.Max(0, Find(limits.Count, n => limits[n].From, from));
            var lowest = int.MaxValue;
            for (; i < limits.Count && limits[i].From <= to; i++)
                lowest = Math.Min(lowest, limits[i].Kmh);
            return lowest == int.MaxValue ? 0 : lowest;
        }

        private static int Find(int count, Func<int, double> start, double distance)
        {
            int lo = 0, hi = count - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (start(mid) <= distance)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }
            return found;
        }

        private void AddGrades(AheadLeg leg, TrackShape shape)
        {
            var runs = shape.Grades;
            var forward = leg.RunsForward;
            var low = Math.Min(leg.From, leg.To);
            var high = Math.Max(leg.From, leg.To);
            for (var n = 0; n < runs.Count; n++)
            {
                var run = runs[forward ? n : runs.Count - 1 - n];
                var a = Math.Max(low, run.From);
                var b = Math.Min(high, run.To);
                if (b <= a)
                    continue;
                var from = leg.Start + (forward ? a - leg.From : leg.From - b);
                AddGrade(new GradeRun(from, from + (b - a), forward ? run.Percent : -run.Percent));
            }
        }

        private void AddGrade(GradeRun run)
        {
            if (grades.Count > 0)
            {
                var last = grades[grades.Count - 1];
                if (Math.Abs(last.Percent - run.Percent) < SameGrade || last.Length < ShortRun || run.Length < ShortRun)
                {
                    var mean = (last.Percent * last.Length + run.Percent * run.Length) / (last.Length + run.Length);
                    grades[grades.Count - 1] = new GradeRun(last.From, run.To, mean);
                    return;
                }
            }
            grades.Add(run);
        }

        private void AddLimits(AheadLeg leg, TrackShape shape, int approachKmh)
        {
            var forward = leg.RunsForward;
            var profile = shape.LimitsFor(forward);
            var approach = leg.End - ApproachLength;
            for (var n = 0; n < profile.Count; n++)
            {
                var i = forward ? n : profile.Count - 1 - n;
                var a = Math.Max(profile.OffsetAt(i), Math.Min(leg.From, leg.To));
                var b = Math.Min(i + 1 < profile.Count ? profile.OffsetAt(i + 1) : shape.Length, Math.Max(leg.From, leg.To));
                if (b <= a)
                    continue;
                var from = leg.Start + (forward ? a - leg.From : leg.From - b);
                var to = from + (b - a);
                var kmh = (int)Math.Round(Math.Min(profile.SpeedAt(i) * 3.6, CurveSpeed.StraightKmh));
                if (to <= approach)
                    AddLimit(from, to, kmh);
                else if (from >= approach)
                    AddLimit(from, to, Math.Min(kmh, approachKmh));
                else
                {
                    AddLimit(from, approach, kmh);
                    AddLimit(approach, to, Math.Min(kmh, approachKmh));
                }
            }
        }

        private void AddLimit(double from, double to, int kmh)
        {
            if (limits.Count > 0 && limits[limits.Count - 1].Kmh == kmh)
                limits[limits.Count - 1] = new LimitRun(limits[limits.Count - 1].From, to, kmh);
            else
                limits.Add(new LimitRun(from, to, kmh));
        }
    }
}
