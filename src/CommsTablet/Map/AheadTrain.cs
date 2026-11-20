using System.Collections.Generic;
using CommsTablet.Core.Tracks;
using UnityEngine;

namespace CommsTablet.Map
{
    /// <summary>
    /// The train Track Ahead looks ahead of, and which way: the player's car's
    /// train, or their last loco's when they are out of it. A moving train
    /// looks the way it moves; a standing one the way its reverser is set, or
    /// the way the player last turned the view.
    /// </summary>
    internal sealed class AheadTrain
    {
        private const float MovingSpeed = 0.5f;
        private const int MostCars = 1000;

        private static TrainCar lastLoco;
        private static int facing = 1;
        private static int lastReverser;

        /// <summary>Where the rear of the train is, and the way along that track toward its front.</summary>
        public TrackPosition Rear;
        public TrackDirection Toward;

        /// <summary>Coupler to coupler, in meters.</summary>
        public double Length;

        /// <summary>The train's speed the way it looks, in m/s; negative if it is rolling back.</summary>
        public double Speed;

        public int Cars;
        public TrainCar Loco;

        /// <summary>The train's coupled ends, named from its loco and so the same whichever way it runs.</summary>
        public TrainEnd LocoFrontEnd, LocoRearEnd;

        /// <summary>The cars, the farthest from the loco first; the loco is last.</summary>
        public readonly List<TrainCar> CarsFromFarthest = new List<TrainCar>();

        /// <summary>The speed toward the end ahead of the loco's front, in m/s.</summary>
        public double SpeedTowardLocoFront;

        /// <summary>Turns the view to the train's other end, until the train or its reverser says otherwise.</summary>
        internal static void Turn() => facing = -facing;

#if !PUBLIC_RELEASE
        /// <summary>As if the player had no train, for the self-test.</summary>
        internal static bool NoTrainForTest;
#endif

        internal static void Forget()
        {
            lastLoco = null;
            facing = 1;
            lastReverser = 0;
        }

        /// <summary>The train now, or null with what to tell the player.</summary>
        internal static AheadTrain Read(TrackNetwork network, out string why)
        {
            var car = PlayerManager.Car != null ? PlayerManager.Car : PlayerManager.LastLoco;
#if !PUBLIC_RELEASE
            if (NoTrainForTest)
                car = null;
#endif
            if (car == null || car.trainset == null)
            {
                why = "Board a train to see the track ahead";
                return null;
            }
            var loco = LocoOf(car);
            if (loco != lastLoco)
            {
                lastLoco = loco;
                facing = 1;
                lastReverser = 0;
            }
            var speed = loco.GetForwardSpeed();
            if (Mathf.Abs(speed) > MovingSpeed)
                facing = speed > 0 ? 1 : -1;
            else
            {
                var reverser = ReverserOf(loco);
                if (reverser != 0 && reverser != lastReverser)
                    facing = reverser;
                lastReverser = reverser;
            }

            // From the loco out to each end, counting every car once.
            var length = (double)loco.InterCouplerDistance;
            var cars = 1;
            var reached = new List<(TrainCar Car, double Distance)>();
            var frontCar = End(loco, true, ref length, ref cars, reached, out var frontIsFront);
            var rearCar = End(loco, false, ref length, ref cars, reached, out var rearIsFront);
            if (!TrainSpans.CouplerEnd(network, frontCar, frontIsFront, out var frontAt, out var frontOut)
                || !TrainSpans.CouplerEnd(network, rearCar, rearIsFront, out var rearAt, out var rearOut))
            {
                why = frontCar.derailed || rearCar.derailed ? "The train is derailed" : "The train is not on the track network";
                return null;
            }
            why = null;
            var train = new AheadTrain
            {
                Rear = facing > 0 ? rearAt : frontAt,
                Toward = (facing > 0 ? rearOut : frontOut).Reverse(),
                Length = length,
                Speed = speed * facing,
                Cars = cars,
                Loco = loco,
                LocoFrontEnd = new TrainEnd(frontCar, frontAt, frontOut),
                LocoRearEnd = new TrainEnd(rearCar, rearAt, rearOut),
                SpeedTowardLocoFront = speed,
            };
            reached.Sort((a, b) => b.Distance.CompareTo(a.Distance));
            foreach (var (car2, _) in reached)
                train.CarsFromFarthest.Add(car2);
            train.CarsFromFarthest.Add(loco);
            return train;
        }

        private static TrainCar LocoOf(TrainCar car)
        {
            var set = car.trainset;
            var index = set.cars.IndexOf(car);
            if (set.locoIndices.Contains(index))
                return car;
            if (PlayerManager.LastLoco != null && PlayerManager.LastLoco.trainset == set)
                return PlayerManager.LastLoco;
            return set.locoIndices.Count > 0 ? set.cars[set.locoIndices[0]] : car;
        }

        /// <summary>1 toward the loco's front, -1 toward its rear, 0 in neutral or without one.</summary>
        private static int ReverserOf(TrainCar loco)
        {
            var control = loco.SimController != null && loco.SimController.controlsOverrider != null ? loco.SimController.controlsOverrider.Reverser : null;
            if (control == null)
                return 0;
            var value = control.Value;
            return value > 0.6f ? 1 : value < 0.4f ? -1 : 0;
        }

        // The last car out from the loco's front or rear, and which of its couplers is the outer one.
        private static TrainCar End(TrainCar loco, bool front, ref double length, ref int cars, List<(TrainCar Car, double Distance)> reached, out bool outerIsFront)
        {
            var car = loco;
            var outer = front ? loco.frontCoupler : loco.rearCoupler;
            var distance = 0.0;
            for (var i = 0; i < MostCars && outer != null && outer.coupledTo != null && outer.coupledTo.train != null; i++)
            {
                var inner = outer.coupledTo;
                car = inner.train;
                length += car.InterCouplerDistance;
                distance += car.InterCouplerDistance;
                reached.Add((car, distance));
                cars++;
                outer = inner.isFrontCoupler ? car.rearCoupler : car.frontCoupler;
            }
            outerIsFront = outer == null || outer.isFrontCoupler;
            return car;
        }
    }

    /// <summary>One end of a train: its last car, its outer coupler, and the way along that track pointing out of the train.</summary>
    internal readonly struct TrainEnd
    {
        public readonly TrainCar Car;
        public readonly TrackPosition At;
        public readonly TrackDirection Outward;

        public TrainEnd(TrainCar car, TrackPosition at, TrackDirection outward)
        {
            Car = car;
            At = at;
            Outward = outward;
        }
    }
}
