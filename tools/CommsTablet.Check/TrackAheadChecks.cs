using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CommsTablet.Core.Ahead;
using CommsTablet.Core.Routing;
using CommsTablet.Core.Tracks;
using DV.Signs;
using HarmonyLib;

namespace CommsTablet.Check
{
 internal static class TrackAheadChecks
    {
        private static Action<bool, string, string> report;

        private static void check(bool ok, string what, string detail = null) => report(ok, what, detail);

        private static bool Near(double a, double b, double within) => Math.Abs(a - b) <= within;

        // A straight track along x, its height given by offset.
        private static TrackShape Straight(double length, Func<double, double> height, string yard = null)
        {
            var n = (int)(length / 10) + 1;
            var offsets = Enumerable.Range(0, n).Select(i => i * 10.0).ToArray();
            return TrackShape.From(offsets, offsets.Select(height).ToArray(), offsets.Select(d => new MapPoint(d, 0)).ToArray(), yard);
        }

        // A level track bent to a radius.
        private static TrackShape Curved(double length, double radius)
        {
            var n = (int)(length / 5) + 1;
            var offsets = Enumerable.Range(0, n).Select(i => i * 5.0).ToArray();
            return TrackShape.From(offsets, new double[n], offsets.Select(d => new MapPoint(radius * Math.Sin(d / radius), radius * (1 - Math.Cos(d / radius)))).ToArray(), null);
        }

        private static int Add(TrackGraphBuilder b, double length) => b.AddTrack(length, new MapPoint(0, 0), new MapPoint(length, 0));

        internal static void Run(Action<bool, string, string> reportTo)
        {
            report = reportTo;
            Paths();
            Profiles();
            Times();
            SignRuleChecks();
            RouteChecks();
        }

        // main (1000 m, level) -> switch -> through (1000 m, climbing 2%) -> joined end to end -> last (1000 m)
        //                              \-> siding (500 m, falling 1%), a dead end
        private static (TrackGraph Graph, TrackShape[] Shapes) Forked()
        {
            var b = new TrackGraphBuilder();
            int main = Add(b, 1000), through = Add(b, 1000), siding = Add(b, 500), last = Add(b, 1000);
            b.AddJunction(main, TrackEnd.End, new[] { (through, TrackEnd.Start), (siding, TrackEnd.Start) }, new MapPoint(1000, 0));
            b.Join(through, TrackEnd.End, last, TrackEnd.End);
            // The last track is read from its End: falling 1% from Start is climbing 1% the way the path runs it.
            var shapes = new[] { Straight(1000, d => 100), Straight(1000, d => 100 + d * 0.02), Straight(500, d => 100 - d * 0.01), Straight(1000, d => 130 - d * 0.01) };
            return (b.Build(), shapes);
        }

        private static string Tracks(AheadPath p) => string.Join(",", p.Legs.Select(l => l.Track));

        private static void Paths()
        {
            var (g, _) = Forked();
            var start = new TrackPosition(0, 0);
            var through = AheadPath.Aligned(g, start, TrackDirection.Forward, 99999, _ => 0);
            check(Tracks(through) == "0,1,3" && through.End == AheadEnd.DeadEnd && through.Length == 3000,
                "Track Ahead: a path follows a switch set through, along a joint run backward, to the dead end", $"{Tracks(through)} {through.End} {through.Length}");
            check(through.Switches.Count == 1 && through.Switches[0].Meeting == SwitchMeeting.Facing && through.Switches[0].Distance == 1000 && through.Switches[0].Branch == 0,
                "Track Ahead: the switch is met at its points, 1000 m on, on its first branch");
            check(!through.Legs[2].RunsForward && through.Legs[2].OffsetAt(2100) == 900, "Track Ahead: a leg run backward counts offsets down");

            var siding = AheadPath.Aligned(g, start, TrackDirection.Forward, 99999, _ => 1);
            check(Tracks(siding) == "0,2" && siding.Length == 1500 && siding.End == AheadEnd.DeadEnd, "Track Ahead: thrown, the same switch leads into the siding", Tracks(siding));

            var short_ = AheadPath.Aligned(g, start, TrackDirection.Forward, 500, _ => 0);
            check(Tracks(short_) == "0" && short_.End == AheadEnd.Horizon && short_.Length == 1000, "Track Ahead: a path stops at its length, its last track whole", Tracks(short_));

            var against = AheadPath.Aligned(g, new TrackPosition(2, 500), TrackDirection.Backward, 99999, _ => 0);
            check(Tracks(against) == "2,0" && against.Switches[0].Meeting == SwitchMeeting.TrailingAgainst, "Track Ahead: from a branch the switch is not set for, it is met trailing against", Tracks(against));
            var with = AheadPath.Aligned(g, new TrackPosition(2, 500), TrackDirection.Backward, 99999, _ => 1);
            check(with.Switches[0].Meeting == SwitchMeeting.Trailing, "Track Ahead: from the branch it is set for, it is met trailing");
            check(through.LegAt(1500).Track == 1 && through.LegAt(-5).Track == 0 && through.LegAt(99999).Track == 3, "Track Ahead: legs are found by distance");
        }

