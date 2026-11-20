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
        private static readonly Dictionary<TrainCar, CarGeometry> geometry = new Dictionary<TrainCar, CarGeometry>();
        private static readonly List<TrainCar> gone = new List<TrainCar>();

        /// <summary>
        /// The stretches of track a car covers, coupler to coupler: one span
        /// on one track, or across a joint, each bogie's track from its
        /// coupler to the joint. Nothing for a derailed car or one not on the
        /// network.
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
            var graph = network.Graph;
            var g = Geometry(car);
            var fs = front.traveller.Span;
            var rs = rear.traveller.Span;
            var fSign = front.TrackDirectionSign >= 0 ? 1 : -1;
            var rSign = rear.TrackDirectionSign >= 0 ? 1 : -1;
            var frontCoupler = Clamp(graph.Length(ft), fs + fSign * g.FrontOverhang);
            var rearCoupler = Clamp(graph.Length(rt), rs - rSign * g.RearOverhang);
            if (ft == rt)
            {
                into.Add((ft, frontCoupler, rearCoupler));
                return;
            }
            // Across a joint: each part runs from its coupler toward the other bogie, to its track's end.
            into.Add((ft, frontCoupler, fSign > 0 ? 0 : graph.Length(ft)));
            into.Add((rt, rearCoupler, rSign > 0 ? graph.Length(rt) : 0));
        }

        /// <summary>
        /// Where a car's coupler at one end lies on the network, and the way
        /// along that track points away from the car; false for a derailed
        /// car or one not on the network.
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
            var carForward = bogie.TrackDirectionSign >= 0 ? 1 : -1;
            var sign = front ? carForward : -carForward;
            var offset = bogie.traveller.Span + sign * (front ? g.FrontOverhang : g.RearOverhang);
            at = new TrackPosition(track, Clamp(network.Graph.Length(track), offset));
            outward = sign > 0 ? TrackDirection.Forward : TrackDirection.Backward;
            return true;
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
                Math.Max(0f, rearZ - car.PivotToRearCouplerZOffset));
            geometry[car] = g;
            return g;
        }

        /// <summary>At unload: every car is gone.</summary>
        public static void Forget() => geometry.Clear();

        private readonly struct CarGeometry
        {
            public readonly float FrontOverhang;
            public readonly float RearOverhang;

            public CarGeometry(float front, float rear)
            {
                FrontOverhang = front;
                RearOverhang = rear;
            }
        }
    }
}
