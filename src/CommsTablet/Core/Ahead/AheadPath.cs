using System;
using System.Collections.Generic;
using CommsTablet.Core.Routing;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Ahead
{
    /// <summary>Why a path stops where it does.</summary>
    public enum AheadEnd : byte
    {
        /// <summary>It reached the distance asked for; the track goes on.</summary>
        Horizon,

        /// <summary>The track ends: a buffer stop, or a turntable, which the network leaves unjoined.</summary>
        DeadEnd,

        /// <summary>It is a route, and the train stops here, on the track it was going to.</summary>
        Destination,
    }

    /// <summary>Why a train stops on a route.</summary>
    public enum AheadStopKind : byte
    {
        /// <summary>The whole train has pulled clear, and its other end leads from here.</summary>
        Reversal,

        Destination,
    }

    /// <summary>A place a route's train comes to a stand, at a distance along the path.</summary>
    public readonly struct AheadStop
    {
        public readonly AheadStopKind Kind;
        public readonly double Distance;

        public AheadStop(AheadStopKind kind, double distance)
        {
            Kind = kind;
            Distance = distance;
        }
    }

    /// <summary>How a train running along a path meets a switch.</summary>
    public enum SwitchMeeting : byte
    {
        /// <summary>At its points: the switch decides which way the train goes.</summary>
        Facing,

        /// <summary>From the branch it is set for.</summary>
        Trailing,

        /// <summary>From a branch it is not set for: the game flips it as the train passes.</summary>
        TrailingAgainst,
    }

    /// <summary>One track's stretch of a path, from where the path enters it to the end it leaves by.</summary>
    public readonly struct AheadLeg
    {
        public readonly int Track;

        /// <summary>The offsets along the track, in the order the path runs them.</summary>
        public readonly double From, To;

        /// <summary>The path's distance where this leg starts.</summary>
        public readonly double Start;

        /// <summary>Whether the leg leaves its track through a switch.</summary>
        public readonly bool EndsAtSwitch;

        public AheadLeg(int track, double from, double to, double start, bool endsAtSwitch)
        {
            Track = track;
            From = from;
            To = to;
            Start = start;
            EndsAtSwitch = endsAtSwitch;
        }

        public double Length => Math.Abs(To - From);

        public double End => Start + Length;

        public bool RunsForward => To >= From;

        /// <summary>The offset along the track at a distance along the path.</summary>
        public double OffsetAt(double distance)
        {
            var along = Math.Max(0, Math.Min(Length, distance - Start));
            return RunsForward ? From + along : From - along;
        }
    }

    /// <summary>A switch on a path, and how the path meets it.</summary>
    public readonly struct AheadSwitch
    {
        public readonly int Junction;
        public readonly double Distance;
        public readonly SwitchMeeting Meeting;

        /// <summary>The branch the path takes through it.</summary>
        public readonly int Branch;

        /// <summary>Whether it already lies for the path. Only a switch met at its points can send a train the wrong way.</summary>
        public readonly bool Lined;

        public AheadSwitch(int junction, double distance, SwitchMeeting meeting, int branch, bool lined = true)
        {
            Junction = junction;
            Distance = distance;
            Meeting = meeting;
            Branch = branch;
            Lined = lined;
        }
    }

    /// <summary>
    /// The way ahead from a point on the network, track by track: along the
    /// switches as they lie, or along a route. A route's distance runs on
    /// through a reversal, counted by whichever end of the train is leading.
    /// </summary>
    public sealed class AheadPath
    {
        private readonly List<AheadLeg> legs = new List<AheadLeg>();
        private readonly List<AheadSwitch> switches = new List<AheadSwitch>();
        private readonly List<AheadStop> stops = new List<AheadStop>();

        public IReadOnlyList<AheadLeg> Legs => legs;

        /// <summary>The switches along it, nearest first.</summary>
        public IReadOnlyList<AheadSwitch> Switches => switches;

        /// <summary>Where a route's train stands to stop, nearest first; none for the aligned tracks.</summary>
        public IReadOnlyList<AheadStop> Stops => stops;

        public AheadEnd End { get; private set; }

        /// <summary>Its length, through the end of its last track.</summary>
        public double Length { get; private set; }

        /// <summary>For a route, the distance where the train's leading end stands now; else 0.</summary>
        public double RouteStart { get; private set; }

        /// <summary>The length of the whole route, if this is the near part of one; else <see cref="Length"/>.</summary>
        public double WholeLength { get; private set; }

        /// <summary>The leg at a distance along the path; the first or last beyond its ends.</summary>
        public AheadLeg LegAt(double distance)
        {
            int lo = 0, hi = legs.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (legs[mid].Start <= distance)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return legs[lo];
        }

        /// <summary>
        /// Follows the track from <paramref name="start"/>, taking each switch
        /// as it is set, until <paramref name="length"/> meters are covered or
        /// the track ends. The last track is kept whole.
        /// </summary>
        public static AheadPath Aligned(TrackGraph graph, TrackPosition start, TrackDirection direction, double length, Func<int, int> selectedBranch)
        {
            var path = new AheadPath();
            var track = start.Track;
            var offset = Math.Max(0, Math.Min(graph.Length(track), start.Offset));
            // A loop is walked round again until the length runs out; each leg but the first is a whole track.
            while (true)
            {
                var exit = direction.ExitEnd();
                var links = graph.Links(track, exit);
                var leg = new AheadLeg(track, offset, graph.OffsetOf(track, exit), path.Length, links.Length > 0 && links[0].NeedsJunction);
                path.legs.Add(leg);
                path.Length = leg.End;
                path.WholeLength = path.Length;
                if (links.Length == 0)
                {
                    path.End = AheadEnd.DeadEnd;
                    return path;
                }
                if (path.Length >= length)
                {
                    path.End = AheadEnd.Horizon;
                    return path;
                }
                var link = path.Take(graph, track, exit, links, selectedBranch);
                track = link.Track;
                offset = graph.OffsetOf(track, link.End);
                direction = link.End.DirectionInto();
            }
        }

        /// <summary>
        /// A route as the train would run it: first the train itself, from
        /// its trailing coupler to its leading one, then the route from
        /// <paramref name="along"/> meters into its first leg, on through each
        /// reversal, to a stop <paramref name="intoLastTrack"/> meters into the
        /// track it ends on, or at the route's own end if that is null. A
        /// switch the route has already taken counts as lying that way from
        /// then on.
        /// </summary>
        public static AheadPath Routed(TrackGraph graph, TrackPosition tail, TrackDirection towardLeader, double trainLength,
            Route route, double along, double? intoLastTrack, Func<int, int> selectedBranch)
        {
            var path = new AheadPath();
            path.AddTrain(graph, tail, towardLeader, trainLength, selectedBranch);
            path.RouteStart = path.Length;
            var taken = new Dictionary<int, int>();
            for (var k = 0; k < route.Legs.Count; k++)
            {
                var leg = route.Legs[k];
                var last = k == route.Legs.Count - 1;
                var skip = k == 0 ? Math.Max(0, along) : leg.StartOverlap;
                for (var i = 0; i < leg.Pieces.Count; i++)
                {
                    var piece = leg.Pieces[i];
                    var begins = leg.PieceStart(i);
                    var finalPiece = last && i == leg.Pieces.Count - 1;
                    // Compute the arrival stop from the untrimmed original route entry end before trimming traveled distance.
                    var to = finalPiece && intoLastTrack.HasValue
                        ? Arrival(graph, piece, intoLastTrack.Value)
                        : piece.To;
                    var pieceLength = Math.Abs(to - piece.From);
                    if (i > 0 && begins >= skip)
                        path.AddRouteSwitch(graph, leg.Pieces[i - 1], piece, taken, selectedBranch);
                    if (begins + pieceLength <= skip)
                        continue;
                    var direction = finalPiece && pieceLength > 1e-6
                        ? (to > piece.From ? TrackDirection.Forward : TrackDirection.Backward)
                        : piece.Direction;
                    var sign = (int)direction;
                    var from = begins >= skip ? piece.From : piece.From + sign * (skip - begins);
                    var exit = direction.ExitEnd();
                    var leavesTrack = Math.Abs(to - graph.OffsetOf(piece.Track, exit)) < 1e-6;
                    var links = graph.Links(piece.Track, exit);
                    path.AddLeg(piece.Track, from, to, leavesTrack && i < leg.Pieces.Count - 1 && links.Length > 0 && links[0].NeedsJunction);
                }
                if (leg.EndsInReversal)
                    path.stops.Add(new AheadStop(AheadStopKind.Reversal, path.Length));
            }
            path.stops.Add(new AheadStop(AheadStopKind.Destination, path.Length));
            path.End = AheadEnd.Destination;
            path.WholeLength = path.Length;
            return path;
        }

        /// <summary>The path as far as the track that reaches a distance, for when the rest is not read yet.</summary>
        public AheadPath Upto(double distance)
        {
            if (distance >= Length)
                return this;
            var near = new AheadPath { RouteStart = RouteStart, WholeLength = WholeLength, End = AheadEnd.Horizon };
            foreach (var leg in legs)
            {
                if (leg.Start >= distance)
                    break;
                near.legs.Add(leg);
                near.Length = leg.End;
            }
            near.switches.AddRange(switches.FindAll(s => s.Distance < near.Length));
            near.stops.AddRange(stops.FindAll(s => s.Distance < near.Length));
            return near;
        }

        // A route ends halfway along its track whichever way it arrives (or already at the
        // siding stop when GoalIntoTrack was set); a train arriving from an end runs in until
        // it is all there, or the track runs out.
        private static double Arrival(TrackGraph graph, RoutePiece piece, double into) =>
            Router.SidingStop(graph.Length(piece.Track), piece.From, into, piece.To);

        private void AddTrain(TrackGraph graph, TrackPosition tail, TrackDirection direction, double trainLength, Func<int, int> selectedBranch)
        {
            var track = tail.Track;
            var offset = Math.Max(0, Math.Min(graph.Length(track), tail.Offset));
            var remaining = trainLength;
            while (remaining > 0)
            {
                var exit = direction.ExitEnd();
                var exitOffset = graph.OffsetOf(track, exit);
                var span = Math.Abs(exitOffset - offset);
                if (span >= remaining)
                {
                    AddLeg(track, offset, offset + (int)direction * remaining, false);
                    return;
                }
                var links = graph.Links(track, exit);
                AddLeg(track, offset, exitOffset, links.Length > 0 && links[0].NeedsJunction);
                remaining -= span;
                if (links.Length == 0)
                    return;
                var link = Take(graph, track, exit, links, selectedBranch);
                track = link.Track;
                offset = graph.OffsetOf(track, link.End);
                direction = link.End.DirectionInto();
            }
        }

        private void AddLeg(int track, double from, double to, bool endsAtSwitch)
        {
            if (Math.Abs(to - from) <= 1e-6)
                return;
            var leg = new AheadLeg(track, from, to, Length, endsAtSwitch);
            legs.Add(leg);
            Length = leg.End;
        }

        private void AddRouteSwitch(TrackGraph graph, RoutePiece before, RoutePiece next, Dictionary<int, int> taken, Func<int, int> selectedBranch)
        {
            var exit = before.Direction.ExitEnd();
            foreach (var link in graph.Links(before.Track, exit))
            {
                if (link.Track != next.Track || graph.OffsetOf(link.Track, link.End) != next.From || !link.NeedsJunction)
                    continue;
                var info = graph.Junction(link.Junction);
                var lies = taken.TryGetValue(link.Junction, out var set) ? set : selectedBranch(link.Junction);
                var lined = lies == link.Branch;
                var facing = info.StemTrack == before.Track && info.StemEnd == exit;
                var meeting = facing ? SwitchMeeting.Facing : lined ? SwitchMeeting.Trailing : SwitchMeeting.TrailingAgainst;
                switches.Add(new AheadSwitch(link.Junction, Length, meeting, link.Branch, lined));
                taken[link.Junction] = link.Branch;
                return;
            }
        }

        private TrackLink Take(TrackGraph graph, int track, TrackEnd exit, TrackLink[] links, Func<int, int> selectedBranch)
        {
            var first = links[0];
            if (!first.NeedsJunction)
                return first;
            var info = graph.Junction(first.Junction);
            var selected = selectedBranch(first.Junction);
            if (info.StemTrack == track && info.StemEnd == exit)
            {
                var taken = first;
                foreach (var link in links)
                {
                    if (link.Branch == selected)
                        taken = link;
                }
                switches.Add(new AheadSwitch(taken.Junction, Length, SwitchMeeting.Facing, taken.Branch));
                return taken;
            }
            var meeting = first.Branch == selected ? SwitchMeeting.Trailing : SwitchMeeting.TrailingAgainst;
            switches.Add(new AheadSwitch(first.Junction, Length, meeting, first.Branch));
            return first;
        }
    }
}
