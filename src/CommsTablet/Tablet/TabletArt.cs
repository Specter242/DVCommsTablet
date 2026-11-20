using System;
using System.Linq;
using UnityEngine;

namespace CommsTablet.Tablet
{
 internal static class TabletArt
    {
        public static readonly Color Screen = Hex(0x100e0c);
        public static readonly Color Text = Hex(0xefe6cf);
        public static readonly Color Soft = Hex(0xb8ab92);
        public static readonly Color Dim = Hex(0x8f8574);
        public static readonly Color Brass = Hex(0xc9a24a);
        public static readonly Color Amber = Hex(0xe0bf6a);
        public static readonly Color Ok = Hex(0x9fd36a);
        public static readonly Color Ink = Hex(0x1d160c);
        /// <summary>A tile's fill behind its side view (the middle of <see cref="Card"/>).</summary>
        public static readonly Color Enamel = Hex(0x17130f);

        private static Texture2D white, plateOff, plateOn, here, chip, chipOn, card, cardOn, panel, well, screenBg, arrow, badge, shadow, glow;
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> glyphs = new System.Collections.Generic.Dictionary<string, Texture2D>();

        public static Texture2D White => Live(ref white, () => Solid(Color.white));

        /// <summary>A plate to press.</summary>
        public static Texture2D PlateOff => Live(ref plateOff, () => Rounded(Hex(0x3a332b), Hex(0x2a251f), Hex(0x5a4a33), 6));

        /// <summary>A lit plate: the open tab, the chosen tile.</summary>
        public static Texture2D PlateOn => Live(ref plateOn, () => Rounded(Hex(0x57462b), Hex(0x3d311f), Amber, 6));

        /// <summary>Where the player is: a dark plate with a brass bar down its left.</summary>
        public static Texture2D Here => Live(ref here, () => Rounded(Hex(0x2a2116), Hex(0x2a2116), Amber, 6, Brass));

        public static Texture2D Chip => Live(ref chip, () => Rounded(Hex(0x14110e), Hex(0x14110e), Hex(0x4b3a29), 6));

        public static Texture2D ChipOn => Live(ref chipOn, () => Rounded(Hex(0x2a2116), Hex(0x2a2116), Amber, 6, Brass));

        public static Texture2D Card => Live(ref card, () => Rounded(Hex(0x1a1612), Hex(0x14110e), Hex(0x4b3a29), 8));

        public static Texture2D CardOn => Live(ref cardOn, () => Rounded(Hex(0x2a2116), Hex(0x1a150e), Amber, 8));

        public static Texture2D Panel => Live(ref panel, () => Rounded(Hex(0x100e0c), Hex(0x100e0c), Hex(0x5a4a33), 6));

        public static Texture2D Well => Live(ref well, () => Rounded(Hex(0x0b0a09), Hex(0x0b0a09), Hex(0x3a3027), 6));

        /// <summary>The screen's own background, a brass hairline round it.</summary>
        public static Texture2D ScreenBg => Live(ref screenBg, () => Rounded(Hex(0x171412), Hex(0x100e0c), Hex(0x4d4130), 5));

        /// <summary>A white triangle pointing right; mirror it for left.</summary>
        public static Texture2D Arrow => Live(ref arrow, () => Triangle(32));

        /// <summary>
        /// An app icon's badge: a round plate of the tablet's smoked iron,
        /// lit from above, in a brass ring, as Android draws its round icons;
        /// the app's symbol goes on it in brass.
        /// </summary>
        public static Texture2D Badge => Live(ref badge, () => BadgeTexture(176));

        /// <summary>The badge's soft shadow.</summary>
        public static Texture2D Shadow => Live(ref shadow, () => ShadowTexture(176));

        /// <summary>A soft white halo for a tile that is on, tinted when drawn.</summary>
        public static Texture2D Glow => Live(ref glow, () => GlowTexture(176));

        /// <summary>An app's symbol in white on clear, drawn once: see <see cref="Shapes"/>.</summary>
        public static Texture2D Glyph(string key)
        {
            if (glyphs.TryGetValue(key ?? "", out var glyph) && glyph != null)
                return glyph;
            glyph = DrawGlyph(Shapes(key), 192);
            glyph.name = "CommsTablet_Glyph_" + key;
            glyph.hideFlags = HideFlags.HideAndDontSave;
            glyphs[key ?? ""] = glyph;
            return glyph;
        }

