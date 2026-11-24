using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommsTablet.Core.Booklet;
using CommsTablet.Core.Tracks;
using CommsTablet.Pages;
using CommsTablet.Tablet;
using UnityEngine;
using static CommsTablet.Tablet.TabletSkin;

namespace CommsTablet.Map
{
    /// <summary>
    /// The Switches app's map (decision 17): the game's station booklet, page
    /// by page (the route map, a station's overview, its yards), drawn by the
    /// game's own render (<see cref="BookletRenderer"/>) with each switch's
    /// setting over it. Station tabs down the left, nine at a time; down the
    /// right, zoom, Map, the station's plate (▲ up from a yard), its yards'
    /// letters, Here, and the arrows. Where the game's Map Blips setting shows
    /// locomotives on its world map, each loco on the page is drawn on its
    /// track with its number, as AI Engineer draws them. A press on a route map's station badge
    /// opens its overview, on an overview's capsule its yard; on a switch,
    /// with the Dispatcher license, it throws it, as the game's touch maps do;
    /// on a track, it picks that track, and the column's ROUTE sends Track Ahead
    /// there. Opened from Track Ahead's SET DESTINATION, it is for picking, and
    /// ROUTE or CANCEL goes back there. The page is drawn again when it, its view, a switch on it, or a loco
    /// on it changes, at most each refresh (twice a second) while it shows.
    ///
    /// AI Engineer's <c>TabletMap</c> (c6e6207,
    /// src/AIEngineer/UI/Tablet/TabletMap.cs): its state, pages, drawing,
    /// title, tabs, column, steps, zoom, presses on badges and capsules, and
    /// its locos and their numbers as they are; its picks, orders, other
    /// cars, the chosen loco's route and orders, and Undo left out;
    /// the chosen loco's place taken by the player's (Here), and a press on a
    /// switch added. As there, the state is the player's, shared by every
    /// tablet, and the map moves with the arrows and + and −, not by a drag.
    /// </summary>
    internal static class TabletMap
    {
        private const float MapX = 12, MapY = 84, MapW = 1056, MapH = 828;
        private const int Per = 9;

        /// <summary>Rows of yard letters the column keeps, two a row: the stock booklet's most is the Harbor's six (B to G).</summary>
        private const int LetterRows = 3;
        private const float PressRadius = 22;

        /// <summary>Where the column's ROUTE and CLEAR go, below its arrows (map pixels).</summary>
        private const float PickY = 650;

        /// <summary>The screen's status strip covers the map's bottom 60 pixels (canvas 852 on), as AI Engineer's last-order strip does.</summary>
        private const float StripTop = MapH - 60;

        /// <summary>How far apart two things to press must lie for a plate of their own in VR: 16 mm, AI Engineer's least pressable size.</summary>
        internal const float PlateSize = 57;

        // The part of the map a page is fitted into: clear of the tabs, the column, the title, and the strip.
        private static readonly PageBox FitArea = new PageBox(136, 60, MapW - 152, MapH - 84);

        private static PageRef at = PageRef.Route;
        private static bool placed;
        private static PageView view, fit;
        private static PageBox content;
        private static int tabSet;
        private static RenderTexture texture;
        private static int textureVersion;
        private static bool dirty = true;
        private static string settingsDrawn;
        private static float shownAt = -1000;

        /// <summary>The place pressed, waiting for ROUTE; null if none.</summary>
        private static AheadGoal picked;

        /// <summary>Opened from Track Ahead to pick its destination, and going back there after.</summary>
        private static bool forAhead;

        /// <summary>Set just before the radio turns to the map for Track Ahead, so that turn alone keeps <see cref="forAhead"/>.</summary>
        private static bool arming;

        /// <summary>Makes the map's next opening one for picking Track Ahead's destination.</summary>
        internal static void PickForAhead()
        {
            forAhead = true;
            arming = true;
            picked = null;
        }

        /// <summary>When the radio turns to the map: any other way there than Track Ahead's is the map as usual.</summary>
        internal static void Opened()
        {
            if (!arming)
                forAhead = false;
            arming = false;
        }

        private static Rect S(float x, float y, float w, float h) => new Rect(MapX + x, MapY + y, w, h);

        /// <summary>The booklet's copies put away while no tablet shows the map (from each screen's update).</summary>
        public static void PutAwayIfUnseen()
        {
            if (Time.unscaledTime - shownAt > 3f)
                BookletRenderer.Hide();
        }

        /// <summary>Lets the map's texture go (with the screen's); it is made again when the map is next drawn.</summary>
        public static void ReleaseTexture()
        {
            if (texture == null)
                return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
            dirty = true;
        }

        /// <summary>At unload: the map opens afresh in the next world.</summary>
        public static void Forget()
        {
            ReleaseTexture();
            at = PageRef.Route;
            placed = false;
            tabSet = 0;
            settingsDrawn = null;
            picked = null;
            forAhead = false;
            arming = false;
            PressArea = Rect.zero;
            locosUsed = 0;
            foreach (var c in locos)
                c.Car = null;
            TrainSpans.Forget();
        }

        /// <summary>
        /// Whether each switch, station badge, and yard capsule on view takes a
        /// plate of its own, as a hand presses it where it is drawn: in VR,
        /// where the screen's map button cannot tell where a hand pressed.
        /// </summary>
#if PUBLIC_RELEASE
        public static bool Plates => VRManager.IsVREnabled();
#else
        public static bool Plates => VRManager.IsVREnabled() || PlatesForTest;

