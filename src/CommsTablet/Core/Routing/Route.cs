using System;
using System.Collections.Generic;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Routing
{
    /// <summary>The two ends of a consist, as the caller names them.</summary>
    public enum ConsistEnd : byte
    {
        A = 0,
        B = 1,
    }

    public static class ConsistEnds
    {
        public static ConsistEnd Other(this ConsistEnd end) => end == ConsistEnd.A ? ConsistEnd.B : ConsistEnd.A;
    }

    /// <summary>A stretch of one track, driven from one offset to another.</summary>
    public readonly struct RoutePiece
    {
        public readonly int Track;
        public readonly double From;
        public readonly double To;

        public RoutePiece(int track, double from, double to)
        {
            Track = track;
            From = from;
            To = to;
        }

        public TrackDirection Direction => To >= From ? TrackDirection.Forward : TrackDirection.Backward;

        public double Length => Math.Abs(To - From);

        public RoutePiece Reversed() => new RoutePiece(Track, To, From);

        public override string ToString() => $"track {Track} {From:F1}->{To:F1}";
    }

    /// <summary>A switch the route needs set, and how far along the leg the leading end reaches it.</summary>
    public readonly struct SwitchSetting
    {
        public readonly int Junction;
        public readonly int Branch;
        public readonly double AtDistance;

        public SwitchSetting(int junction, int branch, double atDistance)
        {
            Junction = junction;
            Branch = branch;
            AtDistance = atDistance;
        }

        public override string ToString() => $"junction {Junction} to {Branch} at {AtDistance:F1} m";
    }

    /// <summary>
    /// One move in one direction: the path the leading end follows, the
    /// switches on it, and where it stops (the end of the path).
    /// </summary>
    public sealed class RouteLeg
    {
        private readonly RoutePiece[] pieces;
        private readonly double[] pieceStart;
        private readonly SwitchSetting[] switches;

        public RouteLeg(IReadOnlyList<RoutePiece> pieces, IReadOnlyList<SwitchSetting> switches, ConsistEnd leader, bool endsInReversal, double startOverlap = 0)
        {
            if (pieces == null || pieces.Count == 0)
                throw new ArgumentException("A leg needs at least one piece.", nameof(pieces));
            this.pieces = new RoutePiece[pieces.Count];
            pieceStart = new double[pieces.Count];
            var d = 0.0;
            for (var i = 0; i < pieces.Count; i++)
            {
                this.pieces[i] = pieces[i];
                pieceStart[i] = d;
                d += pieces[i].Length;
            }
            Length = d;
            var sorted = new List<SwitchSetting>(switches ?? Array.Empty<SwitchSetting>());
            sorted.Sort((a, b) => a.AtDistance.CompareTo(b.AtDistance));
            this.switches = sorted.ToArray();
            Leader = leader;
            EndsInReversal = endsInReversal;
            StartOverlap = Math.Max(0, Math.Min(startOverlap, Length));
        }

        public IReadOnlyList<RoutePiece> Pieces => pieces;

        public IReadOnlyList<SwitchSetting> Switches => switches;

        public double Length { get; }

        /// <summary>
        /// How much of the leg's start the train doesn't run: a leg after a
        /// reversal begins where the old leading end stopped, a train's length
        /// beyond the new leading end, so that end is found on it.
        /// </summary>
        public double StartOverlap { get; }

        /// <summary>The meters the train runs on this leg.</summary>
        public double Running => Length - StartOverlap;

        /// <summary>The consist end leading on this leg.</summary>
        public ConsistEnd Leader { get; }

        /// <summary>Whether the train reverses at this leg's end, for the next leg.</summary>
        public bool EndsInReversal { get; }

        public double PieceStart(int index) => pieceStart[index];

        /// <summary>
        /// Finds a position on the leg, as distance along it, looking from
        /// piece <paramref name="hint"/> forward first, so a leg that passes
        /// a track twice (a loop) resolves to the nearer pass.
        /// </summary>
        public bool TryLocate(TrackPosition position, int hint, double tolerance, out double distance, out int piece)
        {
            hint = Math.Max(0, Math.Min(pieces.Length - 1, hint));
            for (var k = 0; k < pieces.Length; k++)
            {
                // hint, hint + 1, ..., the last piece, then hint - 1 back to the first.
                var i = hint + k < pieces.Length ? hint + k : pieces.Length - 1 - k;
                var p = pieces[i];
                if (p.Track != position.Track)
                    continue;
                var lo = Math.Min(p.From, p.To) - tolerance;
                var hi = Math.Max(p.From, p.To) + tolerance;
                if (position.Offset < lo || position.Offset > hi)
                    continue;
                distance = pieceStart[i] + (position.Offset - p.From) * (int)p.Direction;
                piece = i;
                return true;
            }
            distance = 0;
            piece = -1;
            return false;
        }

        /// <summary>
        /// How far a position lies beyond the leg's end, on the leg's last
        /// track and the way the leg runs there, m; NaN when it isn't.
        /// </summary>
        public double PastEnd(TrackPosition position)
        {
            var last = pieces[pieces.Length - 1];
            if (position.Track != last.Track)
                return double.NaN;
            var past = (position.Offset - last.To) * (int)last.Direction;
            return past > 0 ? past : double.NaN;
        }

        /// <summary>The track position at a distance along the leg, clamped to the leg.</summary>
        public TrackPosition PositionAt(double distance)
        {
            var i = PieceIndexAt(distance);
            var p = pieces[i];
            var along = Math.Max(0, Math.Min(p.Length, distance - pieceStart[i]));
            return new TrackPosition(p.Track, p.From + along * (int)p.Direction);
        }

        public int PieceIndexAt(double distance)
        {
            var i = Array.BinarySearch(pieceStart, distance);
            if (i < 0)
                i = ~i - 1;
            return Math.Max(0, Math.Min(pieces.Length - 1, i));
        }
    }

    /// <summary>A planned route: one or more legs, the leading end alternating at each reversal.</summary>
    public sealed class Route
    {
        public Route(IReadOnlyList<RouteLeg> legs)
        {
            if (legs == null || legs.Count == 0)
                throw new ArgumentException("A route needs at least one leg.", nameof(legs));
            Legs = legs;
            var total = 0.0;
            var running = 0.0;
            foreach (var l in legs)
            {
                total += l.Length;
                running += l.Running;
            }
            Length = total;
            Running = running;
        }

        public IReadOnlyList<RouteLeg> Legs { get; }

        /// <summary>The total distance the leading ends travel, in meters.</summary>
        public double Length { get; }

        /// <summary>The meters the train runs, each leg's overlap with the one before left out.</summary>
        public double Running { get; }

        /// <summary>What the router's search reckoned the route cost, with every penalty of the request; 0 for a route it didn't build.</summary>
        public double SearchCost { get; internal set; }

        public int Reversals => Legs.Count - 1;

        /// <summary>The route's cost for ranking it against others: its length, its reversals, and the meters run shoving.</summary>
        public double Cost(RouteRequest request)
        {
            var cost = Length + request.ReversalPenalty * Reversals;
            if (request.ShovingEnd.HasValue)
            {
                foreach (var leg in Legs)
                {
                    if (leg.Leader == request.ShovingEnd.Value)
                        cost += leg.Running * request.ShovingCost;
                }
            }
            return cost;
        }
    }
}
