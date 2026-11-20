using System;
using System.Collections.Generic;

namespace CommsTablet.Core.Ahead
{
 internal static class SignLimits
    {
        // The sign placer's settings.
        private const float ArcTolerance = 1f, LeastStretch = 300f, LastStretch = 250f;
        private const float MostSpeedDrop = 30f, ShortStretch = 300f, SameGrade = 0.005f, LookAheadSeconds = 2f, RadiusCeiling = 1200f;
        private const int MostFitTries = 100;

        private static readonly float[] RadiusBelow = { 50f, 70f, 95f, 130f, 170f, 230f, 360f, 700f, 900f, 1200f };
        private static readonly float[] KmhForRadius = { 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f };
        private const float OpenLineKmh = 120f;

        // A quarter turn, as Unity's single-precision functions give it.
        private static readonly float QuarterSin = (float)Math.Sin((float)Math.PI / 2f);
        private static readonly float QuarterCos = (float)Math.Cos((float)Math.PI / 2f);

        /// <summary>The track's limits each way. It throws if the curve cannot be fitted, as the game's steps would.</summary>
        internal static PostedLimits Work(SignCurve curve, double trackLength)
        {
            var arcs = FitArcs(curve);
            if (arcs.Count == 0)
                throw new InvalidOperationException("No arcs fit the curve.");
            var forward = OneWay(curve, arcs, trackLength);
            var against = new List<Arc>(arcs.Count);
            for (var i = arcs.Count - 1; i >= 0; i--)
                against.Add(arcs[i].Turned);
            return new PostedLimits(forward, OneWay(curve, against, trackLength));
        }

        private static List<PostedSegment> OneWay(SignCurve curve, List<Arc> arcs, double trackLength)
        {
            var stretches = Gather(arcs);
            SplitLast(stretches, curve.Length);
            MergeAlike(stretches, curve);
            EaseDrops(stretches);
            var result = new List<PostedSegment>(stretches.Count);
            foreach (var s in stretches)
                result.Add(new PostedSegment(s.FromT * trackLength, s.ToT * trackLength, s.Kmh));
            return result;
        }

        // ---- Arcs ---------------------------------------------------------------

        private struct Arc
        {
            public float CenterX, CenterZ, StartAngle, EndAngle, Radius, FromT, ToT;

            public float Length => Math.Abs(Radius * (EndAngle - StartAngle));

            public Arc Turned
            {
                get
                {
                    var arc = this;
                    arc.StartAngle = EndAngle;
                    arc.EndAngle = StartAngle;
                    arc.FromT = ToT;
                    arc.ToT = FromT;
                    return arc;
                }
            }
        }

        // From each arc's start, reach as far along the curve as a circle stays
        // within the tolerance, growing by half and backing off to the midpoint.
        private static List<Arc> FitArcs(SignCurve curve)
        {
            var arcs = new List<Arc>();
            var start = 0f;
            float end;
            do
            {
                var tries = 0f;
                end = 1f;
                var origin = curve.PointAt(start).Flat;
                var arc = default(Arc);
                var kept = arc;
                var fits = false;
                var overshot = false;
                var reached = 1f;
                do
                {
                    var fitBefore = fits;
                    kept = arc;
                    var middle = (start + end) / 2f;
                    arc = Circle(origin, curve.PointAt(middle).Flat, curve.PointAt(end).Flat);
                    arc.FromT = start;
                    arc.ToT = end;
                    fits = Miss(curve, arc, origin, start, end) <= ArcTolerance;
                    overshot = fitBefore && !fits;
                    if (!overshot)
                        reached = end;
                    if (fits)
                    {
                        if (end >= 1f)
                        {
                            reached = 1f;
                            arc.ToT = 1f;
                            kept = arc;
                            break;
                        }
                        end += (end - start) / 2f;
                    }
                    else
                        end = middle;
                }
                while (!overshot && tries++ < MostFitTries);
                if (tries >= MostFitTries)
                    break;
                arcs.Add(kept);
                start = reached;
            }
            while (end < 1f);
            return arcs;
        }

        // How far the curve strays from the arc's circle at its quarter points.
        private static float Miss(SignCurve curve, Arc arc, CurvePoint origin, float start, float end)
        {
            var quarter = (end - start) / 4f;
            var center = new CurvePoint(arc.CenterX, 0f, arc.CenterZ);
            var radius = CurvePoint.Distance(center, origin);
            var first = CurvePoint.Distance(center, curve.PointAt(start + quarter).Flat);
            var second = CurvePoint.Distance(center, curve.PointAt(end - quarter).Flat);
            return Math.Abs(first - radius) + Math.Abs(second - radius);
        }