        /// <summary>Plates as in VR, for the self-test on a desktop.</summary>
        internal static bool PlatesForTest;
#endif

#if !PUBLIC_RELEASE
        // ---- For the self-test ----

        /// <summary>What the map shows, for the self-test's log.</summary>
        internal static string Describe() => $"page {PageShown ?? "none"}, view {view}, fit {fit.Scale:F3}, tab set {tabSet}, drawn {Renders} times";

        /// <summary>The page shown, by the render's name ("[OWN-B]"); null before the booklet is read.</summary>
        internal static string PageShown => BookletReader.Read?.Page(at)?.Source.Name;

        /// <summary>The station the map shows, or null on the route map.</summary>
        internal static string StationShown => BookletReader.Read?.Page(at)?.Station;

        internal static int TabSetShown => tabSet;

        internal static AheadGoal PickedForTest => picked;

        internal static bool ForAheadForTest => forAhead;

        internal static readonly Vector2 RoutePlateForTest = new Vector2(MapX + MapW - 16 - 64, MapY + PickY + 26);

        /// <summary>CLEAR, or CANCEL when picking for Track Ahead: the same plate.</summary>
        internal static readonly Vector2 ClearPlateForTest = new Vector2(MapX + MapW - 16 - 64, MapY + PickY + 84);

        internal static PageView ViewShown => view;

        internal static PageView FitShown => fit;

        /// <summary>How many times a page was drawn into the map's texture.</summary>
        internal static int Renders { get; private set; }

        /// <summary>The map's texture as last drawn, for the self-test's pictures.</summary>
        internal static RenderTexture Texture => texture;

        internal static void OpenForTest(PageRef page)
        {
            var book = BookletReader.Read;
            if (book == null || book.Page(page) == null)
                return;
            placed = true;
            Open(book, page);
        }
#endif

        /// <summary>Where on the canvas a page point (y down) is drawn.</summary>
        internal static Vector2 CanvasPoint(double x, double y)
        {
            var (sx, sy) = view.ToScreen(x, y, MapW / 2, MapH / 2);
            return new Vector2(MapX + (float)sx, MapY + (float)sy);
        }

        // ---- Pages ----

        private static BookletPage PageOf(Booklet book) => book.Page(at) ?? book.Route ?? book.Stations.FirstOrDefault()?.Overview;

        private static PageRef RefOf(Booklet book, BookletPage page) =>
            page.IsRoute ? PageRef.Route : book.Find(page.Station)?.Overview == page ? PageRef.Overview(page.Station) : PageRef.OfYard(page.Station, page.Yard);

        /// <summary>The station the column's plates are for: the page's, or on the route map the player's (the nearest).</summary>
        private static BookletStation StationFor(Booklet book, BookletPage page)
        {
            if (page.Station != null)
                return book.Find(page.Station);
            return Nearest(book) ?? book.Stations.FirstOrDefault();
        }

        /// <summary>The station whose office is nearest the player; null outside the world.</summary>
        private static BookletStation Nearest(Booklet book)
        {
            var player = PlayerManager.PlayerTransform;
            if (player == null || StationController.allStations == null)
                return null;
            BookletStation best = null;
            var bestD = float.MaxValue;
            foreach (var st in StationController.allStations)
            {
                var info = st != null ? st.stationInfo : null;
                var found = info != null ? book.Find(info.YardID) : null;
                if (found == null)
                    continue;
                var d = (st.transform.position - player.position).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = found;
                }
            }
            return best;
        }

        /// <summary>The track the player is on or beside (within 10 m): their car's, else the nearest; null if none.</summary>
        private static int? PlayerTrack(TrackNetwork network)
        {
            var player = PlayerManager.PlayerTransform;
            if (player == null)
                return null;
            var car = PlayerManager.Car;
            var under = car != null && car.Bogies != null && car.Bogies.Length > 0 ? car.FrontBogie?.track : null;
            if (under != null && network.TryGetTrack(under, out var mine))
                return mine;
            // Only the tracks that could pass within reach: a track's every point lies within half its length of an end.
            var p = player.position - WorldMover.currentMove;
            var here = new MapPoint(p.x, p.z);
            var near = new List<RailTrack>();
            var graph = network.Graph;
            for (var t = 0; t < graph.TrackCount; t++)
            {
                var reach = graph.Length(t) / 2 + 20;
                if (graph.EndPoint(t, TrackEnd.Start).DistanceTo(here) <= reach || graph.EndPoint(t, TrackEnd.End).DistanceTo(here) <= reach)
                    near.Add(network.Tracks[t]);
            }
            if (near.Count == 0)
                return null;
            var (track, point) = RailTrack.GetClosest(player.position, 0f, near);
            if (track == null || !point.HasValue || ((Vector3)point.Value.position - p).magnitude > 10f)
                return null;
            return network.TryGetTrack(track, out var index) ? index : (int?)null;
        }

        private static void Open(Booklet book, PageRef page)
        {
            at = page;
            var p = PageOf(book);
            content = Padded(p.Box);
            fit = PageView.Fit(content, FitArea, MapW, MapH, 0, 0, 3);
            view = fit;
            var station = StationFor(book, p);
            if (station != null)
                tabSet = book.SetOf(station.Code, Per);
            dirty = true;
        }