        private static void Profiles()
        {
            var (g, shapes) = Forked();
            var path = AheadPath.Aligned(g, new TrackPosition(0, 0), TrackDirection.Forward, 99999, _ => 0);
            var p = AheadProfile.Build(path, t => shapes[t]);
            check(p.Grades.Count == 3 && Near(p.GradeAt(500), 0, 0.01) && Near(p.GradeAt(1500), 2, 0.01) && Near(p.GradeAt(2500), 1, 0.01),
                "Track Ahead: grades read in the direction of travel, a track run backward included",
                string.Join(" ", p.Grades.Select(r => $"{r.From:F0}-{r.To:F0}:{r.Percent:F2}")));
            check(Near(p.HeightAt(1500), 110, 0.01) && Near(p.HeightAt(2500), 125, 0.01), "Track Ahead: heights are read by distance along the path");
            check(p.KmhAt(500) == 120 && p.KmhAt(800) == 60 && p.KmhAt(1200) == 120 && p.KmhAt(2900) == 20,
                "Track Ahead: straight track is 120, 60 for the last 250 m before a switch, 20 before the track's end", string.Join(" ", p.Limits.Select(r => $"{r.From:F0}:{r.Kmh}")));
            check(p.LowestKmh(500, 2000) == 60 && p.LowestKmh(1100, 1200) == 120, "Track Ahead: the lowest limit over a stretch");
            check(Straight(500, d => 0, "HB").Limits.LimitAt(100) * 3.6 <= 50.01, "Track Ahead: a yard track is capped at 50, as the game's signs treat it");
            check(Near(Curved(600, 100).Limits.LimitAt(300) * 3.6, 40, 0.01), "Track Ahead: a 100 m curve is 40 km/h, from the game's sign table");
            var flat = Straight(1000, d => d < 500 ? 0 : (d - 500) * 0.03);
            check(flat.Grades.Count == 2 && Near(flat.Grades[1].Percent, 3, 0.1) && Near(flat.Grades[0].To, 500, 20), "Track Ahead: a track's grade change is found where it is",
                string.Join(" ", flat.Grades.Select(r => $"{r.From:F0}-{r.To:F0}:{r.Percent:F2}")));

            var posted = Straight(1000, d => 0);
            posted.SetPosted(new PostedLimits(new[] { new PostedSegment(0, 500, 100), new PostedSegment(500, 650, 80), new PostedSegment(650, 1000, 40) },
                new[] { new PostedSegment(1000, 400, 70), new PostedSegment(400, 0, 30) }));
            check(posted.Posted && Near(posted.LimitsFor(true).LimitAt(100) * 3.6, 100, 0.01) && Near(posted.LimitsFor(true).LimitAt(600) * 3.6, 40, 0.01) && Near(posted.LimitsFor(true).LimitAt(800) * 3.6, 40, 0.01),
                "Track Ahead: posted limits are taken, a short stretch before a lower limit taking that limit, as the signs do");
            check(Near(posted.LimitsFor(false).LimitAt(800) * 3.6, 70, 0.01) && Near(posted.LimitsFor(false).LimitAt(100) * 3.6, 30, 0.01), "Track Ahead: the other way has its own limits");
            var postedPath = AheadPath.Aligned(new Func<TrackGraph>(() => { var one = new TrackGraphBuilder(); Add(one, 1000); return one.Build(); })(), new TrackPosition(0, 1000), TrackDirection.Backward, 99999, _ => 0);
            var postedProfile = AheadProfile.Build(postedPath, _ => posted);
            check(postedProfile.KmhAt(100) == 70 && postedProfile.KmhAt(700) == 30, "Track Ahead: a path run backward reads the limits for that way",
                string.Join(" ", postedProfile.Limits.Select(r => $"{r.From:F0}:{r.Kmh}")));

            var ways = AheadWaypoints.Pick(p, 0, t => shapes[t], s => false);
            check(ways.Select(w => w.Kind).SequenceEqual(new[] { WaypointKind.LowerLimit, WaypointKind.Switch, WaypointKind.Grade, WaypointKind.TrackEnd }) && ways[0].Kmh == 60 && ways[1].Distance == 1000
                  && Near(ways[2].Percent, 2, 0.01) && ways[3].Distance == 3000,
                "Track Ahead: the list names the next lower limit, switch, steep grade, and the track's end, nearest first",
                string.Join(" ", ways.Select(w => $"{w.Kind}@{w.Distance:F0}")));
            var past = AheadWaypoints.Pick(p, 1500, t => shapes[t], s => false);
            check(past.All(w => w.Distance > 1500), "Track Ahead: nothing behind the front of the train is listed");
        }