        // The circle through three points, where the perpendicular bisectors of
        // its two chords meet. Parallel bisectors put its center at infinity.
        private static Arc Circle(CurvePoint a, CurvePoint b, CurvePoint c)
        {
            float ax = b.X - a.X, az = b.Z - a.Z, cx = c.X - b.X, cz = c.Z - b.Z;
            var firstX = ax * QuarterCos - az * QuarterSin;
            var firstZ = ax * QuarterSin + az * QuarterCos;
            var secondX = cx * QuarterCos - cz * QuarterSin;
            var secondZ = cx * QuarterSin + cz * QuarterCos;
            float x1 = (a.X + b.X) / 2f, z1 = (a.Z + b.Z) / 2f, x3 = (b.X + c.X) / 2f, z3 = (b.Z + c.Z) / 2f;
            var center = Meeting(x1, z1, x1 + firstX, z1 + firstZ, x3, z3, x3 + secondX, z3 + secondZ);
            var startAngle = (float)Math.Atan2(a.Z - center.Z, a.X - center.X);
            var middleAngle = (float)Math.Atan2(b.Z - center.Z, b.X - center.X);
            var endAngle = (float)Math.Atan2(c.Z - center.Z, c.X - center.X);
            if (startAngle < endAngle)
            {
                if (startAngle > middleAngle || middleAngle > endAngle)
                    startAngle += (float)Math.PI * 2f;
                if (startAngle > endAngle)
                    Swap(ref startAngle, ref endAngle);
            }
            else if (endAngle < middleAngle && middleAngle < startAngle)
                Swap(ref startAngle, ref endAngle);
            else
                endAngle += (float)Math.PI * 2f;
            return new Arc { CenterX = center.X, CenterZ = center.Z, StartAngle = startAngle, EndAngle = endAngle, Radius = CurvePoint.Distance(center, a) };
        }

