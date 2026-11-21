using System;
using System.Collections.Generic;
using CommsTablet.Core.Ahead;
using CommsTablet.Core.Routing;
using CommsTablet.Core.Tracks;
using UnityEngine;

namespace CommsTablet.Map
{
    /// <summary>
    /// Keeps a route for the train and its goal. Searches run in the
    /// background from a copy of the switches and standing cars; meanwhile the
    /// last route stays up, for as long as the train is on it. The route is
    /// searched again when the goal changes, when the train leaves it, and
    /// when the train has stopped to reverse or sets off the other way.
    /// </summary>
    internal static class AheadRoute
    {
        /// <summary>How far off its route the train's leading end may stand, in meters.</summary>
        private const double OnRoute = 5;

        /// <summary>How near a reversal's place counts as there, in meters, and the speed under which a train has stopped, in m/s.</summary>
        private const double AtReversal = 25, Stopped = 1;

        /// <summary>The least time between searches for one goal, in seconds.</summary>
        private const float SearchGap = 3;

        /// <summary>How far in past the whole train a destination's stop lies, in meters.</summary>
        private const double ArrivalMargin = 5;

        private const float MovingCar = 1;

        private static AheadGoal goal;
        private static AheadGoal destination;
        private static Route route;
        private static RouteSearch search;
        private static ConsistEnd? searchLeader;
        private static float nextSearch;
        private static bool throughCars;
        private static bool warned;
        private static bool lastStale = true;

        /// <summary>Monotonic revision bumped whenever the route, note, or destination state changes.</summary>
        internal static int Revision { get; private set; }

        /// <summary>Whether a valid route is held with no search running and the last follow did not need a new search.</summary>
        internal static bool Stable => route != null && search == null && !lastStale;

        /// <summary>A destination the player set: it comes before any job, until the train is there or the player clears it.</summary>
        internal static AheadGoal Destination
        {
            get => destination;
            set
            {
                if (ReferenceEquals(destination, value))
                    return;
                var wasActive = destination != null && destination.SameAs(goal);
                destination = value;
                if (goal != null && !goal.SameAs(destination))
                {
                    CancelSearch();
                    if (wasActive && destination == null)
                        nextSearch = 0;
                }
            }
        }

        /// <summary>Why there is no route to show, or null.</summary>
        internal static string Note { get; private set; }

        /// <summary>Whether the route runs through standing cars, there being no other way.</summary>
        internal static bool ThroughCars => route != null && throughCars;

        internal static void Forget()
        {
            CancelSearch();
            goal = null;
            destination = null;
            route = null;
            searchLeader = null;
            nextSearch = 0;
            throughCars = false;
            lastStale = true;
            Note = null;
            Revision++;
        }

        private static void CancelSearch()
        {
            if (search == null)
                return;
            search.Cancel();
            search = null;
        }

        /// <summary>The goal for this train now, or null: the aligned tracks are all there is to show.</summary>
        internal static AheadGoal GoalFor(TrackNetwork network, AheadTrain train) =>
            Destination ?? AheadGoal.ForCars(network, train.CarsFromFarthest);

        /// <summary>The way to the goal from where the train stands, or null with <see cref="Note"/> saying why.</summary>
        internal static AheadPath Follow(TrackNetwork network, AheadTrain train, AheadGoal wanted)
        {
            if (!wanted.SameAs(goal))
            {
                CancelSearch();
                goal = wanted;
                route = null;
                searchLeader = null;
                nextSearch = 0;
                throughCars = false;
                lastStale = true;
                Note = "Finding the route to " + wanted.Name;
                Revision++;
            }
            TakeResult();

            var selected = network.Junctions;
            AheadPath path = null;
            ConsistEnd? leadNext = null;
            var stale = route == null;
            if (route != null)
            {
                var leg = route.Legs[0];
                var leader = EndOf(train, leg.Leader);
                var towardLeader = SpeedToward(train, leg.Leader);
                if (leg.TryLocate(leader.At, 0, OnRoute, out var along, out var piece)
                    && (leg.Pieces[piece].Length < 1e-6 || leg.Pieces[piece].Direction == leader.Outward))
                {
                    var tail = EndOf(train, leg.Leader.Other());
                    path = AheadPath.Routed(network.Graph, tail.At, tail.Outward.Reverse(), train.Length, route, along,
                        wanted.Offset.HasValue ? (double?)null : train.Length + ArrivalMargin, j => selected[j].selectedBranch);
                    if (ReferenceEquals(wanted, Destination) && route.Legs.Count == 1 && path.Length - path.RouteStart < AtReversal && Math.Abs(towardLeader) < Stopped)
                    {
                        Destination = null;
                        Revision++;
                    }
                    var atReversal = leg.EndsInReversal && leg.Length - along < AtReversal && Math.Abs(towardLeader) < Stopped;
                    if (atReversal || towardLeader < -Stopped)
                    {
                        leadNext = leg.Leader.Other();
                        stale = true;
                    }
                }
                else
                    stale = true;
            }
            lastStale = stale;
            if (stale && search == null && Time.realtimeSinceStartup >= nextSearch)
                Start(network, train, wanted, leadNext);
            return path;
        }

