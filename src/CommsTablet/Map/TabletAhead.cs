using System;
using System.Collections.Generic;
using CommsTablet.Core.Ahead;
using CommsTablet.Core.Tracks;
using CommsTablet.Pages;
using CommsTablet.Tablet;
using UnityEngine;
using static CommsTablet.Tablet.TabletSkin;

namespace CommsTablet.Map
{
 internal static class TabletAhead
    {
        // The chart, in canvas pixels, and its bands within it.
        private const int ChartX = 12, ChartY = 146, ChartW = 1056, ChartH = 430;
        private const int ProfileBottom = 256, LimitTop = 290, LimitBottom = 326, SwitchLine = 374, TickTop = 402, TickBottom = 410;
        private const int Pad = 12;

        /// <summary>How far past the longest view the path is followed, for braking at the view's edge.</summary>
        private const double Beyond = 1500;
        private const double LongestView = 10000;

        private static readonly int[] Ranges = { 2, 5, 10 };

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0), Well = C(0x0b0a09), Rule = C(0x3a3027), Up = C(0xD4A537), Down = C(0x4F9FDC), Level = C(0x6f675b),
            LimitOff = C(0x26211b), LimitLow = C(0x4a3a1c), BrassEdge = C(0xc9a24a), Train = C(0xe0bf6a), Dark = C(0x100e0c), Through = C(0x9BD65F), Dimmed = C(0x8f8574), Against = C(0xD9452C);

        private static int range = 5;
        private static bool showAligned;
        private static Texture2D texture;
        private static Color32[] pixels;
        private static int painted;

        private static View view;
        private static string why;
        private static readonly Dictionary<string, string> yardNames = new Dictionary<string, string>();