        private static void Times()
        {
            var b = new TrackGraphBuilder();
            int first = Add(b, 2000), bend = Add(b, 300), after = Add(b, 8000);
            b.Join(first, TrackEnd.End, bend, TrackEnd.Start);
            b.Join(bend, TrackEnd.End, after, TrackEnd.Start);
            var g = b.Build();
            var shapes = new[] { Straight(2000, d => 0), Curved(300, 100), Straight(8000, d => 0) };
            Func<int, TrackShape> shape = t => shapes[t];
            var path = AheadPath.Aligned(g, new TrackPosition(first, 0), TrackDirection.Forward, 99999, _ => 0);
            var p = AheadProfile.Build(path, shape);

            var steady = RunEstimate.Compute(p, 0, 0, 120 / 3.6);
            check(Near(steady.SecondsTo(500), 15, 0.2), "Track Ahead: at track speed, 500 m at 120 km/h takes 15 s", steady.SecondsTo(500).ToString("F1"));
            var fromRest = RunEstimate.Compute(p, 0, 0, 0);
            check(Near(fromRest.SecondsTo(100), Math.Sqrt(2 * 100 / RunEstimate.DefaultAcceleration), 1.5), "Track Ahead: from a stand the train accelerates at the set rate", fromRest.SecondsTo(100).ToString("F1"));
            check(fromRest.SecondsTo(4000) > fromRest.SecondsTo(1000) && fromRest.SecondsTo(1000) > 0, "Track Ahead: times grow with distance");

            const double bendEnd = 2300;
            var noLength = RunEstimate.Compute(p, 0, 0, 120 / 3.6);
            var longTrain = RunEstimate.Compute(p, 200, 200, 120 / 3.6);
            check(noLength.SpeedAt(2000) * 3.6 <= 40.5 && longTrain.SpeedAt(2000) * 3.6 <= 40.5, "Track Ahead: the train has braked to the limit when its front reaches it",
                $"{noLength.SpeedAt(2000) * 3.6:F1} km/h");
            // From 120 to 40 km/h at 0.35 m/s² takes about 1410 m, so braking starts near 590 m.
            check(noLength.SpeedAt(500) * 3.6 > 119 && noLength.SpeedAt(800) * 3.6 < 115, "Track Ahead: it brakes no sooner than it must", $"{noLength.SpeedAt(500) * 3.6:F1}, then {noLength.SpeedAt(800) * 3.6:F1} km/h");
            check(longTrain.SpeedAt(bendEnd + 150) * 3.6 <= 40.5 && noLength.SpeedAt(bendEnd + 150) * 3.6 > 45,
                "Track Ahead: a long train stays at the limit until its rear has cleared it", $"{longTrain.SpeedAt(bendEnd + 150) * 3.6:F1} against {noLength.SpeedAt(bendEnd + 150) * 3.6:F1} km/h");
            check(longTrain.SpeedAt(bendEnd + 400) * 3.6 > 41, "Track Ahead: and then speeds up");
            check(longTrain.SecondsTo(4000) - longTrain.SecondsTo(200) > noLength.SecondsTo(4000) - noLength.SecondsTo(200), "Track Ahead: train length costs time");
            check(noLength.SpeedAt(path.Length) == 0, "Track Ahead: the run ends stopped where the track ends");

            var open = AheadProfile.Build(AheadPath.Aligned(g, new TrackPosition(after, 0), TrackDirection.Forward, 1000, _ => 0), shape);
            check(open.Path.End == AheadEnd.DeadEnd, "Track Ahead: (the last track is the end of this network)");
            var (forked, forkShapes) = Forked();
            var horizon = AheadProfile.Build(AheadPath.Aligned(forked, new TrackPosition(0, 0), TrackDirection.Forward, 500, _ => 0), t => forkShapes[t]);
            check(RunEstimate.Compute(horizon, 0, 0, 16).SpeedAt(horizon.Path.Length) > 10, "Track Ahead: a path that only reaches its length does not stop there");
        }

