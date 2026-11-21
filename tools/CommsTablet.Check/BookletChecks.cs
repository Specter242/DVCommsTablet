using System;
using System.Collections.Generic;
using System.Linq;
using CommsTablet.Core.Booklet;
using CommsTablet.Core.Routing;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Check
{
    /// <summary>
    /// Engine-free checks for Core/Booklet (YardTrackName, PageBox, PageRef,
    /// PageView, Schematic, Booklet) and graph/route switch and consist helpers,
    /// on small synthetic fixtures.
    /// </summary>
    internal static class BookletChecks
    {
        private static Action<bool, string, string> report;

        private static void check(bool ok, string what, string detail = null) => report(ok, what, detail);

        private static bool Near(double a, double b, double within = 1e-6) => Math.Abs(a - b) <= within;

        internal static void Run(Action<bool, string, string> reportTo)
        {
            report = reportTo;
            YardTrackNameChecks();
            PageGeometryChecks();
            SchematicChecks();
            BookletMatchChecks();
            JunctionAndRoutingChecks();
        }

        private static void YardTrackNameChecks()
        {
            var valid = new (string Raw, string Station, string Yard, int Number, string Kind, string Display, string Short)[]
            {
                ("[Y]_[SM]_[A-04-S]", "SM", "A", 4, "S", "SM-A4S", "A4S"),
                ("[Y]_[HB]_[B-12-L]", "HB", "B", 12, "L", "HB-B12L", "B12L"),
                ("[Y]_[MF]_[C-01-I]", "MF", "C", 1, "I", "MF-C1I", "C1I"),
                ("[Y]_[GF]_[D-03-O]", "GF", "D", 3, "O", "GF-D3O", "D3O"),
                ("[Y]_[CW]_[E-02-LP]", "CW", "E", 2, "LP", "CW-E2LP", "E2LP"),
                ("[Y]_[OWN]_[F-07-SP]", "OWN", "F", 7, "SP", "OWN-F7SP", "F7SP"),
                ("[Y]_[FF]_[G-05-T]", "FF", "G", 5, "T", "FF-G5T", "G5T"),
                ("[Y]_[SM]_[A-0-S]", "SM", "A", 0, "S", "SM-A0S", "A0S"),
                ("[Y]_[SM]_[A-00-S]", "SM", "A", 0, "S", "SM-A0S", "A0S"),
                ("[Y]_[SM]_[A-1-S]", "SM", "A", 1, "S", "SM-A1S", "A1S"),
                ("[Y]_[SM]_[A-99-S]", "SM", "A", 99, "S", "SM-A99S", "A99S"),
                ("[Y]_[SM]_[A-2147483647-S]", "SM", "A", int.MaxValue, "S", "SM-A2147483647S", "A2147483647S"),
            };
            var badValid = new List<string>();
            foreach (var v in valid)
            {
                if (!YardTrackName.TryParse(v.Raw, out var p)
                    || p.Station != v.Station || p.Yard != v.Yard || p.Number != v.Number || p.Kind != v.Kind
                    || p.Display != v.Display || p.Short != v.Short || p.ToString() != v.Display)
                {
                    badValid.Add(v.Raw);
                }
            }
            check(badValid.Count == 0, "Booklet: YardTrackName.TryParse parses yard, storage, loading, transfer, and boundary numbers", string.Join(", ", badValid));

            var invalid = new[]
            {
                null,
                "",
                "   ",
                "[M]_[SM]_[A-04-S]",
                "Y_SM_A-04-S",
                "[Y]_[SM]_[A-04-S]extra",
                "prefix[Y]_[SM]_[A-04-S]",
                "[Y]_[SM]_[A-04-S] ",
                "[Y]_[SM]_[A-04]",
                "[Y]_[SM]_[A--S]",
                "[Y]_[SM]_[A--1-S]",
                "[Y]_[SM]_[A-abc-S]",
                "[Y]_[SM]_[A-4.5-S]",
                "[Y]_[]_[A-04-S]",
                "[Y]_[SM]_[-04-S]",
                "[Y]_[SM]_[A-04-]",
            };
            var acceptedInvalid = new List<string>();
            foreach (var raw in invalid)
            {
                if (YardTrackName.TryParse(raw, out var p) || p.Station != null || p.Yard != null || p.Number != 0 || p.Kind != null)
                    acceptedInvalid.Add(raw ?? "<null>");
            }
            check(acceptedInvalid.Count == 0, "Booklet: YardTrackName.TryParse rejects malformed, negative, and non-numeric names", string.Join(", ", acceptedInvalid));
        }

        private static void PageGeometryChecks()
        {
            var b1 = new PageBox(10, 20, 110, 70);
            check(Near(b1.Width, 100) && Near(b1.Height, 50)
                  && b1.Contains(10, 20) && b1.Contains(110, 70) && b1.Contains(60, 45)
                  && !b1.Contains(9.99, 45) && !b1.Contains(60, 70.01),
                "Booklet: PageBox width, height, and containment");
            var u = b1.Union(new PageBox(-10, 30, 80, 120));
            check(Near(u.Left, -10) && Near(u.Top, 20) && Near(u.Right, 110) && Near(u.Bottom, 120),
                "Booklet: PageBox.Union encloses both boxes");

            var route = PageRef.Route;
            var over = PageRef.Overview("SM");
            var yard = PageRef.OfYard("SM", "B");
            check(route.Kind == PageKind.Route && route.Up.Equals(route) && route.ToString() == "route map",
                "Booklet: PageRef.Route stays at route on Up");
            check(over.Kind == PageKind.Station && over.Station == "SM" && over.Yard == null && over.Up.Equals(route) && over.ToString() == "SM overview",
                "Booklet: PageRef.Overview moves up to Route");
            check(yard.Kind == PageKind.Yard && yard.Station == "SM" && yard.Yard == "B" && yard.Up.Equals(over) && yard.Up.Up.Equals(route) && yard.ToString() == "SM B",
                "Booklet: PageRef.OfYard moves up to station overview then route");
            check(yard.Equals(PageRef.OfYard("SM", "B")) && yard.GetHashCode() == PageRef.OfYard("SM", "B").GetHashCode()
                  && !yard.Equals(PageRef.OfYard("SM", "C")) && !yard.Equals(over),
                "Booklet: PageRef value equality and hash code");

            var box = new PageBox(0, 0, 200, 100);
            var area = new PageBox(0, 0, 400, 300);
            var fit = PageView.Fit(box, area, 400, 300, right: 0, left: 0, cap: 4);
            check(Near(fit.Scale, 2.0) && Near(fit.CenterX, 100) && Near(fit.CenterY, 50),
                "Booklet: PageView.Fit centers content at the largest scale that fits", fit.ToString());
            var sMid = fit.ToScreen(100, 50, 200, 150);
            var sTopLeft = fit.ToScreen(0, 0, 200, 150);
            var back = fit.ToPage(sTopLeft.X + 75, sTopLeft.Y + 125, 200, 150);
            check(Near(sMid.X, 200) && Near(sMid.Y, 150) && Near(sTopLeft.X, 0) && Near(sTopLeft.Y, 50)
                  && Near(back.X, 37.5) && Near(back.Y, 62.5),
                "Booklet: PageView ToScreen and ToPage round-trip");

            var capped = PageView.Fit(box, area, 400, 300, right: 0, left: 0, cap: 1.5);
            var margined = PageView.Fit(box, area, 400, 300, right: 150, left: 50, cap: 4);
            check(Near(capped.Scale, 1.5) && Near(margined.Scale, 1.0),
                "Booklet: PageView.Fit respects scale cap and side margins");

            var wide = new PageBox(0, 0, 2000, 100);
            var floored = PageView.Fit(wide, area, 400, 300, right: 0, left: 10, cap: 4, floor: 0.5);
            var wideLeft = floored.ToScreen(0, 50, 200, 150);
            check(Near(floored.Scale, 0.5) && Near(wideLeft.X, 18) && Near(wideLeft.Y, 150),
                "Booklet: PageView.Fit honors floor scale and aligns a wide page from its left end", $"{floored} -> ({wideLeft.X:F1}, {wideLeft.Y:F1})");

            var zoomed = fit.Zoomed(2.0);
            var moved = fit.Moved(40, -20);
            check(Near(zoomed.Scale, 4.0) && Near(zoomed.CenterX, fit.CenterX)
                  && Near(moved.CenterX, fit.CenterX + 20) && Near(moved.CenterY, fit.CenterY - 10),
                "Booklet: PageView Zoomed and Moved scale and pan in page units");

            var tooSmall = new PageView(-500, 900, 0.25).ClampTo(fit, box);
            var tooBig = new PageView(500, -900, 99).ClampTo(fit, box);
            check(Near(tooSmall.Scale, fit.Scale) && Near(tooSmall.CenterX, 0) && Near(tooSmall.CenterY, 100)
                  && Near(tooBig.Scale, PageView.MaxScale) && Near(tooBig.CenterX, 200) && Near(tooBig.CenterY, 0),
                "Booklet: PageView.ClampTo keeps scale in [fit, MaxScale] and center on content");
        }

        private static void SchematicChecks()
        {
            var trackSprites = new[]
            {
                "SM-TracksStraight", "SM-TracksDiagonal", "SM-TracksCrossover",
                "SM-TracksSwitchDivergingL", "SM-TracksSwitchDivergingR", "SM-TracksSwitchSpread",
                "SM-TracksEnd", "SM-TracksExit",
                "SM-TracksYardFreightLoop", "SM-TracksYardPassengersEnd", "SM-TracksYardMilitaryLoop",
            };
            var nonTrackSprites = new[] { "SM-Compass", "SM-Switch", "SM-Water", "Other", "", null };
            check(trackSprites.All(Schematic.IsTrack) && nonTrackSprites.All(s => !Schematic.IsTrack(s)),
                "Booklet: Schematic.IsTrack recognizes track and capsule sprites and rejects decorations");

            var emptySrc = new SchematicSource("empty", null, null, null, null, null, false,
                Array.Empty<SchematicTile>(), Array.Empty<SchematicBadge>(), Array.Empty<SchematicSwitch>(), cell: 0);
            var empty = Schematic.Trace(emptySrc);
            check(Near(emptySrc.Cell, 50) && empty.Switches.Count == 0 && empty.Runs.Count == 0
                  && Near(empty.Box.Width, 0) && Near(empty.Box.Height, 0),
                "Booklet: empty SchematicSource defaults cell to 50 and traces to empty box");

            // Switch with duplicate tile, exit on stem, badge-bridged through track to buffer, badge-terminated diverging track.
            var tiles = new[]
            {
                new SchematicTile("SM-TracksExit", 0, 50, 180, 50, 50),
                new SchematicTile("SM-TracksSwitchDivergingL", 0, 100, 0, 50, 50),
                new SchematicTile("SM-TracksSwitchDivergingL", 0, 100, 360, 50, 50), // duplicate ignored
                new SchematicTile("SM-TracksStraight", 0, 150, 0, 50, 50),
                // (0, 200) holds badge "4S" bridging (0, 175) to (0, 225)
                new SchematicTile("SM-TracksStraight", 0, 250, 0, 50, 50),
                new SchematicTile("SM-TracksEnd", 0, 300, 0, 50, 50),
                new SchematicTile("SM-TracksStraight", -50, 100, 90, 50, 50),
                // (-100, 100) holds dead-end badge "9L"
                new SchematicTile("SM-Compass", 200, 200, 0, 50, 50), // decoration ignored
            };
            var badges = new[]
            {
                new SchematicBadge(0, 200, "4", "S", "#FFFFFFFF"),
                new SchematicBadge(-100, 100, "9", "L", "#FFFFFFFF"),
            };
            var touch = new[]
            {
                new SchematicSwitch(2, 103, 7),
            };
            var traced = Schematic.Trace(new SchematicSource("[SM-B]", "SM", "Steel Mill", "B", "YARD", "#7C8394FF", false, tiles, badges, touch));
            check(traced.Switches.Count == 1 && traced.Switches[0].Junction == 7 && !traced.Switches[0].Spread && traced.Switches[0].DivergesLeft
                  && Near(traced.Switches[0].Point.X, 0) && Near(traced.Switches[0].Point.Z, 85),
                "Booklet: Schematic.Trace dedupes switches, matches touch cells, and places switch dot at 0.6 toward stem");
            check(traced.Runs.Count == 3, "Booklet: Schematic.Trace walks all three legs of a switch", traced.Runs.Count.ToString());
            var stemRun = traced.Runs.FirstOrDefault(r => r.Start.Kind == RunEndKind.Switch && r.Start.Leg == SwitchLeg.Stem
                                                          || r.End.Kind == RunEndKind.Switch && r.End.Leg == SwitchLeg.Stem);
            var throughRun = traced.Runs.FirstOrDefault(r => r.Start.Kind == RunEndKind.Switch && r.Start.Leg == SwitchLeg.Through
                                                             || r.End.Kind == RunEndKind.Switch && r.End.Leg == SwitchLeg.Through);
            var divRun = traced.Runs.FirstOrDefault(r => r.Start.Kind == RunEndKind.Switch && r.Start.Leg == SwitchLeg.Diverging
                                                         || r.End.Kind == RunEndKind.Switch && r.End.Leg == SwitchLeg.Diverging);
            check(stemRun != null && (stemRun.Start.Kind == RunEndKind.Exit || stemRun.End.Kind == RunEndKind.Exit) && Near(stemRun.Length, 60),
                "Booklet: Schematic.Trace connects switch stem to exit", stemRun?.ToString());
            check(throughRun != null && (throughRun.Start.Kind == RunEndKind.Buffer || throughRun.End.Kind == RunEndKind.Buffer)
                  && throughRun.Badges.Any(b => b.Text == "4" && b.Kind == "S") && Near(throughRun.Length, 215),
                "Booklet: Schematic.Trace bridges a mid-track badge to the buffer stop", throughRun?.ToString());
            check(divRun != null && (divRun.Start.Kind == RunEndKind.Badge || divRun.End.Kind == RunEndKind.Badge)
                  && divRun.Badges.Any(b => b.Text == "9" && b.Kind == "L"),
                "Booklet: Schematic.Trace ends a spur at a dangling badge", divRun?.ToString());
            check(Near(traced.Box.Left, -100) && Near(traced.Box.Right, 0) && Near(traced.Box.Top, 25) && Near(traced.Box.Bottom, 300),
                "Booklet: Schematic.Box encloses all traced runs", traced.Box.ToString());

            // Mirrored diverging switch, spread switch (plain + mirrored), and far-away touch cell (> 10 units).
            var switchTiles = new[]
            {
                new SchematicTile("SM-TracksSwitchDivergingL", 0, 0, 0, -50, 50),
                new SchematicTile("SM-TracksSwitchSpread", 200, 0, 0, 50, 50),
                new SchematicTile("SM-TracksSwitchSpread", 400, 0, 0, -50, 50),
            };
            var switchTouch = new[]
            {
                new SchematicSwitch(0, 0, 1),
                new SchematicSwitch(200, 15, 2), // too far (> 10)
            };
            var swTraced = Schematic.Trace(new SchematicSource("sw", null, null, null, null, null, false, switchTiles, Array.Empty<SchematicBadge>(), switchTouch));
            check(swTraced.Switches.Count == 3
                  && !swTraced.Switches[0].DivergesLeft && swTraced.Switches[0].Junction == 1
                  && swTraced.Switches[1].Spread && swTraced.Switches[1].Junction == -1
                  && swTraced.Switches[1].LegDirections[(int)SwitchLeg.Left].X < 0 && swTraced.Switches[1].LegDirections[(int)SwitchLeg.Right].X > 0
                  && swTraced.Switches[2].Spread
                  && swTraced.Switches[2].LegDirections[(int)SwitchLeg.Left].X < 0 && swTraced.Switches[2].LegDirections[(int)SwitchLeg.Right].X > 0,
                "Booklet: mirrored switches flip DivergesLeft, spread switches orient Left/Right, and distant touch cells do not bind");

            // Crossover keeps straight lines straight, plus diagonal geometry helpers (PointAt, Between, Nearest) on custom cell=40.
            var crossTiles = new[]
            {
                new SchematicTile("SM-TracksEnd", -80, 0, 90, 40, 40),
                new SchematicTile("SM-TracksStraight", -40, 0, 90, 40, 40),
                new SchematicTile("SM-TracksCrossover", 0, 0, 0, 40, 40),
                new SchematicTile("SM-TracksStraight", 40, 0, 90, 40, 40),
                new SchematicTile("SM-TracksEnd", 80, 0, 270, 40, 40),
                new SchematicTile("SM-TracksEnd", 0, -80, 180, 40, 40),
                new SchematicTile("SM-TracksStraight", 0, -40, 0, 40, 40),
                new SchematicTile("SM-TracksStraight", 0, 40, 0, 40, 40),
                new SchematicTile("SM-TracksEnd", 0, 80, 0, 40, 40),
            };
            var cross = Schematic.Trace(new SchematicSource("cross", null, null, null, null, null, false, crossTiles, Array.Empty<SchematicBadge>(), Array.Empty<SchematicSwitch>(), cell: 40));
            check(cross.Runs.Count == 2 && cross.Runs.All(r => Near(r.Length, 160) && r.Start.Kind == RunEndKind.Buffer && r.End.Kind == RunEndKind.Buffer),
                "Booklet: Schematic.Trace carries crossing tracks straight across a crossover at custom cell size",
                string.Join("; ", cross.Runs.Select(r => $"{r.Length:F0}")));

            // Bent run (straight + diagonal + buffer) to test PointAt, Between, and Nearest.
            var bentTiles = new[]
            {
                new SchematicTile("SM-TracksEnd", 0, 0, 180, 50, 50),
                new SchematicTile("SM-TracksDiagonal", 0, 50, 0, 50, 50), // S=(0,25) to E=(25,50)
                new SchematicTile("SM-TracksEnd", 50, 50, 270, 50, 50), // S=(25,50) to Point=(50,50)
            };
            var bent = Schematic.Trace(new SchematicSource("bent", null, null, null, null, null, false, bentTiles, Array.Empty<SchematicBadge>(), Array.Empty<SchematicSwitch>()));
            var bentRun = bent.Runs.Single();
            var expectedLen = 25 + Math.Sqrt(25 * 25 + 25 * 25) + 25;
            var (pStart, hStart) = bentRun.PointAt(0);
            var (pEnd, _) = bentRun.PointAt(bentRun.Length + 50);
            var fwd = bentRun.Between(10, bentRun.Length - 10);
            var rev = bentRun.Between(bentRun.Length - 10, 10);
            var (nearAlong, nearDist) = bentRun.Nearest(12.5, 37.5); // midpoint of diagonal (0,25)-(25,50)
            var (offAlong, offDist) = bentRun.Nearest(10, 10); // 10 units right of (0,10) on first vertical segment
            check(Near(bentRun.Length, expectedLen, 1e-4)
                  && Near(pStart.X, 0) && Near(pStart.Z, 0) && Near(hStart.X, 0) && Near(hStart.Z, 1)
                  && Near(pEnd.X, 50) && Near(pEnd.Z, 50),
                "Booklet: SchematicRun.PointAt interpolates and clamps along bent runs");
            check(fwd.Length >= 3 && fwd.Length == rev.Length
                  && Enumerable.Range(0, fwd.Length).All(i => fwd[i].DistanceTo(rev[rev.Length - 1 - i]) < 1e-6),
                "Booklet: SchematicRun.Between slices forward and reverses cleanly when b < a");
            check(Near(nearAlong, 25 + Math.Sqrt(12.5 * 12.5 * 2), 1e-4) && Near(nearDist, 0, 1e-4)
                  && Near(offAlong, 10, 1e-4) && Near(offDist, 10, 1e-4),
                "Booklet: SchematicRun.Nearest projects on-segment and off-segment points");
        }

        private static void BookletMatchChecks()
        {
            // Synthetic two-station graph:
            // SM:
            //   t0 (stem, 200m) -> j0 (named through/diverging):
            //       branch 0: t1 (through, 100m) -> j1 (loop entry, geometric classify):
            //           branch 0: t3 (250m, upper/left) -> j2
            //           branch 1: t4a (120m) -- t4b (130m) (lower/right) -> j2
            //       j2 stem -> t5 (exit, 400m)
            //       branch 1: t2 (diverging left, 100m) -- t2b ([Y]_[SM]_[B-01-S], 300m, dead end)
            //   t6: standalone [Y]_[SM]_[B-04-S] (180m) matched by badge
            //   t7: [Y]_[SM]_[A-01-I] (150m, overview only)
            //   t8: [M]_mainline (1000m, route map only)
            // HB:
            //   t9 (stem, 150m) -> j3 (spread): branch 0 t10 (left, 200m), branch 1 t11 (right, 200m)
            var b = new TrackGraphBuilder();
            int t0 = b.AddTrack(200, new MapPoint(0, 0), new MapPoint(200, 0));
            int t1 = b.AddTrack(100, new MapPoint(200, 0), new MapPoint(300, 0));
            int t2 = b.AddTrack(100, new MapPoint(200, 0), new MapPoint(280, 60));
            int t2b = b.AddTrack(300, new MapPoint(280, 60), new MapPoint(580, 60));
            int t3 = b.AddTrack(250, new MapPoint(300, 0), new MapPoint(550, 0));
            int t4a = b.AddTrack(120, new MapPoint(300, 0), new MapPoint(425, -50));
            int t4b = b.AddTrack(130, new MapPoint(425, -50), new MapPoint(550, 0));
            int t5 = b.AddTrack(400, new MapPoint(550, 0), new MapPoint(950, 0));
            int t6 = b.AddTrack(180, new MapPoint(0, 200), new MapPoint(180, 200));
            int t7 = b.AddTrack(150, new MapPoint(0, 300), new MapPoint(150, 300));
            int t8 = b.AddTrack(1000, new MapPoint(1000, 0), new MapPoint(2000, 0));
            int t9 = b.AddTrack(150, new MapPoint(0, -300), new MapPoint(150, -300));
            int t10 = b.AddTrack(200, new MapPoint(150, -300), new MapPoint(350, -240)); // left seen from west stem
            int t11 = b.AddTrack(200, new MapPoint(150, -300), new MapPoint(350, -360)); // right seen from west stem

            b.Join(t2, TrackEnd.End, t2b, TrackEnd.Start);
            b.Join(t4a, TrackEnd.End, t4b, TrackEnd.Start);
            int j0 = b.AddJunction(t0, TrackEnd.End, new[] { (t1, TrackEnd.Start), (t2, TrackEnd.Start) }, new MapPoint(200, 0));
            int j1 = b.AddJunction(t1, TrackEnd.End, new[] { (t3, TrackEnd.Start), (t4a, TrackEnd.Start) }, new MapPoint(300, 0));
            int j2 = b.AddJunction(t5, TrackEnd.Start, new[] { (t3, TrackEnd.End), (t4b, TrackEnd.End) }, new MapPoint(550, 0));
            int j3 = b.AddJunction(t9, TrackEnd.End, new[] { (t10, TrackEnd.Start), (t11, TrackEnd.Start) }, new MapPoint(150, -300));
            var graph = b.Build();

            var names = new Dictionary<int, string>
            {
                [t0] = "[M]_[SM]_main",
                [t1] = "[track through]",
                [t2] = "[track diverging]",
                [t2b] = "[Y]_[SM]_[B-01-S]",
                [t3] = "[Y]_[SM]_[B-02-S]",
                [t4a] = "[Y]_[SM]_[B-03-L]",
                [t4b] = "[M]_loop_tail",
                [t5] = "[M]_[SM]_east",
                [t6] = "[Y]_[SM]_[B-04-S]",
                [t7] = "[Y]_[SM]_[A-01-I]",
                [t8] = "[M]_mainline",
                [t9] = "[M]_[HB]_lead",
                [t10] = "[Y]_[HB]_[B-01-S]",
                [t11] = "[Y]_[HB]_[B-02-L]",
            };
            Func<int, string> nameOf = t => names.TryGetValue(t, out var n) ? n : "";

            // SM-A (overview): capsule for yard B + badge-named track A-01-I
            var smOverview = new SchematicSource("[SM-A]", "SM", "Steel Mill", "A", "OVERVIEW", "#7C8394FF", false,
                new[]
                {
                    new SchematicTile("SM-TracksExit", 0, 0, 90, 50, 50), // S=(25,0)
                    new SchematicTile("SM-TracksYardFreightLoop", 50, 0, 270, 50, 50), // S=(25,0), N=(75,0)
                    // (100,0) holds capsule badge "B"
                    new SchematicTile("SM-TracksYardFreightLoop", 150, 0, 270, 50, 50), // S=(125,0), N=(175,0)
                    new SchematicTile("SM-TracksExit", 200, 0, 270, 50, 50), // S=(175,0)
                    // Standalone overview track A-01-I at y=100
                    new SchematicTile("SM-TracksEnd", 0, 100, 90, 50, 50),
                    // (50,100) holds badge "1I"
                    new SchematicTile("SM-TracksEnd", 100, 100, 270, 50, 50),
                },
                new[]
                {
                    new SchematicBadge(100, 0, "B", null, "#7C8394FF"),
                    new SchematicBadge(50, 100, "1", "I", "#7C8394FF"),
                },
                Array.Empty<SchematicSwitch>());

            // SM-B (yard): j0 (left diverging spur to buffer with badge 1S), j1 & j2 (tied loop with badges 2S and 3L), plus standalone 4S.
            var smYardB = new SchematicSource("[SM-B]", "SM", "Steel Mill", "B", "YARD B", "#7C8394FF", false,
                new[]
                {
                    // Stem into j0 from west: Exit at (0,0) rot 90 -> S=(25,0)
                    new SchematicTile("SM-TracksExit", 0, 0, 90, 50, 50),
                    // j0 at (50,0) rot 270: S=(25,0) West, N=(75,0) East, W=(50,25) North (DivergingL)
                    new SchematicTile("SM-TracksSwitchDivergingL", 50, 0, 270, 50, 50),
                    // Diverging north from j0 to buffer with badge 1S: (50,50) straight, (50,100) badge 1S, (50,150) straight, (50,200) buffer
                    new SchematicTile("SM-TracksStraight", 50, 50, 0, 50, 50),
                    new SchematicTile("SM-TracksStraight", 50, 150, 0, 50, 50),
                    new SchematicTile("SM-TracksEnd", 50, 200, 0, 50, 50),
                    // Through east from j0 to j1: j1 at (100,0) rot 270 (DivergingR: S=(75,0) West, N=(125,0) East, E=(100,-25) South)
                    new SchematicTile("SM-TracksSwitchDivergingR", 100, 0, 270, 50, 50),
                    // Upper/through loop leg (t3) from (125,0) to (225,0): straight at (150,0) rot 270, badge 2S at (200,0)
                    new SchematicTile("SM-TracksStraight", 150, 0, 270, 50, 50),
                    // Lower/diverging loop leg (t4a->t4b) from j1 E=(100,-25) to j2 W=(250,-25):
                    // diagonal at (100,-50) rot 90 connects (100,-25) and (125,-50)
                    new SchematicTile("SM-TracksDiagonal", 100, -50, 90, 50, 50),
                    new SchematicTile("SM-TracksStraight", 150, -50, 270, 50, 50), // (125,-50) to (175,-50); badge 3L bridges (175,-50) to (225,-50)
                    // diagonal at (250,-50) rot 180 connects (225,-50) and (250,-25)
                    new SchematicTile("SM-TracksDiagonal", 250, -50, 180, 50, 50),
                    // j2 at (250,0) rot 90 (DivergingL: S=(275,0) East, N=(225,0) West, W=(250,-25) South)
                    new SchematicTile("SM-TracksSwitchDivergingL", 250, 0, 90, 50, 50),
                    // Exit from j2 stem east: Exit at (300,0) rot 270 -> S=(275,0)
                    new SchematicTile("SM-TracksExit", 300, 0, 270, 50, 50),
                    // Standalone track 4S at y=300: Buffer(0,300) rot 90 -> badge 4S at (50,300) -> Buffer(100,300) rot 270
                    new SchematicTile("SM-TracksEnd", 0, 300, 90, 50, 50),
                    new SchematicTile("SM-TracksEnd", 100, 300, 270, 50, 50),
                },
                new[]
                {
                    new SchematicBadge(50, 100, "1", "S", "#7C8394FF"),
                    new SchematicBadge(200, 0, "2", "S", "#7C8394FF"),
                    new SchematicBadge(200, -50, "3", "L", "#7C8394FF"),
                    new SchematicBadge(50, 300, "4", "S", "#7C8394FF"),
                },
                new[]
                {
                    new SchematicSwitch(50, 0, j0),
                    new SchematicSwitch(100, 0, j1),
                    new SchematicSwitch(250, 0, j2),
                });

            // HB-A (overview) and HB-B (spread switch j3)
            var hbOverview = new SchematicSource("[HB-A]", "HB", "Harbor", "A", "OVERVIEW", "#3B6EA5FF", false,
                Array.Empty<SchematicTile>(), Array.Empty<SchematicBadge>(), Array.Empty<SchematicSwitch>());
            var hbYardB = new SchematicSource("[HB-B]", "HB", "Harbor", "B", "YARD B", "#3B6EA5FF", false,
                new[]
                {
                    new SchematicTile("SM-TracksExit", 0, 0, 90, 50, 50), // S=(25,0)
                    // j3 spread at (50,0) rot 270: S=(25,0) West, W=(50,25) North (Left), E=(50,-25) South (Right)
                    new SchematicTile("SM-TracksSwitchSpread", 50, 0, 270, 50, 50),
                    new SchematicTile("SM-TracksStraight", 50, 50, 0, 50, 50),
                    new SchematicTile("SM-TracksEnd", 50, 150, 0, 50, 50), // badge 1S at (50,100)
                    new SchematicTile("SM-TracksStraight", 50, -50, 0, 50, 50),
                    new SchematicTile("SM-TracksEnd", 50, -150, 180, 50, 50), // badge 2L at (50,-100)
                },
                new[]
                {
                    new SchematicBadge(50, 100, "1", "S", "#3B6EA5FF"),
                    new SchematicBadge(50, -100, "2", "L", "#3B6EA5FF"),
                },
                new[] { new SchematicSwitch(50, 0, j3) });

            // Route map showing j0 (so t0/t1/t2/t2b appear on Route too)
            var routeSrc = new SchematicSource("[ROUTE]", null, null, null, "ROUTE", null, true,
                new[]
                {
                    new SchematicTile("SM-TracksExit", 0, 0, 90, 50, 50),
                    new SchematicTile("SM-TracksSwitchDivergingL", 50, 0, 270, 50, 50),
                    new SchematicTile("SM-TracksExit", 100, 0, 270, 50, 50),
                    new SchematicTile("SM-TracksEnd", 50, 50, 0, 50, 50),
                },
                Array.Empty<SchematicBadge>(),
                new[] { new SchematicSwitch(50, 0, j0) });

            var booklet = Booklet.Build(new[] { smOverview, smYardB, hbOverview, hbYardB, routeSrc }, graph, nameOf);

            // Construction and indexing
            check(booklet.Route != null && booklet.Route.IsRoute && booklet.Stations.Count == 2
                  && booklet.Stations[0].Code == "SM" && booklet.Stations[0].Name == "Steel Mill" && booklet.Stations[0].Pages.Count == 2
                  && booklet.Stations[1].Code == "HB" && booklet.Stations[1].Pages.Count == 2
                  && booklet.Pages.Count() == 5,
                "Booklet: Build groups chapters in order and keeps the route map");
            var sm = booklet.Find("SM");
            var hb = booklet.Find("HB");
            check(sm != null && hb != null && booklet.Find("ZZ") == null
                  && booklet.Page(PageRef.Route) == booklet.Route
                  && booklet.Page(PageRef.Overview("SM")) == sm.Overview
                  && booklet.Page(PageRef.OfYard("SM", "B")) == sm.Find("B")
                  && booklet.Page(PageRef.OfYard("SM", "Z")) == null
                  && booklet.Page(PageRef.Overview("ZZ")) == null,
                "Booklet: Find and Page resolve Route, Overview, and Yard refs");
            check(booklet.TabSets(1) == 2 && booklet.TabSets(2) == 1
                  && booklet.TabSet(0, 1).Select(s => s.Code).SequenceEqual(new[] { "SM" })
                  && booklet.TabSet(1, 1).Select(s => s.Code).SequenceEqual(new[] { "HB" })
                  && booklet.SetOf("SM", 1) == 0 && booklet.SetOf("HB", 1) == 1 && booklet.SetOf("ZZ", 1) == 0,
                "Booklet: TabSets, TabSet, and SetOf page through stations");

            // PageFor priority: yard > overview > route
            var smB = sm.Find("B");
            var hbB = hb.Find("B");
            check(booklet.PageFor(new TrackPosition(t1, 50)) == smB
                  && booklet.PageFor(new TrackPosition(t7, 50)) == sm.Overview
                  && booklet.PageFor(new TrackPosition(t8, 50)) == booklet.Route,
                "Booklet: PageFor prefers yard over overview over route map",
                $"{booklet.PageFor(new TrackPosition(t1, 50))}, {booklet.PageFor(new TrackPosition(t7, 50))}, {booklet.PageFor(new TrackPosition(t8, 50))}");

            // Switch legs: settled (j0) and tie-broken loop (j1, j2) and spread (j3)
            var sw0Idx = Enumerable.Range(0, smB.Schematic.Switches.Count).First(i => smB.Schematic.Switches[i].Junction == j0);
            var sw1Idx = Enumerable.Range(0, smB.Schematic.Switches.Count).First(i => smB.Schematic.Switches[i].Junction == j1);
            var sw2Idx = Enumerable.Range(0, smB.Schematic.Switches.Count).First(i => smB.Schematic.Switches[i].Junction == j2);
            check(smB.LegOf(sw0Idx, 0) == SwitchLeg.Through && smB.LegOf(sw0Idx, 1) == SwitchLeg.Diverging
                  && smB.LegOf(sw1Idx, 0) == SwitchLeg.Through && smB.LegOf(sw1Idx, 1) == SwitchLeg.Diverging
                  && smB.LegOf(sw2Idx, 0) == SwitchLeg.Through && smB.LegOf(sw2Idx, 1) == SwitchLeg.Diverging
                  && smB.LegOf(sw0Idx, 99) == null,
                "Booklet: LegOf maps branches on both topology-settled and tie-broken loop switches");
            check(hbB.LegOf(0, 0) == SwitchLeg.Left && hbB.LegOf(0, 1) == SwitchLeg.Right,
                "Booklet: LegOf maps Left and Right on a spread switch");

            // BookletRun / RunTrack tracing, multi-track interpolation, and reversed tracks
            var spurRun = smB.Runs.FirstOrDefault(r => r.Path.Any(p => p.Track == t2b));
            check(spurRun != null && spurRun.Matched && !spurRun.IsCapsule
                  && spurRun.Path.Select(p => p.Track).SequenceEqual(new[] { t2, t2b })
                  && Near(spurRun.PathLength, 400),
                "Booklet: a switch-to-buffer run traces multi-track paths end to end", spurRun?.ToString());
            if (spurRun != null)
            {
                var along = spurRun.AlongOf(new TrackPosition(t2b, 150));
                var pos = along.HasValue ? spurRun.PositionAt(along.Value) : default;
                check(along.HasValue && Near(along.Value, 250.0 / 400.0 * spurRun.Drawn.Length, 1e-4)
                      && pos.Track == t2b && Near(pos.Offset, 150, 1e-4)
                      && spurRun.AlongOf(new TrackPosition(t8, 0)) == null
                      && Near(spurRun.AlongOf(new TrackPosition(t2, -10)).Value, 0)
                      && spurRun.PositionAt(-5).Equals(new TrackPosition(t2, 0))
                      && spurRun.PositionAt(99999).Equals(new TrackPosition(t2b, 300)),
                    "Booklet: BookletRun AlongOf and PositionAt round-trip and clamp");
            }
            var loopLower = smB.Runs.FirstOrDefault(r => r.Path.Any(p => p.Track == t4a));
            check(loopLower != null && loopLower.Path.Count == 2 && Near(loopLower.PathLength, 250),
                "Booklet: a loop run traces both joined tracks between switches", loopLower?.ToString());
            // Standalone badge-matched track
            check(smB.Shows(t6) && smB.Runs.Any(r => r.Path.Count == 1 && r.Path[0].Track == t6),
                "Booklet: an unswitched run matches its track by station/yard/number/kind badge");

            // Page coordinate flip (y down), PointOf, Stretch, TrackAt, SwitchAt, CapsuleAt
            check(Near(BookletPage.ToPage(new MapPoint(10, 25)).X, 10) && Near(BookletPage.ToPage(new MapPoint(10, 25)).Z, -25)
                  && Near(smB.Box.Top, -smB.Schematic.Box.Bottom) && Near(smB.Box.Bottom, -smB.Schematic.Box.Top),
                "Booklet: BookletPage flips schematic y down into page coordinates");
            var pt = smB.PointOf(new TrackPosition(t2b, 150));
            var stretches = smB.Stretch(t2b, 50, 250).ToList();
            check(pt.HasValue && Near(Math.Sqrt(pt.Value.Heading.X * pt.Value.Heading.X + pt.Value.Heading.Z * pt.Value.Heading.Z), 1, 1e-4)
                  && smB.PointOf(new TrackPosition(t8, 0)) == null
                  && stretches.Count == 1 && stretches[0].From < stretches[0].To,
                "Booklet: PointOf and Stretch project track positions and spans onto the page");
            if (pt.HasValue)
            {
                var hit = smB.TrackAt(pt.Value.At.X, pt.Value.At.Z, 5);
                check(hit.HasValue && hit.Value.Track == t2b && Near(hit.Value.Offset, 150, 1e-2)
                      && smB.TrackAt(9999, 9999, 5) == null,
                    "Booklet: TrackAt resolves a page point back to its track position");
            }
            var sw0 = smB.Schematic.Switches[sw0Idx];
            check(smB.SwitchAt(sw0.X, -sw0.Y, 5) == sw0 && smB.SwitchAt(9999, 9999, 5) == null,
                "Booklet: SwitchAt finds the switch within radius in y-down page coordinates");
            check(sm.Overview.CapsuleAt(100, 0, 5) == "B" && sm.Overview.CapsuleAt(9999, 9999, 5) == null,
                "Booklet: CapsuleAt finds a yard capsule on the station overview");

            // Stats on clean booklet + mismatch problem reporting on a mismatched badge fixture
            check(booklet.Stats.Matched > 0 && booklet.Stats.Capsules == 1
                  && booklet.Stats.SwitchesNamed == booklet.Stats.Switches
                  && booklet.Stats.LegsSettled >= 1 && booklet.Stats.LegsTied >= 1
                  && booklet.Stats.BadgesChecked >= 4 && booklet.Stats.BadgesAgree == booklet.Stats.BadgesChecked
                  && booklet.Stats.Problems.Count == 0,
                "Booklet: MatchStats tracks matched runs, capsules, switches, legs, and agreeing badges", booklet.Stats.ToString());

            var badBadgeSrc = new SchematicSource("[SM-B]", "SM", "Steel Mill", "B", "BAD", "#7C8394FF", false,
                new[]
                {
                    new SchematicTile("SM-TracksExit", 0, 0, 90, 50, 50),
                    new SchematicTile("SM-TracksSwitchDivergingL", 50, 0, 270, 50, 50),
                    new SchematicTile("SM-TracksExit", 100, 0, 270, 50, 50),
                    new SchematicTile("SM-TracksStraight", 50, 50, 0, 50, 50),
                    new SchematicTile("SM-TracksEnd", 50, 150, 0, 50, 50),
                },
                new[] { new SchematicBadge(50, 100, "99", "S", "#7C8394FF") },
                new[] { new SchematicSwitch(50, 0, j0) });
            var badBooklet = Booklet.Build(new[] { badBadgeSrc }, graph, nameOf);
            check(badBooklet.Stats.BadgesChecked == 1 && badBooklet.Stats.BadgesAgree == 0 && badBooklet.Stats.Problems.Count == 1,
                "Booklet: MatchStats records a problem when a track badge disagrees with the matched run", badBooklet.Stats.ToString());
        }

        private static void JunctionAndRoutingChecks()
        {
            var b = new TrackGraphBuilder();
            int stem = b.AddTrack(300, new MapPoint(0, 0), new MapPoint(300, 0));
            int main = b.AddTrack(400, new MapPoint(300, 0), new MapPoint(700, 0));
            int side = b.AddTrack(200, new MapPoint(300, 0), new MapPoint(480, 80));
            int tail = b.AddTrack(250, new MapPoint(700, 0), new MapPoint(950, 0));
            int j = b.AddJunction(stem, TrackEnd.End, new[] { (main, TrackEnd.Start), (side, TrackEnd.Start) }, new MapPoint(300, 0));
            b.Join(main, TrackEnd.End, tail, TrackEnd.Start);
            var g = b.Build();

            var info = g.Junction(j);
            check(info.StemTrack == stem && info.StemEnd == TrackEnd.End
                  && info.Branches.Length == 2 && info.Branches[0] == (main, TrackEnd.Start) && info.Branches[1] == (side, TrackEnd.Start)
                  && Near(info.Position.X, 300) && Near(info.Position.Z, 0),
                "Booklet: JunctionInfo exposes stem, branches in order, and position");
            check(g.TryFindLink(stem, TrackEnd.End, side, out var jLink) && jLink.NeedsJunction && jLink.Junction == j && jLink.Branch == 1
                  && g.TryFindLink(main, TrackEnd.End, tail, out var pLink) && !pLink.NeedsJunction && pLink.Junction == TrackLink.NoJunction
                  && !g.TryFindLink(stem, TrackEnd.End, tail, out _),
                "Booklet: TrackGraph.TryFindLink distinguishes junction links, plain joints, and absent links");

            // RouteLeg sorts SwitchSetting by AtDistance and supports TryLocate, PastEnd, PositionAt, and Route.Cost with ConsistEnd.
            var leg1 = new RouteLeg(
                new[] { new RoutePiece(stem, 0, 300), new RoutePiece(main, 0, 400), new RoutePiece(stem, 300, 100) },
                new[] { new SwitchSetting(2, 1, 700), new SwitchSetting(j, 0, 300) },
                ConsistEnd.A,
                endsInReversal: true,
                startOverlap: 50);
            check(leg1.Switches.Count == 2 && leg1.Switches[0].Junction == j && Near(leg1.Switches[0].AtDistance, 300)
                  && leg1.Switches[1].Junction == 2 && Near(leg1.Switches[1].AtDistance, 700),
                "Booklet: RouteLeg sorts SwitchSetting entries by distance");
            check(Near(leg1.Length, 900) && Near(leg1.StartOverlap, 50) && Near(leg1.Running, 850)
                  && leg1.PositionAt(350).Equals(new TrackPosition(main, 50))
                  && leg1.PositionAt(750).Equals(new TrackPosition(stem, 250)),
                "Booklet: RouteLeg computes Length, Running, and PositionAt across forward and backward pieces");
            check(leg1.TryLocate(new TrackPosition(stem, 200), hint: 0, tolerance: 1, out var d0, out var p0) && p0 == 0 && Near(d0, 200)
                  && leg1.TryLocate(new TrackPosition(stem, 200), hint: 2, tolerance: 1, out var d2, out var p2) && p2 == 2 && Near(d2, 800)
                  && !leg1.TryLocate(new TrackPosition(side, 50), hint: 0, tolerance: 1, out _, out _)
                  && Near(leg1.PastEnd(new TrackPosition(stem, 50)), 50)
                  && double.IsNaN(leg1.PastEnd(new TrackPosition(stem, 150))),
                "Booklet: RouteLeg TryLocate honors loop hint and PastEnd measures overshoot");

            var leg2 = new RouteLeg(new[] { new RoutePiece(side, 0, 200) }, Array.Empty<SwitchSetting>(), ConsistEnd.A.Other(), endsInReversal: false, startOverlap: 20);
            var route = new Route(new[] { leg1, leg2 });
            var req = new RouteRequest { ReversalPenalty = 500, ShovingEnd = ConsistEnd.B, ShovingCost = 0.5 };
            check(ConsistEnd.A.Other() == ConsistEnd.B && ConsistEnd.B.Other() == ConsistEnd.A
                  && route.Reversals == 1 && Near(route.Length, 1100) && Near(route.Running, 1030)
                  && Near(route.Cost(req), 1100 + 500 + 180 * 0.5),
                "Booklet: Route aggregates length, reversals, and shoving cost by ConsistEnd");
        }
    }
}
