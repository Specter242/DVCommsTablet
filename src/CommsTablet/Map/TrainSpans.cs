using System;
using System.Collections.Generic;
using CommsTablet.Core.Tracks;
using UnityEngine;

namespace CommsTablet.Map
{
    /// <summary>
    /// Where a car stands on the track network, for drawing it on the map:
    /// AI Engineer's <c>World.SpansOf</c> and <c>World.Geometry</c>
    /// (c6e6207, src/AIEngineer/Game/World.cs), without its occupancy,
    /// interlocking, and routing.
    /// </summary>
    internal static class TrainSpans
    {
        private const int MaxOverhangHops = 32;
        private const int MaxBridgeHops = 16;
        private const int MaxBridgeStates = 64;

        private static readonly Dictionary<TrainCar, CarGeometry> geometry = new Dictionary<TrainCar, CarGeometry>();
        private static readonly List<TrainCar> gone = new List<TrainCar>();

        /// <summary>
        /// The stretches of track a car covers, coupler to coupler: the bogie
        /// tracks, any short intermediate tracks wholly between the bogies, and
        /// coupler overhang carried onto adjoining tracks when connected.
        /// Nothing for a derailed car or one not on the network.
        /// </summary>
        public static void SpansOf(TrackNetwork network, TrainCar car, List<(int Track, double From, double To)> into)
        {
            if (car == null || car.derailed || car.Bogies == null || car.Bogies.Length < 2)
                return;
            var front = car.FrontBogie;
            var rear = car.RearBogie;
            if (front.track == null || rear.track == null || front.traveller == null || rear.traveller == null)
                return;
            if (!network.TryGetTrack(front.track, out var ft) || !network.TryGetTrack(rear.track, out var rt))
                return;
            var g = Geometry(car);
            var fDir = front.TrackDirectionSign >= 0 ? TrackDirection.Forward : TrackDirection.Backward;
            var rDir = rear.TrackDirectionSign >= 0 ? TrackDirection.Forward : TrackDirection.Backward;
            SpansOnGraph(network.Graph, ft, front.traveller.Span, fDir, g.FrontOverhang,
                rt, rear.traveller.Span, rDir, g.RearOverhang,
                Math.Max(20.0, g.Wheelbase + 10.0), SelectedBranch(network), into);
        }

        /// <summary>
        /// Where a car's coupler at one end lies on the network, carrying
        /// overhang across connected track joints, and the way along that track
        /// pointing away from the car; false for a derailed car or one not on
        /// the network.
        /// </summary>
        public static bool CouplerEnd(TrackNetwork network, TrainCar car, bool front, out TrackPosition at, out TrackDirection outward)
        {
            at = default;
            outward = TrackDirection.Forward;
            if (car == null || car.derailed || car.Bogies == null || car.Bogies.Length < 1)
                return false;
            var bogie = front ? car.FrontBogie : car.RearBogie;
            if (bogie == null || bogie.track == null || bogie.traveller == null || !network.TryGetTrack(bogie.track, out var track))
                return false;
            var g = Geometry(car);
            var carForward = bogie.TrackDirectionSign >= 0 ? TrackDirection.Forward : TrackDirection.Backward;
            var direction = front ? carForward : carForward.Reverse();
            var overhang = front ? g.FrontOverhang : g.RearOverhang;
            AdvanceOverhang(network.Graph, track, bogie.traveller.Span, direction, overhang, SelectedBranch(network), out at, out outward, null);
            return true;
        }

        /// <summary>
        /// Pure graph computation of a car's physical occupancy spans from its
        /// two bogies and coupler overhangs (unit-testable without Unity).
        /// </summary>
        internal static void SpansOnGraph(
            TrackGraph graph,
            int frontTrack, double frontOffset, TrackDirection frontDir, double frontOverhang,
            int rearTrack, double rearOffset, TrackDirection rearDir, double rearOverhang,
            double maxBridgeLength, Func<int, int> selectedBranch,
            List<(int Track, double From, double To)> into)
        {
            var frontLen = graph.Length(frontTrack);
            var rearLen = graph.Length(rearTrack);
            var fs = Clamp(frontLen, frontOffset);
            var rs = Clamp(rearLen, rearOffset);
            var fSign = (int)frontDir;
            var rSign = (int)rearDir;
            var frontOnTrack = Clamp(frontLen, fs + fSign * Math.Max(0, frontOverhang));
            var rearOnTrack = Clamp(rearLen, rs - rSign * Math.Max(0, rearOverhang));

            if (frontTrack == rearTrack)
            {
                into.Add((frontTrack, frontOnTrack, rearOnTrack));
            }
            else
            {
                var frontEntry = frontDir.Reverse().ExitEnd();
                var rearExit = rearDir.ExitEnd();
                into.Add((frontTrack, frontOnTrack, graph.OffsetOf(frontTrack, frontEntry)));
                AddIntermediateTracks(graph, rearTrack, rearExit, frontTrack, frontEntry, maxBridgeLength, selectedBranch, into);
                into.Add((rearTrack, rearOnTrack, graph.OffsetOf(rearTrack, rearExit)));
            }

            // Carry coupler overhang onto adjoining tracks whenever connected.
            AdvanceOverhang(graph, frontTrack, fs, frontDir, frontOverhang, selectedBranch, out _, out _, into);
            AdvanceOverhang(graph, rearTrack, rs, rearDir.Reverse(), rearOverhang, selectedBranch, out _, out _, into);
        }