        // ---- Routes ---------------------------------------------------------------

        private static Route Wait(RouteSearch search)
        {
            for (var waited = 0; !search.Done && waited < 5000; waited += 5)
                Thread.Sleep(5);
            return search.Route;
        }

        private static void RouteChecks()
        {
            // Forward through a switch set the other way, to a track entered from its far end.
            var (forked, forkShapes) = Forked();
            var request = new RouteRequest
            {
                EndA = new TrackPosition(0, 300),
                OutwardA = TrackDirection.Forward,
                EndB = new TrackPosition(0, 100),
                OutwardB = TrackDirection.Backward,
                TrainLength = 200,
                Goal = new TrackPosition(3, 500),
            };
            var route = Wait(new RouteSearch(forked, request));
            check(route != null && route.Reversals == 0, "Track Ahead: a route is found in the background", route?.Length.ToString("F0"));
            if (route == null)
                return;
            var path = AheadPath.Routed(forked, request.EndB, TrackDirection.Forward, 200, route, 0, 205, _ => 1);
            check(Near(path.RouteStart, 200, 0.01), "Track Ahead: a routed path starts with the train itself", path.RouteStart.ToString("F1"));
            check(path.Legs.Select(l => l.Track).Distinct().SequenceEqual(new[] { 0, 1, 3 }), "Track Ahead: it runs by the tracks of the route", string.Join(",", path.Legs.Select(l => l.Track)));
            var sw = path.Switches.Single();
            check(sw.Meeting == SwitchMeeting.Facing && sw.Branch == 0 && !sw.Lined, "Track Ahead: a switch set the other way is flagged", $"{sw.Meeting} branch {sw.Branch}");
            check(AheadPath.Routed(forked, request.EndB, TrackDirection.Forward, 200, route, 0, 205, _ => 0).Switches.Single().Lined, "Track Ahead: and one set for it is not");
            var finalLeg = path.Legs.Last();
            check(finalLeg.Track == 3 && Near(finalLeg.From, 1000, 0.01) && Near(finalLeg.To, 795, 0.01), "Track Ahead: the train stops with all of it on the last track", $"{finalLeg.From:F0} to {finalLeg.To:F0}");
            check(path.End == AheadEnd.Destination && path.Stops.Single().Kind == AheadStopKind.Destination && Near(path.Stops[0].Distance, path.Length, 0.01), "Track Ahead: the route ends in a stop");
            Func<int, TrackShape> forkShape = t => forkShapes[t];
            var profile = AheadProfile.Build(path, forkShape);
            var run = RunEstimate.Compute(profile, path.RouteStart, 200, 10);
            check(run.SpeedAt(path.Length) == 0, "Track Ahead: the run ends stopped at the destination");
            var along = AheadPath.Routed(forked, new TrackPosition(0, 200), TrackDirection.Forward, 200, route, 100, 205, _ => 0);
            check(Near(along.WholeLength, path.WholeLength - 100, 0.01), "Track Ahead: further along the route, less of it is left", along.WholeLength.ToString("F0"));
            var atPlace = AheadPath.Routed(forked, request.EndB, TrackDirection.Forward, 200, route, 0, null, _ => 0);
            check(Near(atPlace.Legs.Last().To, 500, 0.01), "Track Ahead: a route to a place on a track stops at that place", atPlace.Legs.Last().To.ToString("F0"));
            var near = path.Upto(1000);
            check(near.End == AheadEnd.Horizon && near.Stops.Count == 0 && near.Length < path.Length && Near(near.WholeLength, path.Length, 0.01), "Track Ahead: the near part of a route knows how long the whole is", near.Length.ToString("F0"));
            var wanted = AheadWaypoints.Pick(AheadProfile.Build(near, forkShape), near.RouteStart, forkShape, s => !s.Lined);
            check(wanted.Any(w => w.Kind == WaypointKind.Destination && Near(w.Distance, path.Length, 0.01)), "Track Ahead: its end is listed all the same");
            check(wanted.Any(w => w.Kind == WaypointKind.Switch && !near.Switches[w.SwitchIndex].Lined), "Track Ahead: and so is the switch to throw");

            var many = new[]
            {
                new AheadWaypoint(WaypointKind.Grade, 100), new AheadWaypoint(WaypointKind.LowerLimit, 200), new AheadWaypoint(WaypointKind.Yard, 300),
                new AheadWaypoint(WaypointKind.Switch, 400, switchIndex: 0), new AheadWaypoint(WaypointKind.Grade, 500), new AheadWaypoint(WaypointKind.Destination, 900),
            };
            var rows = AheadWaypoints.Shortlist(many, 4, w => w.Kind == WaypointKind.Switch || w.Kind == WaypointKind.Destination);
            check(rows.Select(w => w.Distance).SequenceEqual(new double[] { 100, 200, 400, 900 }), "Track Ahead: short of rows, what must be acted on stays, then the nearest", string.Join(",", rows.Select(w => w.Distance)));

            // A train on one branch going to the other: out onto the stem, clear, and back.
            var b = new TrackGraphBuilder();
            int stem = Add(b, 1000), left = Add(b, 1000), right = Add(b, 1000);
            b.AddJunction(stem, TrackEnd.Start, new[] { (left, TrackEnd.End), (right, TrackEnd.End) }, new MapPoint(0, 0));
            var graph = b.Build();
            var turning = new RouteRequest
            {
                EndA = new TrackPosition(left, 800),
                OutwardA = TrackDirection.Forward,
                EndB = new TrackPosition(left, 700),
                OutwardB = TrackDirection.Backward,
                TrainLength = 100,
                Goal = new TrackPosition(right, 500),
            };
            var back = Wait(new RouteSearch(graph, turning));
            check(back != null && back.Reversals == 1, "Track Ahead: reaching the other branch takes a reversal", back?.Reversals.ToString());
            if (back == null)
                return;
            var shapes = new[] { Straight(1000, d => 0), Straight(1000, d => 0), Straight(1000, d => 0) };
            var turned = AheadPath.Routed(graph, turning.EndB, TrackDirection.Forward, 100, back, 0, 105, _ => 0);
            check(turned.Stops.Count == 2 && turned.Stops[0].Kind == AheadStopKind.Reversal, "Track Ahead: the reversal is a stop on the way", string.Join(",", turned.Stops.Select(s => $"{s.Kind} {s.Distance:F0}")));
            // 100 m of train, 200 m to the switch, 115 m past it; then 15 m back to it and 105 m into the other branch.
            check(Near(turned.Stops[0].Distance, 100 + 200 + 115, 1), "Track Ahead: the train pulls its length past the switch and a little", turned.Stops[0].Distance.ToString("F0"));
            check(Near(turned.Length - turned.Stops[0].Distance, 15 + 105, 1), "Track Ahead: the distance goes on with the new leading end", (turned.Length - turned.Stops[0].Distance).ToString("F0"));
            check(turned.Switches.Count == 2 && turned.Switches[0].Meeting == SwitchMeeting.Trailing && turned.Switches[1].Meeting == SwitchMeeting.Facing && !turned.Switches[1].Lined,
                "Track Ahead: the switch passed going out must be thrown to come back", string.Join(",", turned.Switches.Select(s => $"{s.Meeting} {s.Lined}")));
            var turnedRun = RunEstimate.Compute(AheadProfile.Build(turned, t => shapes[t]), turned.RouteStart, 100, 5);
            check(turnedRun.SpeedAt(turned.Stops[0].Distance) < 0.5 && turnedRun.SpeedAt(turned.Stops[0].Distance + 60) > 1, "Track Ahead: the run stops to reverse, then goes on", turnedRun.SpeedAt(turned.Stops[0].Distance).ToString("F2"));

            var cutOff = new TrackGraphBuilder();
            int here = Add(cutOff, 500), away = Add(cutOff, 500);
            var none = new RouteSearch(cutOff.Build(), new RouteRequest
            {
                EndA = new TrackPosition(here, 300),
                OutwardA = TrackDirection.Forward,
                EndB = new TrackPosition(here, 200),
                OutwardB = TrackDirection.Backward,
                TrainLength = 100,
                Goal = new TrackPosition(away, 250),
                Blocked = (t, x, y) => false,
            });
            check(Wait(none) == null && none.Done && none.Failure == RouteFailure.NoPath, "Track Ahead: with no way there, the search says so");
        }

