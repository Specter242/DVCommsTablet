using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace CommsTablet.Core.Ahead
{
    /// <summary>What the worker made of one track.</summary>
    public readonly struct SignLimitResult
    {
        public readonly int Track;
        public readonly double TrackLength;

        /// <summary>The limits, or null if the curve could not be worked out.</summary>
        public readonly PostedLimits Limits;

        public readonly Exception Error;
        public readonly double Milliseconds;

        public SignLimitResult(int track, double trackLength, PostedLimits limits, Exception error, double milliseconds)
        {
            Track = track;
            TrackLength = trackLength;
            Limits = limits;
            Error = error;
            Milliseconds = milliseconds;
        }
    }

    /// <summary>
    /// Works out posted limits on a background thread. The caller hands in
    /// copies of curves and takes finished results back on its own thread. The
    /// thread is below normal priority so the game comes first, and it ends by
    /// itself once it has had nothing to do for a while, so a caller that goes
    /// away leaves nothing behind.
    /// </summary>
    public sealed class SignLimitWorker
    {
        private const int IdleMilliseconds = 2000;

        private readonly object gate = new object();
        private readonly Queue<Job> waiting = new Queue<Job>();
        private readonly Queue<SignLimitResult> finished = new Queue<SignLimitResult>();
        private Thread thread;
        private int unfinished;
        private bool stopped;

        /// <summary>Curves handed in and not yet begun.</summary>
        public int Waiting
        {
            get
            {
                lock (gate)
                    return waiting.Count;
            }
        }

        /// <summary>Curves handed in whose results have not yet been taken.</summary>
        public int Unfinished
        {
            get
            {
                lock (gate)
                    return unfinished;
            }
        }

        public void Add(int track, SignCurve curve, double trackLength)
        {
            lock (gate)
            {
                if (stopped)
                    return;
                waiting.Enqueue(new Job(track, curve, trackLength));
                unfinished++;
                if (thread != null)
                    Monitor.Pulse(gate);
                else
                {
                    thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "CommsTablet sign limits" };
                    thread.Start();
                }
            }
        }

        public bool TryTake(out SignLimitResult result)
        {
            lock (gate)
            {
                if (finished.Count == 0)
                {
                    result = default;
                    return false;
                }
                result = finished.Dequeue();
                unfinished--;
                return true;
            }
        }

        /// <summary>Drops what has not begun. A curve being worked on finishes, and its result is dropped too.</summary>
        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                waiting.Clear();
                finished.Clear();
                unfinished = 0;
                Monitor.PulseAll(gate);
            }
        }

        private void Run()
        {
            var watch = new Stopwatch();
            while (TryNext(out var job))
            {
                watch.Restart();
                PostedLimits limits = null;
                Exception error = null;
                try
                {
                    limits = SignLimits.Work(job.Curve, job.TrackLength);
                }
                catch (Exception e)
                {
                    // An exception left to end this thread would take the game with it.
                    error = e;
                }
                var result = new SignLimitResult(job.Track, job.TrackLength, limits, error, watch.Elapsed.TotalMilliseconds);
                lock (gate)
                {
                    if (!stopped)
                        finished.Enqueue(result);
                }
            }
        }

        private bool TryNext(out Job job)
        {
            lock (gate)
            {
                while (waiting.Count == 0)
                {
                    if (stopped || !Monitor.Wait(gate, IdleMilliseconds) && waiting.Count == 0)
                    {
                        thread = null;
                        job = default;
                        return false;
                    }
                }
                job = waiting.Dequeue();
                return true;
            }
        }

        private readonly struct Job
        {
            public readonly int Track;
            public readonly SignCurve Curve;
            public readonly double TrackLength;

            public Job(int track, SignCurve curve, double trackLength)
            {
                Track = track;
                Curve = curve;
                TrackLength = trackLength;
            }
        }
    }
}
