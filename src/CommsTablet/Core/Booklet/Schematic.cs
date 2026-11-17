using System;
using System.Collections.Generic;
using System.Linq;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Booklet
{
    /// <summary>
    /// One piece of a page's schematic as the game's render draws it: a
    /// sprite in a box, in the schematic's own units (a cell is 50), x to the
    /// right and y up, turned counterclockwise by its rotation. A negative
    /// width or height is a mirrored piece.
    /// </summary>
    public sealed class SchematicTile
    {
        public SchematicTile(string sprite, double x, double y, double rotation, double width, double height)
        {
            Sprite = sprite;
            X = x;
            Y = y;
            Rotation = rotation;
            Width = width;
            Height = height;
        }

        public string Sprite { get; }
        public double X { get; }
        public double Y { get; }
        public double Rotation { get; }
        public double Width { get; }
        public double Height { get; }

        public override string ToString() => $"{Sprite} ({X:F0}, {Y:F0}) {Rotation:F0}";
    }

    /// <summary>
    /// A badge on a page: a track's number and kind on a yard's page ("8",
    /// "S"), a yard's letter on an overview's capsule ("B"), a station's code
    /// on the route map ("SM").
    /// </summary>
    public sealed class SchematicBadge
    {
        public SchematicBadge(double x, double y, string text, string kind, string color)
        {
            X = x;
            Y = y;
            Text = text ?? "";
            Kind = kind;
            Color = color;
        }

        public double X { get; }
        public double Y { get; }
        public string Text { get; }

        /// <summary>The track's kind (I, O, L, S, LP, SP), or null.</summary>
        public string Kind { get; }

        /// <summary>Its color as the game gives it ("#7B8494FF"), or null.</summary>
        public string Color { get; }

        public override string ToString() => Kind == null ? Text : Text + Kind;
    }

    /// <summary>A switch the page's touch grid names: the middle of its cell on the schematic, and the switch as an index into the graph.</summary>
    public readonly struct SchematicSwitch
    {
        public readonly double X;
        public readonly double Y;
        public readonly int Junction;

        public SchematicSwitch(double x, double y, int junction)
        {
            X = x;
            Y = y;
            Junction = junction;
        }
    }

    /// <summary>A page of the station booklet, or the route map, as read from the game.</summary>
    public sealed class SchematicSource
    {
        public SchematicSource(string name, string station, string stationName, string yard, string title, string color, bool isRoute,
                               IReadOnlyList<SchematicTile> tiles, IReadOnlyList<SchematicBadge> badges, IReadOnlyList<SchematicSwitch> switches)
        {
            Name = name;
            Station = station;
            StationName = stationName;
            Yard = yard;
            Title = title;
            Color = color;
            IsRoute = isRoute;
            Tiles = tiles;
            Badges = badges;
            Switches = switches;
        }

        /// <summary>The render's name for it: "[SM-B]".</summary>
        public string Name { get; }

        /// <summary>The station's code ("SM"); null on the route map.</summary>
        public string Station { get; }

        public string StationName { get; }

        /// <summary>The page's letter ("A" is a station's overview); null on the route map.</summary>
        public string Yard { get; }

        /// <summary>What the page shows: "OVERVIEW", "TRANSFER YARD".</summary>
        public string Title { get; }

        /// <summary>The station's color, from the page's header ("#7C8394FF").</summary>
        public string Color { get; }

        public bool IsRoute { get; }

        public IReadOnlyList<SchematicTile> Tiles { get; }
        public IReadOnlyList<SchematicBadge> Badges { get; }
        public IReadOnlyList<SchematicSwitch> Switches { get; }

        public override string ToString() => Name;
    }

    /// <summary>How a stretch of drawn track ends.</summary>
    public enum RunEndKind : byte
    {
        /// <summary>At a switch's points, frog, or leg.</summary>
        Switch,

        /// <summary>At a buffer stop.</summary>
        Buffer,

        /// <summary>Off the page: the line goes on.</summary>
        Exit,

        /// <summary>At a badge the track stops under.</summary>
        Badge,

        /// <summary>Nowhere the drawing says: a gap in it.</summary>
        Open,
    }

    /// <summary>A switch's three ways: its stem, and its two legs.</summary>
    public enum SwitchLeg : byte
    {
        Stem,
        Through,
        Diverging,

        /// <summary>A spread switch's left leg, seen from its stem.</summary>
        Left,

        /// <summary>A spread switch's right leg.</summary>
        Right,
    }

    /// <summary>A switch drawn on a page: where, which way its legs go, and the switch in the graph its touch cell names.</summary>
    public sealed class DrawnSwitch
    {
        internal DrawnSwitch(double x, double y, MapPoint point, int junction, bool spread, bool divergesLeft, MapPoint[] legDirections)
        {
            X = x;
            Y = y;
            Point = point;
            Junction = junction;
            Spread = spread;
            DivergesLeft = divergesLeft;
            LegDirections = legDirections;
        }

        /// <summary>The middle of its cell: where its red touch circle is drawn.</summary>
        public double X { get; }
        public double Y { get; }

        /// <summary>Where its legs meet, as the game's sprite draws it: the black dot, toward the points from the cell's middle.</summary>
        public MapPoint Point { get; }

        /// <summary>The graph's switch its touch cell names; -1 if none does.</summary>
        public int Junction { get; }

        /// <summary>A spread switch: both legs turn, left and right.</summary>
        public bool Spread { get; }

        /// <summary>For a plain switch, whether its diverging leg goes off to the left seen from its stem.</summary>
        public bool DivergesLeft { get; }

        /// <summary>The page direction (a unit vector, X right and Z up) each leg leaves <see cref="Point"/> by, indexed by <see cref="SwitchLeg"/>; zero where the switch has no such leg.</summary>
        public MapPoint[] LegDirections { get; }

        public override string ToString() => $"switch {Junction} at ({X:F0}, {Y:F0})";
    }

    /// <summary>One end of a run: how it ends, and at a switch, which switch and leg.</summary>
    public readonly struct RunEnd
    {
        public readonly RunEndKind Kind;
        public readonly int Switch;
        public readonly SwitchLeg Leg;

        public RunEnd(RunEndKind kind, int @switch = -1, SwitchLeg leg = SwitchLeg.Stem)
        {
            Kind = kind;
            Switch = @switch;
            Leg = leg;
        }

        public override string ToString() => Kind == RunEndKind.Switch ? $"switch #{Switch} {Leg}" : Kind.ToString();
    }

    /// <summary>
    /// A stretch of drawn track from one end to the next: switch to switch,
    /// switch to buffer stop, and so on. Its points are a line on the page
    /// (X right, Z up) from <see cref="Start"/> to <see cref="End"/>.
    /// </summary>
    public sealed class SchematicRun
    {
        internal SchematicRun(RunEnd start, RunEnd end, MapPoint[] points, string capsule, List<SchematicBadge> badges)
        {
            Start = start;
            End = end;
            Points = points;
            Capsule = capsule;
            Badges = badges;
            var length = 0.0;
            for (var i = 1; i < points.Length; i++)
                length += points[i - 1].DistanceTo(points[i]);
            Length = length;
        }

        public RunEnd Start { get; }
        public RunEnd End { get; }
        public MapPoint[] Points { get; }

        /// <summary>Its length on the page, schematic units.</summary>
        public double Length { get; }

        /// <summary>The yard letter, if the run is drawn as a yard's capsule (an overview stands a capsule for a yard).</summary>
        public string Capsule { get; }

        /// <summary>The badges on it: the track's number, or the capsule's letter.</summary>
        public IReadOnlyList<SchematicBadge> Badges { get; }

        /// <summary>The point at a distance along it, page units, and the way it runs there.</summary>
        public (MapPoint At, MapPoint Heading) PointAt(double along)
        {
            var left = Math.Max(0, along);
            for (var i = 1; i < Points.Length; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                var d = a.DistanceTo(b);
                if (d <= 0)
                    continue;
                if (left <= d || i == Points.Length - 1)
                {
                    var f = Math.Min(1, left / d);
                    return (new MapPoint(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f), new MapPoint((b.X - a.X) / d, (b.Z - a.Z) / d));
                }
                left -= d;
            }
            return (Points[Points.Length - 1], new MapPoint(1, 0));
        }

        /// <summary>Its line between two distances along it (either way round), page units: for drawing a car or a stretch over it.</summary>
        public MapPoint[] Between(double a, double b)
        {
            var reverse = b < a;
            double lo = Math.Max(0, Math.Min(a, b)), hi = Math.Min(Length, Math.Max(a, b));
            var list = new List<MapPoint> { PointAt(lo).At };
            var run = 0.0;
            for (var i = 1; i < Points.Length; i++)
            {
                run += Points[i - 1].DistanceTo(Points[i]);
                if (run > lo && run < hi)
                    list.Add(Points[i]);
            }
            list.Add(PointAt(hi).At);
            if (reverse)
                list.Reverse();
            return list.ToArray();
        }

        /// <summary>How far along it a page point is, and how far from it.</summary>
        public (double Along, double Distance) Nearest(double x, double y)
        {
            double best = double.MaxValue, bestAlong = 0, run = 0;
            for (var i = 1; i < Points.Length; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                double dx = b.X - a.X, dy = b.Z - a.Z;
                var len2 = dx * dx + dy * dy;
                var len = Math.Sqrt(len2);
                var t = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Z) * dy) / len2)) : 0;
                double px = a.X + dx * t - x, py = a.Z + dy * t - y;
                var dist = Math.Sqrt(px * px + py * py);
                if (dist < best)
                {
                    best = dist;
                    bestAlong = run + t * len;
                }
                run += len;
            }
            return (bestAlong, best);
        }

        public override string ToString() => $"{Start} - {End}{(Capsule != null ? " capsule " + Capsule : "")}{(Badges.Count > 0 ? " [" + string.Join(", ", Badges) + "]" : "")}";
    }

    /// <summary>
    /// A page's schematic read as track: its switches, and the runs of track
    /// between them. The game draws a page from 50-unit tiles; each track
    /// tile joins the middles of its cell's edges (a straight: bottom to top;
    /// a diagonal: bottom to right; a switch: points at the bottom, through
    /// to the top, diverging to one side), turned by its rotation. A badge or
    /// an icon may stand in a track's cell, with the track drawn up to it from
    /// both sides: the track runs on under it.
    /// </summary>
    public sealed class Schematic
    {
        private const double Half = 25;

        /// <summary>How far a switch's dot stands from its cell's middle toward its points' edge, as a share of the way (measured on the game's own drawing).</summary>
        private const double DotToward = 0.6;

        // Directions, at rotation 0: north (up), east, south, west.
        private const int N = 0, E = 1, S = 2, W = 3;
        private static readonly (double X, double Y)[] Dirs = { (0, 1), (1, 0), (0, -1), (-1, 0) };

        private enum PieceKind : byte { Pass, Switch, Buffer, Exit, BadgeEnd }

        private sealed class Shape
        {
            public PieceKind Kind;
            public (int A, int B)[] Links;
            public int[] Ends;
            public bool Capsule;
            public bool SpreadSwitch;
            public bool DivergesLeft;
        }

        private static readonly Dictionary<string, Shape> Shapes = MakeShapes();

        private static Dictionary<string, Shape> MakeShapes()
        {
            var d = new Dictionary<string, Shape>(StringComparer.Ordinal)
            {
                ["SM-TracksStraight"] = new Shape { Kind = PieceKind.Pass, Links = new[] { (S, N) } },
                ["SM-TracksDiagonal"] = new Shape { Kind = PieceKind.Pass, Links = new[] { (S, E) } },
                ["SM-TracksCrossover"] = new Shape { Kind = PieceKind.Pass, Links = new[] { (S, N), (W, E) } },
                ["SM-TracksSwitchDivergingL"] = new Shape { Kind = PieceKind.Switch, Links = new[] { (S, N), (S, W) }, DivergesLeft = true },
                ["SM-TracksSwitchDivergingR"] = new Shape { Kind = PieceKind.Switch, Links = new[] { (S, N), (S, E) } },
                ["SM-TracksSwitchSpread"] = new Shape { Kind = PieceKind.Switch, Links = new[] { (S, W), (S, E) }, SpreadSwitch = true },
                ["SM-TracksEnd"] = new Shape { Kind = PieceKind.Buffer, Links = new (int, int)[0], Ends = new[] { S } },
                ["SM-TracksExit"] = new Shape { Kind = PieceKind.Exit, Links = new (int, int)[0], Ends = new[] { S } },
            };
            foreach (var k in new[] { "Freight", "Passengers", "Military" })
            {
                d[$"SM-TracksYard{k}Loop"] = new Shape { Kind = PieceKind.Pass, Links = new[] { (S, N) }, Capsule = true };
                d[$"SM-TracksYard{k}End"] = new Shape { Kind = PieceKind.Pass, Links = new[] { (S, N) }, Capsule = true };
            }
            foreach (var s in d.Values)
                if (s.Ends == null)
                    s.Ends = s.Links.SelectMany(l => new[] { l.A, l.B }).Distinct().ToArray();
            return d;
        }

        // Pieces that never stand on a track: the compass, the office's arrow, the switch's red marker.
        private static readonly HashSet<string> Decoration = new HashSet<string>(StringComparer.Ordinal)
        {
            "SM-Compass", "SM-CompassOffice", "SM-CompassOfficeArrow", "SM-Switch", "SM-Water",
        };

        private sealed class Piece
        {
            public PieceKind Kind;
            public double X, Y;
            public MapPoint[] EndPoints = new MapPoint[4];
            public int[] Ends;
            public (int A, int B)[] Links;
            public bool Capsule;
            public int Switch = -1;
            public SchematicBadge Badge;
            public SwitchLeg[] Legs;
            /// <summary>Where the track meets in the piece: its middle, or a switch's dot.</summary>
            public MapPoint Point;
        }

        private Schematic(List<DrawnSwitch> switches, List<SchematicRun> runs, PageBox box)
        {
            Switches = switches;
            Runs = runs;
            Box = box;
        }

        public IReadOnlyList<DrawnSwitch> Switches { get; }
        public IReadOnlyList<SchematicRun> Runs { get; }

        /// <summary>What the page's track takes: the box around its runs, schematic units, y up.</summary>
        public PageBox Box { get; }

        /// <summary>Whether a sprite is one of the track's own pieces (a straight, a switch, a capsule).</summary>
        public static bool IsTrack(string sprite) => sprite != null && Shapes.ContainsKey(sprite);

        public static Schematic Trace(SchematicSource source)
        {
            var pieces = new List<Piece>();
            var switches = new List<DrawnSwitch>();
            // Some pages draw a piece twice, one on the other: once is enough.
            var drawnOnce = new HashSet<(string, long, long, long, long, long)>();
            foreach (var t in source.Tiles)
            {
                if (!Shapes.TryGetValue(t.Sprite, out var shape))
                    continue;
                var rot = (long)Math.Round(((t.Rotation % 360) + 360) % 360);
                if (!drawnOnce.Add((t.Sprite, (long)Math.Round(t.X), (long)Math.Round(t.Y), rot, (long)Math.Round(t.Width), (long)Math.Round(t.Height))))
                    continue;
                var p = new Piece { Kind = shape.Kind, X = t.X, Y = t.Y, Ends = shape.Ends, Links = shape.Links, Capsule = shape.Capsule, Point = new MapPoint(t.X, t.Y) };
                var a = t.Rotation * Math.PI / 180;
                double c = Math.Cos(a), s = Math.Sin(a);
                for (var d = 0; d < 4; d++)
                {
                    double lx = Dirs[d].X * t.Width / 2, ly = Dirs[d].Y * t.Height / 2;
                    p.EndPoints[d] = new MapPoint(t.X + lx * c - ly * s, t.Y + lx * s + ly * c);
                }
                if (shape.Kind == PieceKind.Switch)
                {
                    var mirrored = t.Width * t.Height < 0;
                    p.Legs = new SwitchLeg[4];
                    var dirs = new MapPoint[5];
                    // The sprite's dot, where the legs meet, is 0.6 of the way from the cell's middle to its points' edge.
                    var stem = p.EndPoints[S];
                    p.Point = new MapPoint(t.X + (stem.X - t.X) * DotToward, t.Y + (stem.Z - t.Y) * DotToward);
                    MapPoint Unit(int d)
                    {
                        var e = p.EndPoints[d];
                        double dx = e.X - p.Point.X, dy = e.Z - p.Point.Z, len = Math.Sqrt(dx * dx + dy * dy);
                        return len > 0 ? new MapPoint(dx / len, dy / len) : new MapPoint(0, 0);
                    }
                    p.Legs[S] = SwitchLeg.Stem;
                    dirs[(int)SwitchLeg.Stem] = Unit(S);
                    if (shape.SpreadSwitch)
                    {
                        // Seen from the stem (south), west is the left leg; a mirrored piece swaps them.
                        p.Legs[W] = mirrored ? SwitchLeg.Right : SwitchLeg.Left;
                        p.Legs[E] = mirrored ? SwitchLeg.Left : SwitchLeg.Right;
                        dirs[(int)p.Legs[W]] = Unit(W);
                        dirs[(int)p.Legs[E]] = Unit(E);
                    }
                    else
                    {
                        p.Legs[N] = SwitchLeg.Through;
                        var side = shape.DivergesLeft ? W : E;
                        p.Legs[side] = SwitchLeg.Diverging;
                        dirs[(int)SwitchLeg.Through] = Unit(N);
                        dirs[(int)SwitchLeg.Diverging] = Unit(side);
                    }
                    p.Switch = switches.Count;
                    switches.Add(new DrawnSwitch(t.X, t.Y, p.Point, -1, shape.SpreadSwitch, shape.DivergesLeft ^ mirrored, dirs));
                }
                pieces.Add(p);
            }
            // The touch cells name the switches: each cell's middle is its switch piece's.
            for (var i = 0; i < switches.Count; i++)
            {
                var sw = switches[i];
                var best = -1;
                var bestGap = 10.0;
                foreach (var cell in source.Switches)
                {
                    var gap = Math.Sqrt((cell.X - sw.X) * (cell.X - sw.X) + (cell.Y - sw.Y) * (cell.Y - sw.Y));
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = cell.Junction;
                    }
                }
                if (best >= 0)
                    switches[i] = new DrawnSwitch(sw.X, sw.Y, sw.Point, best, sw.Spread, sw.DivergesLeft, sw.LegDirections);
            }

            var at = Join(pieces);
            AddBridges(source, pieces, at);
            at = Join(pieces);

            var runs = new List<SchematicRun>();
            var seen = new HashSet<(int, int)>();
            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.Kind == PieceKind.Pass)
                    continue;
                foreach (var d in p.Ends)
                {
                    if (seen.Contains((i, d)))
                        continue;
                    var run = Walk(pieces, at, i, d, out var last);
                    seen.Add((i, d));
                    if (last.Piece >= 0)
                        seen.Add((last.Piece, last.Dir));
                    runs.Add(run);
                }
            }
            PageBox? box = null;
            foreach (var r in runs)
                foreach (var pt in r.Points)
                {
                    var b = new PageBox(pt.X, pt.Z, pt.X, pt.Z);
                    box = box.HasValue ? box.Value.Union(b) : b;
                }
            return new Schematic(switches, runs, box ?? new PageBox(0, 0, 0, 0));
        }

        private static (long, long) Key(MapPoint p) => ((long)Math.Round(p.X), (long)Math.Round(p.Z));

        private static Dictionary<(long, long), List<(int Piece, int Dir)>> Join(List<Piece> pieces)
        {
            var at = new Dictionary<(long, long), List<(int, int)>>();
            for (var i = 0; i < pieces.Count; i++)
            {
                foreach (var d in pieces[i].Ends)
                {
                    var k = Key(pieces[i].EndPoints[d]);
                    if (!at.TryGetValue(k, out var list))
                        at[k] = list = new List<(int, int)>();
                    list.Add((i, d));
                }
            }
            return at;
        }

        /// <summary>
        /// Badges and icons stand in a track's cell, the track drawn up to the
        /// cell from both sides (a yard's number badges, a capsule's letter, a
        /// loading area's hatching): the cell carries the track across. A cell
        /// a track comes to from one side only ends it there.
        /// </summary>
        private static void AddBridges(SchematicSource source, List<Piece> pieces, Dictionary<(long, long), List<(int Piece, int Dir)>> at)
        {
            var cells = new Dictionary<(long, long), SchematicBadge>();
            var taken = new HashSet<(long, long)>(pieces.Select(p => Key(new MapPoint(p.X, p.Y))));
            foreach (var b in source.Badges)
            {
                var k = Key(new MapPoint(b.X, b.Y));
                if (!taken.Contains(k))
                    cells[k] = b;
            }
            foreach (var t in source.Tiles)
            {
                if (Shapes.ContainsKey(t.Sprite) || Decoration.Contains(t.Sprite) || Math.Abs(Math.Abs(t.Width) - 2 * Half) > 1)
                    continue;
                var k = Key(new MapPoint(t.X, t.Y));
                if (!taken.Contains(k) && !cells.ContainsKey(k))
                    cells[k] = null;
            }
            bool Dangling((long, long) k) => at.TryGetValue(k, out var l) && l.Count == 1;
            (long, long) Step((long X, long Y) cell, int d, double by) => ((long)Math.Round(cell.X + Dirs[d].X * by), (long)Math.Round(cell.Y + Dirs[d].Y * by));
            // Whether, going one way from a cell through any badges in line, the track comes up to them.
            bool Reaches((long, long) cell, int d)
            {
                for (var i = 0; i < 64; i++)
                {
                    if (Dangling(Step(cell, d, Half)))
                        return true;
                    cell = Step(cell, d, 2 * Half);
                    if (!cells.ContainsKey(cell))
                        return false;
                }
                return false;
            }
            foreach (var kv in cells)
            {
                var c = kv.Key;
                var links = new List<(int, int)>();
                if (Reaches(c, E) && Reaches(c, W))
                    links.Add((E, W));
                if (Reaches(c, N) && Reaches(c, S))
                    links.Add((N, S));
                var direct = Enumerable.Range(0, 4).Where(d => Dangling(Step(c, d, Half))).ToArray();
                var kind = PieceKind.Pass;
                if (links.Count == 0 && direct.Length == 2)
                    links.Add((direct[0], direct[1]));
                else if (links.Count == 0 && direct.Length == 1)
                    kind = PieceKind.BadgeEnd;
                else if (links.Count == 0)
                    continue;
                var live = kind == PieceKind.BadgeEnd ? direct : new int[0];
                var p = new Piece
                {
                    Kind = kind, X = c.Item1, Y = c.Item2, Badge = kv.Value, Point = new MapPoint(c.Item1, c.Item2),
                    Links = links.ToArray(),
                    Ends = kind == PieceKind.BadgeEnd ? new[] { live[0] } : links.SelectMany(l => new[] { l.Item1, l.Item2 }).ToArray(),
                };
                for (var d = 0; d < 4; d++)
                    p.EndPoints[d] = new MapPoint(c.Item1 + Dirs[d].X * Half, c.Item2 + Dirs[d].Y * Half);
                pieces.Add(p);
            }
        }

        private static SchematicRun Walk(List<Piece> pieces, Dictionary<(long, long), List<(int Piece, int Dir)>> at, int from, int dir, out (int Piece, int Dir) last)
        {
            var start = pieces[from];
            var points = new List<MapPoint> { start.Point, start.EndPoints[dir] };
            var badges = new List<SchematicBadge>();
            if (start.Badge != null)
                badges.Add(start.Badge);
            string capsule = null;
            var capsuleRun = false;
            int cur = from, d = dir;
            RunEnd end = new RunEnd(RunEndKind.Open);
            last = (-1, -1);
            var visited = new HashSet<(int, int)>();
            for (var step = 0; step < 100000; step++)
            {
                if (!visited.Add((cur, d)))
                    break;
                var here = Key(pieces[cur].EndPoints[d]);
                var next = at[here].Where(x => x.Piece != cur).ToList();
                if (next.Count == 0)
                    break;
                // Where lines cross at a point, the one that carries on the way this one comes.
                var (np, nd) = next.Count == 1 ? next[0] : next.OrderByDescending(x => Onward(pieces, points, x.Piece, x.Dir)).First();
                var piece = pieces[np];
                if (piece.Badge != null && !badges.Contains(piece.Badge))
                    badges.Add(piece.Badge);
                if (piece.Kind != PieceKind.Pass)
                {
                    points.Add(piece.Kind == PieceKind.Exit ? piece.EndPoints[(nd + 2) % 4] : piece.Point);
                    last = (np, nd);
                    end = piece.Kind switch
                    {
                        PieceKind.Switch => new RunEnd(RunEndKind.Switch, piece.Switch, piece.Legs[nd]),
                        PieceKind.Buffer => new RunEnd(RunEndKind.Buffer),
                        PieceKind.Exit => new RunEnd(RunEndKind.Exit),
                        _ => new RunEnd(RunEndKind.Badge),
                    };
                    break;
                }
                var link = Array.FindIndex(piece.Links, l => l.A == nd || l.B == nd);
                if (link < 0)
                    break;
                var od = piece.Links[link].A == nd ? piece.Links[link].B : piece.Links[link].A;
                capsuleRun |= piece.Capsule;
                points.Add(piece.EndPoints[od]);
                cur = np;
                d = od;
            }
            var startEnd = start.Kind switch
            {
                PieceKind.Switch => new RunEnd(RunEndKind.Switch, start.Switch, start.Legs[dir]),
                PieceKind.Buffer => new RunEnd(RunEndKind.Buffer),
                PieceKind.Exit => new RunEnd(RunEndKind.Exit),
                _ => new RunEnd(RunEndKind.Badge),
            };
            if (start.Kind == PieceKind.Exit)
                points[0] = start.EndPoints[(dir + 2) % 4];
            if (capsuleRun)
                capsule = badges.Select(b => b.Text).FirstOrDefault(t => t.Length > 0 && char.IsLetter(t[0])) ?? "";
            return new SchematicRun(startEnd, end, Dedupe(points), capsule, badges);
        }

        /// <summary>How nearly a piece, entered by an end, carries on the line's last way: the cosine between them.</summary>
        private static double Onward(List<Piece> pieces, List<MapPoint> points, int piece, int end)
        {
            var p = pieces[piece];
            var a = points[points.Count - 2];
            var b = points[points.Count - 1];
            MapPoint to;
            var link = p.Kind == PieceKind.Pass ? Array.FindIndex(p.Links, l => l.A == end || l.B == end) : -1;
            if (link >= 0)
                to = p.EndPoints[p.Links[link].A == end ? p.Links[link].B : p.Links[link].A];
            else
                to = p.Point;
            double ux = b.X - a.X, uy = b.Z - a.Z, vx = to.X - b.X, vy = to.Z - b.Z;
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            return lu > 0 && lv > 0 ? (ux * vx + uy * vy) / (lu * lv) : -1;
        }

        private static MapPoint[] Dedupe(List<MapPoint> points)
        {
            var list = new List<MapPoint>(points.Count);
            foreach (var p in points)
                if (list.Count == 0 || list[list.Count - 1].DistanceTo(p) > 1e-6)
                    list.Add(p);
            return list.ToArray();
        }
    }
}
