using System;
using System.Collections.Generic;
using System.Threading;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Routing
{
    /// <summary>What the router is asked: where the consist's ends are, how long it is, and where its leading end must stop.</summary>
    public sealed class RouteRequest
    {
        /// <summary>End A's outer coupler, and the track direction pointing out of the consist there.</summary>
        public TrackPosition EndA;
        public TrackDirection OutwardA;

        /// <summary>End B's outer coupler, and the track direction pointing out of the consist there.</summary>
        public TrackPosition EndB;
        public TrackDirection OutwardB;

        /// <summary>Coupler to coupler, in meters.</summary>
        public double TrainLength;

        /// <summary>Where the leading end stops.</summary>
        public TrackPosition Goal;

        /// <summary>If set, the direction along the goal's track the train must be moving as it arrives.</summary>
        public TrackDirection? GoalDirection;

        /// <summary>If set, only this end may lead the first leg.</summary>
        public ConsistEnd? FirstLeader;

        /// <summary>
        /// If set, the end that must lead the last leg into the goal: which
        /// end of the train arrives first (the cars, or the loco cab-first).
        /// A route may turn the train on a wye or a loop to manage it.
        /// </summary>
        public ConsistEnd? LastLeader;

        public bool AllowReversals = true;

        /// <summary>How far past a switch's points the whole train pulls before reversing, in meters.</summary>
        public double ReversalClearance = 15;

        /// <summary>The cost of a reversal beyond the distance it adds, in meters of running.</summary>
        public double ReversalPenalty = 400;

        /// <summary>
        /// The end whose leading is shoving: a train's cars end, its loco at
        /// the other. Null for none (running light, or a loco at each end).
        /// </summary>
        public ConsistEnd? ShovingEnd;

        /// <summary>
        /// The extra cost of each meter run with <see cref="ShovingEnd"/>
        /// leading, a reversal's pull forward and way back included, so a
        /// crew's way is cheapest: the loco leads on the road,
        /// and a train turned or reversed near where it must arrive cars
        /// first, not shoved its whole way.
        /// </summary>
        public double ShovingCost = 1;

        /// <summary>The cost of taking a switch's diverging branch (any branch but the first), in meters.</summary>
        public double DivergingPenalty = 10;

        /// <summary>Extra cost for running onto a track (an occupied track, say); infinity forbids it. Null for none.</summary>
        public Func<int, double> TrackPenalty;

        /// <summary>Whether a switch may be set to a branch (not locked under another train). Null for all.</summary>
        public Func<int, int, bool> CanSetJunction;

        /// <summary>
        /// Extra cost for passing a switch by a branch, in meters: a switch
        /// that can't be thrown just now (another engineer holds it, a train
        /// is moving near it) is worth waiting at, not going a long way
        /// round. Null for none.
        /// </summary>
        public Func<int, int, double> JunctionPenalty;

        /// <summary>
        /// The branch each switch lies for, to follow the train's own cars
        /// back from its ends when they stand across more than one switch.
        /// Null if not known: the train is then followed only as far as the
        /// first switch it faces.
        /// </summary>
        public Func<int, int> SwitchLie;

        /// <summary>
        /// Whether a goal under the train may be reached by backing onto
        /// it: the end beyond it stops there, the other end leading the
        /// move. Not for a coupling, which runs on from where it stands.
        /// </summary>
        public bool AllowBackOnto = true;

        /// <summary>
        /// Whether cars stand on a track between two offsets, so a train
        /// can't run through there (not the train's own, nor trains on the
        /// move, which will likely clear). Null for none.
        /// </summary>
        public Func<int, double, double, bool> Blocked;

        /// <summary>
        /// Optional exact span (low, high) on the goal track of a car the route
        /// is explicitly allowed to meet at the goal (e.g. coupling). All
        /// intervening track between entry and the car's near edge is still
        /// checked so earlier or touching cars still block the approach.
        /// </summary>
        public (double Low, double High)? GoalCarSpan;

        /// <summary>
        /// When set (a siding/track destination with no explicit offset),
        /// entering the goal track from either end stops this many meters in
        /// (capped at track length minus end clearance) rather than at
        /// <see cref="Goal"/>'s offset. Both approach occupancy and the final
        /// route piece use that stop.
        /// </summary>
        public double? GoalIntoTrack;
    }

    public enum RouteFailure
    {
        None,
        NoPath,
        GoalUnderTrain,
        BadTrack,
        Cancelled,
    }

    /// <summary>
    /// Finds the cheapest way to bring a consist's leading end to a goal:
    /// A* over track ends and which end of the train leads, with switchbacks
    /// allowed wherever the whole train can pull clear past a switch it has
    /// just run through (each one changes the leading end). Read-only on the
    /// graph, so searches may run on any thread.
    /// </summary>
    public sealed class Router
    {
        /// <summary>
        /// How many tracks deep a pull-forward may run. The stock map's
        /// median track is 24 m (turnouts are tracks of their own), so a long
        /// train's room to turn lies over dozens of them.
        /// </summary>
        private const int MaxRunDepth = 64;

        /// <summary>How many tracks one pull-forward search may try, so a big yard's fan of switches stays cheap.</summary>
        private const int MaxRunSteps = 256;

        /// <summary>How many tracks back from an end the train's own cars are followed for a pull clear of a switch they stand across.</summary>
        private const int MaxUnderTrain = 64;

        /// <summary>The least track past a switch's points a train's tail is pulled onto before going back through it, m.</summary>
        private const double MinPullClear = 5;

        /// <summary>How far beyond where the tail comes to rest, pulled clear, the leg back begins, m.</summary>
        private const double PullClearSlack = 10;

        /// <summary>How far past a train's leading coupler a blocked check starts at the outset, m.</summary>
        private const double StartMargin = 0.5;

        /// <summary>Clearance outside a permitted goal car's span when checking intervening approach track, m.</summary>
        private const double GoalEdgeMargin = 0.01;

        /// <summary>Clearance left before a siding's far end when stopping a train inside a track-level goal, m.</summary>
        public const double GoalEndClearance = 10;

        /// <summary>
        /// Where a train entering a track of <paramref name="trackLength"/> from
        /// <paramref name="entryOffset"/> stops when asked to pull
        /// <paramref name="into"/> meters into it (matching <c>AheadPath.Arrival</c>),
        /// or <paramref name="fallback"/> if <paramref name="entryOffset"/> is not an end.
        /// </summary>
        public static double SidingStop(double trackLength, double entryOffset, double into, double fallback)
        {
            var atStart = Math.Abs(entryOffset) < 1e-6;
            var atEnd = Math.Abs(entryOffset - trackLength) < 1e-6;
            if (!atStart && !atEnd)
                return fallback;
            var sign = atStart ? 1 : -1;
            var room = Math.Max(0, trackLength - GoalEndClearance);
            return entryOffset + sign * Math.Min(room, Math.Max(0, into));
        }

        private readonly TrackGraph graph;

        public Router(TrackGraph graph)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public TrackGraph Graph => graph;

        /// <summary>How many searches have been run, for tests and diagnostics.</summary>
        public int Searches => searches;

        private int searches;

        private enum StepKind : byte
        {
            None,
            Start,
            Traverse,
            Reverse,

            /// <summary>The first move: one end pulls the train clear of a switch it stands across, and the other leads back through it.</summary>
            StartReverse,

            /// <summary>The only move: one end leads until the other stands at a goal that was under the train.</summary>
            StartBack,
        }

        private struct NodeState
        {
            public double Cost;
            public int Parent;
            public StepKind Kind;
            public ConsistEnd StartEnd;
            public TrackLink Link;
            public bool Closed;
        }

        public Route Find(RouteRequest request) => Find(request, out _, CancellationToken.None);

        public Route Find(RouteRequest request, out RouteFailure failure) => Find(request, out failure, CancellationToken.None);

        // A search node: the leading end at one end of a track, about to leave it,
        // and which end of the consist that is: ((track * 2 + end) << 1) | leader.
        private static int Node(int track, TrackEnd end, ConsistEnd leader) => ((track * 2 + (int)end) << 1) | (int)leader;

        private static int TrackOf(int node) => node >> 2;

        private static TrackEnd EndOf(int node) => (TrackEnd)((node >> 1) & 1);

        private static ConsistEnd LeaderOf(int node) => (ConsistEnd)(node & 1);

        public Route Find(RouteRequest request, out RouteFailure failure, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (cancellationToken.IsCancellationRequested)
            {
                failure = RouteFailure.Cancelled;
                return null;
            }
            System.Threading.Interlocked.Increment(ref searches);
            if (!ValidPosition(request.EndA) || !ValidPosition(request.EndB) || !ValidPosition(request.Goal))
            {
                failure = RouteFailure.BadTrack;
                return null;
            }

            var goalNode = graph.TrackCount * 4;
            var states = new Dictionary<int, NodeState>();
            var reversalRuns = new Dictionary<int, Run>();
            var runCache = new Dictionary<int, Run>();
            var runs = new RunSearch(request, cancellationToken);
            var backOnto = new Run[2];
            var pullsClear = new Dictionary<int, (Run Pull, List<RoutePiece> Back, double Room)>();
            var heap = new MinHeap();
            var goalTrack = request.Goal.Track;
            var goalLength = graph.Length(goalTrack);
            var goalStart = graph.EndPoint(goalTrack, TrackEnd.Start);
            var goalEnd = graph.EndPoint(goalTrack, TrackEnd.End);
            double GoalStop(double entryOffset) =>
                request.GoalIntoTrack.HasValue
                    ? SidingStop(goalLength, entryOffset, request.GoalIntoTrack.Value, request.Goal.Offset)
                    : request.Goal.Offset;
            var goalFromStart = GoalStop(0);
            var goalFromEnd = goalLength - GoalStop(goalLength);
            var blocked = request.Blocked;
            double goalSpanLow = request.Goal.Offset, goalSpanHigh = request.Goal.Offset;
            var hasGoalCarSpan = false;
            if (blocked != null && request.GoalCarSpan.HasValue)
            {
                var span = request.GoalCarSpan.Value;
                goalSpanLow = Math.Max(0, Math.Min(span.Low, span.High) - GoalEdgeMargin);
                goalSpanHigh = Math.Min(goalLength, Math.Max(span.Low, span.High) + GoalEdgeMargin);
                hasGoalCarSpan = true;
            }
            bool ApproachBlocked(int track, double from, double to)
            {
                if (blocked == null)
                    return false;
                if (!hasGoalCarSpan)
                    return blocked(track, from, to);
                if (from <= to)
                {
                    if (to <= goalSpanLow || from >= goalSpanHigh)
                        return blocked(track, from, to);
                    return from < goalSpanLow && blocked(track, from, goalSpanLow);
                }
                if (to >= goalSpanHigh || from <= goalSpanLow)
                    return blocked(track, to, from);
                return from > goalSpanHigh && blocked(track, goalSpanHigh, from);
            }

            double Shove(ConsistEnd leader, double meters) => request.ShovingEnd == leader ? meters * request.ShovingCost : 0;

            double Heuristic(int node)
            {
                if (node == goalNode)
                    return 0;
                var p = graph.EndPoint(TrackOf(node), EndOf(node));
                return Math.Min(p.DistanceTo(goalStart) + goalFromStart, p.DistanceTo(goalEnd) + goalFromEnd);
            }

            void Relax(int node, double cost, int parent, StepKind kind, ConsistEnd startEnd, TrackLink link)
            {
                if (double.IsNaN(cost) || double.IsInfinity(cost))
                    return;
                if (states.TryGetValue(node, out var s) && s.Cost <= cost)
                    return;
                states[node] = new NodeState { Cost = cost, Parent = parent, Kind = kind, StartEnd = startEnd, Link = link };
                heap.Push(cost + Heuristic(node), node);
            }

            var anyStart = false;
            foreach (var end in new[] { ConsistEnd.A, ConsistEnd.B })
            {
                if (request.FirstLeader.HasValue && request.FirstLeader.Value != end)
                    continue;
                anyStart = true;
                var p = end == ConsistEnd.A ? request.EndA : request.EndB;
                var d = end == ConsistEnd.A ? request.OutwardA : request.OutwardB;
                var exit = d.ExitEnd();
                var exitOffset = graph.OffsetOf(p.Track, exit);
                var toExit = Math.Abs(exitOffset - p.Offset);
                // Cars beyond the leading end bar that way off the track (a margin skips its own coupler), but not the goal short of them.
                var ahead = p.Offset + (int)d * StartMargin;
                var barred = blocked != null && toExit > StartMargin && blocked(p.Track, ahead, exitOffset);
                var directStop = p.Track == goalTrack ? GoalStop(p.Offset) : request.Goal.Offset;
                if (p.Track == goalTrack && (directStop - p.Offset) * (int)d >= 0
                    && (!request.GoalDirection.HasValue || request.GoalDirection.Value == d)
                    && (!request.LastLeader.HasValue || request.LastLeader.Value == end)
                    && (Math.Abs(directStop - p.Offset) <= StartMargin || !ApproachBlocked(p.Track, ahead, directStop)))
                {
                    var direct = Math.Abs(directStop - p.Offset);
                    Relax(goalNode, direct + Shove(end, direct), -1, StepKind.Start, end, default);
                }
                // The node for this start: the leading end at the exit of its track.
                if (!barred)
                    Relax(Node(p.Track, exit, end), toExit + Shove(end, toExit), -1, StepKind.Start, end, default);
            }
            if (!anyStart)
            {
                failure = RouteFailure.NoPath;
                return null;
            }

            foreach (var end in new[] { ConsistEnd.A, ConsistEnd.B })
            {
                var other = end.Other();
                var p = end == ConsistEnd.A ? request.EndA : request.EndB;
                var d = end == ConsistEnd.A ? request.OutwardA : request.OutwardB;
                var q = end == ConsistEnd.A ? request.EndB : request.EndA;
                var dq = end == ConsistEnd.A ? request.OutwardB : request.OutwardA;

                // A goal under the train, behind this end: the other end leads until this one
                // stands at it. The train ends where it would have had this end led it there.
                var back = (p.Offset - request.Goal.Offset) * (int)d;
                if (request.AllowBackOnto && p.Track == goalTrack && back > StartMargin && back < request.TrainLength
                    && (!request.FirstLeader.HasValue || request.FirstLeader.Value == other)
                    && (!request.GoalDirection.HasValue || request.GoalDirection.Value == d)
                    && (!request.LastLeader.HasValue || request.LastLeader.Value == end))
                {
                    var run = FindRunFrom(q, dq, back, runs);
                    if (run != null)
                    {
                        backOnto[(int)other] = run;
                        Relax(goalNode, back + Shove(other, back) + run.Penalty, -1, StepKind.StartBack, other, default);
                    }
                }

                // A train standing across a switch: this end pulls on until the other is clear
                // past the points, which may then lead back through them by another branch.
                // Without it the only reversals are past a link the leading end has crossed,
                // and a train with its tail still over a switch must run to the far end of
                // the lead it stands on to find one.
                if (request.AllowReversals && (!request.FirstLeader.HasValue || request.FirstLeader.Value == end))
                    SeedPullsClear(end, p, d, q, dq);
            }

            void SeedPullsClear(ConsistEnd puller, TrackPosition from, TrackDirection outward, TrackPosition tail, TrackDirection tailOutward)
            {
                // The train's own cars, tail to head, as far as they can be followed, and the
                // switches they lie across from a branch onto the stem.
                var body = new List<RoutePiece>();
                List<(int Index, double At, int Track, TrackEnd End)> stems = null;
                var track = tail.Track;
                var offset = tail.Offset;
                var dir = tailOutward.Reverse();
                var covered = 0.0;
                var whole = false;
                for (var hop = 0; hop < MaxUnderTrain; hop++)
                {
                    var exit = dir.ExitEnd();
                    var exitOffset = graph.OffsetOf(track, exit);
                    var span = Math.Abs(exitOffset - offset);
                    if (covered + span >= request.TrainLength - StartMargin)
                    {
                        // The head's own track. The train's length along the track and its two ends
                        // as given may differ by a little: the head is where it was said to be.
                        whole = track == from.Track && (from.Offset - offset) * (int)dir >= 0;
                        var head = whole ? from.Offset : offset + (int)dir * Math.Min(span, request.TrainLength - covered);
                        body.Add(new RoutePiece(track, offset, head));
                        covered += Math.Abs(head - offset);
                        break;
                    }
                    body.Add(new RoutePiece(track, offset, exitOffset));
                    covered += span;
                    var links = graph.Links(track, exit);
                    TrackLink? next = null;
                    if (links.Length == 1)
                    {
                        next = links[0];
                    }
                    else if (request.SwitchLie != null)
                    {
                        foreach (var l in links)
                        {
                            if (l.NeedsJunction && request.SwitchLie(l.Junction) == l.Branch)
                                next = l;
                        }
                    }
                    if (next == null)
                        break;
                    var onto = next.Value;
                    // Onto a stem from one of its branches: clear past these points, the tail has the other branches.
                    if (graph.Links(onto.Track, onto.End).Length > 1)
                        (stems ?? (stems = new List<(int, double, int, TrackEnd)>())).Add((body.Count, covered, onto.Track, onto.End));
                    track = onto.Track;
                    offset = graph.OffsetOf(track, onto.End);
                    dir = onto.End.DirectionInto();
                }
                if (stems == null)
                    return;
                foreach (var stem in stems)
                {
                    // With the whole train followed, the tail's place past the points may lie
                    // beyond where the head stands now; otherwise only as far as the cars were followed.
                    var room = whole ? request.ReversalClearance : Math.Min(request.ReversalClearance, covered - stem.At - StartMargin);
                    if (room < MinPullClear)
                        continue;
                    var pull = stem.At + room;
                    var node = Node(stem.Track, stem.End, puller.Other());
                    var run = FindRunFrom(from, outward, pull, runs);
                    if (run == null)
                        continue;
                    var cost = pull + Shove(puller, pull) + room + Shove(puller.Other(), room) + request.ReversalPenalty + run.Penalty;
                    if (states.TryGetValue(node, out var existing) && existing.Cost <= cost)
                        continue;
                    // The tail's way back to the points: from a little beyond where it comes to rest
                    // (so it is found on the leg wherever in it the pull ends), along the train's own path.
                    var back = new List<RoutePiece>();
                    var want = room + PullClearSlack;
                    for (var i = stem.Index; i < body.Count && want > 1e-6; i++)
                        want -= Take(body[i], want, back);
                    if (whole)
                    {
                        for (var i = 0; i < run.Pieces.Count && want > 1e-6; i++)
                            want -= Take(run.Pieces[i], want, back);
                    }
                    back.Reverse();
                    for (var i = 0; i < back.Count; i++)
                        back[i] = back[i].Reversed();
                    pullsClear[node] = (run, back, room);
                    Relax(node, cost, -1, StepKind.StartReverse, puller, default);
                }
            }

            var need = request.TrainLength + request.ReversalClearance;
            while (heap.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    failure = RouteFailure.Cancelled;
                    return null;
                }
                var node = heap.Pop();
                var state = states[node];
                if (state.Closed)
                    continue;
                state.Closed = true;
                states[node] = state;
                if (node == goalNode)
                    break;

                var track = TrackOf(node);
                var end = EndOf(node);
                var leader = LeaderOf(node);
                foreach (var link in graph.Links(track, end))
                {
                    if (link.NeedsJunction && request.CanSetJunction != null && !request.CanSetJunction(link.Junction, link.Branch))
                        continue;
                    var penalty = request.TrackPenalty?.Invoke(link.Track) ?? 0;
                    if (double.IsPositiveInfinity(penalty))
                        continue;
                    if (link.NeedsJunction && link.Branch > 0)
                        penalty += request.DivergingPenalty;
                    if (link.NeedsJunction && request.JunctionPenalty != null)
                        penalty += request.JunctionPenalty(link.Junction, link.Branch);
                    var entry = graph.OffsetOf(link.Track, link.End);
                    var inward = link.End.DirectionInto();

                    if (link.Track == goalTrack && (!request.GoalDirection.HasValue || request.GoalDirection.Value == inward)
                        && (!request.LastLeader.HasValue || request.LastLeader.Value == leader))
                    {
                        var stop = GoalStop(entry);
                        if (!ApproachBlocked(link.Track, entry, stop))
                        {
                            var dist = Math.Abs(stop - entry);
                            Relax(goalNode, state.Cost + dist + Shove(leader, dist) + penalty, node, StepKind.Traverse, default, link);
                        }
                    }

                    if (blocked == null || !blocked(link.Track, 0, graph.Length(link.Track)))
                        Relax(Node(link.Track, link.End.Other(), leader), state.Cost + graph.Length(link.Track) + Shove(leader, graph.Length(link.Track)) + penalty, node, StepKind.Traverse, default, link);

                    if (!request.AllowReversals)
                        continue;
                    // A reversal: the other end leads on.
                    var reverseNode = Node(link.Track, link.End, leader.Other());
                    if (states.TryGetValue(reverseNode, out var existing) && existing.Closed)
                        continue;
                    var runKey = link.Track * 2 + (int)link.End;
                    if (!runCache.TryGetValue(runKey, out var run))
                        runCache[runKey] = run = FindRun(link.Track, link.End, need, runs);
                    if (run == null)
                        continue;
                    // The pull forward, this end leading, and the tail's way back to the points it has just cleared, the other.
                    var cost = state.Cost + need + Shove(leader, need) + request.ReversalClearance + Shove(leader.Other(), request.ReversalClearance)
                        + request.ReversalPenalty + penalty + run.Penalty;
                    if (states.TryGetValue(reverseNode, out existing) && existing.Cost <= cost)
                        continue;
                    reversalRuns[reverseNode] = run;
                    Relax(reverseNode, cost, node, StepKind.Reverse, default, link);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                failure = RouteFailure.Cancelled;
                return null;
            }
            if (!states.TryGetValue(goalNode, out var goalState) || !goalState.Closed)
            {
                failure = GoalUnderTrain(request) ? RouteFailure.GoalUnderTrain : RouteFailure.NoPath;
                return null;
            }

            failure = RouteFailure.None;
            var found = Reconstruct(request, states, reversalRuns, backOnto, pullsClear, goalNode);
            found.SearchCost = goalState.Cost;
            return found;
        }

        private bool ValidPosition(TrackPosition p) =>
            p.Track >= 0 && p.Track < graph.TrackCount && p.Offset >= -0.5 && p.Offset <= graph.Length(p.Track) + 0.5;

        private static bool GoalUnderTrain(RouteRequest r)
        {
            if (r.EndA.Track != r.Goal.Track || r.EndB.Track != r.Goal.Track)
                return false;
            var lo = Math.Min(r.EndA.Offset, r.EndB.Offset);
            var hi = Math.Max(r.EndA.Offset, r.EndB.Offset);
            return r.Goal.Offset > lo && r.Goal.Offset < hi;
        }

        /// <summary>A reversal's pull-forward: the pieces from a track end inward, with the switches it sets.</summary>
        private sealed class Run
        {
            public readonly List<RoutePiece> Pieces = new List<RoutePiece>();
            public readonly List<SwitchSetting> Switches = new List<SwitchSetting>();
            public double Penalty;
        }

        /// <summary>One request's pull-forward searches: its limits, and the runs already known not to be there.</summary>
        private sealed class RunSearch
        {
            public readonly RouteRequest Request;
            public readonly CancellationToken CancellationToken;

            /// <summary>By track end entered: the shortest run found not to exist beyond it (a longer one doesn't either).</summary>
            public readonly Dictionary<int, double> NoRoom = new Dictionary<int, double>();

            public int Budget;

            /// <summary>Set when a search gave up at its limits rather than at the end of track: what it didn't find isn't known to be missing.</summary>
            public bool CutShort;

            public RunSearch(RouteRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                CancellationToken = cancellationToken;
            }
        }

        /// <summary>
        /// A path of exactly <paramref name="need"/> meters into a track from
        /// the end it's entered by, through switches as needed; null where
        /// every way ends in a buffer stop first.
        /// </summary>
        private Run FindRun(int track, TrackEnd entered, double need, RunSearch search)
        {
            var run = new Run();
            search.Budget = MaxRunSteps;
            return Extend(track, entered, need, 0, 0, run, search) ? run : null;
        }

        /// <summary>The same from a point on a track, the way given; the cars standing at the point itself (the train's own end) aside.</summary>
        private Run FindRunFrom(TrackPosition from, TrackDirection direction, double need, RunSearch search)
        {
            var request = search.Request;
            var run = new Run();
            search.Budget = MaxRunSteps;
            var exit = direction.ExitEnd();
            var exitOffset = graph.OffsetOf(from.Track, exit);
            var toExit = Math.Abs(exitOffset - from.Offset);
            var sign = (int)direction;
            var ahead = from.Offset + sign * StartMargin;
            if (toExit >= need)
            {
                if (request.Blocked != null && need > StartMargin && request.Blocked(from.Track, ahead, from.Offset + sign * need))
                    return null;
                run.Pieces.Add(new RoutePiece(from.Track, from.Offset, from.Offset + sign * need));
                return run;
            }
            if (request.Blocked != null && toExit > StartMargin && request.Blocked(from.Track, ahead, exitOffset))
                return null;
            run.Pieces.Add(new RoutePiece(from.Track, from.Offset, exitOffset));
            return Onward(from.Track, exit, need - toExit, toExit, 0, run, search) ? run : null;
        }

        private bool Extend(int track, TrackEnd entered, double remaining, double travelled, int depth, Run run, RunSearch search)
        {
            if (search.CancellationToken.IsCancellationRequested)
            {
                search.CutShort = true;
                return false;
            }
            var key = track * 2 + (int)entered;
            if (search.NoRoom.TryGetValue(key, out var known) && remaining >= known)
                return false;
            if (--search.Budget < 0)
            {
                search.CutShort = true;
                return false;
            }
            var request = search.Request;
            var length = graph.Length(track);
            var from = graph.OffsetOf(track, entered);
            var sign = (int)entered.DirectionInto();
            if (length >= remaining)
            {
                if (request.Blocked != null && request.Blocked(track, from, from + sign * remaining))
                    return false;
                run.Pieces.Add(new RoutePiece(track, from, from + sign * remaining));
                return true;
            }
            if (request.Blocked != null && request.Blocked(track, 0, length))
                return false;
            if (depth >= MaxRunDepth)
            {
                search.CutShort = true;
                return false;
            }
            run.Pieces.Add(new RoutePiece(track, from, from + sign * length));
            var cutBefore = search.CutShort;
            search.CutShort = false;
            if (Onward(track, entered.Other(), remaining - length, travelled + length, depth, run, search))
            {
                search.CutShort = cutBefore;
                return true;
            }
            // Every way on from here ran out of track: no run this long, or longer, starts here.
            if (!search.CutShort && (!search.NoRoom.TryGetValue(key, out known) || remaining < known))
                search.NoRoom[key] = remaining;
            search.CutShort |= cutBefore;
            run.Pieces.RemoveAt(run.Pieces.Count - 1);
            return false;
        }

        /// <summary>Carries a run on past a track's end, by each link that may be taken, the first branch first: through routes are likelier long.</summary>
        private bool Onward(int track, TrackEnd exit, double remaining, double travelled, int depth, Run run, RunSearch search)
        {
            var request = search.Request;
            foreach (var link in OrderByBranch(graph.Links(track, exit)))
            {
                if (link.NeedsJunction && request.CanSetJunction != null && !request.CanSetJunction(link.Junction, link.Branch))
                    continue;
                var penalty = request.TrackPenalty?.Invoke(link.Track) ?? 0;
                if (double.IsPositiveInfinity(penalty))
                    continue;
                if (link.NeedsJunction && request.JunctionPenalty != null)
                    penalty += request.JunctionPenalty(link.Junction, link.Branch);
                var switchCount = run.Switches.Count;
                if (link.NeedsJunction)
                    run.Switches.Add(new SwitchSetting(link.Junction, link.Branch, travelled));
                run.Penalty += penalty;
                if (Extend(link.Track, link.End, remaining, travelled, depth + 1, run, search))
                    return true;
                run.Penalty -= penalty;
                run.Switches.RemoveRange(switchCount, run.Switches.Count - switchCount);
            }
            return false;
        }

        private static IEnumerable<TrackLink> OrderByBranch(TrackLink[] links)
        {
            if (links.Length <= 1)
                return links;
            var sorted = (TrackLink[])links.Clone();
            Array.Sort(sorted, (a, b) => Math.Max(a.Branch, 0).CompareTo(Math.Max(b.Branch, 0)));
            return sorted;
        }

        /// <summary>Adds the first <paramref name="want"/> meters of a piece to a path, and returns how much that was.</summary>
        private static double Take(RoutePiece piece, double want, List<RoutePiece> into)
        {
            var length = Math.Min(piece.Length, want);
            if (length > 1e-6)
                into.Add(new RoutePiece(piece.Track, piece.From, piece.From + (int)piece.Direction * length));
            return length;
        }

        private Route Reconstruct(RouteRequest request, Dictionary<int, NodeState> states, Dictionary<int, Run> runs, Run[] backOnto,
            Dictionary<int, (Run Pull, List<RoutePiece> Back, double Room)> pullsClear, int goalNode)
        {
            // Walk back from the goal to the start, then replay forward.
            var chain = new List<int>();
            for (var n = goalNode; n != -1; n = states[n].Parent)
                chain.Add(n);
            chain.Reverse();

            var first = states[chain[0]];
            var leader = first.StartEnd;
            var start = leader == ConsistEnd.A ? request.EndA : request.EndB;
            var legs = new List<RouteLeg>();
            var pieces = new List<RoutePiece>();
            var switches = new List<SwitchSetting>();
            var distance = 0.0;
            var overlap = 0.0;

            void AddPiece(RoutePiece p)
            {
                if (p.Length <= 1e-6)
                    return;
                pieces.Add(p);
                distance += p.Length;
            }

            if (chain[0] == goalNode && first.Kind == StepKind.StartBack)
            {
                // The leader runs on until the other end stands at the goal.
                var back = backOnto[(int)leader];
                return new Route(new[] { new RouteLeg(back.Pieces, back.Switches, leader, false) });
            }

            if (chain[0] == goalNode)
            {
                var directStop = request.GoalIntoTrack.HasValue
                    ? SidingStop(graph.Length(start.Track), start.Offset, request.GoalIntoTrack.Value, request.Goal.Offset)
                    : request.Goal.Offset;
                AddPiece(new RoutePiece(start.Track, start.Offset, directStop));
                if (pieces.Count == 0)
                    pieces.Add(new RoutePiece(start.Track, start.Offset, directStop));
                legs.Add(new RouteLeg(pieces, switches, leader, false));
                return new Route(legs);
            }

            // The starting piece stays even when empty (the leading end right at a
            // track's end), so the engineer finds the train on its own route.
            if (first.Kind == StepKind.StartReverse)
            {
                // The leader pulls the train clear of the switch its tail stands across; the tail
                // then leads the next leg from where it has come to rest, back to the points.
                var (pull, back, room) = pullsClear[chain[0]];
                legs.Add(new RouteLeg(pull.Pieces, pull.Switches, leader, true));
                leader = leader.Other();
                foreach (var p in back)
                {
                    if (pieces.Count > 0)
                    {
                        var before = pieces[pieces.Count - 1];
                        if (before.Track == p.Track && before.Direction == p.Direction && Math.Abs(before.To - p.From) < 1e-6)
                        {
                            // The train's own track and the run on from it, end to end: one piece.
                            pieces[pieces.Count - 1] = new RoutePiece(p.Track, before.From, p.To);
                            distance += p.Length;
                            continue;
                        }
                        if (graph.TryFindLink(before.Track, before.Direction.ExitEnd(), p.Track, out var joint) && joint.NeedsJunction)
                            switches.Add(new SwitchSetting(joint.Junction, joint.Branch, distance));
                    }
                    AddPiece(p);
                }
                overlap = Math.Max(0, distance - room);
                if (pieces.Count == 0)
                {
                    var points = graph.OffsetOf(TrackOf(chain[0]), EndOf(chain[0]));
                    pieces.Add(new RoutePiece(TrackOf(chain[0]), points, points));
                }
            }
            else
            {
                var startExit = EndOf(chain[0]);
                pieces.Add(new RoutePiece(start.Track, start.Offset, graph.OffsetOf(start.Track, startExit)));
                distance += pieces[0].Length;
            }

            for (var i = 1; i < chain.Count; i++)
            {
                var node = chain[i];
                var s = states[node];
                var link = s.Link;
                var entry = graph.OffsetOf(link.Track, link.End);
                if (link.NeedsJunction)
                    switches.Add(new SwitchSetting(link.Junction, link.Branch, distance));

                if (node == goalNode)
                {
                    var stop = request.GoalIntoTrack.HasValue
                        ? SidingStop(graph.Length(link.Track), entry, request.GoalIntoTrack.Value, request.Goal.Offset)
                        : request.Goal.Offset;
                    AddPiece(new RoutePiece(link.Track, entry, stop));
                    break;
                }

                if (s.Kind == StepKind.Traverse)
                {
                    AddPiece(new RoutePiece(link.Track, entry, graph.OffsetOf(link.Track, link.End.Other())));
                    continue;
                }

                // A reversal: pull forward the run, stop, and start a new leg back along it.
                var run = runs[node];
                var runStart = distance;
                foreach (var sw in run.Switches)
                    switches.Add(new SwitchSetting(sw.Junction, sw.Branch, runStart + sw.AtDistance));
                foreach (var p in run.Pieces)
                    AddPiece(p);
                if (pieces.Count == 0)
                    pieces.Add(new RoutePiece(link.Track, entry, entry));
                legs.Add(new RouteLeg(pieces, switches, leader, true, overlap));

                leader = leader.Other();
                pieces = new List<RoutePiece>();
                switches = new List<SwitchSetting>();
                distance = 0;
                // The new leg begins where the old leading end stopped, so the
                // new leading end (the old rear) is found on it wherever the
                // train actually stands; the switches of the run come up again.
                var runLength = 0.0;
                foreach (var p in run.Pieces)
                    runLength += p.Length;
                overlap = Math.Max(0, runLength - request.ReversalClearance);
                for (var k = run.Pieces.Count - 1; k >= 0; k--)
                    AddPiece(run.Pieces[k].Reversed());
                for (var k = run.Switches.Count - 1; k >= 0; k--)
                    switches.Add(new SwitchSetting(run.Switches[k].Junction, run.Switches[k].Branch, runLength - run.Switches[k].AtDistance));
            }

            if (pieces.Count == 0)
                pieces.Add(new RoutePiece(request.Goal.Track, request.Goal.Offset, request.Goal.Offset));
            legs.Add(new RouteLeg(pieces, switches, leader, false, overlap));
            return new Route(legs);
        }

        /// <summary>A binary min-heap of node indices by priority.</summary>
        private sealed class MinHeap
        {
            private readonly List<(double Priority, int Node)> items = new List<(double, int)>();

            public int Count => items.Count;

            public void Push(double priority, int node)
            {
                items.Add((priority, node));
                var i = items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (items[parent].Priority <= items[i].Priority)
                        break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                var top = items[0].Node;
                var last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                var i = 0;
                while (true)
                {
                    var l = 2 * i + 1;
                    var r = l + 1;
                    var m = i;
                    if (l < items.Count && items[l].Priority < items[m].Priority)
                        m = l;
                    if (r < items.Count && items[r].Priority < items[m].Priority)
                        m = r;
                    if (m == i)
                        break;
                    (items[m], items[i]) = (items[i], items[m]);
                    i = m;
                }
                return top;
            }
        }
    }
}
