using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Booklet
{
    /// <summary>A track a drawn run stands for: entered by one end, beginning a distance along the run's way through the graph.</summary>
    public readonly struct RunTrack
    {
        public readonly int Track;
        public readonly TrackEnd Entered;
        public readonly double Start;
        public readonly double Length;

        public RunTrack(int track, TrackEnd entered, double start, double length)
        {
            Track = track;
            Entered = entered;
            Start = start;
            Length = length;
        }

        public override string ToString() => $"{Track} from {Entered}";
    }

    /// <summary>
    /// A drawn run and the tracks it stands for, end to end. A position on
    /// one of them is drawn as far along the run as it is along the tracks:
    /// the page's schematic, not the map, sets the run's length.
    /// </summary>
    public sealed class BookletRun
    {
        internal BookletRun(SchematicRun drawn, RunTrack[] path)
        {
            Drawn = drawn;
            Path = path;
            PathLength = path.Length == 0 ? 0 : path[path.Length - 1].Start + path[path.Length - 1].Length;
        }

        public SchematicRun Drawn { get; }

        /// <summary>The tracks, in the run's order from its start; empty if the run was not matched to the graph.</summary>
        public IReadOnlyList<RunTrack> Path { get; }

        /// <summary>The tracks' length end to end, meters.</summary>
        public double PathLength { get; }

        public bool Matched => Path.Count > 0;

        /// <summary>Whether it stands for a whole yard (an overview's capsule) rather than one track.</summary>
        public bool IsCapsule => Drawn.Capsule != null;

        /// <summary>How far along the run (page units) a position on one of its tracks is drawn; null if the track isn't one of them.</summary>
        public double? AlongOf(TrackPosition p)
        {
            if (PathLength <= 0)
                return null;
            foreach (var t in Path)
            {
                if (t.Track != p.Track)
                    continue;
                var off = Math.Max(0, Math.Min(t.Length, p.Offset));
                var along = t.Start + (t.Entered == TrackEnd.Start ? off : t.Length - off);
                return along / PathLength * Drawn.Length;
            }
            return null;
        }

        /// <summary>The track position drawn a distance along the run (page units).</summary>
        public TrackPosition PositionAt(double along)
        {
            var a = Math.Max(0, Math.Min(PathLength, along / Math.Max(1e-9, Drawn.Length) * PathLength));
            for (var i = 0; i < Path.Count; i++)
            {
                var t = Path[i];
                if (a <= t.Start + t.Length || i == Path.Count - 1)
                {
                    var into = Math.Max(0, Math.Min(t.Length, a - t.Start));
                    return new TrackPosition(t.Track, t.Entered == TrackEnd.Start ? into : t.Length - into);
                }
            }
            return default;
        }

        public override string ToString() => $"{Drawn} = [{string.Join(", ", Path.Select(p => p.Track))}]";
    }

    /// <summary>
    /// A page of the booklet as the tablet shows it: the game's own
    /// schematic, read as track and matched to the graph. Page coordinates
    /// are the schematic's, with y turned down (a page's y is the schematic's
    /// negated), as the screen runs.
    /// </summary>
    public sealed class BookletPage
    {
        internal BookletPage(SchematicSource source, Schematic schematic, BookletRun[] runs, Dictionary<(int, SwitchLeg), int> sides)
        {
            this.sides = sides;
            Source = source;
            Schematic = schematic;
            Runs = runs;
            var b = schematic.Box;
            Box = new PageBox(b.Left, -b.Bottom, b.Right, -b.Top);
            var own = new Dictionary<int, List<BookletRun>>();
            foreach (var r in runs)
            {
                if (r.IsCapsule)
                    continue;
                foreach (var t in r.Path)
                {
                    if (!own.TryGetValue(t.Track, out var list))
                        own[t.Track] = list = new List<BookletRun>();
                    if (!list.Contains(r))
                        list.Add(r);
                }
            }
            byTrack = own;
        }

        private readonly Dictionary<int, List<BookletRun>> byTrack;
        private readonly Dictionary<(int, SwitchLeg), int> sides;

        /// <summary>The drawn leg of a page's switch (by its index in <see cref="Schematic.Switches"/>) that is the graph switch's branch; null if none is.</summary>
        public SwitchLeg? LegOf(int drawnSwitch, int branch)
        {
            foreach (var kv in sides)
                if (kv.Key.Item1 == drawnSwitch && kv.Value == branch && kv.Key.Item2 != SwitchLeg.Stem)
                    return kv.Key.Item2;
            return null;
        }

        public SchematicSource Source { get; }
        public Schematic Schematic { get; }
        public IReadOnlyList<BookletRun> Runs { get; }

        /// <summary>The station's code, null on the route map.</summary>
        public string Station => Source.Station;

        /// <summary>The page's letter: "A" is the station's overview.</summary>
        public string Yard => Source.Yard;

        public string Title => Source.Title;

        public bool IsRoute => Source.IsRoute;

        /// <summary>What its track takes on the page (y down).</summary>
        public PageBox Box { get; }

        /// <summary>A schematic point (y up) on the page (y down).</summary>
        public static MapPoint ToPage(MapPoint schematic) => new MapPoint(schematic.X, -schematic.Z);

        /// <summary>Whether the page draws a track (as one of its runs, not inside a capsule).</summary>
        public bool Shows(int track) => byTrack.ContainsKey(track);

        /// <summary>The tracks it draws.</summary>
        public IEnumerable<int> Tracks => byTrack.Keys;

        /// <summary>Where a position is drawn on the page (y down) and the way its run goes there; null if the page doesn't draw its track.</summary>
        public (MapPoint At, MapPoint Heading)? PointOf(TrackPosition p)
        {
            if (!byTrack.TryGetValue(p.Track, out var runs))
                return null;
            foreach (var r in runs)
            {
                var along = r.AlongOf(p);
                if (along == null)
                    continue;
                var (at, heading) = r.Drawn.PointAt(along.Value);
                return (ToPage(at), new MapPoint(heading.X, -heading.Z));
            }
            return null;
        }

        /// <summary>
        /// Where a stretch of a track (from one offset to another) is drawn:
        /// for each run that draws the track, the run and the stretch's two
        /// distances along it, page units. Empty if the page doesn't draw it.
        /// </summary>
        public IEnumerable<(BookletRun Run, double From, double To)> Stretch(int track, double from, double to)
        {
            if (!byTrack.TryGetValue(track, out var runs))
                yield break;
            foreach (var r in runs)
            {
                var a = r.AlongOf(new TrackPosition(track, from));
                var b = r.AlongOf(new TrackPosition(track, to));
                if (a != null && b != null)
                    yield return (r, a.Value, b.Value);
            }
        }

        /// <summary>The track position drawn nearest a page point (y down), within <paramref name="radius"/> page units; null if none.</summary>
        public TrackPosition? TrackAt(double x, double y, double radius)
        {
            BookletRun best = null;
            double bestAlong = 0, bestDistance = radius;
            foreach (var r in Runs)
            {
                if (!r.Matched || r.IsCapsule)
                    continue;
                var (along, distance) = r.Drawn.Nearest(x, -y);
                if (distance <= bestDistance)
                {
                    best = r;
                    bestAlong = along;
                    bestDistance = distance;
                }
            }
            return best?.PositionAt(bestAlong);
        }

        /// <summary>The switch drawn nearest a page point (y down), within <paramref name="radius"/>; null if none.</summary>
        public DrawnSwitch SwitchAt(double x, double y, double radius)
        {
            DrawnSwitch best = null;
            var bestDistance = radius;
            foreach (var s in Schematic.Switches)
            {
                var d = Math.Sqrt((s.X - x) * (s.X - x) + (-s.Y - y) * (-s.Y - y));
                if (s.Junction >= 0 && d <= bestDistance)
                {
                    best = s;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>The yard whose capsule is drawn nearest a page point (y down), within <paramref name="radius"/>; null if none.</summary>
        public string CapsuleAt(double x, double y, double radius)
        {
            string best = null;
            var bestDistance = radius;
            foreach (var r in Runs)
            {
                if (!r.IsCapsule || string.IsNullOrEmpty(r.Drawn.Capsule))
                    continue;
                var (_, d) = r.Drawn.Nearest(x, -y);
                if (d <= bestDistance)
                {
                    best = r.Drawn.Capsule;
                    bestDistance = d;
                }
            }
            return best;
        }

        public override string ToString() => Source.Name;
    }

    /// <summary>A station's chapter: its pages in the booklet's order, the first its overview.</summary>
    public sealed class BookletStation
    {
        internal BookletStation(string code, string name, string color, BookletPage[] pages)
        {
            Code = code;
            Name = name;
            Color = color;
            Pages = pages;
        }

        public string Code { get; }
        public string Name { get; }
        public string Color { get; }
        public IReadOnlyList<BookletPage> Pages { get; }

        public BookletPage Overview => Pages[0];

        /// <summary>Its yards' pages: every page after the overview.</summary>
        public IEnumerable<BookletPage> Yards => Pages.Skip(1);

        public BookletPage Find(string letter) => Pages.FirstOrDefault(p => p.Yard == letter);

        public override string ToString() => Code;
    }

    /// <summary>
    /// The game's station booklet and route map, read from its render and
    /// matched to the graph: what the tablet's Map tab shows. The stations
    /// come in the booklet's chapter order.
    /// </summary>
    public sealed class Booklet
    {
        /// <summary>How far a run's way through the graph is followed past switches the page doesn't draw, meters.</summary>
        private const double MaxWalk = 20000;

        private const int MaxSteps = 20000;

        private Booklet(BookletPage route, BookletStation[] stations, MatchStats stats)
        {
            Route = route;
            Stations = stations;
            Stats = stats;
        }

        /// <summary>The route map; null if the game's wasn't read.</summary>
        public BookletPage Route { get; }

        public IReadOnlyList<BookletStation> Stations { get; }

        public MatchStats Stats { get; }

        public IEnumerable<BookletPage> Pages => Stations.SelectMany(s => s.Pages).Concat(Route != null ? new[] { Route } : new BookletPage[0]);

        public BookletStation Find(string code) => Stations.FirstOrDefault(s => s.Code == code);

        public BookletPage Page(PageRef page)
        {
            if (page.Kind == PageKind.Route)
                return Route;
            var s = Find(page.Station);
            if (s == null)
                return null;
            return page.Kind == PageKind.Station ? s.Overview : s.Find(page.Yard);
        }

        /// <summary>The station tabs of a set (sets of <paramref name="per"/> in chapter order).</summary>
        public IReadOnlyList<BookletStation> TabSet(int set, int per) => Stations.Skip(set * per).Take(per).ToArray();

        public int TabSets(int per) => (Stations.Count + per - 1) / per;

        /// <summary>The set a station's tab is in.</summary>
        public int SetOf(string code, int per)
        {
            for (var i = 0; i < Stations.Count; i++)
                if (Stations[i].Code == code)
                    return i / per;
            return 0;
        }

        /// <summary>The page to show a position on: a yard's page if one draws it, else its station's overview, else the route map.</summary>
        public BookletPage PageFor(TrackPosition p)
        {
            BookletPage overview = null;
            foreach (var s in Stations)
            {
                foreach (var page in s.Yards)
                    if (page.Shows(p.Track))
                        return page;
                if (overview == null && s.Overview.Shows(p.Track))
                    overview = s.Overview;
            }
            return overview ?? Route;
        }

        /// <summary>How well the pages matched the graph: for the tests and the log.</summary>
        public sealed class MatchStats
        {
            public int Runs;
            public int Matched;
            public int Capsules;
            public int Switches;
            public int SwitchesNamed;
            /// <summary>Drawn switches whose legs the ways they lead settle, and those it doesn't (both lead alike).</summary>
            public int LegsSettled;
            public int LegsTied;
            /// <summary>Of the settled: those whose left leg is the branch on the left, and (plain switches) whose through leg is the through branch.</summary>
            public int SideAgrees;
            public int NameAgrees;
            /// <summary>Runs with a track's number badge, and those whose tracks include the track it names.</summary>
            public int BadgesChecked;
            public int BadgesAgree;
            public readonly List<string> Problems = new List<string>();

            public override string ToString() =>
                $"{Matched}/{Runs} runs matched ({Capsules} capsules), {SwitchesNamed}/{Switches} switches named, legs settled {LegsSettled} (tied {LegsTied}): left is left {SideAgrees}, through is through {NameAgrees}, badges agree {BadgesAgree}/{BadgesChecked}";
        }

        public static Booklet Build(IReadOnlyList<SchematicSource> sources, TrackGraph graph, Func<int, string> nameOf)
        {
            var stats = new MatchStats();
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var t = 0; t < graph.TrackCount; t++)
            {
                var n = nameOf(t);
                if (!string.IsNullOrEmpty(n) && !byName.ContainsKey(n))
                    byName[n] = t;
            }
            var legs = new Dictionary<int, Legs>();
            Legs LegsOf(int j)
            {
                if (!legs.TryGetValue(j, out var l))
                    legs[j] = l = Classify(graph, j, nameOf);
                return l;
            }

            BookletPage route = null;
            var chapters = new List<(string Code, List<BookletPage> Pages)>();
            foreach (var src in sources)
            {
                var page = Match(src, graph, nameOf, byName, LegsOf, stats);
                if (src.IsRoute)
                {
                    route = page;
                    continue;
                }
                if (string.IsNullOrEmpty(src.Station))
                    continue;
                var ch = chapters.FindIndex(c => c.Code == src.Station);
                if (ch < 0)
                {
                    chapters.Add((src.Station, new List<BookletPage>()));
                    ch = chapters.Count - 1;
                }
                chapters[ch].Pages.Add(page);
            }
            var stations = chapters.Select(c => new BookletStation(c.Code, c.Pages[0].Source.StationName, c.Pages[0].Source.Color, c.Pages.ToArray())).ToArray();
            return new Booklet(route, stations, stats);
        }

        /// <summary>A graph switch's branches as the booklet draws them: which is through, which diverges, and to which side.</summary>
        private readonly struct Legs
        {
            public readonly int Through;
            public readonly int Diverging;
            public readonly bool DivergingLeft;

            public Legs(int through, int diverging, bool divergingLeft)
            {
                Through = through;
                Diverging = diverging;
                DivergingLeft = divergingLeft;
            }
        }

        private static MapPoint Away(TrackGraph g, int track, TrackEnd end)
        {
            var a = g.EndPoint(track, end);
            var b = g.EndPoint(track, end.Other());
            double dx = b.X - a.X, dz = b.Z - a.Z, len = Math.Sqrt(dx * dx + dz * dz);
            return len > 0 ? new MapPoint(dx / len, dz / len) : new MapPoint(0, 0);
        }

        private static double Cross(MapPoint a, MapPoint b) => a.X * b.Z - a.Z * b.X;

        /// <summary>
        /// The game names a switch's own two tracks "[track through]" and
        /// "[track diverging]"; failing that, the branch that turns least from
        /// the stem's line is through. Map X east and Z north turn the same way
        /// as a page's x right and y up, so left is left on both.
        /// </summary>
        private static Legs Classify(TrackGraph g, int j, Func<int, string> nameOf)
        {
            var info = g.Junction(j);
            var n = info.Branches.Length;
            if (n < 2)
                return new Legs(0, 0, false);
            var stem = Away(g, info.StemTrack, info.StemEnd);
            var dirs = info.Branches.Select(b => Away(g, b.Track, b.End)).ToArray();
            var through = -1;
            for (var k = 0; k < n; k++)
            {
                var name = nameOf(info.Branches[k].Track) ?? "";
                if (name.IndexOf("through", StringComparison.OrdinalIgnoreCase) >= 0)
                    through = through == -1 ? k : -2;
            }
            if (through < 0)
            {
                // Most nearly straight on from the stem: the direction most opposite the stem's.
                through = 0;
                for (var k = 1; k < n; k++)
                    if (dirs[k].X * stem.X + dirs[k].Z * stem.Z < dirs[through].X * stem.X + dirs[through].Z * stem.Z)
                        through = k;
            }
            var diverging = through == 0 ? 1 : 0;
            return new Legs(through, diverging, Cross(dirs[through], dirs[diverging]) > 0);
        }

        private enum Goal : byte { Switch, DeadEnd, Away, Any }

        /// <summary>A side of a graph switch a walk is to arrive by: its stem, a branch, or any branch.</summary>
        private const int AnySide = -2, AnyBranch = -3;

        private struct WalkFrame
        {
            public int Key;
            public TrackLink[] Links;
            public int NextLink;
            public int LinkCount;
            public double Distance;
        }

        private sealed class Walker
        {
            public TrackGraph Graph;
            public HashSet<int> OnPage;
            public Goal Goal;
            public int Junction;
            public int Side;
            public int Steps;
            public readonly List<(int Track, TrackEnd Entered)> Path = new List<(int, TrackEnd)>();

            public bool Walk(int track, TrackEnd entered, double distance)
            {
                var stack = new List<WalkFrame>();
                var active = new HashSet<int>();

                bool TryPush(int t, TrackEnd e, double d, out bool found)
                {
                    found = false;
                    var key = t * 2 + (int)e;
                    if (active.Contains(key))
                        return false;
                    if (++Steps > MaxSteps)
                        return false;

                    Path.Add((t, e));
                    active.Add(key);
                    var nextDistance = d + Graph.Length(t);
                    var far = e.Other();
                    var links = Graph.Links(t, far);
                    if (links.Length == 0)
                    {
                        if (Goal != Goal.Switch)
                        {
                            found = true;
                            return true;
                        }
                        Path.RemoveAt(Path.Count - 1);
                        active.Remove(key);
                        return false;
                    }
                    if (nextDistance > MaxWalk)
                    {
                        if (Goal == Goal.Away || Goal == Goal.Any)
                        {
                            found = true;
                            return true;
                        }
                        Path.RemoveAt(Path.Count - 1);
                        active.Remove(key);
                        return false;
                    }
                    var j = links[0].Junction;
                    int linkCount;
                    if (j == TrackLink.NoJunction)
                    {
                        linkCount = 1;
                    }
                    else
                    {
                        var info = Graph.Junction(j);
                        var atStem = info.StemTrack == t && info.StemEnd == far;
                        var side = atStem ? -1 : links[0].Branch;
                        if (OnPage.Contains(j))
                        {
                            if (Goal == Goal.Any || (Goal == Goal.Switch && j == Junction && (Side == AnySide || Side == side || (Side == AnyBranch && side >= 0))))
                            {
                                found = true;
                                return true;
                            }
                            Path.RemoveAt(Path.Count - 1);
                            active.Remove(key);
                            return false;
                        }
                        if (Goal == Goal.Away || Goal == Goal.Any)
                        {
                            found = true;
                            return true;
                        }
                        linkCount = atStem ? links.Length : 1;
                    }
                    stack.Add(new WalkFrame { Key = key, Links = links, NextLink = 0, LinkCount = linkCount, Distance = nextDistance });
                    return true;
                }

                if (!TryPush(track, entered, distance, out var matched))
                    return false;
                if (matched)
                    return true;

                while (stack.Count > 0)
                {
                    var topIndex = stack.Count - 1;
                    var frame = stack[topIndex];
                    if (frame.NextLink >= frame.LinkCount)
                    {
                        stack.RemoveAt(topIndex);
                        Path.RemoveAt(Path.Count - 1);
                        active.Remove(frame.Key);
                        continue;
                    }
                    var link = frame.Links[frame.NextLink];
                    frame.NextLink++;
                    stack[topIndex] = frame;
                    if (TryPush(link.Track, link.End, frame.Distance, out matched) && matched)
                        return true;
                    if (Steps > MaxSteps)
                    {
                        while (stack.Count > 0)
                        {
                            var last = stack.Count - 1;
                            active.Remove(stack[last].Key);
                            stack.RemoveAt(last);
                            Path.RemoveAt(Path.Count - 1);
                        }
                        return false;
                    }
                }
                return false;
            }
        }

        /// <summary>The track end a walk leaves a graph switch by, on a side: -1 its stem, else a branch.</summary>
        private static (int Track, TrackEnd End) Leaving(TrackGraph g, int j, int side)
        {
            var info = g.Junction(j);
            return side < 0 ? (info.StemTrack, info.StemEnd) : info.Branches[side];
        }

        /// <summary>
        /// Matches a page to the graph in two steps. First each drawn switch's
        /// legs are given the graph switch's branches: the booklet draws a
        /// switch to keep its rows straight, not as it lies, so a leg is the
        /// branch whose way leads where the leg's run does (to the next drawn
        /// switch, a buffer stop, or off the page). Where both ways lead alike
        /// (a loop between two switches), the leg on the left is the branch on
        /// the left. Then each run is walked from its switch's branch to the
        /// switch, or the end, it is drawn to.
        /// </summary>
        private static BookletPage Match(SchematicSource src, TrackGraph graph, Func<int, string> nameOf, Dictionary<string, int> byName, Func<int, Legs> legsOf, MatchStats stats)
        {
            var schematic = Schematic.Trace(src);
            var drawn = schematic.Switches;
            int JunctionOf(int s) => s >= 0 && drawn[s].Junction >= 0 && drawn[s].Junction < graph.JunctionCount ? drawn[s].Junction : -1;
            var onPage = new HashSet<int>(Enumerable.Range(0, drawn.Count).Select(JunctionOf).Where(j => j >= 0));
            var legRuns = new Dictionary<(int Switch, SwitchLeg Leg), (SchematicRun Run, bool AtStart)>();
            foreach (var r in schematic.Runs)
            {
                if (r.Start.Kind == RunEndKind.Switch && r.Start.Switch >= 0)
                    legRuns[(r.Start.Switch, r.Start.Leg)] = (r, true);
                if (r.End.Kind == RunEndKind.Switch && r.End.Switch >= 0)
                    legRuns[(r.End.Switch, r.End.Leg)] = (r, false);
            }

            Walker Toward(RunEnd far, Func<int, SwitchLeg, int> sideOf)
            {
                var w = new Walker { Graph = graph, OnPage = onPage };
                var j2 = far.Kind == RunEndKind.Switch ? JunctionOf(far.Switch) : -1;
                switch (far.Kind)
                {
                    case RunEndKind.Switch when j2 >= 0:
                        w.Goal = Goal.Switch;
                        w.Junction = j2;
                        w.Side = sideOf(far.Switch, far.Leg);
                        break;
                    case RunEndKind.Buffer:
                    case RunEndKind.Badge:
                        w.Goal = Goal.DeadEnd;
                        break;
                    case RunEndKind.Exit:
                        w.Goal = Goal.Away;
                        break;
                    default:
                        w.Goal = Goal.Any;
                        break;
                }
                return w;
            }

            // First: each drawn switch's legs, by where they lead. The far end's side is known only for a stem.
            int Loose(int s, SwitchLeg leg) => leg == SwitchLeg.Stem ? -1 : AnyBranch;
            var sides = new Dictionary<(int, SwitchLeg), int>();
            for (var s = 0; s < drawn.Count; s++)
            {
                stats.Switches++;
                var j = JunctionOf(s);
                if (j < 0)
                    continue;
                stats.SwitchesNamed++;
                sides[(s, SwitchLeg.Stem)] = -1;
                var info = graph.Junction(j);
                if (info.Branches.Length < 2)
                    continue;
                var (l1, l2) = drawn[s].Spread ? (SwitchLeg.Left, SwitchLeg.Right) : (SwitchLeg.Through, SwitchLeg.Diverging);
                bool Reaches(SwitchLeg leg, int branch)
                {
                    if (!legRuns.TryGetValue((s, leg), out var lr))
                        return true;
                    var far = lr.AtStart ? lr.Run.End : lr.Run.Start;
                    if (far.Kind == RunEndKind.Switch && far.Switch == s)
                        return true;
                    var w = Toward(far, Loose);
                    var (t, e) = Leaving(graph, j, branch);
                    return w.Walk(t, e, 0);
                }
                bool okA = Reaches(l1, 0) && Reaches(l2, 1), okB = Reaches(l1, 1) && Reaches(l2, 0);
                // The tie-break: the drawn leg on the left is the branch on the left.
                var legs = legsOf(j);
                var gameLeft = legs.DivergingLeft ? legs.Diverging : legs.Through;
                // The drawn left leg: a spread's l1 (Left), a plain switch's diverging leg (l2) if it turns left, else its through leg (l1).
                var leftIsL2 = !drawn[s].Spread && drawn[s].DivergesLeft;
                var sideA = leftIsL2 ? gameLeft == 1 : gameLeft == 0;
                var nameA = legs.Through == 0;
                bool a;
                if (okA != okB)
                {
                    a = okA;
                    stats.LegsSettled++;
                    if (a == sideA)
                        stats.SideAgrees++;
                    if (!drawn[s].Spread && a == nameA)
                        stats.NameAgrees++;
                }
                else
                {
                    a = sideA;
                    stats.LegsTied++;
                }
                sides[(s, l1)] = a ? 0 : 1;
                sides[(s, l2)] = a ? 1 : 0;
            }
            int SideOf(int s, SwitchLeg leg) => sides.TryGetValue((s, leg), out var v) ? v : AnySide;

            var runs = new List<BookletRun>();
            foreach (var run in schematic.Runs)
            {
                stats.Runs++;
                if (run.Capsule != null)
                    stats.Capsules++;
                var path = PathOf(run, graph, byName, JunctionOf, SideOf, Toward, src);
                if (path.Length > 0)
                    stats.Matched++;
                var matched = new BookletRun(run, path);
                runs.Add(matched);
                // A track's number badge names the track: the run's tracks should include it.
                if (matched.Matched && run.Capsule == null && !src.IsRoute)
                {
                    foreach (var b in run.Badges)
                    {
                        if (b.Text.Length == 0 || !b.Text.All(char.IsDigit) || b.Kind == null)
                            continue;
                        if (!int.TryParse(b.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var want))
                            continue;
                        stats.BadgesChecked++;
                        var named = path.Any(p => YardTrackName.TryParse(nameOf(p.Track), out var yn) && yn.Number == want && yn.Kind == b.Kind);
                        if (named)
                            stats.BadgesAgree++;
                        else if (stats.Problems.Count < 200)
                            stats.Problems.Add($"{src.Name} badge {b}: run's tracks are {string.Join(", ", path.Select(p => nameOf(p.Track)))}");
                    }
                }
            }
            return new BookletPage(src, schematic, runs.ToArray(), sides);
        }

        private static RunTrack[] PathOf(SchematicRun run, TrackGraph graph, Dictionary<string, int> byName, Func<int, int> junctionOf,
                                         Func<int, SwitchLeg, int> sideOf, Func<RunEnd, Func<int, SwitchLeg, int>, Walker> toward, SchematicSource src)
        {
            RunEnd from = run.Start, to = run.End;
            int j1 = from.Kind == RunEndKind.Switch ? junctionOf(from.Switch) : -1;
            int j2 = to.Kind == RunEndKind.Switch ? junctionOf(to.Switch) : -1;
            var reversed = false;
            if (j1 < 0 && j2 >= 0)
            {
                reversed = true;
                (from, to) = (to, from);
                (j1, j2) = (j2, j1);
            }
            List<(int Track, TrackEnd Entered)> found = null;
            if (j1 >= 0)
            {
                var side = sideOf(from.Switch, from.Leg);
                if (side != AnySide)
                {
                    var w = toward(to, sideOf);
                    var (t, e) = Leaving(graph, j1, side);
                    if (w.Walk(t, e, 0))
                        found = w.Path;
                    else
                    {
                        // Drawn to an end the graph doesn't lead to that way (an exit drawn on a yard's row): as far as the way goes.
                        var any = new Walker { Graph = graph, OnPage = w.OnPage, Goal = Goal.Any };
                        if (any.Walk(t, e, 0))
                            found = any.Path;
                    }
                }
            }
            else
            {
                // No switch at either end: the track its number badge names, if it has one.
                foreach (var b in run.Badges)
                {
                    if (b.Kind == null || !int.TryParse(b.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || src.Station == null)
                        continue;
                    if (byName.TryGetValue($"[Y]_[{src.Station}]_[{src.Yard}-{number:00}-{b.Kind}]", out var t))
                    {
                        found = new List<(int, TrackEnd)> { (t, TrackEnd.Start) };
                        break;
                    }
                }
            }
            if (found == null)
                return new RunTrack[0];
            if (reversed)
            {
                found.Reverse();
                for (var i = 0; i < found.Count; i++)
                    found[i] = (found[i].Track, found[i].Entered.Other());
            }
            var result = new RunTrack[found.Count];
            var start = 0.0;
            for (var i = 0; i < found.Count; i++)
            {
                var len = graph.Length(found[i].Track);
                result[i] = new RunTrack(found[i].Track, found[i].Entered, start, len);
                start += len;
            }
            return result;
        }
    }
}