        // ---- App symbols -----------------------------------------------------
        // Each symbol is a few shapes in a unit square, y up, drawn by their
        // distance fields: strokes, rings and arcs, discs, and boxes.

        private delegate float Shape(float x, float y);

        private static Shape Line(float ax, float ay, float bx, float by, float width) => (x, y) =>
        {
            float dx = bx - ax, dy = by - ay;
            var t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float cx = ax + t * dx - x, cy = ay + t * dy - y;
            return Mathf.Sqrt(cx * cx + cy * cy) - width * 0.5f;
        };

        private static Shape[] Path(float width, params float[] points)
        {
            var lines = new Shape[points.Length / 2 - 1];
            for (var i = 0; i < lines.Length; i++)
                lines[i] = Line(points[i * 2], points[i * 2 + 1], points[i * 2 + 2], points[i * 2 + 3], width);
            return lines;
        }

        private static Shape Disc(float cx, float cy, float r) => (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        /// <summary>A ring, or with angles (degrees, counterclockwise from +x) an arc with round ends.</summary>
        private static Shape Arc(float cx, float cy, float r, float width, float from = 0f, float to = 360f) => (x, y) =>
        {
            var angle = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg;
            var a = Mathf.Repeat(angle - from, 360f);
            if (a <= to - from)
                return Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - width * 0.5f;
            float ex = cx + r * Mathf.Cos(from * Mathf.Deg2Rad), ey = cy + r * Mathf.Sin(from * Mathf.Deg2Rad);
            float fx = cx + r * Mathf.Cos(to * Mathf.Deg2Rad), fy = cy + r * Mathf.Sin(to * Mathf.Deg2Rad);
            return Mathf.Min(Mathf.Sqrt((x - ex) * (x - ex) + (y - ey) * (y - ey)), Mathf.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy))) - width * 0.5f;
        };