        private static PageBox Padded(PageBox b) => new PageBox(b.Left - 30, b.Top - 30, b.Right + 30, b.Bottom + 30);

        /// <summary>The page the player stands on (its yard's if one draws their track), fitted; out on the line, the nearest station's overview.</summary>
        private static void ShowHere(Booklet book, TrackNetwork network)
        {
            var track = PlayerTrack(network);
            var page = track.HasValue ? book.PageFor(new TrackPosition(track.Value, 0)) : null;
            if (page != null && !page.IsRoute)
            {
                Open(book, RefOf(book, page));
                return;
            }
            var near = Nearest(book);
            Open(book, near != null ? PageRef.Overview(near.Code) : PageRef.Route);
        }

        // ---- Drawing the page into its texture ----

        /// <summary>Draws the page into the map's texture, if it, its view, or a switch's setting on it changed.</summary>
        private static void RenderIfNeeded(Booklet book, TrackNetwork network)
        {
            shownAt = Time.unscaledTime;
            if (!placed)
            {
                placed = true;
                ShowHere(book, network);
                if (PageOf(book) == null || content.Width <= 0)
                    Open(book, PageRef.Route);
            }
            int w = (int)MapW, h = (int)MapH;
            if (texture == null || !texture.IsCreated())
            {
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }
                texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "CommsTablet map" };
                texture.Create();
                textureVersion++;
                dirty = true;
            }
            var page = PageOf(book);
            if (page == null)
                return;
            // Drawn again only when it changes: a switch thrown by anyone, or a loco on it moved.
            ReadLocos(network, page);
            var settings = SettingsOf(page, network);
            if (!dirty && settings == settingsDrawn && locoHash == locoHashDrawn)
                return;
            var rendered = BookletRenderer.Render(page, view.CenterX, -view.CenterY, view.Scale, texture, px => Overlay(page, network, px));
            if (!rendered)
                return;
            dirty = false;
            settingsDrawn = settings;
            locoHashDrawn = locoHash;
            textureVersion++;
#if !PUBLIC_RELEASE
            Renders++;
