using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using CommsTablet.Core.Ahead;
using CommsTablet.Core.Booklet;
using CommsTablet.Core.Routing;
using CommsTablet.Core.Tracks;
using CommsTablet.Map;
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
            CurveAndProfileChecks();
            Paths();
            Profiles();
            Times();
            SignRuleChecks();
            RouteChecks();
            CorrectnessChecks();
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void CurveAndProfileChecks()
        {
            // CurveSpeed.KmhForRadius: straight, tight, and moderate table boundaries.
            var tableCases = new (double Radius, double Kmh)[]
            {
                (30, 10), (49.9, 10),
                (50, 20), (69.9, 20),
                (70, 30), (94.9, 30),
                (95, 40), (129.9, 40),
                (130, 50), (169.9, 50),
                (170, 60), (229.9, 60),
                (230, 70), (359.9, 70),
                (360, 80), (699.9, 80),
                (700, 90), (899.9, 90),
                (900, 100), (1199.9, 100),
                (1200, CurveSpeed.StraightKmh), (5000, CurveSpeed.StraightKmh), (double.PositiveInfinity, CurveSpeed.StraightKmh),
            };
            var badRadius = tableCases.Where(c => !Near(CurveSpeed.KmhForRadius(c.Radius), c.Kmh, 1e-9)).Select(c => $"{c.Radius}->{CurveSpeed.KmhForRadius(c.Radius)}").ToList();
            check(badRadius.Count == 0, "CurveSpeed: KmhForRadius matches straight, tight, and moderate curve limits", string.Join(", ", badRadius));

            // CurveSpeed.Radius: straight, coincident, and known circles in either winding order.
            check(double.IsPositiveInfinity(CurveSpeed.Radius(new MapPoint(0, 0), new MapPoint(10, 0), new MapPoint(20, 0)))
                  && double.IsPositiveInfinity(CurveSpeed.Radius(new MapPoint(5, 5), new MapPoint(5, 5), new MapPoint(10, 10))),
                "CurveSpeed: Radius is infinite for collinear or coincident points");
            MapPoint OnCircle(double r, double rad) => new MapPoint(r * Math.Cos(rad), r * Math.Sin(rad));
            var r100 = CurveSpeed.Radius(OnCircle(100, 0), OnCircle(100, 0.2), OnCircle(100, 0.5));
            var r100Rev = CurveSpeed.Radius(OnCircle(100, 0.5), OnCircle(100, 0.2), OnCircle(100, 0));
            var r300 = CurveSpeed.Radius(OnCircle(300, -0.1), OnCircle(300, 0.1), OnCircle(300, 0.3));
            check(Near(r100, 100, 1e-6) && Near(r100Rev, 100, 1e-6) && Near(r300, 300, 1e-6),
                "CurveSpeed: Radius recovers circle radius regardless of point order", $"{r100:F3}, {r100Rev:F3}, {r300:F3}");

            // CurveSpeed.Profile: < 3 points, straight + cap, tight & moderate curves, compound straight-bend-straight, non-zero offset start.
            var tiny = CurveSpeed.Profile(new[] { 0.0, 10.0 }, new[] { new MapPoint(0, 0), new MapPoint(10, 0) }, capKmh: 50);
            check(tiny.Count == 1 && Near(tiny.LimitAt(0) * 3.6, 50, 1e-6), "CurveSpeed: fewer than three points returns a capped single-entry profile");

            var straightOffs = Enumerable.Range(0, 41).Select(i => 100.0 + i * 10.0).ToArray();
            var straightPts = straightOffs.Select(d => new MapPoint(d, 0)).ToArray();
            var straightProf = CurveSpeed.Profile(straightOffs, straightPts);
            var cappedProf = CurveSpeed.Profile(straightOffs, straightPts, capKmh: 60);
            check(Near(straightProf.OffsetAt(0), 0, 1e-9) && Near(straightProf.LimitAt(200) * 3.6, 120, 1e-6)
                  && Near(cappedProf.LimitAt(200) * 3.6, 60, 1e-6),
                "CurveSpeed: straight profile normalizes start offset to 0 and applies capKmh");

            SpeedLimitProfile ArcProfile(double radius, double length)
            {
                var n = (int)(length / 5) + 1;
                var offs = Enumerable.Range(0, n).Select(i => i * 5.0).ToArray();
                var pts = offs.Select(d => new MapPoint(radius * Math.Sin(d / radius), radius * (1 - Math.Cos(d / radius)))).ToArray();
                return CurveSpeed.Profile(offs, pts);
            }
            var tightProf = ArcProfile(60, 300);
            var moderateProf = ArcProfile(200, 400);
            check(Near(tightProf.LimitAt(150) * 3.6, 20, 1e-6) && Near(moderateProf.LimitAt(200) * 3.6, 60, 1e-6),
                "CurveSpeed: tight (60 m -> 20 km/h) and moderate (200 m -> 60 km/h) curves get their table limits");

            // Compound track: 200 m straight, 200 m curve (R=100 m -> 40 km/h), 200 m straight.
            var compOffs = new List<double>();
            var compPts = new List<MapPoint>();
            const double R = 100.0;
            for (var d = 0.0; d <= 200.0; d += 5.0)
            {
                compOffs.Add(d);
                compPts.Add(new MapPoint(d, 0));
            }
            for (var d = 5.0; d <= 200.0; d += 5.0)
            {
                var a = d / R;
                compOffs.Add(200.0 + d);
                compPts.Add(new MapPoint(200.0 + R * Math.Sin(a), R * (1 - Math.Cos(a))));
            }
            var endAngle = 200.0 / R;
            double ex = 200.0 + R * Math.Sin(endAngle), ez = R * (1 - Math.Cos(endAngle));
            double tx = Math.Cos(endAngle), tz = Math.Sin(endAngle);
            for (var d = 5.0; d <= 200.0; d += 5.0)
            {
                compOffs.Add(400.0 + d);
                compPts.Add(new MapPoint(ex + d * tx, ez + d * tz));
            }
            var compProf = CurveSpeed.Profile(compOffs, compPts);
            check(Near(compProf.LimitAt(50) * 3.6, 120, 1e-6)
                  && Near(compProf.LimitAt(300) * 3.6, 40, 1e-6)
                  && Near(compProf.LimitAt(550) * 3.6, 120, 1e-6)
                  && Near(compProf.MinLimit(0, 600) * 3.6, 40, 1e-6),
                "CurveSpeed: compound straight-curve-straight profile drops only through the curve");

            // SpeedLimitProfile: Unlimited, constructor validation, defensive copy, queries outside range, MinLimit, FromSamples merging/overlapping.
            check(SpeedLimitProfile.Unlimited.Count == 1
                  && double.IsPositiveInfinity(SpeedLimitProfile.Unlimited.LimitAt(-10))
                  && double.IsPositiveInfinity(SpeedLimitProfile.Unlimited.LimitAt(5000))
                  && double.IsPositiveInfinity(SpeedLimitProfile.Unlimited.MinLimit(0, 5000)),
                "SpeedLimitProfile: Unlimited is positive infinity everywhere");

            check(Throws<ArgumentException>(() => new SpeedLimitProfile(null, new[] { 10.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new double[0], new double[0]))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 0.0, 100.0 }, new[] { 10.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 10.0 }, new[] { 10.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 0.0, 100.0, 100.0 }, new[] { 10.0, 20.0, 30.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 0.0, 100.0, 50.0 }, new[] { 10.0, 20.0, 30.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 0.0 }, new[] { 0.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 0.0 }, new[] { -5.0 }))
                  && Throws<ArgumentException>(() => new SpeedLimitProfile(new[] { 0.0 }, new[] { double.NaN })),
                "SpeedLimitProfile: constructor rejects invalid offsets and non-positive speeds");

            var srcOffs = new[] { 0.0, 200.0, 500.0 };
            var srcSpds = new[] { 30.0, 15.0, 25.0 };
            var prof = new SpeedLimitProfile(srcOffs, srcSpds);
            srcOffs[1] = 999.0;
            srcSpds[1] = 999.0;
            check(prof.Count == 3 && Near(prof.OffsetAt(1), 200, 1e-9) && Near(prof.SpeedAt(1), 15, 1e-9),
                "SpeedLimitProfile: constructor copies input arrays defensively");

            check(Near(prof.LimitAt(-100), 30, 1e-9) && Near(prof.LimitAt(0), 30, 1e-9)
                  && Near(prof.LimitAt(199.9), 30, 1e-9) && Near(prof.LimitAt(200), 15, 1e-9)
                  && Near(prof.LimitAt(499.9), 15, 1e-9) && Near(prof.LimitAt(500), 25, 1e-9)
                  && Near(prof.LimitAt(99999), 25, 1e-9),
                "SpeedLimitProfile: LimitAt handles boundaries and queries outside range");

            check(Near(prof.MinLimit(0, 150), 30, 1e-9)
                  && Near(prof.MinLimit(0, 200), 30, 1e-9)
                  && Near(prof.MinLimit(0, 201), 15, 1e-9)
                  && Near(prof.MinLimit(600, 100), 15, 1e-9)
                  && Near(prof.MinLimit(100, 600), 15, 1e-9)
                  && Near(prof.MinLimit(-50, 50), 30, 1e-9)
                  && Near(prof.MinLimit(600, 1200), 25, 1e-9),
                "SpeedLimitProfile: MinLimit spans zones in either order and outside range");

            var emptySamples = SpeedLimitProfile.FromSamples(Array.Empty<double>(), Array.Empty<double>());
            check(emptySamples == SpeedLimitProfile.Unlimited, "SpeedLimitProfile.FromSamples: empty input returns Unlimited");

            // Quantum merging + overlapping/non-increasing offsets taking the lower limit + infinite values.
            var merged = SpeedLimitProfile.FromSamples(
                new[] { 0.0, 50.0, 100.0, 100.0, 80.0, 200.0, 300.0, 400.0 },
                new[] { 30.0, 29.4, 20.0, 14.0, 12.0, 12.5, double.PositiveInfinity, double.PositiveInfinity },
                quantum: 1.0);
            check(merged.Count == 3
                  && Near(merged.OffsetAt(0), 0, 1e-9) && Near(merged.SpeedAt(0), 29.4, 1e-9)
                  && Near(merged.OffsetAt(1), 100, 1e-9) && Near(merged.SpeedAt(1), 12.0, 1e-9)
                  && Near(merged.OffsetAt(2), 300, 1e-9) && double.IsPositiveInfinity(merged.SpeedAt(2)),
                "SpeedLimitProfile.FromSamples: merges within quantum, folds overlapping offsets to lowest limit, and handles infinity",
                string.Join(", ", Enumerable.Range(0, merged.Count).Select(i => $"{merged.OffsetAt(i):F0}:{merged.SpeedAt(i):F1}")));
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
            var elapsed = Stopwatch.StartNew();
            while (!search.Done && elapsed.ElapsedMilliseconds < 30000)
                Thread.Sleep(5);
            if (!search.Done)
            {
                check(false, "Track Ahead: route search timed out after 30 s", $"failure={search.Failure}");
                return null;
            }
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
            check(Wait(none) == null && none.Done && none.Error == null && none.Failure == RouteFailure.NoPath,
                $"Track Ahead: with no way there, the search says so (failure={none.Failure}, error={none.Error?.GetType().Name})");
        }

        private static void CorrectnessChecks()
        {
            // 1. Stable siding arrival point: stop must not jump to midpoint after the train's nose enters the siding.
            {
                var b = new TrackGraphBuilder();
                int approach = Add(b, 500), siding = Add(b, 400);
                b.Join(approach, TrackEnd.End, siding, TrackEnd.Start);
                var g = b.Build();
                var route = new Router(g).Find(new RouteRequest
                {
                    EndA = new TrackPosition(approach, 200),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(approach, 120),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 80,
                    Goal = new TrackPosition(siding, 200),
                });
                check(route != null, "Arrival: route into siding found");
                if (route != null)
                {
                    // Before entering siding: 300 m on approach + 85 m into siding = 385 m ahead of nose.
                    var before = AheadPath.Routed(g, new TrackPosition(approach, 120), TrackDirection.Forward, 80, route, 0, 85, _ => 0);
                    // 1 m into siding (along = 301): stop must remain at offset 85, not jump to 200.
                    var entered = AheadPath.Routed(g, new TrackPosition(approach, 421), TrackDirection.Forward, 80, route, 301, 85, _ => 0);
                    // Past arrival stop (along = 390, offset 90): remaining route distance is 0, no backward leg.
                    var past = AheadPath.Routed(g, new TrackPosition(siding, 10), TrackDirection.Forward, 80, route, 390, 85, _ => 0);
                    check(Near(before.Legs.Last().To, 85, 0.01) && Near(entered.Legs.Last().To, 85, 0.01)
                          && Near(entered.WholeLength - entered.RouteStart, 84, 0.01),
                        "Arrival: siding stop stays fixed after nose enters siding",
                        $"before={before.Legs.Last().To:F1}, entered={entered.Legs.Last().To:F1}, rem={entered.WholeLength - entered.RouteStart:F1}");
                    check(Near(past.Length - past.RouteStart, 0, 0.01),
                        "Arrival: past the computed stop, remaining distance is zero with no backward leg",
                        $"rem={past.Length - past.RouteStart:F1}");
                }

                // Entering siding from TrackEnd.End (backward into siding).
                var bRev = new TrackGraphBuilder();
                int appRev = Add(bRev, 500), sidRev = Add(bRev, 400);
                bRev.Join(appRev, TrackEnd.End, sidRev, TrackEnd.End);
                var gRev = bRev.Build();
                var routeRev = new Router(gRev).Find(new RouteRequest
                {
                    EndA = new TrackPosition(appRev, 200),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(appRev, 120),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 80,
                    Goal = new TrackPosition(sidRev, 200),
                });
                check(routeRev != null, "Arrival: reversed siding route found");
                if (routeRev != null)
                {
                    var beforeRev = AheadPath.Routed(gRev, new TrackPosition(appRev, 120), TrackDirection.Forward, 80, routeRev, 0, 85, _ => 0);
                    var enteredRev = AheadPath.Routed(gRev, new TrackPosition(appRev, 421), TrackDirection.Forward, 80, routeRev, 301, 85, _ => 0);
                    check(Near(beforeRev.Legs.Last().To, 315, 0.01) && Near(enteredRev.Legs.Last().To, 315, 0.01)
                          && Near(enteredRev.WholeLength - enteredRev.RouteStart, 84, 0.01),
                        "Arrival: siding entered from End keeps its stop fixed after entry",
                        $"before={beforeRev.Legs.Last().To:F1}, entered={enteredRev.Legs.Last().To:F1}");
                }

                // Long train (250 m + 5 m margin = 255 m on 400 m siding): GoalIntoTrack keeps route valid past midpoint 200 m.
                var longRoute = new Router(g).Find(new RouteRequest
                {
                    EndA = new TrackPosition(approach, 300),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(approach, 50),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 250,
                    Goal = new TrackPosition(siding, 200),
                    GoalIntoTrack = 255,
                });
                check(longRoute != null && Near(longRoute.Legs[0].Pieces.Last().To, 255, 0.01)
                      && longRoute.Legs[0].TryLocate(new TrackPosition(siding, 220), 0, 5, out _, out _),
                    "Arrival: long train past siding midpoint stays located on the route up to its siding stop");

                // Siding far-end clearance cap (10 m): 120 m train on 100 m siding stops at 90 m, leaving a car at 94 m clear.
                var bShortSid = new TrackGraphBuilder();
                int appCap = Add(bShortSid, 300), sidCap = Add(bShortSid, 100);
                bShortSid.Join(appCap, TrackEnd.End, sidCap, TrackEnd.Start);
                var gCap = bShortSid.Build();
                var capSearch = new RouteSearch(gCap, new RouteRequest
                {
                    EndA = new TrackPosition(appCap, 200),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(appCap, 80),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 120,
                    Goal = new TrackPosition(sidCap, 50),
                    GoalIntoTrack = 125,
                    Blocked = (t, x, y) => t == sidCap && Math.Min(x, y) <= 98 && Math.Max(x, y) >= 94,
                });
                var capRoute = Wait(capSearch);
                check(capRoute != null && Near(capRoute.Legs[0].Pieces.Last().To, 90, 0.01) && !capSearch.ThroughCars,
                    "Arrival: siding stop caps 10 m short of the far end and checks occupancy only up to that cap");

                // Direct start right at siding entrance (offset 0) uses GoalIntoTrack for stop and occupancy.
                var directSearch = new RouteSearch(g, new RouteRequest
                {
                    EndA = new TrackPosition(siding, 0),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(approach, 250),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 250,
                    Goal = new TrackPosition(siding, 200),
                    GoalIntoTrack = 255,
                    Blocked = (t, x, y) => t == siding && Math.Min(x, y) <= 235 && Math.Max(x, y) >= 225,
                });
                var directRoute = Wait(directSearch);
                check(directRoute != null && Near(directRoute.Legs[0].Pieces.Last().To, 255, 0.01) && directSearch.ThroughCars,
                    "Arrival: direct start at siding entrance routes to the siding stop and checks occupancy up to it");
            }

            // 2. Occupied-destination standing-car warning: siding stop reach and exact GoalCarSpan exemption.
            {
                var b = new TrackGraphBuilder();
                int main = Add(b, 500), siding = Add(b, 400);
                b.Join(main, TrackEnd.End, siding, TrackEnd.Start);
                var g = b.Build();

                bool CarOverlap(double a, double c, double lo, double hi)
                {
                    var low = Math.Min(a, c);
                    var high = Math.Max(a, c);
                    return lo <= high && hi >= low;
                }

                RouteRequest MakeReq(Func<int, double, double, bool> blocked, (double, double)? goalSpan = null, double? goalInto = null, double trainLength = 100) => new RouteRequest
                {
                    EndA = new TrackPosition(main, 300),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(main, 300 - trainLength),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = trainLength,
                    Goal = new TrackPosition(siding, 200),
                    GoalIntoTrack = goalInto,
                    Blocked = blocked,
                    GoalCarSpan = goalSpan,
                };

                // Goal car [190, 210] AND earlier car [50, 70]: default (no span) triggers ThroughCars.
                var bothDefault = new RouteSearch(g, MakeReq((t, x, y) => t == siding && (CarOverlap(x, y, 50, 70) || CarOverlap(x, y, 190, 210))));
                check(Wait(bothDefault) != null && bothDefault.ThroughCars,
                    "Occupancy: standing cars at and before goal trigger ThroughCars by default");

                // Goal car [190, 210] ONLY: default triggers ThroughCars for an exact-offset goal.
                var goalOnlyDefault = new RouteSearch(g, MakeReq((t, x, y) => t == siding && CarOverlap(x, y, 190, 210)));
                check(Wait(goalOnlyDefault) != null && goalOnlyDefault.ThroughCars,
                    "Occupancy: a car covering an exact goal triggers ThroughCars unless exempted by GoalCarSpan");

                // Production-style siding goal (100 m train -> stop at 105 m) with cars at [180, 400]: stop is clear -> no ThroughCars!
                var sidingClearBeforeCars = new RouteSearch(g, MakeReq((t, x, y) => t == siding && CarOverlap(x, y, 180, 400), goalInto: 105, trainLength: 100));
                check(Wait(sidingClearBeforeCars) != null && !sidingClearBeforeCars.ThroughCars,
                    "Occupancy: partly occupied siding beyond the train's arrival stop does not falsely warn ThroughCars");

                // Long train (250 m -> stop at 255 m) with car at [230, 240] (past midpoint 200, before stop 255): MUST warn ThroughCars!
                var sidingCarBeforeLongStop = new RouteSearch(g, MakeReq((t, x, y) => t == siding && CarOverlap(x, y, 230, 240), goalInto: 255, trainLength: 250));
                check(Wait(sidingCarBeforeLongStop) != null && sidingCarBeforeLongStop.ThroughCars,
                    "Occupancy: car past siding midpoint but before long train's arrival stop triggers ThroughCars");

                // GoalCarSpan still blocks on an earlier car [50, 70] AND on a touching car [170, 190] against [190, 210].
                var bothSpan = new RouteSearch(g, MakeReq((t, x, y) => t == siding && (CarOverlap(x, y, 50, 70) || CarOverlap(x, y, 190, 210)), goalSpan: (190, 210)));
                check(Wait(bothSpan) != null && bothSpan.ThroughCars,
                    "Occupancy: GoalCarSpan still checks intervening track up to the goal car's near edge");

                var touchingSpan = new RouteSearch(g, MakeReq((t, x, y) => t == siding && (CarOverlap(x, y, 170, 190) || CarOverlap(x, y, 190, 210)), goalSpan: (190, 210)));
                check(Wait(touchingSpan) != null && touchingSpan.ThroughCars,
                    "Occupancy: GoalCarSpan does not exempt a second touching car before the target car");

                // GoalCarSpan with clear approach succeeds without ThroughCars.
                var clearSpan = new RouteSearch(g, MakeReq((t, x, y) => t == siding && CarOverlap(x, y, 190, 210), goalSpan: (190, 210)));
                check(Wait(clearSpan) != null && !clearSpan.ThroughCars,
                    "Occupancy: GoalCarSpan succeeds without ThroughCars when approach up to goal car is clear");
            }

            // 3. Full physical car occupancy & coupler overhang across joints.
            {
                var b = new TrackGraphBuilder();
                int t0 = Add(b, 50), mid = Add(b, 8), t2 = Add(b, 50), t3 = Add(b, 50);
                b.Join(t0, TrackEnd.End, mid, TrackEnd.Start);
                b.Join(mid, TrackEnd.End, t2, TrackEnd.Start);
                // Reversed joint between t2.End and t3.End.
                b.Join(t2, TrackEnd.End, t3, TrackEnd.End);
                var g = b.Build();

                // Car straddling t0 -> mid -> t2: mid must be marked occupied.
                var spans = new List<(int Track, double From, double To)>();
                TrainSpans.SpansOnGraph(g, t2, 3, TrackDirection.Forward, 3, t0, 47, TrackDirection.Forward, 3, 25, _ => 0, spans);
                check(spans.Any(s => s.Track == mid && Near(Math.Min(s.From, s.To), 0, 0.01) && Near(Math.Max(s.From, s.To), 8, 0.01)),
                    "TrainSpans: short intermediate track wholly between bogies is marked occupied",
                    string.Join(", ", spans.Select(s => $"{s.Track}:{s.From:F1}-{s.To:F1}")));

                // Front overhang crossing reversed joint t2.End -> t3.End carries onto t3 with flipped outward direction.
                spans.Clear();
                TrainSpans.SpansOnGraph(g, t2, 48, TrackDirection.Forward, 4, t2, 36, TrackDirection.Forward, 3, 25, _ => 0, spans);
                check(spans.Any(s => s.Track == t3 && Near(s.From, 50, 0.01) && Near(s.To, 48, 0.01)),
                    "TrainSpans: coupler overhang carries across connected joints onto adjoining tracks",
                    string.Join(", ", spans.Select(s => $"{s.Track}:{s.From:F1}-{s.To:F1}")));

                TrainSpans.AdvanceOverhang(g, t2, 48, TrackDirection.Forward, 4, _ => 0, out var couplerAt, out var outward, null);
                check(couplerAt.Track == t3 && Near(couplerAt.Offset, 48, 0.01) && outward == TrackDirection.Backward,
                    "TrainSpans: CouplerEnd advances across reversed joint with updated track and outward direction",
                    $"{couplerAt} {outward}");

                // Dead end clamps cleanly.
                TrainSpans.AdvanceOverhang(g, t0, 2, TrackDirection.Backward, 5, _ => 0, out var deadAt, out var deadOut, null);
                check(deadAt.Track == t0 && Near(deadAt.Offset, 0, 0.01) && deadOut == TrackDirection.Backward,
                    "TrainSpans: overhang clamps at dead ends", $"{deadAt} {deadOut}");

                // Diamond bridge: long branch (15 m) reaches merge (2 m) within 20 m budget (17 m) but fails on downstream tail (4 m -> 21 m);
                // short branch (5 m) must re-admit merge at 7 m and reach tail at 11 m <= 20 m.
                var bd = new TrackGraphBuilder();
                int rTrack = Add(bd, 30), bLong = Add(bd, 15), bShort = Add(bd, 5), merge = Add(bd, 2), tail = Add(bd, 4), fTrack = Add(bd, 30);
                bd.AddJunction(rTrack, TrackEnd.End, new[] { (bLong, TrackEnd.Start), (bShort, TrackEnd.Start) }, new MapPoint(30, 0));
                bd.AddJunction(merge, TrackEnd.Start, new[] { (bLong, TrackEnd.End), (bShort, TrackEnd.End) }, new MapPoint(50, 0));
                bd.Join(merge, TrackEnd.End, tail, TrackEnd.Start);
                bd.Join(tail, TrackEnd.End, fTrack, TrackEnd.Start);
                var gd = bd.Build();
                spans.Clear();
                // selectedBranch returns 0 (bLong first), wheelbase 14 m -> maxBridgeLength = 20 m.
                TrainSpans.SpansOnGraph(gd, fTrack, 2, TrackDirection.Forward, 2, rTrack, 28, TrackDirection.Forward, 2, 14, _ => 0, spans);
                check(spans.Any(s => s.Track == bShort) && spans.Any(s => s.Track == merge) && spans.Any(s => s.Track == tail),
                    "TrainSpans: bogie bridge re-admits shared merge via shorter branch to reach downstream track within budget",
                    string.Join(", ", spans.Select(s => $"{s.Track}:{s.From:F1}-{s.To:F1}")));
            }

            // 4. Iterative cycle-safe booklet walk & overflow-safe track parsing.
            {
                check(!YardTrackName.TryParse("[Y]_[SM]_[A-999999999999999-S]", out _),
                    "Booklet: YardTrackName.TryParse rejects oversized track numbers without throwing");
                check(YardTrackName.TryParse("[Y]_[SM]_[A-04-S]", out var okName) && okName.Number == 4 && okName.Kind == "S",
                    "Booklet: YardTrackName.TryParse still parses valid track names");

                // Off-page loop on branch 0 of an off-page switch, valid on-page target on branch 1.
                var lb = new TrackGraphBuilder();
                int stem0 = Add(lb, 100), b0 = Add(lb, 1), b1 = Add(lb, 100), targetStem = Add(lb, 100), targetA = Add(lb, 100), targetB = Add(lb, 100);
                int jStart = lb.AddJunction(stem0, TrackEnd.End, new[] { (b0, TrackEnd.Start), (targetB, TrackEnd.Start) }, new MapPoint(0, 0));
                int loopIn = Add(lb, 2), loopBack = Add(lb, 2);
                int jLoop = lb.AddJunction(loopIn, TrackEnd.End, new[] { (loopBack, TrackEnd.Start), (b1, TrackEnd.Start) }, new MapPoint(10, 0));
                int jMerge = lb.AddJunction(loopIn, TrackEnd.Start, new[] { (b0, TrackEnd.End), (loopBack, TrackEnd.End) }, new MapPoint(5, 0));
                int jEnd = lb.AddJunction(targetStem, TrackEnd.Start, new[] { (b1, TrackEnd.End), (targetA, TrackEnd.Start) }, new MapPoint(200, 0));
                var loopGraph = lb.Build();
                var walkerType = typeof(Booklet).GetNestedType("Walker", BindingFlags.NonPublic);
                var goalType = typeof(Booklet).GetNestedType("Goal", BindingFlags.NonPublic);
                var walker = Activator.CreateInstance(walkerType);
                walkerType.GetField("Graph").SetValue(walker, loopGraph);
                walkerType.GetField("OnPage").SetValue(walker, new HashSet<int> { jStart, jEnd });
                walkerType.GetField("Goal").SetValue(walker, Enum.Parse(goalType, "Switch"));
                walkerType.GetField("Junction").SetValue(walker, jEnd);
                walkerType.GetField("Side").SetValue(walker, -2); // AnySide
                var walked = (bool)walkerType.GetMethod("Walk").Invoke(walker, new object[] { b0, TrackEnd.Start, 0.0 });
                var steps = (int)walkerType.GetField("Steps").GetValue(walker);
                check(walked && steps < 50 && jLoop >= 0 && jMerge >= 0,
                    "Booklet: iterative Walker skips short-track loops and finds valid exit without burning budget",
                    $"walked={walked}, steps={steps}");

                // Deep linear chain (5,000 tracks) completes iteratively without blowing the stack.
                var deepB = new TrackGraphBuilder();
                const int deepLen = 5000;
                int deepPrev = Add(deepB, 2);
                for (var i = 1; i < deepLen; i++)
                {
                    var next = Add(deepB, 2);
                    deepB.Join(deepPrev, TrackEnd.End, next, TrackEnd.Start);
                    deepPrev = next;
                }
                var deepWalker = Activator.CreateInstance(walkerType);
                walkerType.GetField("Graph").SetValue(deepWalker, deepB.Build());
                walkerType.GetField("OnPage").SetValue(deepWalker, new HashSet<int>());
                walkerType.GetField("Goal").SetValue(deepWalker, Enum.Parse(goalType, "DeadEnd"));
                var deepOk = (bool)walkerType.GetMethod("Walk").Invoke(deepWalker, new object[] { 0, TrackEnd.Start, 0.0 });
                var deepPath = (List<(int Track, TrackEnd Entered)>)walkerType.GetField("Path").GetValue(deepWalker);
                check(deepOk && deepPath.Count == deepLen,
                    "Booklet: iterative Walker handles deep chains without stack overflow",
                    $"ok={deepOk}, count={deepPath.Count}");

                // Oversized numeric badge in Booklet.Build skips cleanly without OverflowException.
                var badgeGraphB = new TrackGraphBuilder();
                Add(badgeGraphB, 100);
                var badgeTiles = new[]
                {
                    new SchematicTile("SM-TracksEnd", 0, 0, 180, 50, 50),
                    new SchematicTile("SM-TracksStraight", 0, 100, 0, 50, 50),
                    new SchematicTile("SM-TracksEnd", 0, 200, 0, 50, 50),
                };
                var badgeList = new[]
                {
                    new SchematicBadge(0, 50, "4", "S", "#ffffff"),
                    new SchematicBadge(0, 150, "999999999999999999", "S", "#ffffff"),
                };
                var badgeSrc = new SchematicSource("[SM-B]", "SM", "Steel Mill", "B", "YARD", "#ffffff", false, badgeTiles, badgeList, Array.Empty<SchematicSwitch>());
                var builtBooklet = Booklet.Build(new[] { badgeSrc }, badgeGraphB.Build(), _ => "[Y]_[SM]_[B-04-S]");
                check(builtBooklet.Stats.Matched == 1 && builtBooklet.Stats.BadgesChecked == 1 && builtBooklet.Stats.BadgesAgree == 1,
                    "Booklet: oversized numeric badge skips cleanly instead of throwing OverflowException",
                    builtBooklet.Stats.ToString());
            }

            // 5. Cooperative cancellation for Router and RouteSearch.
            {
                var b = new TrackGraphBuilder();
                const int chain = 200;
                int prev = Add(b, 50);
                for (var i = 1; i < chain; i++)
                {
                    var next = Add(b, 50);
                    b.Join(prev, TrackEnd.End, next, TrackEnd.Start);
                    prev = next;
                }
                var g = b.Build();

                // Already-cancelled token returns Cancelled immediately.
                var pre = new CancellationTokenSource();
                pre.Cancel();
                var rPre = new Router(g).Find(new RouteRequest
                {
                    EndA = new TrackPosition(0, 30),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(0, 10),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 20,
                    Goal = new TrackPosition(chain - 1, 25),
                }, out var failPre, pre.Token);
                check(rPre == null && failPre == RouteFailure.Cancelled,
                    "Cancellation: pre-cancelled token aborts Router.Find immediately");

                // Mid-search cancellation stops expansion promptly.
                var midCts = new CancellationTokenSource();
                var penaltiesCalled = 0;
                var rMid = new Router(g).Find(new RouteRequest
                {
                    EndA = new TrackPosition(0, 30),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(0, 10),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 20,
                    Goal = new TrackPosition(chain - 1, 25),
                    TrackPenalty = _ =>
                    {
                        if (++penaltiesCalled >= 5)
                            midCts.Cancel();
                        return 0;
                    },
                }, out var failMid, midCts.Token);
                check(rMid == null && failMid == RouteFailure.Cancelled && penaltiesCalled < 20,
                    "Cancellation: Router.Find stops promptly when cancelled mid-search",
                    $"fail={failMid}, calls={penaltiesCalled}");

                // RouteSearch.Cancel prevents the fallback pass from running.
                var entered = new ManualResetEventSlim(false);
                var release = new ManualResetEventSlim(false);
                var rs = new RouteSearch(g, new RouteRequest
                {
                    EndA = new TrackPosition(0, 30),
                    OutwardA = TrackDirection.Forward,
                    EndB = new TrackPosition(0, 10),
                    OutwardB = TrackDirection.Backward,
                    TrainLength = 20,
                    Goal = new TrackPosition(chain - 1, 25),
                    Blocked = (_, __, ___) =>
                    {
                        entered.Set();
                        release.Wait(2000);
                        return true;
                    },
                });
                entered.Wait(2000);
                rs.Cancel();
                release.Set();
                var cancelledRoute = Wait(rs);
                check(rs.Done && rs.Cancelled && cancelledRoute == null && rs.Failure == RouteFailure.Cancelled && !rs.ThroughCars,
                    "Cancellation: RouteSearch.Cancel stops search cleanly without running fallback pass",
                    $"done={rs.Done}, cancelled={rs.Cancelled}, fail={rs.Failure}, through={rs.ThroughCars}");
            }
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