        private static Shape Box(float cx, float cy, float halfWidth, float halfHeight) => (x, y) =>
        {
            float qx = Mathf.Abs(x - cx) - halfWidth, qy = Mathf.Abs(y - cy) - halfHeight;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f);
        };

        private static Shape RoundBox(float cx, float cy, float halfWidth, float halfHeight, float radius) => (x, y) =>
        {
            float qx = Mathf.Abs(x - cx) - halfWidth + radius, qy = Mathf.Abs(y - cy) - halfHeight + radius;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        };

        private static Shape Outline(Shape shape, float width) => (x, y) => Mathf.Abs(shape(x, y)) - width * 0.5f;

        private static Shape[] Join(params object[] parts)
        {
            var all = new System.Collections.Generic.List<Shape>();
            foreach (var part in parts)
                if (part is Shape shape) all.Add(shape);
                else if (part is Shape[] shapes) all.AddRange(shapes);
            return all.ToArray();
        }

        private static Shape[] Shapes(string key)
        {
            switch (key)
            {
                // The home screen's symbols, filled and chunky like the game's own interface icons
                // (UI_TrainLoco, UI_Item, UI_Mods), which stand in for all but the paint roller when loaded.
                case "vehicles": // a locomotive's front
                    return Join(Outline(RoundBox(0.5f, 0.56f, 0.2f, 0.25f, 0.07f), 0.09f), Line(0.3f, 0.6f, 0.7f, 0.6f, 0.08f),
                        Disc(0.4f, 0.42f, 0.045f), Disc(0.6f, 0.42f, 0.045f), Line(0.38f, 0.27f, 0.31f, 0.17f, 0.09f), Line(0.62f, 0.27f, 0.69f, 0.17f, 0.09f));
                case "cargo": // a box
                    return Join(RoundBox(0.5f, 0.45f, 0.26f, 0.22f, 0.05f), RoundBox(0.5f, 0.75f, 0.3f, 0.07f, 0.04f));
                case "workshop": // a paint roller
                    return Join(RoundBox(0.44f, 0.71f, 0.25f, 0.1f, 0.05f), Path(0.09f, 0.69f, 0.71f, 0.8f, 0.71f, 0.8f, 0.52f, 0.52f, 0.52f, 0.52f, 0.42f),
                        RoundBox(0.52f, 0.27f, 0.07f, 0.14f, 0.04f));
                case "switches": // a route forking in two
                    return Join(Line(0.5f, 0.16f, 0.5f, 0.47f, 0.1f), Line(0.5f, 0.47f, 0.27f, 0.78f, 0.1f), Line(0.5f, 0.47f, 0.73f, 0.78f, 0.1f),
                        Disc(0.27f, 0.79f, 0.09f), Disc(0.73f, 0.79f, 0.09f), Disc(0.5f, 0.17f, 0.09f));
                case "map": // a map folded in three, the middle panel set lower
                    return Join(Path(0.08f, 0.18f, 0.76f, 0.39f, 0.82f, 0.61f, 0.76f, 0.82f, 0.82f, 0.82f, 0.24f, 0.61f, 0.18f, 0.39f, 0.24f, 0.18f, 0.18f, 0.18f, 0.76f),
                        Line(0.39f, 0.8f, 0.39f, 0.26f, 0.07f), Line(0.61f, 0.74f, 0.61f, 0.2f, 0.07f));
                case "ahead": // a climbing line of track, its end an arrow
                    return Join(Path(0.09f, 0.14f, 0.3f, 0.38f, 0.3f, 0.6f, 0.62f, 0.84f, 0.62f), Line(0.7f, 0.76f, 0.84f, 0.62f, 0.09f), Line(0.7f, 0.48f, 0.84f, 0.62f, 0.09f));
                case "light": // a bulb
                    return Join(Disc(0.5f, 0.6f, 0.21f), RoundBox(0.5f, 0.33f, 0.11f, 0.09f, 0.03f), Line(0.42f, 0.2f, 0.58f, 0.2f, 0.07f));
                case "tools": // a wrench
                    return Join(Line(0.24f, 0.24f, 0.56f, 0.56f, 0.13f), Arc(0.66f, 0.66f, 0.13f, 0.11f, 80f, 370f));
                case "settings": // a gear: a thick ring with eight square-ended teeth
                {
                    var teeth = new Shape[8];
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * Mathf.PI / 4f;
                        float cx = 0.5f + Mathf.Cos(a) * 0.3f, cy = 0.5f + Mathf.Sin(a) * 0.3f;
                        teeth[i] = Line(0.5f + Mathf.Cos(a) * 0.24f, 0.5f + Mathf.Sin(a) * 0.24f, cx, cy, 0.11f);
                    }
                    return Join(Arc(0.5f, 0.5f, 0.2f, 0.14f), teeth);
                }
                default:
                    return Join(Arc(0.5f, 0.5f, 0.3f, 0.08f));
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> wheelDiagrams = new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>
        /// A side view of a wheel arrangement from its Whyte groups ("2-8-2" is
        /// 2, 8, 2): carrying wheels small, driving wheels large and coupled,
        /// on a rail under a frame. With <paramref name="allSmall"/>, every
        /// wheel is small (a geared Shay). Null for no groups.
        /// </summary>
        public static Sprite WheelDiagram(int[] groups, bool allSmall = false)
        {
            if (groups == null || groups.Length < 3)
                return null;
            var key = string.Join("-", groups) + (allSmall ? "s" : "");
            if (wheelDiagrams.TryGetValue(key, out var cached) && cached != null)
                return cached;
            const int width = 296, height = 112;
            // Wheels per side: half of each group; the first and last groups carry, the rest drive.
            var wheels = new System.Collections.Generic.List<(float r, int group, bool drives)>();
            for (var g = 0; g < groups.Length; g++)
                for (var i = 0; i < groups[g] / 2; i++)
                    wheels.Add((0f, g, !allSmall && g > 0 && g < groups.Length - 1));
            float big = 17f, small = 10f, gap = 4f, groupGap = 14f;
            float Span() => wheels.Select((w, i) => (w.drives ? big : small) * 2f + (i == 0 ? 0f : wheels[i - 1].group == w.group ? gap : groupGap)).Sum();
            var span = Span();
            if (span > width - 30f)
            {
                var scale = (width - 30f) / span;
                big *= scale; small *= scale; gap *= scale; groupGap *= scale;
                span = Span();
            }
            const float rail = 16f;
            var shapes = new System.Collections.Generic.List<Shape> { Box(width * 0.5f, rail, width * 0.5f - 10f, 2f) };
            var x = (width - span) * 0.5f;
            var centers = new System.Collections.Generic.List<(float x, float y, float r, int group, bool drives)>();
            for (var i = 0; i < wheels.Count; i++)
            {
                var r = wheels[i].drives ? big : small;
                if (i > 0) x += wheels[i - 1].group == wheels[i].group ? gap : groupGap;
                centers.Add((x + r, rail + 2f + r, r, wheels[i].group, wheels[i].drives));
                x += r * 2f;
            }
            foreach (var c in centers)
            {
                shapes.Add(Arc(c.x, c.y, c.r - 2f, 4f));
                shapes.Add(Disc(c.x, c.y, Mathf.Max(2f, c.r * 0.22f)));
            }
            // A coupling rod along each group of drivers.
            foreach (var group in centers.Where(c => c.drives).GroupBy(c => c.group))
            {
                var list = group.ToList();
                if (list.Count > 1)
                    shapes.Add(Line(list[0].x, list[0].y - list[0].r * 0.4f, list[list.Count - 1].x, list[list.Count - 1].y - list[0].r * 0.4f, 4f));
            }
            if (centers.Count > 0)
            {
                var top = centers.Max(c => c.y + c.r) + 7f;
                shapes.Add(Box((centers[0].x - centers[0].r + centers[centers.Count - 1].x + centers[centers.Count - 1].r) * 0.5f, top,
                    (centers[centers.Count - 1].x + centers[centers.Count - 1].r - centers[0].x + centers[0].r) * 0.5f, 3f));
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[width * height];
            for (var py = 0; py < height; py++)
                for (var px = 0; px < width; px++)
                {
                    var d = float.MaxValue;
                    foreach (var shape in shapes)
                        d = Mathf.Min(d, shape(px + 0.5f, py + 0.5f));
                    pixels[py * width + px] = new Color32(238, 231, 214, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.name = "CommsTablet_Wheels_" + key;
            texture.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            wheelDiagrams[key] = sprite;
            return sprite;
        }

        private static Texture2D DrawGlyph(Shape[] shapes, int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    var d = float.MaxValue;
                    foreach (var shape in shapes)
                        d = Mathf.Min(d, shape(u, v));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d * size) * 255f));
                }
            t.SetPixels32(pixels);
            t.Apply();
            return t;
        }

        private static Texture2D Live(ref Texture2D texture, Func<Texture2D> make)
        {
            if (texture == null)
            {
                texture = make();
                texture.hideFlags = HideFlags.HideAndDontSave;
            }
            return texture;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// <summary>A rounded plate, 32 pixels: its fill shaded top to bottom, a one-pixel edge, and optionally a bar down its left (AI Engineer's <c>TabletSkin.Rounded</c>).</summary>
        private static Texture2D Rounded(Color top, Color bottom, Color edge, int radius, Color? bar = null)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, n - radius), cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                    var d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - radius;
                    var fill = Color.Lerp(bottom, top, y / (float)(n - 1));
                    if (bar.HasValue && x < 5)
                        fill = bar.Value;
                    var c = d > -1.5f ? edge : fill;
                    c.a *= Mathf.Clamp01(0.5f - d);
                    px[y * n + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D BadgeTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float r = n * 0.5f - 1f, ring = n * 0.03f;
            Color top = Hex(0x423a30), bottom = Hex(0x1c1814), brassHigh = Amber, brassLow = Hex(0x8a6a2c);
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    var v = y / (float)(n - 1);
                    var c = Color.Lerp(bottom, top, v);
                    // A brass ring, bright where the light falls on it.
                    if (d > -ring)
                        c = Color.Lerp(brassLow, brassHigh, v);
                    else if (d > -ring - 1.5f)
                        c = Color.Lerp(c, Color.black, 0.5f);
                    c.a = Mathf.Clamp01(0.5f - d);
                    px[y * n + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D GlowTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float r = n * 0.36f, soft = n * 0.14f;
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    var a = Mathf.Clamp01(1f - d / soft);
                    px[y * n + x] = new Color(1f, 1f, 1f, 0.85f * a * a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D ShadowTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float r = n * 0.42f, soft = n * 0.08f;
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    px[y * n + x] = new Color(0f, 0f, 0f, 0.5f * Mathf.Clamp01(1f - d / soft));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D Solid(Color color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        private static Texture2D Triangle(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Points (0.2, 0.15), (0.2, 0.85), (0.85, 0.5) in [0, 1].
                    var u = (x + 0.5f) / size;
                    var v = (y + 0.5f) / size;
                    var halfHeight = 0.35f * (0.85f - u) / 0.65f;
                    var inside = Mathf.Min(u - 0.2f, Mathf.Min(halfHeight - Mathf.Abs(v - 0.5f), 0.85f - u)) * size;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(inside + 0.5f));
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }
    }
}
