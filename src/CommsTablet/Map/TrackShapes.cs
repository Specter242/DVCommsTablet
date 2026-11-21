using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using CommsTablet.Core.Ahead;
using CommsTablet.Core.Tracks;
using DV.Utils;
using UnityEngine;

namespace CommsTablet.Map
{
    /// <summary>
    /// The network's tracks as Track Ahead reads them (<see cref="TrackShape"/>),
    /// each made from the game's track the first time it is wanted and kept
    /// with the network. Heights come from the curve itself and not the point
    /// set trains run on, which carries a little vertical noise for the ride.
    /// The posted limits come in behind that, from the app's first use, the
    /// tracks ahead of the train first. Each frame gives about a millisecond to
    /// copying curves (<see cref="SignRules"/>), and a background thread works
    /// the limits out from the copies (<see cref="SignLimitWorker"/>).
    /// </summary>
    internal sealed class TrackShapes
    {
        /// <summary>Spacing of the points read from a curve, in meters: plenty for grades and curve radii.</summary>
        private const float Spacing = 5f;

        /// <summary>Copying stops for the frame once this much of it has gone, in milliseconds.</summary>
        private const double FrameBudgetMs = 1;

        /// <summary>Copies queued ahead of the worker: few, so that a new route's tracks still go next.</summary>
        private const int MostWaiting = 2;

        private readonly TrackNetwork network;
        private readonly TrackShape[] shapes;
        private readonly bool[] readFailed;
        private readonly PostedLimits[] posted;
        private readonly bool[] sent;
        private readonly List<int> wanted = new List<int>();
        private readonly List<int> toRead = new List<int>();
        private bool filling, reading;
        private int nextTrack;

        /// <summary>How many tracks have their posted limits.</summary>
        internal int PostedCount { get; private set; }

        /// <summary>Counts posted limits reaching a track already in use: it changes what the app draws.</summary>
        internal int PostedChanges { get; private set; }

        /// <summary>Counts track shapes read so far.</summary>
        internal int ShapesRead { get; private set; }

        /// <summary>Whether every track has been gone through.</summary>
        internal bool PostedDone { get; private set; }

        /// <summary>The most this work has taken of any one frame on the main thread, in milliseconds.</summary>
        internal double SlowestFrameMs { get; private set; }

#if !PUBLIC_RELEASE
        /// <summary>What copying each curve cost the main thread, for the self-test.</summary>
        internal readonly List<(double Ms, double Length, int Knots)> CopyTimesForTest = new List<(double, double, int)>();

        /// <summary>What each track cost the worker, for the self-test.</summary>
        internal readonly List<(double Ms, double Length)> WorkTimesForTest = new List<(double, double)>();

        /// <summary>A track's posted limits, whether or not its shape has been read, for the self-test.</summary>
        internal PostedLimits PostedForTest(int track) => posted[track];
#endif

        internal TrackShapes(TrackNetwork network)
        {
            this.network = network;
            shapes = new TrackShape[network.Tracks.Length];
            readFailed = new bool[shapes.Length];
            posted = new PostedLimits[shapes.Length];
            sent = new bool[shapes.Length];
        }

        public TrackShape this[int track] => shapes[track] ?? (shapes[track] = Read(track));

        internal bool TryGet(int track, out TrackShape shape)
        {
            if (track < 0 || track >= shapes.Length)
            {
                shape = null;
                return false;
            }
            if (shapes[track] != null)
            {
                shape = shapes[track];
                return true;
            }
            if (readFailed[track])
            {
                shape = null;
                return false;
            }
            try
            {
                shape = shapes[track] = Read(track);
                return shape != null;
            }
            catch (Exception e)
            {
                readFailed[track] = true;
                Main.Mod?.Logger.LogException($"Track Ahead: could not read track {track}", e);
                shape = null;
                return false;
            }
        }

        /// <summary>
        /// Whether every readable track of the path has settled (read or known
        /// unreadable). Tracks not yet read are read over the coming frames, so
        /// a long route costs no one frame much, while a known-failed track
        /// does not pin the profile back to the horizon.
        /// </summary>
        internal bool ReadSoon(AheadPath path)
        {
            toRead.Clear();
            for (var i = path.Legs.Count - 1; i >= 0; i--)
            {
                var track = path.Legs[i].Track;
                if (shapes[track] != null || readFailed[track])
                    continue;
                toRead.Add(track);
            }
            if (toRead.Count == 0)
                return true;
            var runner = SingletonBehaviour<CoroutineManager>.Instance;
            if (!reading && runner != null)
            {
                reading = true;
                try
                {
                    runner.Run(ReadShapes());
                }
                catch
                {
                    reading = false;
                    throw;
                }
            }
            return false;
        }

        private IEnumerator ReadShapes()
        {
            try
            {
                var frame = new Stopwatch();
                while (toRead.Count > 0 && MapWorld.HasNetwork && MapWorld.Network == network)
                {
                    frame.Restart();
                    while (toRead.Count > 0 && frame.Elapsed.TotalMilliseconds < FrameBudgetMs)
                    {
                        var track = toRead[toRead.Count - 1];
                        toRead.RemoveAt(toRead.Count - 1);
                        try
                        {
                            _ = this[track];
                        }
                        catch (Exception e)
                        {
                            readFailed[track] = true;
                            Main.Mod?.Logger.LogException($"Track Ahead: could not read track {track}", e);
                        }
                    }
                    yield return null;
                }
            }
            finally
            {
                reading = false;
            }
        }