        /// <summary>
        /// Walks <paramref name="distance"/> meters from (<paramref name="track"/>,
        /// <paramref name="offset"/>) in <paramref name="direction"/>, crossing
        /// connected track joints and stopping at dead ends. Emits any spans on
        /// adjoining tracks into <paramref name="extraSpans"/> when non-null.
        /// </summary>
        internal static void AdvanceOverhang(
            TrackGraph graph,
            int track, double offset, TrackDirection direction, double distance,
            Func<int, int> selectedBranch,
            out TrackPosition endAt, out TrackDirection endDirection,
            List<(int Track, double From, double To)> extraSpans)
        {
            var trackLen = graph.Length(track);
            var clamped = Clamp(trackLen, offset);
            var remaining = Math.Max(0, distance);
            var exit = direction.ExitEnd();
            var exitOffset = graph.OffsetOf(track, exit);
            var room = Math.Abs(exitOffset - clamped);
            if (remaining <= room)
            {
                endAt = new TrackPosition(track, Clamp(trackLen, clamped + (int)direction * remaining));
                endDirection = direction;
                return;
            }

            remaining -= room;
            var curTrack = track;
            var curExit = exit;
            var curDir = direction;
            var curOffset = exitOffset;
            for (var hop = 0; hop < MaxOverhangHops && remaining > 1e-6; hop++)
            {
                if (!TryNextLink(graph, curTrack, curExit, selectedBranch, out var link))
                    break;
                curTrack = link.Track;
                curDir = link.End.DirectionInto();
                var entryOffset = graph.OffsetOf(curTrack, link.End);
                var len = graph.Length(curTrack);
                var farEnd = link.End.Other();
                var farOffset = graph.OffsetOf(curTrack, farEnd);
                if (remaining <= len)
                {
                    curOffset = Clamp(len, entryOffset + (int)curDir * remaining);
                    if (extraSpans != null && Math.Abs(curOffset - entryOffset) > 1e-6)
                        extraSpans.Add((curTrack, entryOffset, curOffset));
                    endAt = new TrackPosition(curTrack, curOffset);
                    endDirection = curDir;
                    return;
                }
                if (extraSpans != null && len > 1e-6)
                    extraSpans.Add((curTrack, entryOffset, farOffset));
                remaining -= len;
                curExit = farEnd;
                curOffset = farOffset;
            }

            endAt = new TrackPosition(curTrack, curOffset);
            endDirection = curDir;
        }

        private readonly struct BridgeNode
        {
            public readonly int Track;
            public readonly TrackEnd Entered;
            public readonly int Parent;
            public readonly int Depth;
            public readonly double Distance;

            public BridgeNode(int track, TrackEnd entered, int parent, int depth, double distance)
            {
                Track = track;
                Entered = entered;
                Parent = parent;
                Depth = depth;
                Distance = distance;
            }
        }

        // Bounded BFS from rearTrack's exit toward frontTrack's entry to mark short intermediate tracks wholly between bogies.
        private static void AddIntermediateTracks(
            TrackGraph graph, int rearTrack, TrackEnd rearExit, int frontTrack, TrackEnd frontEntry,
            double maxBridgeLength, Func<int, int> selectedBranch,
            List<(int Track, double From, double To)> into)
        {
            var initial = graph.Links(rearTrack, rearExit);
            for (var i = 0; i < initial.Length; i++)
            {
                if (initial[i].Track == frontTrack && initial[i].End == frontEntry)
                    return;
            }
            if (initial.Length == 0 || maxBridgeLength <= 0)
                return;

            var nodes = new List<BridgeNode>();
            var bestDist = new Dictionary<int, double>();
            EnqueueLinks(graph, initial, rearTrack, frontTrack, frontEntry, -1, 0, 0, maxBridgeLength, selectedBranch, nodes, bestDist, out _);

            var head = 0;
            var foundParent = -1;
            while (head < nodes.Count && foundParent < 0)
            {
                var node = nodes[head];
                var links = graph.Links(node.Track, node.Entered.Other());
                if (EnqueueLinks(graph, links, rearTrack, frontTrack, frontEntry, head, node.Depth, node.Distance, maxBridgeLength, selectedBranch, nodes, bestDist, out foundParent))
                    break;
                head++;
            }

            if (foundParent < 0)
                return;

            var path = new List<int>();
            for (var cur = foundParent; cur >= 0; cur = nodes[cur].Parent)
                path.Add(cur);
            for (var i = path.Count - 1; i >= 0; i--)
            {
                var n = nodes[path[i]];
                into.Add((n.Track, graph.OffsetOf(n.Track, n.Entered), graph.OffsetOf(n.Track, n.Entered.Other())));
            }
        }