        // ---- The sign rules, on curves whose answers are known ------------------

        // The size of each game method SignCurve and SignLimits repeat. A game
        // update that changes one shows here, without running the game.
        private static readonly (Type Type, string Method, int Size)[] SignSteps =
        {
            (typeof(SignPlacer), "GetSegmentInfos", 579),
            (typeof(SignPlacer), "GetMinRadiusInFirstXMeters", 120),
            (typeof(SignPlacer), "SplitLastSegmentIfNeeded", 247),
            (typeof(SignPlacer), "MergeSameSpeed", 149),
            (typeof(SignPlacer), "MinimizeSpeedDifference", 352),
            (AccessTools.Inner(typeof(SignPlacer), "CurveSegmentInfo"), "GetSpeed", 27),
            (AccessTools.Inner(typeof(SignPlacer), "CurveSegmentInfo"), "GetMaxSpeedForRadius", 196),
            (AccessTools.Inner(typeof(SignPlacer), "CurveSegmentInfo"), "GetGradeAngle", 63),
            (typeof(SignPlacerUtils), "ChunkifyNumbers", 291),
            (typeof(SignPlacerUtils), "MinimizeSpeedDifference", 345),
            (typeof(SignPlacerUtils), "GetMidpointSpeed", 18),
            (typeof(BezierArcApproximation), "CalculateArcs", 450),
            (typeof(BezierArcApproximation), "GetCCenter", 507),
            (typeof(BezierArcApproximation), "LineLineIntersection2D", 111),
            (typeof(BezierArcApproximation), "Error", 74),
            (typeof(BezierCurve), "GetPointAt", 32),
            (typeof(BezierCurve), "GetTBetweenPoints", 529),
            (typeof(BezierCurve), "GetCubicCurvePoint", 139),
            (typeof(BezierCurve), "GetQuadraticCurvePoint", 84),
            (typeof(BezierCurve), "GetLinearPoint", 20),
        };

