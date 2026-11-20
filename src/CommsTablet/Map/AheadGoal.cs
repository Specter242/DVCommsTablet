using System;
using System.Collections.Generic;
using CommsTablet.Core.Tracks;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;

namespace CommsTablet.Map
{
    /// <summary>
    /// Where Track Ahead's route leads: a track, and what sent the train
    /// there. The player may pick one on the Switches map; otherwise a train's
    /// goal is its end car's job (decision 21): from the car
    /// farthest from the loco inward, the first car whose taken job still has a
    /// move for it. A job is a tree of tasks; a sequence works its first task
    /// not done, a parallel all of them (AI Engineer's <c>Jobs.Read</c>).
    /// </summary>
    internal sealed class AheadGoal
    {
        /// <summary>From this length, in meters, a track is too long to mean "anywhere on it".</summary>
        private const double LongLine = 1000;

        public int Track;

        /// <summary>The track's name on the job's booklet, "HB-F2L".</summary>
        public string Name;

        /// <summary>What the row under the name says.</summary>
        public string Why;

        /// <summary>The job, or null for a goal the player set.</summary>
        public string JobId;

        /// <summary>The place on the track to stop, or null to bring the whole train onto it.</summary>
        public double? Offset;

        public bool SameAs(AheadGoal other) => other != null && other.Track == Track && other.JobId == JobId && other.Offset == Offset;

        /// <summary>
        /// The goal for a place the player pressed. A long stretch of line is
        /// a place to stop at; anything shorter, a track to bring the train onto.
        /// </summary>
        internal static AheadGoal ForPlace(TrackNetwork network, TrackPosition place, string where)
        {
            var rail = network.Tracks[place.Track];
            var name = TrackName(rail.LogicTrack());
            // Tracks the booklet does not number have only an internal ID ("#Y-#S203#T").
            if (string.IsNullOrEmpty(name) || name == "?" || name.IndexOf('#') >= 0)
                name = where != null ? "Track at " + where : "Main line";
            return new AheadGoal
            {
                Track = place.Track,
                Name = name,
                Why = "Your destination",
                Offset = network.Graph.Length(place.Track) >= LongLine ? place.Offset : (double?)null,
            };
        }

        /// <summary>The goal the first of these cars with a move to make gives, or null.</summary>
        internal static AheadGoal ForCars(TrackNetwork network, IReadOnlyList<TrainCar> carsFromFarthest)
        {
            var jobs = SingletonBehaviour<JobsManager>.Instance;
            if (jobs == null)
                return null;
            var leaves = new List<Task>();
            foreach (var car in carsFromFarthest)
            {
                var logic = car != null ? car.logicCar : null;
                var job = logic != null ? jobs.GetJobOfCar(logic, true) : null;
                if (job == null || job.State != JobState.InProgress)
                    continue;
                leaves.Clear();
                foreach (var task in job.tasks)
                    Current(task, leaves);
                foreach (var task in leaves)
                {
                    var goal = Move(network, job, task, logic);
                    if (goal != null)
                        return goal;
                }
            }
            return null;
        }

        private static AheadGoal Move(TrackNetwork network, Job job, Task task, Car car)
        {
            var type = task.InstanceTaskType;
            if (type != TaskType.Transport && type != TaskType.Warehouse)
                return null;
            var data = task.GetTaskData();
            var to = data.destinationTrack;
            if (to == null || data.cars == null || !data.cars.Contains(car) || car.CurrentTrack == to)
                return null;
            var rail = to.RailTrack();
            if (rail == null || !network.TryGetTrack(rail, out var index))
                return null;
            string why;
            if (type == TaskType.Transport)
                why = "Job destination";
            else
                why = data.warehouseTaskType == WarehouseTaskType.Loading ? "Load here" : "Unload here";
            return new AheadGoal { Track = index, Name = TrackName(to), Why = why, JobId = job.ID };
        }

        private static void Current(Task task, List<Task> into)
        {
            if (task == null || task.state == TaskState.Done)
                return;
            switch (task.InstanceTaskType)
            {
                case TaskType.Sequential:
                    foreach (var t in task.GetTaskData().nestedTasks)
                    {
                        if (t != null && t.state != TaskState.Done)
                        {
                            Current(t, into);
                            return;
                        }
                    }
                    return;
                case TaskType.Parallel:
                    foreach (var t in task.GetTaskData().nestedTasks)
                        Current(t, into);
                    return;
                default:
                    into.Add(task);
                    return;
            }
        }

        private static string TrackName(Track track)
        {
            try
            {
                return track?.ID != null ? track.ID.FullDisplayID : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }
}
