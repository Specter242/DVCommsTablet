using System;
using System.Text.RegularExpressions;

namespace CommsTablet.Core.Booklet
{
    /// <summary>
    /// The game's name for a yard track: "[Y]_[SM]_[A-04-S]" is the Steel
    /// Mill's A yard, track 4, of kind S (storage). The booklet and the job
    /// booklets show it as "SM-A4S".
    /// </summary>
    public readonly struct YardTrackName
    {
        private static readonly Regex Pattern = new Regex(@"^\[Y\]_\[(\w+)\]_\[(\w+)-(\d+)-(\w+)\]$", RegexOptions.CultureInvariant);

        public readonly string Station;
        public readonly string Yard;
        public readonly int Number;
        public readonly string Kind;

        public YardTrackName(string station, string yard, int number, string kind)
        {
            Station = station;
            Yard = yard;
            Number = number;
            Kind = kind;
        }

        public static bool TryParse(string name, out YardTrackName parsed)
        {
            parsed = default;
            if (string.IsNullOrEmpty(name))
                return false;
            var m = Pattern.Match(name);
            if (!m.Success)
                return false;
            parsed = new YardTrackName(m.Groups[1].Value, m.Groups[2].Value, int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[4].Value);
            return true;
        }

        /// <summary>As the booklet shows it: "SM-A4S".</summary>
        public string Display => $"{Station}-{Yard}{Number}{Kind}";

        /// <summary>The part after the station: "A4S".</summary>
        public string Short => $"{Yard}{Number}{Kind}";

        public override string ToString() => Display;
    }

    /// <summary>A box on the map or on a page: left, top, right, bottom (top less than bottom).</summary>
    public readonly struct PageBox
    {
        public readonly double Left;
        public readonly double Top;
        public readonly double Right;
        public readonly double Bottom;

        public PageBox(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public double Width => Right - Left;

        public double Height => Bottom - Top;

        public PageBox Union(PageBox other) => new PageBox(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

        public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

        public override string ToString() => $"[{Left:F0}, {Top:F0}] - [{Right:F0}, {Bottom:F0}]";
    }

    public enum PageKind
    {
        /// <summary>The route map: the whole network, a badge for each station.</summary>
        Route,

        /// <summary>A station's overview (the booklet's page A): its tracks, a capsule for each yard.</summary>
        Station,

        /// <summary>One of a station's yards (pages B on).</summary>
        Yard,
    }

    /// <summary>Which page of the booklet: the route map, a station's overview, or one of its yards.</summary>
    public readonly struct PageRef : IEquatable<PageRef>
    {
        public readonly PageKind Kind;
        public readonly string Station;
        public readonly string Yard;

        private PageRef(PageKind kind, string station, string yard)
        {
            Kind = kind;
            Station = station;
            Yard = yard;
        }

        public static PageRef Route => new PageRef(PageKind.Route, null, null);

        public static PageRef Overview(string station) => new PageRef(PageKind.Station, station, null);

        public static PageRef OfYard(string station, string yard) => new PageRef(PageKind.Yard, station, yard);

        /// <summary>The page a level up: a yard's station overview; a station's route map.</summary>
        public PageRef Up => Kind == PageKind.Yard ? Overview(Station) : Route;

        public bool Equals(PageRef other) => Kind == other.Kind && Station == other.Station && Yard == other.Yard;

        public override bool Equals(object obj) => obj is PageRef p && Equals(p);

        public override int GetHashCode() => ((int)Kind * 397) ^ (Station?.GetHashCode() ?? 0) ^ ((Yard?.GetHashCode() ?? 0) * 31);

        public override string ToString() => Kind == PageKind.Route ? "route map" : Kind == PageKind.Station ? $"{Station} overview" : $"{Station} {Yard}";
    }

    /// <summary>
    /// How a page is shown in the map's area: the page point at its middle,
    /// and the screen pixels a page unit takes. A page opens fitted to its
    /// area and can be zoomed in and moved, never zoomed out past its fit nor
    /// moved off its own content.
    /// </summary>
    public readonly struct PageView
    {
        public readonly double CenterX;
        public readonly double CenterY;
        public readonly double Scale;

        public PageView(double centerX, double centerY, double scale)
        {
            CenterX = centerX;
            CenterY = centerY;
            Scale = scale;
        }

        /// <summary>The most a page is zoomed in: four screen pixels a page unit.</summary>
        public const double MaxScale = 4;

        /// <summary>
        /// The view that fits <paramref name="box"/> into <paramref name="area"/>
        /// (pixels of a map <paramref name="mapWidth"/> by <paramref name="mapHeight"/>:
        /// the part of it not under the tabs, the column, or the bottom strip),
        /// keeping <paramref name="right"/> pixels for name plates and
        /// <paramref name="left"/> for capsules, at most <paramref name="cap"/>
        /// pixels a unit. Never under <paramref name="floor"/> (a yard's rows at
        /// the 16 mm floor): a page too long for it opens at that size from its
        /// left end.
        /// </summary>
        public static PageView Fit(PageBox box, PageBox area, double mapWidth, double mapHeight, double right, double left, double cap, double floor = 0)
        {
            double bw = area.Width - right - left, bh = area.Height;
            double w = Math.Max(1e-6, box.Width), h = Math.Max(1e-6, box.Height);
            double mx = mapWidth / 2, my = mapHeight / 2;
            var s = Math.Min(cap, Math.Min(bw / w, bh / h));
            if (s < floor)
            {
                s = floor;
                return new PageView(box.Left + (mx - (area.Left + left + 8)) / s, (box.Top + box.Bottom) / 2 + (my - (area.Top + area.Bottom) / 2) / s, s);
            }
            double x0 = area.Left + left + (bw - s * w) / 2, y0 = area.Top + (bh - s * h) / 2;
            return new PageView(box.Left + (mx - x0) / s, box.Top + (my - y0) / s, s);
        }

        /// <summary>This view kept on its page: no smaller than <paramref name="fit"/>, its middle over the page's content.</summary>
        public PageView ClampTo(PageView fit, PageBox box)
        {
            var s = Math.Min(MaxScale, Math.Max(fit.Scale, Scale));
            double lx = Math.Min(box.Left, fit.CenterX), hx = Math.Max(box.Right, fit.CenterX);
            double ly = Math.Min(box.Top, fit.CenterY), hy = Math.Max(box.Bottom, fit.CenterY);
            return new PageView(Math.Max(lx, Math.Min(hx, CenterX)), Math.Max(ly, Math.Min(hy, CenterY)), s);
        }

        /// <summary>A page point on the screen, for an area whose middle is (<paramref name="mx"/>, <paramref name="my"/>).</summary>
        public (double X, double Y) ToScreen(double x, double y, double mx, double my) => ((x - CenterX) * Scale + mx, (y - CenterY) * Scale + my);

        public (double X, double Y) ToPage(double sx, double sy, double mx, double my) => ((sx - mx) / Scale + CenterX, (sy - my) / Scale + CenterY);

        public PageView Zoomed(double factor) => new PageView(CenterX, CenterY, Scale * factor);

        public PageView Moved(double dxPixels, double dyPixels) => new PageView(CenterX + dxPixels / Scale, CenterY + dyPixels / Scale, Scale);

        public override string ToString() => $"({CenterX:F0}, {CenterY:F0}) x{Scale:F3}";
    }
}
