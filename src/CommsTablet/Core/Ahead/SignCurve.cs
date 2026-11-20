using System;

namespace CommsTablet.Core.Ahead
{
    /// <summary>A place in the world, in the single precision the game's curves use.</summary>
    internal readonly struct CurvePoint
    {
        public readonly float X, Y, Z;

        public CurvePoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public CurvePoint Flat => new CurvePoint(X, 0f, Z);

        public static float Distance(CurvePoint a, CurvePoint b)
        {
            var x = a.X - b.X;
            var y = a.Y - b.Y;
            var z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        // Unity's test for two equal vectors: a handle this near its point is no handle.
        public static bool Same(CurvePoint a, CurvePoint b)
        {
            var x = a.X - b.X;
            var y = a.Y - b.Y;
            var z = a.Z - b.Z;
            return x * x + y * y + z * z < 9.9999994E-11f;
        }
    }

    /// <summary>
    /// A track's Bezier curve copied out of the game, so it can be read away
    /// from the main thread: the game's own curve goes through Unity transforms
    /// and walks every control point for each place asked of it. Places come
    /// out as the game's <c>BezierCurve.GetPointAt</c> gives them, down to its
    /// single-precision sums, because the sign rules (<see cref="SignLimits"/>)
    /// branch on small differences. One thread at a time may use a curve.
    /// </summary>
    public sealed class SignCurve
    {
        /// <summary>Numbers kept for a control point: its place and its two handles, in world coordinates.</summary>
        public const int FloatsPerKnot = 9;

        // The game looks for a place's span with lengths from this many steps, then twice as many, and so on.
        private const int SpanSearchSteps = 10, SpanSearchTries = 10;

        private readonly Span[] spans;
        private readonly float resolution;
        private readonly float[][] shares = new float[SpanSearchTries][];
        private readonly float[][] reached = new float[SpanSearchTries][];
        private float length = -1f;

        /// <param name="knots">For each control point: x, y, z of its place, of its incoming handle, and of its outgoing handle.</param>
        /// <param name="closed">Whether the curve runs on from its last point to its first.</param>
        /// <param name="resolution">The curve's own setting for how finely its length is measured.</param>
        public SignCurve(float[] knots, bool closed, float resolution)
        {
            if (knots == null || knots.Length < 2 * FloatsPerKnot || knots.Length % FloatsPerKnot != 0)
                throw new ArgumentException("A curve needs at least two control points, nine numbers each.", nameof(knots));
            KnotCount = knots.Length / FloatsPerKnot;
            this.resolution = resolution;
            spans = new Span[closed ? KnotCount : KnotCount - 1];
            for (var i = 0; i < spans.Length; i++)
            {
                var from = i * FloatsPerKnot;
                var to = (i + 1) % KnotCount * FloatsPerKnot;
                spans[i] = new Span(Read(knots, from), Read(knots, from + 6), Read(knots, to + 3), Read(knots, to));
            }
        }

        public int KnotCount { get; }

        /// <summary>The curve's length as the game measures it.</summary>
        public float Length
        {
            get
            {
                if (length < 0f)
                {
                    var total = 0f;
                    foreach (var span in spans)
                    {
                        var steps = Math.Max(2, (int)Math.Round(span.Measure(3) * resolution));
                        total += span.Measure(steps);
                    }
                    length = total;
                }
                return length;
            }
        }

        /// <summary>The place a fraction of the way along the curve.</summary>
        internal CurvePoint PointAt(float t)
        {
            if (t <= 0f)
                return spans[0].At(0f);
            if (t >= 1f)
                return spans[spans.Length - 1].At(1f);
            for (var attempt = 0; attempt < SpanSearchTries; attempt++)
            {
                MeasureSpans(attempt);
                var share = shares[attempt];
                var before = reached[attempt];
                for (var i = 0; i < spans.Length; i++)
                {
                    var after = before[i] + share[i];
                    if (after > t || Math.Abs(t - after) < 5E-06)
                        return spans[i].At((t - before[i]) / share[i]);
                }
            }
            throw new InvalidOperationException("The curve has no span at " + t + ".");
        }

        private void MeasureSpans(int attempt)
        {
            if (shares[attempt] != null)
                return;
            var whole = Length;
            var share = new float[spans.Length];
            var before = new float[spans.Length];
            var sum = 0f;
            for (var i = 0; i < spans.Length; i++)
            {
                share[i] = spans[i].Measure(SpanSearchSteps * (attempt + 1)) / whole;
                before[i] = sum;
                sum += share[i];
            }
            shares[attempt] = share;
            reached[attempt] = before;
        }

        private static CurvePoint Read(float[] knots, int at) => new CurvePoint(knots[at], knots[at + 1], knots[at + 2]);

        // From one control point to the next: cubic with both handles, quadratic with one, straight with none.
        private readonly struct Span
        {
            private readonly CurvePoint start, first, second, end;
            private readonly int handles;

            public Span(CurvePoint start, CurvePoint startHandle, CurvePoint endHandle, CurvePoint end)
            {
                this.start = start;
                this.end = end;
                var hasStart = !CurvePoint.Same(startHandle, start);
                var hasEnd = !CurvePoint.Same(endHandle, end);
                handles = (hasStart ? 1 : 0) + (hasEnd ? 1 : 0);
                first = hasStart ? startHandle : endHandle;
                second = endHandle;
            }

            public CurvePoint At(float t)
            {
                if (handles == 0)
                    return new CurvePoint(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t, start.Z + (end.Z - start.Z) * t);
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                var rest = 1f - t;
                if (handles == 1)
                {
                    var a = (float)Math.Pow(rest, 2f);
                    var b = 2f * rest * t;
                    var c = (float)Math.Pow(t, 2f);
                    return new CurvePoint(a * start.X + b * first.X + c * end.X, a * start.Y + b * first.Y + c * end.Y, a * start.Z + b * first.Z + c * end.Z);
                }
                var p = (float)Math.Pow(rest, 3f);
                var q = 3f * (float)Math.Pow(rest, 2f) * t;
                var r = 3f * rest * (float)Math.Pow(t, 2f);
                var s = (float)Math.Pow(t, 3f);
                return new CurvePoint(p * start.X + q * first.X + r * second.X + s * end.X,
                    p * start.Y + q * first.Y + r * second.Y + s * end.Y,
                    p * start.Z + q * first.Z + r * second.Z + s * end.Z);
            }

            public float Measure(int steps)
            {
                var total = 0f;
                var last = start;
                for (var i = 0; i <= steps; i++)
                {
                    var point = At(i / (float)steps);
                    total += CurvePoint.Distance(point, last);
                    last = point;
                }
                return total;
            }
        }
    }
}