        internal static IEnumerable<string> ChangedSignSteps()
        {
            foreach (var (type, name, size) in SignSteps)
            {
                var method = type != null ? AccessTools.Method(type, name) : null;
                var now = method?.GetMethodBody()?.GetILAsByteArray().Length ?? -1;
                if (now != size)
                    yield return $"{type?.Name}.{name} is {now} bytes, was {size}";
            }
        }

        // Control points a sixteenth of a turn or less apart around a circle, handles tangent to it.
        private static SignCurve Circle(double radius, double length, double climb = 0)
        {
            var turn = length / radius;
            var spans = Math.Max(2, (int)Math.Ceiling(turn / (Math.PI / 8)));
            var step = turn / spans;
            var handle = 4.0 / 3.0 * Math.Tan(step / 4) * radius;
            return Knotted(spans + 1, i =>
            {
                var a = i * step;
                var (x, z) = (radius * Math.Sin(a), radius * (1 - Math.Cos(a)));
                return (x, climb * i / spans, z, Math.Cos(a) * handle, Math.Sin(a) * handle, climb / spans / 3);
            });
        }

        // Bends one after another, each turning the opposite way to the last.
        private static SignCurve Bends(params (double Radius, double Length)[] parts)
        {
            var knots = new List<(double, double, double, double, double, double)>();
            double x = 0, z = 0, heading = 0;
            var side = 1;
            foreach (var (radius, length) in parts)
            {
                var spans = Math.Max(2, (int)Math.Ceiling(length / radius / (Math.PI / 8)));
                var step = length / radius / spans;
                var handle = 4.0 / 3.0 * Math.Tan(step / 4) * radius;
                for (var i = knots.Count == 0 ? 0 : 1; i <= spans; i++)
                {
                    var a = i * step;
                    var along = radius * Math.Sin(a);
                    var off = side * radius * (1 - Math.Cos(a));
                    var h = heading + side * a;
                    knots.Add((x + along * Math.Cos(heading) - off * Math.Sin(heading), 0, z + along * Math.Sin(heading) + off * Math.Cos(heading), Math.Cos(h) * handle, Math.Sin(h) * handle, 0));
                }
                var last = knots[knots.Count - 1];
                x = last.Item1;
                z = last.Item3;
                heading += side * length / radius;
                side = -side;
            }
            return Knotted(knots.Count, i => knots[i]);
        }