        /// <summary>Puts these tracks next for their posted limits, and starts the work if it has not begun.</summary>
        internal void WantPosted(AheadPath path)
        {
            if (PostedDone)
                return;
            wanted.Clear();
            for (var i = path.Legs.Count - 1; i >= 0; i--)
                wanted.Add(path.Legs[i].Track);
            if (filling)
                return;
            var runner = SingletonBehaviour<CoroutineManager>.Instance;
            if (runner == null)
                return;
            filling = true;
            try
            {
                runner.Run(FillPosted());
            }
            catch
            {
                filling = false;
                throw;
            }
        }

        private IEnumerator FillPosted()
        {
            var worker = new SignLimitWorker();
            try
            {
                var frame = new Stopwatch();
                while (MapWorld.HasNetwork && MapWorld.Network == network)
                {
                    frame.Restart();
                    TakeFinished(worker);
                    var more = CopyCurves(worker, frame);
                    SlowestFrameMs = Math.Max(SlowestFrameMs, frame.Elapsed.TotalMilliseconds);
                    if (!more && worker.Unfinished == 0)
                    {
                        PostedDone = true;
                        break;
                    }
                    yield return null;
                }
            }
            finally
            {
                worker.Stop();
                filling = false;
            }
        }

        private void TakeFinished(SignLimitWorker worker)
        {
            while (worker.TryTake(out var result))
            {
                try
                {
#if !PUBLIC_RELEASE
                    WorkTimesForTest.Add((result.Milliseconds, result.TrackLength));
#endif
                    if (result.Limits == null)
                    {
                        SignRules.Warn(result.Error);
                        continue;
                    }
                    posted[result.Track] = result.Limits;
                    PostedCount++;
                    // A track nobody has looked at takes its limits when it is first read.
                    var shape = shapes[result.Track];
                    if (shape != null)
                    {
                        shape.SetPosted(result.Limits);
                        PostedChanges++;
                    }
                }
                catch (Exception e)
                {
                    SignRules.Warn(e);
                }
            }
        }

        // False once every track has been handed over.
        private bool CopyCurves(SignLimitWorker worker, Stopwatch frame)
        {
            while (worker.Waiting < MostWaiting && frame.Elapsed.TotalMilliseconds < FrameBudgetMs)
            {
                if (!NextTrack(out var index))
                    return false;
                sent[index] = true;
                try
                {
                    var track = network.Tracks[index];
                    if (track == null)
                        continue;
                    var length = network.Graph.Length(index);
                    if (!SignRules.Signed(track, length))
                        continue;
#if !PUBLIC_RELEASE
                    var began = frame.Elapsed.TotalMilliseconds;
#endif
                    var curve = SignRules.Copy(track);
                    if (curve == null)
                        continue;
#if !PUBLIC_RELEASE
                    CopyTimesForTest.Add((frame.Elapsed.TotalMilliseconds - began, length, curve.KnotCount));
#endif
                    worker.Add(index, curve, length);
                }
                catch (Exception e)
                {
                    SignRules.Warn(e);
                }
            }
            return true;
        }

        private bool NextTrack(out int track)
        {
            while (wanted.Count > 0)
            {
                track = wanted[wanted.Count - 1];
                wanted.RemoveAt(wanted.Count - 1);
                if (!sent[track])
                    return true;
            }
            while (nextTrack < sent.Length)
            {
                track = nextTrack++;
                if (!sent[track])
                    return true;
            }
            track = -1;
            return false;
        }

        private TrackShape Read(int index)
        {
            var track = network.Tracks[index];
            var length = network.Graph.Length(index);
            // The game refuses a spacing over half the curve.
            var set = track.GetUnkinkedPointSet(Mathf.Min(Spacing, Mathf.Max(0.05f, (float)length / 4f)));
            var points = set.points;
            var n = points.Length;
            var offsets = new double[n];
            var heights = new double[n];
            var places = new MapPoint[n];
            // The graph's length is the running line's, a little longer than the curve's.
            var scale = set.span > 0 ? length / set.span : 1;
            for (var i = 0; i < n; i++)
            {
                offsets[i] = i == n - 1 ? length : points[i].span * scale;
                heights[i] = points[i].position.y;
                places[i] = new MapPoint(points[i].position.x, points[i].position.z);
            }
            var shape = TrackShape.From(offsets, heights, places, YardOf(track.name));
            if (posted[index] != null)
                shape.SetPosted(posted[index]);
            ShapesRead++;
            return shape;
        }

        /// <summary>The yard in a yard track's name ("[Y]_[HB]_[C-02-I]"), or null.</summary>
        internal static string YardOf(string name)
        {
            if (name == null || !name.StartsWith("[Y]_[", StringComparison.Ordinal))
                return null;
            var end = name.IndexOf(']', 5);
            return end > 5 ? name.Substring(5, end - 5) : null;
        }
    }
}