        private static void TakeResult()
        {
            if (search == null || !search.Done)
                return;
            var done = search;
            search = null;
            if (done.Cancelled || done.Failure == RouteFailure.Cancelled)
                return;
            if (done.Error != null)
            {
                if (!warned)
                    Main.Mod?.Logger.LogException("Track Ahead: a route search failed", done.Error);
                warned = true;
            }
            if (done.Route != null)
            {
                route = done.Route;
                throughCars = done.ThroughCars;
                Note = null;
                Revision++;
                return;
            }
            // With the leading end asked for and no way from it, the route in hand stands.
            if (searchLeader.HasValue && route != null)
                return;
            route = null;
            var there = done.Failure == RouteFailure.GoalUnderTrain;
            if (there && goal.SameAs(Destination))
                Destination = null;
            Note = there ? "The train is on " + goal.Name : "No way found to " + goal.Name;
            Revision++;
        }

        private static void Start(TrackNetwork network, AheadTrain train, AheadGoal wanted, ConsistEnd? leader)
        {
            CancelSearch();
            nextSearch = Time.realtimeSinceStartup + SearchGap;
            searchLeader = leader;
            search = new RouteSearch(network.Graph, RequestFor(network, train, wanted, leader));
        }

        /// <summary>What to search for, with copies of what the search must not read from the game itself. Main thread.</summary>
        internal static RouteRequest RequestFor(TrackNetwork network, AheadTrain train, AheadGoal wanted, ConsistEnd? leader)
        {
            var junctions = network.Junctions;
            var lie = new int[junctions.Length];
            for (var i = 0; i < lie.Length; i++)
                lie[i] = junctions[i].selectedBranch;
            var standing = StandingCars(network, train);
            var front = train.LocoFrontEnd;
            var rear = train.LocoRearEnd;
            ConsistEnd? shoving = null;
            if (front.Car.IsLoco != rear.Car.IsLoco)
                shoving = front.Car.IsLoco ? ConsistEnd.B : ConsistEnd.A;
            return new RouteRequest
            {
                EndA = front.At,
                OutwardA = front.Outward,
                EndB = rear.At,
                OutwardB = rear.Outward,
                TrainLength = train.Length,
                Goal = new TrackPosition(wanted.Track, wanted.Offset ?? network.Graph.Length(wanted.Track) / 2),
                GoalIntoTrack = wanted.Offset.HasValue ? (double?)null : train.Length + ArrivalMargin,
                FirstLeader = leader,
                ShovingEnd = shoving,
                SwitchLie = j => lie[j],
                Blocked = (track, from, to) => IsBlocked(standing, track, from, to),
            };
        }

        // Every car at a stand that is not this train's, by track.
        private static Dictionary<int, List<(double Low, double High)>> StandingCars(TrackNetwork network, AheadTrain train)
        {
            var own = new HashSet<TrainCar>(train.CarsFromFarthest);
            var result = new Dictionary<int, List<(double, double)>>();
            var spans = new List<(int Track, double From, double To)>();
            foreach (var car in DV.Utils.SingletonBehaviour<CarSpawner>.Instance.AllCars)
            {
                if (car == null || own.Contains(car) || Mathf.Abs(car.GetForwardSpeed()) > MovingCar)
                    continue;
                spans.Clear();
                TrainSpans.SpansOf(network, car, spans);
                foreach (var (track, from, to) in spans)
                {
                    if (!result.TryGetValue(track, out var list))
                        result[track] = list = new List<(double, double)>();
                    list.Add((Math.Min(from, to), Math.Max(from, to)));
                }
            }
            return result;
        }

        private static bool IsBlocked(Dictionary<int, List<(double Low, double High)>> standing, int track, double from, double to)
        {
            if (!standing.TryGetValue(track, out var cars))
                return false;
            var low = Math.Min(from, to);
            var high = Math.Max(from, to);
            foreach (var (carLow, carHigh) in cars)
            {
                if (carLow <= high && carHigh >= low)
                    return true;
            }
            return false;
        }

        private static TrainEnd EndOf(AheadTrain train, ConsistEnd end) => end == ConsistEnd.A ? train.LocoFrontEnd : train.LocoRearEnd;

        /// <summary>The train's speed toward this end, in m/s.</summary>
        internal static double SpeedToward(AheadTrain train, ConsistEnd end) =>
            end == ConsistEnd.A ? train.SpeedTowardLocoFront : -train.SpeedTowardLocoFront;

        /// <summary>The end leading the route now, if there is a route.</summary>
        internal static ConsistEnd? Leader => route?.Legs[0].Leader;
    }
}