        private static SignCurve Knotted(int count, Func<int, (double X, double Y, double Z, double HandleX, double HandleZ, double HandleY)> knot)
        {
            var floats = new float[count * SignCurve.FloatsPerKnot];
            for (var i = 0; i < count; i++)
            {
                var k = knot(i);
                var values = new[]
                {
                    k.X, k.Y, k.Z,
                    k.X - k.HandleX, k.Y - k.HandleY, k.Z - k.HandleZ,
                    k.X + k.HandleX, k.Y + k.HandleY, k.Z + k.HandleZ,
                };
                for (var v = 0; v < values.Length; v++)
                    floats[i * SignCurve.FloatsPerKnot + v] = (float)values[v];
            }
            return new SignCurve(floats, false, 5f);
        }

        private static string Listed(IEnumerable<PostedSegment> segments) => string.Join(" ", segments.Select(s => $"{s.From:F0}-{s.To:F0}:{s.Kmh:F0}"));

        private static void SignRuleChecks()
        {
            var bend = Circle(300, 1200);
            check(Near(bend.Length, 1200, 1), "Sign rules: a curve's length is measured along it", bend.Length.ToString("F2"));
            var start = bend.PointAt(0);
            var end = bend.PointAt(1);
            check(Near(start.X, 0, 0.01) && Near(start.Z, 0, 0.01) && Near(end.X, 300 * Math.Sin(4), 0.01) && Near(end.Z, 300 * (1 - Math.Cos(4)), 0.01), "Sign rules: a curve runs from its first point to its last");
            var off = Enumerable.Range(1, 99).Max(i =>
            {
                var p = bend.PointAt(i / 100f);
                return Math.Abs(Math.Sqrt(p.X * p.X + (p.Z - 300.0) * (p.Z - 300.0)) - 300);
            });
            check(off < 0.05, "Sign rules: places along the curve lie on it", off.ToString("F3"));

            // 300 m radius is under the table's 360 m, so 70 km/h every way and all the way.
            var same = SignLimits.Work(bend, 1200);
            check(same.Forward.All(s => s.Kmh == 70) && same.Backward.All(s => s.Kmh == 70), "Sign rules: a steady curve takes the limit for its radius", Listed(same.Forward));
            check(Near(same.Forward[0].From, 0, 0.01) && Near(same.Forward.Last().To, 1200, 0.01), "Sign rules: forward runs from the track's start to its end", Listed(same.Forward));
            check(Near(same.Backward[0].From, 1200, 0.01) && Near(same.Backward.Last().To, 0, 0.01), "Sign rules: backward runs from its end to its start", Listed(same.Backward));
            check(same.Forward.Zip(same.Forward.Skip(1), (a, b) => Near(a.To, b.From, 0.01)).All(x => x), "Sign rules: its stretches meet", Listed(same.Forward));

            // 2,000 m at 1,500 m radius, 400 m at 200 m, 2,000 m at 1,500 m.
            var parts = Bends((1500, 2000), (200, 400), (1500, 2000));
            var limits = SignLimits.Work(parts, 4400);
            Func<IReadOnlyList<PostedSegment>, double, double> at = (list, d) => list.First(s => d >= Math.Min(s.From, s.To) && d <= Math.Max(s.From, s.To)).Kmh;
            check(at(limits.Forward, 2200) <= 60 && at(limits.Backward, 2200) <= 60, "Sign rules: the tight bend is limited each way", Listed(limits.Forward) + " / " + Listed(limits.Backward));
            check(at(limits.Forward, 200) == 120 && at(limits.Backward, 4200) == 120, "Sign rules: the open line before it is not", Listed(limits.Forward) + " / " + Listed(limits.Backward));
            var drops = limits.Forward.Zip(limits.Forward.Skip(1), (a, b) => a.Kmh - b.Kmh).ToList();
            check(drops.Max() <= 30, "Sign rules: a limit never falls more than 30 km/h at once", Listed(limits.Forward));
            check(limits.Forward.Any(s => s.Kmh > 60 && s.Kmh < 120), "Sign rules: the fall to the bend comes in steps", Listed(limits.Forward));

            // The worker gives what the rules give, keeps going past a curve it cannot use, and stops.
            var worker = new SignLimitWorker();
            var flat = new float[2 * SignCurve.FloatsPerKnot];
            worker.Add(1, Circle(300, 1200), 1200);
            worker.Add(2, new SignCurve(flat, false, 5f), 100);
            worker.Add(3, Bends((1500, 2000), (200, 400), (1500, 2000)), 4400);
            var results = new List<SignLimitResult>();
            for (var waited = 0; results.Count < 3 && waited < 5000; waited += 5)
            {
                if (worker.TryTake(out var result))
                    results.Add(result);
                else
                    Thread.Sleep(5);
            }
            check(results.Select(r => r.Track).SequenceEqual(new[] { 1, 2, 3 }) && worker.Unfinished == 0, "Sign limit worker: every curve comes back, in order", string.Join(",", results.Select(r => r.Track)));
            if (results.Count == 3)
            {
                check(results[1].Limits == null && results[1].Error != null, "Sign limit worker: a curve it cannot use comes back as an error", results[1].Error?.Message);
                check(results[2].Limits != null && Listed(results[2].Limits.Forward) == Listed(limits.Forward) && Listed(results[2].Limits.Backward) == Listed(limits.Backward),
                    "Sign limit worker: its limits are the rules' limits");
            }
            worker.Stop();
            worker.Add(4, Circle(300, 1200), 1200);
            check(worker.Unfinished == 0 && !worker.TryTake(out _), "Sign limit worker: once stopped it takes nothing more");
        }
    }
}