#endif
        }

        private static readonly StringBuilder settingsText = new StringBuilder();

        /// <summary>The page, its view, and how each of its switches is set, as one string to compare.</summary>
        private static string SettingsOf(BookletPage page, TrackNetwork network)
        {
            settingsText.Clear().Append(page.Source.Name).Append(view.ToString()).Append('|');
            settingsText.Append(picked?.Track ?? -1).Append('|').Append(AheadRoute.Destination?.Track ?? -1).Append('|');
            foreach (var s in page.Schematic.Switches)
            {
                var j = s.Junction >= 0 && s.Junction < network.Junctions.Length ? network.Junctions[s.Junction] : null;
                settingsText.Append(j != null ? (char)('0' + j.selectedBranch) : '-');
            }
            return settingsText.ToString();
        }

        private static readonly Color SetColor = new Color(0.12f, 0.58f, 0.22f, 0.95f);

        /// <summary>The track picked on the map, and the one Track Ahead is routing to.</summary>
        private static readonly Color PickedColor = Hex("c98a1c"), DestinationColor = Hex("b5381f");

        /// <summary>What the tablet draws over the page: each switch's setting, a bar along the leg it is set to.</summary>
        private static void Overlay(BookletPage page, TrackNetwork network, Func<double, double, Vector2> px)
        {
            var switches = page.Schematic.Switches;
            for (var i = 0; i < switches.Count; i++)
            {
                var s = switches[i];
                if (s.Junction < 0 || s.Junction >= network.Junctions.Length || network.Junctions[s.Junction] == null)
                    continue;
                var leg = page.LegOf(i, network.Junctions[s.Junction].selectedBranch);
                if (leg == null)
                    continue;
                var d = s.LegDirections[(int)leg.Value];
                BookletRenderer.Line(px(s.Point.X, s.Point.Z), px(s.Point.X + d.X * 22, s.Point.Z + d.Z * 22), 6, SetColor);
            }
            Mark(page, network, AheadRoute.Destination, DestinationColor, px);
            if (picked != null && (AheadRoute.Destination == null || picked.Track != AheadRoute.Destination.Track))
                Mark(page, network, picked, PickedColor, px);
            // The locos: each a short bar along its track, a little gap at each end, smoked iron with an ink edge (the player's own in brass).
            // Where a long line is drawn short (the route map), a bar is drawn at least LocoMinPixels long about where it stands.
            var minimum = LocoMinPixels / Math.Max(1e-6, view.Scale);
            Color edge = AsDrawn(LocoEdge), fill = AsDrawn(LocoColor), own = AsDrawn(Brass);
            for (var i = 0; i < locosUsed; i++)
            {
                var c = locos[i];
                foreach (var (run, a, b) in c.Parts)
                {
                    var perMeter = run.PathLength > 0 ? run.Drawn.Length / run.PathLength : 0;
                    var gap = Math.Min(0.6 * perMeter, Math.Abs(b - a) * 0.2);
                    double lo = Math.Min(a, b) + gap, hi = Math.Max(a, b) - gap;
                    if (hi - lo < minimum)
                    {
                        var middle = (lo + hi) / 2;
                        lo = Math.Max(0, middle - minimum / 2);
                        hi = Math.Min(run.Drawn.Length, middle + minimum / 2);
                    }
                    Along(px, run, lo, hi, 18, edge);
                    Along(px, run, lo, hi, 10, c.Own ? own : fill);
                }
            }
        }

        // A goal's track, drawn over the page's own line.
        private static void Mark(BookletPage page, TrackNetwork network, AheadGoal goal, Color color, Func<double, double, Vector2> px)
        {
            if (goal == null)
                return;
            foreach (var (run, a, b) in page.Stretch(goal.Track, 0, network.Graph.Length(goal.Track)))
                Along(px, run, Math.Min(a, b), Math.Max(a, b), 12, AsDrawn(color));
        }

        private static void Along(Func<double, double, Vector2> px, BookletRun run, double a, double b, float width, Color color)
        {
            var line = run.Drawn.Between(a, b);
            for (var i = 1; i < line.Length; i++)
                BookletRenderer.Line(px(line[i - 1].X, line[i - 1].Z), px(line[i].X, line[i].Z), width, color);
        }

        // ---- Locos (AI Engineer's cars, c6e6207: TabletMap.ReadCars, Overlay, DrawMarks), as the game's map blips ----

 private static readonly Color LocoColor = Hex("3a332b");
        private static readonly Color LocoEdge = Hex("1d160c");

        /// <summary>
        /// A color as it is to look on the page: the game renders in linear
        /// color space, where the overlay's colors are taken as linear and
        /// show lighter than written (the smoked iron as a mid gray).
        /// </summary>
        private static Color AsDrawn(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        /// <summary>The least a loco's bar is drawn long, map pixels (its width): a 20 m loco on the route map's long lines is otherwise a tick.</summary>
        private const double LocoMinPixels = 18;

        /// <summary>A loco the page draws: where, as parts of its runs, and whether it is the player's own.</summary>
        private sealed class DrawnLoco
        {
            public TrainCar Car;
            public bool Own;
            public readonly List<(int Track, double From, double To)> Spans = new List<(int, double, double)>();
            public readonly List<(BookletRun Run, double From, double To)> Parts = new List<(BookletRun, double, double)>();
        }

        // Kept from pass to pass, so reading them twice a second makes no garbage of its own.
        private static readonly List<DrawnLoco> locos = new List<DrawnLoco>();
        private static int locosUsed;
        private static int locoHash, locoHashDrawn;
        private static readonly List<(int Track, double From, double To)> carSpans = new List<(int, double, double)>();

        /// <summary>
        /// Whether the map shows locos: as the game's world map item shows
        /// their blips, by the difficulty's Map Blips
        /// (<c>GameParams.LocoMarkersDisplayed</c>).
        /// </summary>
        internal static bool LocosAllowed => DV.Globals.G?.GameParams != null && DV.Globals.G.GameParams.LocoMarkersDisplayed;

        /// <summary>
        /// Every loco the page draws a track of, and where on the page it
        /// stands. As the world map: not a loco awaiting restoration, and the
        /// player's own train's only with the player's blip
        /// (<c>PlayerMarkerDisplayed</c>), in brass.
        /// </summary>
        private static void ReadLocos(TrackNetwork network, BookletPage page)
        {
            locosUsed = 0;
            locoHash = 17;
            var spawner = CarSpawner.Instance;
            if (!LocosAllowed || spawner == null || spawner.AllLocos == null)
                return;
            var own = PlayerManager.Car != null ? PlayerManager.Car.trainset : null;
            var player = DV.Globals.G.GameParams.PlayerMarkerDisplayed;
            foreach (var car in spawner.AllLocos)
            {
                if (car == null || car.preventFastTravelDestination)
                    continue;
                var mine = own != null && car.trainset == own;
                if (mine && !player)
                    continue;
                carSpans.Clear();
                TrainSpans.SpansOf(network, car, carSpans);
                DrawnLoco drawn = null;
                foreach (var (t, a, b) in carSpans)
                {
                    if (!page.Shows(t))
                        continue;
                    if (drawn == null)
                    {
                        if (locosUsed == locos.Count)
                            locos.Add(new DrawnLoco());
                        drawn = locos[locosUsed++];
                        drawn.Car = car;
                        drawn.Own = mine;
                        drawn.Spans.Clear();
                        drawn.Parts.Clear();
                        locoHash = locoHash * 31 + car.GetInstanceID() + (mine ? 1 : 0);
                    }
                    drawn.Spans.Add((t, a, b));
                    foreach (var part in page.Stretch(t, a, b))
                        drawn.Parts.Add(part);
                    // Half a meter's move draws it again.
                    locoHash = (locoHash * 31 + t) * 31 + (int)Math.Round(a * 2) * 7 + (int)Math.Round(b * 2);
                }
            }
            for (var i = locosUsed; i < locos.Count; i++)
                locos[i].Car = null;
        }

        /// <summary>
        /// The locos' numbers above them, on the screen's layer: one for each
        /// train on the page, "+1" for another loco in it, so coupled locos
        /// do not print over each other; on a small plate of the tablet's, a
        /// brass-edged one for the player's own, legible on the light map.
        /// </summary>
        private static void DrawMarks()
        {
            for (var i = 0; i < locosUsed; i++)
            {
                var c = locos[i];
                if (c.Parts.Count == 0 || c.Car == null)
                    continue;
                var others = 0;
                var first = true;
                for (var j = 0; j < locosUsed && first; j++)
                {
                    if (j == i || locos[j].Car == null || locos[j].Parts.Count == 0 || locos[j].Car.trainset == null || locos[j].Car.trainset != c.Car.trainset)
                        continue;
                    if (j < i)
                        first = false;
                    else
                        others++;
                }
                if (!first)
                    continue;
                var (run, a, b) = c.Parts[0];
                var (pt, _) = run.Drawn.PointAt((a + b) / 2);
                var (x, y) = view.ToScreen(pt.X, -pt.Z, MapW / 2, MapH / 2);
                if (x < 0 || y < 0 || x > MapW || y > MapH)
                    continue;
                var text = others > 0 ? $"{c.Car.ID} +{others}" : c.Car.ID;
                var width = 20 + 15 * text.Length;
                var r = S((float)x - width / 2f, (float)y - 50, width, 34);
                Box(r, c.Own ? PlateOn : Plate);
                Label(r, text, c.Own ? Centered(PlateOn) : Centered(Small));
            }
        }

#if !PUBLIC_RELEASE
        /// <summary>The locos drawn on the page shown, each with where its mark is (canvas pixels), for the self-test.</summary>
        internal static List<(TrainCar Car, Vector2 At, int Track, bool Own)> LocoMarks()
        {
            var list = new List<(TrainCar, Vector2, int, bool)>();
            for (var i = 0; i < locosUsed; i++)
            {
                var c = locos[i];
                if (c.Parts.Count == 0 || c.Car == null)
                    continue;
                var (run, a, b) = c.Parts[0];
                var (pt, _) = run.Drawn.PointAt((a + b) / 2);
                var track = run.PositionAt((a + b) / 2).Track;
                list.Add((c.Car, CanvasPoint(pt.X, -pt.Z), track, c.Own));
            }
            return list;
        }

        /// <summary>The track drawn nearest a canvas point, within a few pixels; -1 if none (the self-test checks a loco's mark lies on its own track).</summary>
        internal static int TrackAtCanvas(Vector2 canvas)
        {
            var page = BookletReader.Read?.Page(at);
            if (page == null)
                return -1;
            var (px, py) = view.ToPage(canvas.x - MapX, canvas.y - MapY, MapW / 2, MapH / 2);
            var p = page.TrackAt(px, py, 4 / view.Scale);
            return p.HasValue ? p.Value.Track : -1;
        }
#endif

        // ---- The page on the tablet ----

        /// <summary>A pass of the map on the screen's layer (<see cref="TabletSurface"/>).</summary>
        public static void Draw(MapPage owner)
        {
            var network = MapWorld.Network;
            var book = MapWorld.Booklet;
            if (book == null)
            {
                Box(S(0, 0, MapW, MapH), Well);
                Label(S(24, 24, MapW - 48, 60), network == null ? "The world isn't loaded yet." : "The station booklet couldn't be read (see the mod's log).", SoftText);
                PressArea = Rect.zero;
                return;
            }
            // The page drawn now, in this pass, so a press that opens a page shows it at once.
            RenderIfNeeded(book, network);
            var page = PageOf(book);
            Box(S(0, 0, MapW, MapH), Well);
            if (texture != null && placed)
                Picture(S(0, 0, MapW, MapH), texture, textureVersion);
            if (page == null)
            {
                PressArea = Rect.zero;
                return;
            }
            DrawMarks();
            DrawTitle(page);
            DrawTabs(owner, book, page);
            DrawColumn(owner, book, page, network);
            // Between the tabs and the column, above the status strip: no plate there.
            PressArea = S(128, 0, MapW - 128 - 152, StripTop - 4);
            if (Plates)
                DrawPlates(owner, book, page, network);
        }

        private static void DrawTitle(BookletPage page)
        {
            var title = page.IsRoute ? "Route map" : $"{page.Source.StationName} · {page.Station}-{page.Yard} · {Title(page.Title)}";
            Label(S(136, 16, 760, 40), title, TitleLook);
        }

        /// <summary>The title in dark brown, not AI Engineer's soft beige: legible on the map's light, flat background.</summary>
        private static Look TitleLook => titleLook ?? (titleLook = Section.With(TextAnchor.MiddleLeft, true).Tinted(Hex("4b3a29")));

        private static Look titleLook;

        private static string Title(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        /// <summary>Nine station tabs at a time, in the booklet's order, with ▲ and ▼ for the sets before and after.</summary>
        private static void DrawTabs(MapPage owner, Booklet book, BookletPage page)
        {
            var sets = book.TabSets(Per);
            tabSet = Mathf.Clamp(tabSet, 0, Math.Max(0, sets - 1));
            if (Button(S(16, 16, 104, 52), "▲", Plate, tabSet > 0))
            {
                tabSet--;
                Changed();
            }
            var list = book.TabSet(tabSet, Per);
            for (var i = 0; i < Per; i++)
            {
                if (i >= list.Count)
                    continue;
                var st = list[i];
                var r = S(16, 16 + 58 * (i + 1), 104, 52);
                var onOverview = !page.IsRoute && page.Station == st.Code && st.Overview == page;
                var inYard = !page.IsRoute && page.Station == st.Code && !onOverview;
                if (Button(r, onOverview ? Tint(st.Code, Brass2) : inYard ? Tint(st.Code, Brass2) : st.Code, onOverview ? Here : inYard ? PlateOn : Plate) && !onOverview)
                    Nav(owner, book, PageRef.Overview(st.Code));
            }
            if (Button(S(16, 16 + 58 * (Per + 1), 104, 52), "▼", Plate, tabSet < sets - 1))
            {
                tabSet++;
                Changed();
            }
        }

        private static void Nav(MapPage owner, Booklet book, PageRef page)
        {
            Open(book, page);
            picked = null;
            owner.Notice = null;
            Changed();
        }

        /// <summary>The column down the right edge; every plate in the same place on every page.</summary>
        private static void DrawColumn(MapPage owner, Booklet book, BookletPage page, TrackNetwork network)
        {
            const float x = MapW - 16 - 128;
            if (Button(S(x, 16, 60, 60), "+", Plate, view.Scale < PageView.MaxScale - 1e-6))
                Zoom(2, MapW / 2, MapH / 2);
            if (Button(S(x + 68, 16, 60, 60), "–", Plate, view.Scale > fit.Scale + 1e-6))
                Zoom(0.5, MapW / 2, MapH / 2);
            if (Button(S(x, 82, 128, 72), page.IsRoute ? Tint("Map", Brass2) : "Map", page.IsRoute ? Here : Plate) && !page.IsRoute)
                Nav(owner, book, PageRef.Route);
            var station = StationFor(book, page);
            if (station != null)
            {
                var onOverview = page.Station == station.Code && station.Overview == page;
                var inYard = page.Station == station.Code && !onOverview;
                var plate = Two(onOverview ? Tint(station.Code, Brass2) : station.Code, inYard ? "▲ up" : "overview");
                if (Button(S(x, 160, 128, 72), plate, onOverview ? Here : Plate) && !onOverview)
                    Nav(owner, book, PageRef.Overview(station.Code));
                // Its yards' letters, two a row; the one shown is lit, and a press on it goes up a level.
                var yards = station.Yards.ToList();
                for (var i = 0; i < Math.Min(2 * LetterRows, yards.Count); i++)
                {
                    var y = yards[i];
                    var on = page == y;
                    var r = S(x + (i % 2) * 68, 238 + (i / 2) * 66, 60, 60);
                    if (Button(r, on ? Tint(y.Yard, Brass2) : y.Yard, on ? PlateOn : Plate))
                        Nav(owner, book, on ? PageRef.Overview(station.Code) : PageRef.OfYard(station.Code, y.Yard));
                }
            }
            // In place of AI Engineer's Show (the chosen loco's page): the player's own.
            var near = Nearest(book);
            if (Button(S(x, 238 + LetterRows * 66, 128, 72), $"<size=23>{Tint("Here", Soft)}</size>\n<b>{(near != null ? near.Code : "—")}</b>", Plate, near != null))
            {
                ShowHere(book, network);
                picked = null;
                owner.Notice = null;
                Changed();
            }
            var ay = 238 + LetterRows * 66 + 78;
            if (Button(S(x, ay, 60, 60), "◀", Plate))
                Step(-1, 0);
            if (Button(S(x + 68, ay, 60, 60), "▶", Plate))
                Step(1, 0);
            if (Button(S(x, ay + 66, 60, 60), "▲", Plate))
                Step(0, -1);
            if (Button(S(x + 68, ay + 66, 60, 60), "▼", Plate))
                Step(0, 1);
            // Picking for Track Ahead, ROUTE waits for a track, and CANCEL goes back with nothing sent.
            if ((picked != null || forAhead) && Button(S(x, PickY, 128, 52), picked != null ? Tint("ROUTE", Brass2) : "ROUTE", picked != null ? PlateOn : Plate, picked != null))
            {
                AheadRoute.Destination = picked;
                picked = null;
                dirty = true;
                if (forAhead)
                {
                    forAhead = false;
                    owner.Notice = null;
                    owner.BackToAhead();
                    return;
                }
                owner.Notice = $"Route set to {AheadRoute.Destination.Name}. It throws no switches; Track Ahead shows those set against it";
                Changed();
            }
            if (forAhead)
            {
                if (Button(S(x, PickY + 58, 128, 52), "CANCEL", Plate))
                {
                    forAhead = false;
                    picked = null;
                    owner.Notice = null;
                    dirty = true;
                    owner.BackToAhead();
                }
            }
            else if (AheadRoute.Destination != null && Button(S(x, PickY + 58, 128, 52), "CLEAR", Plate))
            {
                owner.Notice = $"Track Ahead no longer routes to {AheadRoute.Destination.Name}";
                AheadRoute.Destination = null;
                dirty = true;
                Changed();
            }
        }

        /// <summary>A step with an arrow: three fifths of what the map shows.</summary>
        private static void Step(int dx, int dy)
        {
            var wd = FitArea.Width / view.Scale * 0.6;
            var ht = FitArea.Height / view.Scale * 0.6;
            view = new PageView(view.CenterX + dx * wd, view.CenterY + dy * ht, view.Scale).ClampTo(fit, content);
            dirty = true;
            Changed();
        }

        /// <summary>Zooms about a point of the map (map pixels), the page point under it staying put.</summary>
        private static void Zoom(double factor, double lx, double ly)
        {
            var (px, py) = view.ToPage(lx, ly, MapW / 2, MapH / 2);
            var z = view.Zoomed(factor).ClampTo(fit, content);
            view = new PageView(px - (lx - MapW / 2) / z.Scale, py - (ly - MapH / 2) / z.Scale, z.Scale).ClampTo(fit, content);
            dirty = true;
            Changed();
        }

        // ---- Presses on the map ----

        /// <summary>The part of the map no plate is over (canvas pixels), where a press acts on the map; empty off the map page.</summary>
        public static Rect PressArea { get; private set; }

        /// <summary>The line on the status strip when nothing has been said.</summary>
        public static string Status
        {
            get
            {
                var book = BookletReader.Read;
                if (book == null)
                    return MapWorld.HasNetwork ? "Reading the station booklet…" : "Waiting for the world";
                var page = book.Page(at);
                if (forAhead)
                    return page == null || page.IsRoute ? "Tap a track, or a station to open it, then ROUTE" : "Tap a track, then ROUTE";
                var opens = page == null || page.IsRoute ? "Press a station to open it" : book.Find(page.Station)?.Overview == page ? "Press a yard to open it" : null;
                var throws = TabletMaps.CanSwitch ? "a switch to throw it" : "the Dispatcher license throws switches";
                if (opens == null)
                    return TabletMaps.CanSwitch ? "Press a switch to throw it, a track to route there" : "Press a track to route there; switches take the Dispatcher license";
                return TabletMaps.CanSwitch ? $"{opens}, {throws}" : $"{opens}; {throws}";
            }
        }

        /// <summary>A press on the map, at a canvas point: opens a station or a yard, or throws a switch.</summary>
        public static void PressAt(MapPage owner, Vector2 canvas)
        {
            var network = MapWorld.Network;
            var book = BookletReader.Get(network);
            if (book == null)
                return;
            var page = PageOf(book);
            if (page == null)
                return;
            var lx = canvas.x - MapX;
            var ly = canvas.y - MapY;
            // A press on the tabs, the column, or the status strip is theirs, between their plates too.
            if (lx < 0 || ly < 0 || lx > MapW || ly > StripTop
                || S(16, 16, 104, 58 * (Per + 2)).Contains(canvas) || S(MapW - 16 - 128, 16, 128, StripTop - 16).Contains(canvas))
                return;
            try
            {
                Press(owner, book, network, page, lx, ly);
            }
            catch (TabletSurface.Redraw)
            {
            }
            dirty = true;
        }

        /// <summary>
        /// What a press at a map point does: on the route map a station's
        /// badge opens its overview; else the switch nearest it throws; else
        /// an overview's capsule opens its yard; else the track there is picked.
        /// </summary>
        private static void Press(MapPage owner, Booklet book, TrackNetwork network, BookletPage page, float lx, float ly)
        {
            var (px, py) = view.ToPage(lx, ly, MapW / 2, MapH / 2);
            var radius = PressRadius / view.Scale;
            if (page.IsRoute)
            {
                var code = BadgeAt(book, page, px, py, radius * 1.6);
                if (code != null)
                {
                    Nav(owner, book, PageRef.Overview(code));
                    return;
                }
            }
            var drawn = page.SwitchAt(px, py, radius);
            if (drawn != null)
            {
                Throw(owner, network, drawn);
                return;
            }
            var capsule = page.CapsuleAt(px, py, radius);
            if (capsule != null && page.Station != null && book.Find(page.Station)?.Find(capsule) != null)
            {
                Nav(owner, book, PageRef.OfYard(page.Station, capsule));
                return;
            }
            var place = page.TrackAt(px, py, radius);
            if (place.HasValue)
                Pick(owner, network, page, place.Value);
        }

        /// <summary>Picks a track, or lets it go if it was the one picked: nothing is sent until ROUTE is pressed.</summary>
        private static void Pick(MapPage owner, TrackNetwork network, BookletPage page, TrackPosition place)
        {
            if (picked != null && picked.Track == place.Track)
            {
                picked = null;
                owner.Notice = null;
            }
            else
            {
                var where = page.Station == null || string.IsNullOrEmpty(page.Yard) ? page.Station : $"{page.Station}-{page.Yard}";
                picked = AheadGoal.ForPlace(network, place, where);
                owner.Notice = $"{picked.Name}: press ROUTE to go there";
            }
            dirty = true;
            Changed();
        }

        /// <summary>The station whose route-map badge is nearest a page point (y down), within <paramref name="radius"/>; null if none.</summary>
        private static string BadgeAt(Booklet book, BookletPage page, double px, double py, double radius)
        {
            SchematicBadge best = null;
            var bestD = radius;
            foreach (var b in page.Source.Badges)
            {
                var d = Math.Sqrt((b.X - px) * (b.X - px) + (-b.Y - py) * (-b.Y - py));
                if (d < bestD)
                {
                    bestD = d;
                    best = b;
                }
            }
            var code = best?.Text.Replace("\n", "").Replace(" ", "");
            return code != null && book.Find(code) != null ? code : null;
        }

        /// <summary>Throws a pressed switch with the Dispatcher license, as the game's touch maps do; without it, says so.</summary>
        private static void Throw(MapPage owner, TrackNetwork network, DrawnSwitch drawn)
        {
            var junction = drawn.Junction >= 0 && drawn.Junction < network.Junctions.Length ? network.Junctions[drawn.Junction] : null;
            var name = junction?.junctionData.junctionIdLong ?? "The switch";
            if (!TabletMaps.CanSwitch)
                owner.Notice = "Throwing switches from the map takes the Dispatcher license";
            else
                owner.Notice = TabletMaps.Flip(junction, owner.Where)
                    ? $"{name} set {(junction.selectedBranch == 0 ? "left" : "right")}"
                    : $"{name} cannot be thrown now";
            dirty = true;
            Changed();
        }

        // ---- Plates over what a hand presses (VR) ----

        /// <summary>
        /// Each station badge (route map), switch, and yard capsule on view
        /// as an invisible plate of its own, where it lies at least
        /// <see cref="PlateSize"/> from every other: a hand presses the plate
        /// where the thing is drawn. Closer ones are pressed zoomed in.
        /// </summary>
        private static void DrawPlates(MapPage owner, Booklet book, BookletPage page, TrackNetwork network)
        {
            var targets = Targets(book, page);
            var area = PressArea;
            var inside = Rect.MinMaxRect(area.xMin + PlateSize / 2, area.yMin + PlateSize / 2, area.xMax - PlateSize / 2, area.yMax - PlateSize / 2);
            var shown = targets.Where(t => inside.Contains(t.At)).ToList();
            foreach (var t in shown)
            {
                if (shown.Any(o => !ReferenceEquals(o, t) && (o.At - t.At).sqrMagnitude < PlateSize * PlateSize))
                    continue;
                var r = new Rect(Mathf.Round(t.At.x - PlateSize / 2), Mathf.Round(t.At.y - PlateSize / 2), PlateSize, PlateSize);
                if (!Button(r, null, Invisible))
                    continue;
                if (t.Switch != null)
                    Throw(owner, network, t.Switch);
                else if (t.Station != null)
                    Nav(owner, book, PageRef.Overview(t.Station));
                else if (t.Yard != null)
                    Nav(owner, book, PageRef.OfYard(page.Station, t.Yard));
            }
            if (page.IsRoute)
                return;
            // Then the tracks, each at its middle, wherever a plate still fits: what acts on the map comes first.
            var taken = shown.Select(t => t.At).ToList();
            foreach (var t in TrackTargets(page))
            {
                if (!inside.Contains(t.At) || taken.Any(o => (o - t.At).sqrMagnitude < PlateSize * PlateSize))
                    continue;
                taken.Add(t.At);
                var r = new Rect(Mathf.Round(t.At.x - PlateSize / 2), Mathf.Round(t.At.y - PlateSize / 2), PlateSize, PlateSize);
                if (Button(r, null, Invisible))
                    Pick(owner, network, page, t.Place);
            }
        }

        /// <summary>The middle of each track line on the page, where it is drawn now.</summary>
        internal static List<(Vector2 At, TrackPosition Place)> TrackTargets(BookletPage page)
        {
            var list = new List<(Vector2, TrackPosition)>();
            foreach (var r in page.Runs)
            {
                if (!r.Matched || r.IsCapsule)
                    continue;
                var (mid, _) = r.Drawn.PointAt(r.Drawn.Length / 2);
                list.Add((CanvasPoint(mid.X, -mid.Z), r.PositionAt(r.Drawn.Length / 2)));
            }
            return list;
        }

        /// <summary>A thing to press on the map, where it is drawn (canvas pixels).</summary>
        internal sealed class Target
        {
            public Vector2 At;
            public DrawnSwitch Switch;
            public string Station;
            public string Yard;
        }

        /// <summary>The page's station badges (route map), switches, and yard capsules, where each is drawn now.</summary>
        internal static List<Target> Targets(Booklet book, BookletPage page)
        {
            var list = new List<Target>();
            if (page.IsRoute)
                foreach (var b in page.Source.Badges)
                {
                    var code = b.Text.Replace("\n", "").Replace(" ", "");
                    if (book.Find(code) != null)
                        list.Add(new Target { At = CanvasPoint(b.X, -b.Y), Station = code });
                }
            foreach (var s in page.Schematic.Switches)
                if (s.Junction >= 0)
                    list.Add(new Target { At = CanvasPoint(s.X, -s.Y), Switch = s });
            if (page.Station != null)
            {
                var station = book.Find(page.Station);
                foreach (var r in page.Runs)
                {
                    if (!r.IsCapsule || string.IsNullOrEmpty(r.Drawn.Capsule) || station?.Find(r.Drawn.Capsule) == null || list.Any(t => t.Yard == r.Drawn.Capsule))
                        continue;
                    // The capsule's letter badge, else its middle.
                    var badge = r.Drawn.Badges.FirstOrDefault();
                    var (mid, _) = r.Drawn.PointAt(r.Drawn.Length / 2);
                    list.Add(new Target { At = badge != null ? CanvasPoint(badge.X, -badge.Y) : CanvasPoint(mid.X, -mid.Z), Yard = r.Drawn.Capsule });
                }
            }
            return list;
        }
    }
}