        private static Color32 C(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        /// <summary>What was read for one pass.</summary>
        internal sealed class View
        {
            public TrackNetwork Network;
            public AheadTrain Train;
            public AheadProfile Profile;
            public RunEstimate Run;
            public double Nose;
            public List<AheadWaypoint> Waypoints;

            /// <summary>Where the train is bound, whether or not its route is what is shown.</summary>
            public AheadGoal Goal;

            /// <summary>Whether the path is the route to <see cref="Goal"/>, and not the aligned tracks.</summary>
            public bool Routed;
        }

        /// <summary>The status strip's line.</summary>
        internal static string Status { get; private set; } = "";

        /// <summary>At unload: every track and car is gone.</summary>
        internal static void Forget()
        {
            ReleaseTexture();
            view = null;
            why = null;
            Status = "";
            showAligned = false;
            yardNames.Clear();
            AheadTrain.Forget();
            AheadRoute.Forget();
        }

        /// <summary>Lets the chart's picture go (with the screen's); it is painted again when next shown.</summary>
        internal static void ReleaseTexture()
        {
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
            texture = null;
            pixels = null;
            painted = 0;
        }

        private static View Read()
        {
            var network = MapWorld.Network;
            if (network == null)
            {
                why = "Reading the track network";
                return null;
            }
            var train = AheadTrain.Read(network, out why);
            if (train == null)
                return null;
            var junctions = network.Junctions;
            var shapes = network.Shapes;
            var horizon = train.Length + LongestView + Beyond;
            var goal = AheadRoute.GoalFor(network, train);
            // Looking at the aligned tracks lasts as long as there is a route to come back to.
            if (goal == null)
                showAligned = false;
            var route = goal != null && !showAligned ? AheadRoute.Follow(network, train, goal) : null;
            AheadPath path;
            double nose;
            double speed;
            if (route != null)
            {
                // Past the view, tracks are read a few a frame, and the time to the end waits for them.
                path = shapes.ReadSoon(route) ? route : route.Upto(horizon);
                nose = route.RouteStart;
                speed = Math.Max(0, AheadRoute.SpeedToward(train, AheadRoute.Leader.Value));
            }
            else
            {
                path = AheadPath.Aligned(network.Graph, train.Rear, train.Toward, horizon, j => junctions[j].selectedBranch);
                nose = Math.Min(train.Length, path.Length);
                speed = Math.Abs(train.Speed);
            }
            shapes.WantPosted(route ?? path);
            var profile = AheadProfile.Build(path, t => shapes[t]);
            var routed = route != null;
            var result = new View
            {
                Network = network,
                Train = train,
                Profile = profile,
                Nose = nose,
                Run = RunEstimate.Compute(profile, nose, train.Length, speed),
                Goal = goal,
                Routed = routed,
            };
            result.Waypoints = AheadWaypoints.Pick(profile, nose, t => shapes[t], s => Flagged(result, s));
            return result;
        }

        // The switch to point out: on a route one set against it, on the aligned tracks one set off the main line.
        private static bool Flagged(View v, AheadSwitch s) => v.Routed ? !s.Lined : Diverging(v.Network, s);

        private static bool Diverging(TrackNetwork network, AheadSwitch s)
        {
            var branches = network.Junctions[s.Junction].outBranches;
            return s.Branch >= 0 && s.Branch < branches.Count && branches[s.Branch].track != null && branches[s.Branch].track.name.Contains("diverging");
        }

        private static string SwitchName(TrackNetwork network, AheadSwitch s)
        {
            var id = network.Junctions[s.Junction].junctionData.junctionIdLong;
            return string.IsNullOrEmpty(id) ? "Switch" : id;
        }

        private static string YardName(string yard)
        {
            if (yardNames.TryGetValue(yard, out var name))
                return name;
            var station = StationController.GetStationByYardID(yard);
            name = station != null && station.stationInfo != null && !string.IsNullOrEmpty(station.stationInfo.Name) ? station.stationInfo.Name : yard;
            yardNames[yard] = name;
            return name;
        }

        private static string Km(double meters) => meters < 950 ? $"{Math.Round(meters / 10) * 10:F0} m" : $"{meters / 1000:F1} km";

        private static string Clock(double seconds)
        {
            var s = (int)Math.Round(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        private static string Percent(double grade) => Math.Abs(grade) < 0.05 ? "0.0" : (grade > 0 ? "+" : "−") + Math.Abs(grade).ToString("F1");

        // ---- The pass ------------------------------------------------------

        internal static void Draw(AheadPage owner)
        {
            view = Read();
            DestinationRow(owner, view != null);
            if (view == null)
            {
                Status = AheadRoute.Destination == null && why != null && why.StartsWith("Board", StringComparison.Ordinal)
                    ? "Board a train to see the track ahead and set a destination"
                    : why ?? "";
                Label(new Rect(60, 340, 960, 80), why, Centered(SoftText));
                return;
            }
            Controls();
            Summary(view);
            var span = range * 1000.0;
            var back = span * 0.044;
            var start = view.Nose - back;
            var total = span + back;
            Paint(view, start, total);
            Box(new Rect(ChartX - 2, ChartY - 2, ChartW + 4, ProfileBottom + 4), Look_Well);
            Picture(new Rect(ChartX, ChartY, ChartW, ChartH), texture);
            ChartWords(view, start, total, span);
            Rows(view);
            Status = StatusLine(view);
        }

        private static Look Look_Well => TabletSkin.Well;

        /// <summary>
        /// Along the top: where the player sent the train, with CLEAR beside it;
        /// else SET DESTINATION, which opens the Switches map to pick one, once
        /// there is a train to route and the map is ready.
        /// </summary>
        private static void DestinationRow(AheadPage owner, bool train)
        {
            var destination = AheadRoute.Destination;
            if (destination != null)
            {
                Label(new Rect(268, 16, 56, 56), "TO", Heading22);
                Label(new Rect(324, 16, 620, 56), destination.Name, Stat30);
                if (Button(new Rect(952, 16, 112, 56), "CLEAR", Plate))
                {
                    AheadRoute.Destination = null;
                    Changed();
                }
                return;
            }
            if (Button(new Rect(784, 16, 280, 56), "SET DESTINATION", Plate, train && owner.MapReady))
                owner.PickDestination();
        }

        private static void Controls()
        {
            // Beside it, the one that fits: back to the route, or dropping the destination the player picked.
            if (showAligned)
            {
                if (view.Goal != null && Button(new Rect(520, 84, 112, 56), "ROUTE", Plate))
                {
                    showAligned = false;
                    Changed();
                }
            }
            if (view.Routed)
            {
                if (Button(new Rect(640, 84, 112, 56), "AS SET", Plate))
                {
                    showAligned = true;
                    Changed();
                }
            }
            else
            {
                if (Button(new Rect(640, 84, 112, 56), "TURN", Plate))
                {
                    AheadTrain.Turn();
                    Changed();
                }
            }
            Label(new Rect(760, 84, 36, 56), "km", Soft22);
            for (var i = 0; i < Ranges.Length; i++)
            {
                var r = Ranges[i];
                if (Button(new Rect(800 + i * 90, 84, 84, 56), r.ToString(), r == range ? PlateOn : Plate))
                {
                    range = r;
                    Changed();
                }
            }
        }

        private static Look soft22, small21, bold21, row, rowBold, rowRight, rowTime, mark20, limit23, heading22, heading21, stat30, trainWord, heightWord;

        private static Look Soft22 => soft22 ?? (soft22 = SoftText.Sized(22, 18));
        private static Look Small21 => small21 ?? (small21 = SoftText.Sized(21, 16));
        private static Look Bold21 => bold21 ?? (bold21 = Centered(Small).Sized(21, 16));
        private static Look Row => row ?? (row = SoftText.Sized(23, 18));
        private static Look RowBold => rowBold ?? (rowBold = Big.Sized(27, 20));
        private static Look RowRight => rowRight ?? (rowRight = Big.Sized(27, 20).With(TextAnchor.MiddleRight));
        private static Look RowTime => rowTime ?? (rowTime = SoftText.Sized(27, 20).With(TextAnchor.MiddleRight));
        private static Look Mark20 => mark20 ?? (mark20 = Centered(Small).Sized(20, 16).Tinted(Ink));
        private static Look Limit23 => limit23 ?? (limit23 = Centered(Small).Sized(23, 18));
        private static Look Heading22 => heading22 ?? (heading22 = Section.Sized(22, 18));
        private static Look Heading21 => heading21 ?? (heading21 = Section.Sized(21, 16));
        private static Look Stat30 => stat30 ?? (stat30 = Big.Sized(30, 22));
        private static Look TrainWord => trainWord ?? (trainWord = Bold21.Tinted(Brass2));
        private static Look HeightWord => heightWord ?? (heightWord = Small21.With(TextAnchor.MiddleRight));

        private static void Summary(View v)
        {
            Stat(16, 66, "NOW", 146, $"{Math.Abs(v.Train.Speed) * 3.6:F0} km/h");
            Stat(232, 92, "GRADE", 126, Percent(v.Profile.GradeAt(v.Nose)) + " %");
            // ROUTE takes this place while the aligned tracks are shown instead of a route.
            if (!showAligned || v.Goal == null)
                Stat(456, 84, "TRAIN", 96, v.Train.Length >= 950 ? $"{v.Train.Length / 1000:F1} km" : $"{v.Train.Length:F0} m");
        }

        private static void Stat(float x, float labelWidth, string label, float valueWidth, string value)
        {
            Label(new Rect(x, 88, labelWidth, 48), label, Heading22);
            Label(new Rect(x + labelWidth, 88, valueWidth, 48), value, Stat30);
        }

        private static string StatusLine(View v)
        {
            var path = v.Profile.Path;
            if (v.Routed)
            {
                foreach (var w in v.Waypoints)
                {
                    if (w.Kind == WaypointKind.Switch && !path.Switches[w.SwitchIndex].Lined)
                        return $"As set, this train leaves the route at {SwitchName(v.Network, path.Switches[w.SwitchIndex])} in {Km(w.Distance - v.Nose)}";
                }
                var whose = v.Goal.JobId != null ? $"Job {v.Goal.JobId} to " : "To ";
                return AheadRoute.ThroughCars
                    ? $"{whose}{v.Goal.Name}: the only way runs through standing cars"
                    : $"{whose}{v.Goal.Name}, every switch ahead set for it";
            }
            if (v.Goal != null && !showAligned && AheadRoute.Note != null)
                return AheadRoute.Note;
            foreach (var w in v.Waypoints)
            {
                if (w.Kind == WaypointKind.Switch && Diverging(v.Network, path.Switches[w.SwitchIndex]))
                    return $"{SwitchName(v.Network, path.Switches[w.SwitchIndex])} is set to diverge in {Km(w.Distance - v.Nose)}";
            }
            return path.End == AheadEnd.DeadEnd
                ? $"The track ends in {Km(path.Length - v.Nose)}"
                : "Following the switches as they are set";
        }

        // ---- The list ------------------------------------------------------

        private const int RowCount = 4;

        private static List<AheadWaypoint> RowsFor(View v) => AheadWaypoints.Shortlist(v.Waypoints, RowCount, w =>
            w.Kind == WaypointKind.Destination || w.Kind == WaypointKind.Reversal
            || (v.Routed && w.Kind == WaypointKind.Switch && !v.Profile.Path.Switches[w.SwitchIndex].Lined));

        private static void Rows(View v)
        {
            var path = v.Profile.Path;
            var shown = RowsFor(v);
            var count = shown.Count;
            for (var i = 0; i < count; i++)
            {
                var w = shown[i];
                var y = 582 + i * 66;
                Color lamp;
                string mark, title, note;
                switch (w.Kind)
                {
                    case WaypointKind.Switch:
                        var s = path.Switches[w.SwitchIndex];
                        title = SwitchName(v.Network, s);
                        if (v.Routed)
                        {
                            lamp = s.Lined ? (Color)Through : Against;
                            mark = s.Lined ? "Y" : "!";
                            note = s.Lined ? "Set for this route" : "Set against this route";
                            break;
                        }
                        var off = Diverging(v.Network, s);
                        lamp = off ? (Color)Up : Through;
                        mark = "Y";
                        note = off ? "Set to diverge" : "Set straight through";
                        break;
                    case WaypointKind.Reversal:
                        lamp = Brass2;
                        mark = "R";
                        title = "Reverse";
                        note = "Whole train clear, then back";
                        break;
                    case WaypointKind.Destination:
                        lamp = Brass2;
                        mark = "■";
                        title = v.Goal.Name;
                        note = v.Goal.Why;
                        break;
                    case WaypointKind.LowerLimit:
                        lamp = (Color)BrassEdge;
                        mark = "▼";
                        title = $"Slow to {w.Kmh} km/h";
                        note = "Speed limit";
                        break;
                    case WaypointKind.Grade:
                        lamp = w.Percent > 0 ? (Color)Up : Down;
                        mark = w.Percent > 0 ? "▲" : "▼";
                        title = (w.Percent > 0 ? "Climb " : "Descent ") + Percent(w.Percent) + " %";
                        note = $"{Math.Round(w.Length / 10) * 10:F0} m long";
                        break;
                    case WaypointKind.Yard:
                        lamp = Brass2;
                        mark = "■";
                        title = YardName(w.Yard);
                        note = "Yard";
                        break;
                    default:
                        lamp = (Color)Dimmed;
                        mark = "■";
                        title = "End of track";
                        note = "Stop here";
                        break;
                }
                Box(new Rect(16, y, 1048, 60), Card);
                Lamp(32, y + 13, 34, lamp);
                Label(new Rect(32, y + 13, 34, 34), mark, Mark20);
                Label(new Rect(80, y, 380, 60), title, RowBold);
                Label(new Rect(464, y, 300, 60), note, Row);
                Label(new Rect(770, y, 150, 60), Km(w.Distance - v.Nose), RowRight);
                Label(new Rect(924, y, 126, 60), w.Distance > path.Length ? "…" : Clock(v.Run.SecondsTo(w.Distance)), RowTime);
            }
            if (count == 0)
                Label(new Rect(60, 640, 960, 60), "Nothing to note on the track ahead", Centered(SoftText));
        }

        // ---- The chart's words ------------------------------------------------

        private static float XOf(double distance, double start, double total) => (float)(Pad + (distance - start) / total * (ChartW - 2 * Pad));

        private static float HeightY(double height, double low, double scale) => (float)(ProfileBottom - 12 - (height - low) * scale);

        private static void HeightRange(View v, double start, double total, out double low, out double scale)
        {
            double min = double.MaxValue, max = double.MinValue;
            var from = Math.Max(0, start);
            var to = Math.Min(v.Profile.Path.Length, start + total);
            for (var d = from; d <= to; d += total / 200)
            {
                var h = v.Profile.HeightAt(d);
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
            if (min > max)
                min = max = 0;
            // At least 10 m tall, so level track does not look like hills.
            var spread = Math.Max(10, max - min);
            low = (min + max - spread) / 2;
            scale = (ProfileBottom - 72) / spread;
        }

        private static void ChartWords(View v, double start, double total, double span)
        {
            HeightRange(v, start, total, out var low, out var scale);
            var end = start + total;

            // The key.
            var x = ChartX + 10f;
            foreach (var (color, word, width) in new[] { ((Color)Up, "climb", 66f), ((Color)Down, "descent", 90f), ((Color)Level, "under 0.5 %", 128f) })
            {
                Fill(new Rect(x, ChartY + 11, 18, 12), color);
                Label(new Rect(x + 24, ChartY + 3, width, 28), word, Small21);
                x += 24 + width + 12;
            }

            // Heights at the two rules.
            foreach (var m in Rules(low, low + (ProfileBottom - 72) / scale))
                Label(new Rect(ChartX + ChartW - 96, ChartY + HeightY(m, low, scale) - 26, 88, 24), $"{m:F0} m", HeightWord);

            // Grades, where there is room for them.
            var lastRight = -1000f;
            foreach (var g in v.Profile.Grades)
            {
                if (g.To <= v.Nose || g.From >= end || Math.Abs(g.Percent) < 0.5)
                    continue;
                var a = Math.Max(g.From, v.Nose);
                var b = Math.Min(g.To, end);
                var left = XOf(a, start, total);
                var right = XOf(b, start, total);
                if (right - left < 62)
                    continue;
                var mid = (left + right) / 2;
                if (mid - 42 < lastRight)
                    continue;
                lastRight = mid + 42;
                var top = Mathf.Min(HeightY(v.Profile.HeightAt(a), low, scale), HeightY(v.Profile.HeightAt(b), low, scale));
                Label(new Rect(ChartX + mid - 42, ChartY + top - 32, 84, 26), (g.Percent > 0 ? "▲ " : "▼ ") + Percent(g.Percent), Bold21);
            }

            // The train.
            var noseX = XOf(v.Nose, start, total);
            Label(new Rect(ChartX + Mathf.Max(34, noseX) - 34, ChartY + HeightY(v.Profile.HeightAt(v.Nose), low, scale) - 36, 68, 24), "TRAIN", TrainWord);

            // Limits, on the blocks wide enough.
            Label(new Rect(ChartX + Pad, ChartY + LimitTop - 26, 200, 24), "LIMIT km/h", Heading21);
            foreach (var r in v.Profile.Limits)
            {
                if (r.To <= start || r.From >= end)
                    continue;
                var left = XOf(Math.Max(r.From, start), start, total);
                var right = XOf(Math.Min(r.To, end), start, total);
                if (right - left >= 42)
                    Label(new Rect(ChartX + left, ChartY + LimitTop, right - left, LimitBottom - LimitTop), r.Kmh.ToString(), Limit23);
            }

            // The switches to mind by name, and the axis.
            Label(new Rect(ChartX + Pad, ChartY + LimitBottom + 4, 200, 24), "SWITCHES", Heading21);
            var nameRight = 150f;
            foreach (var s in v.Profile.Path.Switches)
            {
                if (s.Distance <= v.Nose || s.Distance >= end || s.Meeting != SwitchMeeting.Facing || !Flagged(v, s))
                    continue;
                var at = Mathf.Min(XOf(s.Distance, start, total), ChartW - 66);
                if (at - 62 < nameRight)
                    continue;
                nameRight = at + 62;
                Label(new Rect(ChartX + at - 62, ChartY + LimitBottom + 4, 124, 24), SwitchName(v.Network, s), Bold21);
            }
            if (v.Profile.Path.End == AheadEnd.DeadEnd && v.Profile.Path.Length < end)
            {
                var at = XOf(v.Profile.Path.Length, start, total);
                Label(new Rect(ChartX + at + 8, ChartY + ProfileBottom - 34, 60, 24), "END", Small21);
            }
            foreach (var stop in v.Profile.Path.Stops)
            {
                if (stop.Distance <= v.Nose || stop.Distance >= end)
                    continue;
                var at = XOf(stop.Distance, start, total);
                var word = stop.Kind == AheadStopKind.Reversal ? "REVERSE" : "ARRIVE";
                Label(new Rect(ChartX + Mathf.Min(at + 8, ChartW - 112), ChartY + ProfileBottom - 34, 104, 24), word, Small21);
            }
            var tick = range == 2 ? 500 : range == 5 ? 1000 : 2000;
            for (var m = 0; m <= span + 1; m += tick)
            {
                var at = XOf(v.Nose + m, start, total);
                var text = m == 0 ? "0" : m % 1000 == 0 ? $"{m / 1000} km" : $"{m / 1000.0:F1}";
                Label(new Rect(ChartX + Mathf.Clamp(at, 40, ChartW - 40) - 40, ChartY + TickBottom, 80, 22), text, Centered(Small21));
            }
        }

        private static IEnumerable<double> Rules(double low, double high)
        {
            var step = Math.Max(5, Math.Ceiling((high - low) / 2 / 5) * 5);
            for (var m = Math.Ceiling(low / step) * step; m <= high; m += step)
                yield return m;
        }

        // ---- The picture ------------------------------------------------------

        private static int Fingerprint(View v, double start)
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + range;
                h = h * 31 + v.Train.Rear.Track;
                h = h * 31 + (int)Math.Round(v.Train.Rear.Offset);
                h = h * 31 + (int)v.Train.Toward;
                h = h * 31 + (int)Math.Round(v.Train.Length);
                h = h * 31 + v.Network.Shapes.PostedChanges;
                h = h * 31 + (int)Math.Round(v.Profile.Path.Length);
                h = h * 31 + (v.Routed ? 1 : 0);
                h = h * 31 + (int)Math.Round(v.Nose);
                foreach (var s in v.Profile.Path.Switches)
                    h = h * 31 + s.Junction * 3 + s.Branch + (int)s.Meeting * 7 + (s.Lined ? 0 : 11);
                return h;
            }
        }

        private static void Paint(View v, double start, double total)
        {
            if (texture == null)
            {
                texture = new Texture2D(ChartW, ChartH, TextureFormat.RGBA32, false)
                {
                    name = "CommsTablet_TrackAhead",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                };
                pixels = new Color32[ChartW * ChartH];
                painted = 0;
            }
            var print = Fingerprint(v, start);
            if (print == painted && painted != 0)
                return;
            painted = print;
            Array.Clear(pixels, 0, pixels.Length);
            HeightRange(v, start, total, out var low, out var scale);
            var profile = v.Profile;
            var length = profile.Path.Length;
            var inner = ChartW - 2 * Pad;

            foreach (var m in Rules(low, low + (ProfileBottom - 72) / scale))
                Span(0, ChartW - 1, (int)HeightY(m, low, scale), Rule);

            var lineY = new int[ChartW];
            for (var x = 0; x < ChartW; x++)
            {
                lineY[x] = -1;
                var d = start + (x - Pad) / (double)inner * total;
                if (x < Pad || x >= ChartW - Pad || d < 0 || d > length)
                    continue;
                var y = Mathf.Clamp((int)HeightY(profile.HeightAt(d), low, scale), 0, ProfileBottom - 1);
                lineY[x] = y;
                var grade = profile.GradeAt(d);
                var flat = Math.Abs(grade) < 0.5;
                var color = flat ? Level : grade > 0 ? Up : Down;
                var fill = Dim(color, Math.Abs(grade) >= 2 ? 0.5f : 0.24f);
                Column(x, y, ProfileBottom - 1, fill);
                Column(x, y - 1, y + 1, color);

                var kmh = profile.KmhAt(d);
                var low50 = kmh <= 50;
                Column(x, LimitTop, LimitBottom - 1, low50 ? LimitLow : LimitOff);
                if (low50)
                {
                    Put(x, LimitTop, BrassEdge);
                    Put(x, LimitBottom - 1, BrassEdge);
                }
                Column(x, SwitchLine - 1, SwitchLine + 1, Rule);
            }

            // A gap where one limit gives way to the next.
            foreach (var r in profile.Limits)
            {
                var x = (int)XOf(r.From, start, total);
                if (x >= Pad && x < ChartW - Pad)
                    Column(x, LimitTop, LimitBottom - 1, Clear);
            }

            // Yards along the switch line.
            foreach (var leg in profile.Path.Legs)
            {
                if (v.Network.Shapes[leg.Track].Yard == null || leg.End <= v.Nose)
                    continue;
                var a = Mathf.Max(Pad, (int)XOf(Math.Max(leg.Start, v.Nose), start, total));
                var b = Mathf.Min(ChartW - Pad - 1, (int)XOf(leg.End, start, total));
                for (var x = a; x <= b; x++)
                    Column(x, SwitchLine - 5, SwitchLine + 5, BrassEdge);
            }

            // The train on the line, dark-edged so it stands off the profile.
            var tail = Mathf.Max(Pad, (int)XOf(v.Nose - v.Train.Length, start, total));
            var nose = Mathf.Min(ChartW - Pad - 1, (int)XOf(v.Nose, start, total));
            for (var x = tail; x <= nose; x++)
            {
                if (lineY[x] < 0)
                    continue;
                Column(x, lineY[x] - 7, lineY[x] + 7, Dark);
            }
            for (var x = tail; x <= nose; x++)
            {
                if (lineY[x] >= 0)
                    Column(x, lineY[x] - 4, lineY[x] + 4, Train);
            }

            // The switches: met at the points a diamond, solid straight through, ringed set to diverge;
            // met from behind, a small mark pointing back, hollow where the train will flip it.
            foreach (var s in profile.Path.Switches)
            {
                var x = (int)XOf(s.Distance, start, total);
                if (s.Distance <= v.Nose || x < Pad || x >= ChartW - Pad)
                    continue;
                if (s.Meeting == SwitchMeeting.Facing)
                {
                    var off = Flagged(v, s);
                    Diamond(x, SwitchLine, 19, Dark);
                    Diamond(x, SwitchLine, 17, !off ? Through : v.Routed ? Against : Up);
                    if (off)
                        Diamond(x, SwitchLine, 8, Dark);
                }
                else
                    Back(x, SwitchLine, s.Meeting == SwitchMeeting.TrailingAgainst);
            }

            if (profile.Path.End == AheadEnd.DeadEnd && length < start + total)
            {
                var x = (int)XOf(length, start, total);
                for (var w = 0; w < 3; w++)
                    Column(x + w, 0, SwitchLine + 12, BrassEdge);
            }

            foreach (var stop in profile.Path.Stops)
            {
                var x = (int)XOf(stop.Distance, start, total);
                if (stop.Distance <= v.Nose || x < Pad || x >= ChartW - Pad)
                    continue;
                for (var w = 0; w < 3; w++)
                    Column(x + w, 0, SwitchLine + 12, stop.Kind == AheadStopKind.Reversal ? Train : BrassEdge);
            }

            for (var m = 0; m <= range * 1000 + 1; m += range == 2 ? 500 : range == 5 ? 1000 : 2000)
            {
                var x = (int)XOf(v.Nose + m, start, total);
                if (x >= 0 && x < ChartW)
                    Column(x, TickTop, TickBottom, Dimmed);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static Color32 Dim(Color32 c, float alpha) => new Color32(c.r, c.g, c.b, (byte)(alpha * 255));

        private static void Put(int x, int y, Color32 c)
        {
            if (x >= 0 && x < ChartW && y >= 0 && y < ChartH)
                pixels[(ChartH - 1 - y) * ChartW + x] = c;
        }

        private static void Column(int x, int top, int bottom, Color32 c)
        {
            for (var y = top; y <= bottom; y++)
                Put(x, y, c);
        }

        private static void Span(int left, int right, int y, Color32 c)
        {
            for (var x = left; x <= right; x++)
                Put(x, y, c);
        }

        private static void Diamond(int cx, int cy, int r, Color32 c)
        {
            for (var dy = -r; dy <= r; dy++)
                Span(cx - (r - Math.Abs(dy)), cx + (r - Math.Abs(dy)), cy + dy, c);
        }

        private static void Back(int cx, int cy, bool hollow)
        {
            for (var dx = -8; dx <= 8; dx++)
            {
                var half = (dx + 8) * 10 / 16;
                Column(cx + dx, cy - half, cy + half, Dimmed);
                if (hollow && dx > -5 && dx < 7)
                    Column(cx + dx, cy - half + 2, cy + half - 2, Dark);
            }
        }

#if !PUBLIC_RELEASE
        // ---- For the self-test ----

        internal static View ShownForTest => view;

        internal static View ReadForTest() => Read();

        internal static int RangeForTest => range;

        internal static List<AheadWaypoint> RowsForTest(View v) => RowsFor(v);

        internal static bool ShowAlignedForTest
        {
            get => showAligned;
            set => showAligned = value;
        }

        internal static Texture2D TextureForTest => texture;

        internal static string SwitchNameForTest(View v, AheadSwitch s) => SwitchName(v.Network, s);

        internal static bool DivergingForTest(View v, AheadSwitch s) => Diverging(v.Network, s);

        internal static readonly Vector2 TurnPlateForTest = new Vector2(696, 112);

        internal static readonly Vector2 ClearPlateForTest = new Vector2(1008, 44);

        internal static readonly Vector2 SetDestinationPlateForTest = new Vector2(924, 44);

        /// <summary>AS SET while a route is shown, TURN otherwise: the same plate.</summary>
        internal static readonly Vector2 AsSetPlateForTest = TurnPlateForTest;

        internal static readonly Vector2 RoutePlateForTest = new Vector2(576, 112);

        internal static Vector2 RangePlateForTest(int index) => new Vector2(842 + index * 90, 112);
#endif
    }
}