        private static CurvePoint Meeting(float x1, float z1, float x2, float z2, float x3, float z3, float x4, float z4)
        {
            var first = x1 * z2 - z1 * x2;
            var second = x3 * z4 - z3 * x4;
            var x = first * (x3 - x4) - (x1 - x2) * second;
            var z = first * (z3 - z4) - (z1 - z2) * second;
            var across = (x1 - x2) * (z3 - z4) - (z1 - z2) * (x3 - x4);
            return across != 0f
                ? new CurvePoint(x / across, 0f, z / across)
                : new CurvePoint(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        }

        private static void Swap(ref float a, ref float b)
        {
            var was = a;
            a = b;
            b = was;
        }

        // ---- Stretches ----------------------------------------------------------

        private sealed class Stretch
        {
            public float Length, Radius, FromT, ToT, Assigned;

            public float Kmh => Assigned > 0f || float.IsNaN(Assigned) ? Assigned : KmhFor(Radius);

            public Stretch Copy() => (Stretch)MemberwiseClone();
        }

        private static float KmhFor(float radius)
        {
            for (var i = 0; i < RadiusBelow.Length; i++)
            {
                if (radius < RadiusBelow[i])
                    return KmhForRadius[i];
            }
            return OpenLineKmh;
        }

        private static List<Stretch> Gather(List<Arc> arcs)
        {
            var lengths = new float[arcs.Count];
            for (var i = 0; i < lengths.Length; i++)
            {
                lengths[i] = arcs[i].Length;
                if (lengths[i] <= 0f)
                    throw new InvalidOperationException("An arc has no length.");
            }
            var sizes = new List<int>();
            Chunk(lengths, 0, lengths.Length, sizes);

            var result = new List<Stretch>(sizes.Count);
            var first = 0;
            for (var c = 0; c < sizes.Count; c++)
            {
                var count = sizes[c];
                var stretch = new Stretch { FromT = arcs[first].FromT, ToT = arcs[first + count - 1].ToT, Radius = float.PositiveInfinity };
                for (var i = first; i < first + count; i++)
                {
                    stretch.Length += arcs[i].Length;
                    if (arcs[i].Radius < stretch.Radius)
                        stretch.Radius = arcs[i].Radius;
                }
                first += count;
                if (c + 1 < sizes.Count)
                {
                    // A tighter arc just past the end pulls the stretch's limit down in good time.
                    var ahead = TightestIn(arcs, first, sizes[c + 1], stretch.Kmh * LookAheadSeconds);
                    if (ahead < stretch.Radius)
                        stretch.Radius = (Clamp(stretch.Radius, ahead, RadiusCeiling) + ahead) / 2f;
                }
                result.Add(stretch);
            }
            return result;
        }

        private static float TightestIn(List<Arc> arcs, int first, int count, float meters)
        {
            var tightest = float.PositiveInfinity;
            var covered = 0f;
            for (var i = first; i < first + count && covered < meters; i++)
            {
                covered += arcs[i].Length;
                if (arcs[i].Radius < tightest)
                    tightest = arcs[i].Radius;
            }
            return tightest;
        }

        private static float Clamp(float value, float low, float high) => value < low ? low : value > high ? high : value;

        // Splits a run of arcs into as many chunks as can each reach the least stretch.
        private static void Chunk(float[] lengths, int from, int to, List<int> sizes)
        {
            if (to - from == 1)
            {
                sizes.Add(1);
                return;
            }
            var split = from;
            while (Sum(lengths, from, split) < LeastStretch && to - split >= 2 && Sum(lengths, split, to) - lengths[split] >= LeastStretch)
                split++;
            if (Sum(lengths, from, split) < LeastStretch || Sum(lengths, split, to) < LeastStretch)
            {
                sizes.Add(to - from);
                return;
            }
            Chunk(lengths, from, split, sizes);
            Chunk(lengths, split, to, sizes);
        }

        // Added in double precision and then narrowed, as LINQ's Sum does for the game.
        private static float Sum(float[] values, int from, int to)
        {
            double sum = 0;
            for (var i = from; i < to; i++)
                sum += values[i];
            return (float)sum;
        }

        // A long last stretch gives up its end, so the limit before the next track is local.
        private static void SplitLast(List<Stretch> stretches, float curveLength)
        {
            var last = stretches[stretches.Count - 1];
            if (last.Length > 2f * LastStretch)
            {
                var tail = last.Copy();
                last.Length -= LastStretch;
                tail.Length = LastStretch;
                var step = LastStretch / curveLength;
                last.ToT += last.FromT > last.ToT ? step : -step;
                tail.FromT = last.ToT;
                stretches.Add(tail);
            }
            else if (last.Length > 1.5 * LastStretch)
            {
                var tail = last.Copy();
                tail.Length = last.Length *= 0.5f;
                tail.FromT = last.ToT = (last.FromT + last.ToT) * 0.5f;
                stretches.Add(tail);
            }
        }

        private static void MergeAlike(List<Stretch> stretches, SignCurve curve)
        {
            for (var i = stretches.Count - 1; i > 0; i--)
            {
                var before = stretches[i - 1];
                var after = stretches[i];
                if (Math.Abs(Grade(after, curve) - Grade(before, curve)) >= SameGrade || before.Kmh != after.Kmh)
                    continue;
                before.ToT = after.ToT;
                before.Length += after.Length;
                before.Radius = after.Radius < before.Radius ? after.Radius : before.Radius;
                stretches.RemoveAt(i);
            }
        }

        private static float Grade(Stretch stretch, SignCurve curve)
        {
            var from = curve.PointAt(stretch.FromT);
            var to = curve.PointAt(stretch.ToT);
            return Math.Abs(from.Y - to.Y) / CurvePoint.Distance(from, to);
        }

        // From the far end back: a drop of more than 30 km/h gets a step between, in
        // the second half of a long stretch, or the whole of a short one comes down.
        private static void EaseDrops(List<Stretch> stretches)
        {
            for (var i = stretches.Count - 2; i >= 0; i--)
            {
                var stretch = stretches[i];
                var next = stretches[i + 1];
                float high = stretch.Kmh, low = next.Kmh;
                if (high - low <= MostSpeedDrop)
                    continue;
                var step = Midpoint(high, low);
                if (step - low > MostSpeedDrop)
                    step = low + MostSpeedDrop;
                if (stretch.Length < ShortStretch)
                {
                    stretch.Assigned = step;
                    continue;
                }
                var inserted = new Stretch { Assigned = step, FromT = (stretch.FromT + stretch.ToT) / 2f, ToT = next.FromT, Radius = stretch.Radius };
                stretch.ToT = inserted.FromT;
                stretch.Length /= 2f;
                inserted.Length = stretch.Length;
                stretches.Insert(i + 1, inserted);
                if (high - step > MostSpeedDrop)
                    stretch.Assigned = step + MostSpeedDrop;
            }
        }

        private static float Midpoint(float a, float b)
        {
            var middle = (a + b) / 2f;
            return middle - middle % 10f;
        }
    }
}