        private static bool EnqueueLinks(
            TrackGraph graph, TrackLink[] links, int rearTrack, int frontTrack, TrackEnd frontEntry,
            int parentIndex, int parentDepth, double parentDistance, double maxBridgeLength,
            Func<int, int> selectedBranch, List<BridgeNode> nodes, Dictionary<int, double> bestDist, out int foundParent)
        {
            foundParent = -1;
            if (links.Length == 0)
                return false;

            // Check direct hit first, then prefer the currently lined branch at a switch stem.
            for (var i = 0; i < links.Length; i++)
            {
                if (links[i].Track == frontTrack && links[i].End == frontEntry)
                {
                    foundParent = parentIndex;
                    return true;
                }
            }

            var preferred = links.Length > 1 && links[0].NeedsJunction && selectedBranch != null
                ? selectedBranch(links[0].Junction)
                : -1;

            void TryEnqueue(TrackLink link)
            {
                if (parentDepth >= MaxBridgeHops || nodes.Count >= MaxBridgeStates || link.Track == rearTrack || link.Track == frontTrack)
                    return;
                var nextDist = parentDistance + graph.Length(link.Track);
                if (nextDist > maxBridgeLength)
                    return;
                var key = link.Track * 2 + (int)link.End;
                if (bestDist.TryGetValue(key, out var prevDist) && nextDist >= prevDist - 1e-6)
                    return;
                bestDist[key] = nextDist;
                nodes.Add(new BridgeNode(link.Track, link.End, parentIndex, parentDepth + 1, nextDist));
            }

            if (preferred >= 0)
            {
                for (var i = 0; i < links.Length; i++)
                    if (links[i].Branch == preferred)
                        TryEnqueue(links[i]);
                for (var i = 0; i < links.Length; i++)
                    if (links[i].Branch != preferred)
                        TryEnqueue(links[i]);
            }
            else
            {
                for (var i = 0; i < links.Length; i++)
                    TryEnqueue(links[i]);
            }
            return false;
        }

        private static bool TryNextLink(TrackGraph graph, int track, TrackEnd exit, Func<int, int> selectedBranch, out TrackLink next)
        {
            var links = graph.Links(track, exit);
            if (links.Length == 0)
            {
                next = default;
                return false;
            }
            var first = links[0];
            if (!first.NeedsJunction || links.Length == 1)
            {
                next = first;
                return true;
            }
            var selected = selectedBranch != null ? selectedBranch(first.Junction) : 0;
            for (var i = 0; i < links.Length; i++)
            {
                if (links[i].Branch == selected)
                {
                    next = links[i];
                    return true;
                }
            }
            next = first;
            return true;
        }

        private static Func<int, int> SelectedBranch(TrackNetwork network)
        {
            var junctions = network.Junctions;
            return j => j >= 0 && j < junctions.Length && junctions[j] != null ? junctions[j].selectedBranch : 0;
        }

        private static double Clamp(double length, double offset) => Math.Max(0, Math.Min(length, offset));

        /// <summary>A car's coupler overhangs beyond its outer bogies, measured once.</summary>
        private static CarGeometry Geometry(TrainCar car)
        {
            if (geometry.TryGetValue(car, out var g))
                return g;
            // Cars come and go (deleted, pooled): the destroyed ones are dropped now and then.
            if (geometry.Count >= 256)
            {
                gone.Clear();
                foreach (var known in geometry.Keys)
                    if (known == null)
                        gone.Add(known);
                foreach (var known in gone)
                    geometry.Remove(known);
                gone.Clear();
            }
            var t = car.transform;
            var frontZ = t.InverseTransformPoint(car.FrontBogie.transform.position).z;
            var rearZ = t.InverseTransformPoint(car.RearBogie.transform.position).z;
            g = new CarGeometry(
                Math.Max(0f, car.PivotToFrontCouplerZOffset - frontZ),
                Math.Max(0f, rearZ - car.PivotToRearCouplerZOffset),
                Math.Abs(frontZ - rearZ));
            geometry[car] = g;
            return g;
        }

        /// <summary>At unload: every car is gone.</summary>
        public static void Forget() => geometry.Clear();

        private readonly struct CarGeometry
        {
            public readonly float FrontOverhang;
            public readonly float RearOverhang;
            public readonly float Wheelbase;

            public CarGeometry(float front, float rear, float wheelbase)
            {
                FrontOverhang = front;
                RearOverhang = rear;
                Wheelbase = wheelbase;
            }
        }
    }
}
