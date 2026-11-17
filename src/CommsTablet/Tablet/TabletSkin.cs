using System.Collections.Generic;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>How a plate, a box, or a line of text looks: its nine-sliced background, and its text.</summary>
    internal sealed class Look
    {
        public Sprite Sprite;
        public Color Tint = Color.white;
        public Color Text;
        public int Size;
        public bool Bold;
        public TextAnchor Align = TextAnchor.MiddleCenter;
        public bool Wrap;
        public float PadLeft = 4, PadRight = 4;
        /// <summary>The smallest size best fit may shrink the text to.</summary>
        public int MinSize = 12;

        /// <summary>This look with its text in another color.</summary>
        public Look Tinted(Color text)
        {
            var l = (Look)MemberwiseClone();
            l.Text = text;
            return l;
        }

        /// <summary>This look at another size, never shrunk below <paramref name="min"/>.</summary>
        public Look Sized(int size, int min)
        {
            var l = (Look)MemberwiseClone();
            l.Size = size;
            l.MinSize = min;
            return l;
        }

        public Look With(TextAnchor align, bool? bold = null)
        {
            var l = (Look)MemberwiseClone();
            l.Align = align;
            if (bold.HasValue)
                l.Bold = bold.Value;
            return l;
        }
    }

    /// <summary>
    /// The looks and drawing helpers of the pages that draw themselves (the
    /// map) on the screen's immediate-mode layer (<see cref="TabletSurface"/>):
    /// AI Engineer's <c>TabletSkin</c> (c6e6207,
    /// src/AIEngineer/UI/Tablet/TabletSkin.cs), its looks, plates, and
    /// helpers kept and its job colors and order plates left out. Its palette
    /// and plate textures are the screen's own (<see cref="TabletArt"/>,
    /// itself a copy of that palette), so the two layers look alike. Every
    /// size is in canvas pixels of the 1080 by 924 screen.
    /// </summary>
    internal static class TabletSkin
    {
        public const float W = TabletScreen.Width, H = TabletScreen.Height;

        public static readonly Color Cream = TabletArt.Text, Soft = TabletArt.Soft, Dim = TabletArt.Dim, Brass = TabletArt.Brass, Brass2 = TabletArt.Amber;
        public static readonly Color Ok = TabletArt.Ok, Ink = TabletArt.Ink;

        public static Look Plate, PlateOn, Here, Chip, ChipOn, Text, Small, SoftText, Brass2Text, Section, Big, Card, CardOn, Well, Strip, Panel, ScreenBg, Empty, Invisible;

        public static Sprite White, Disc;
        public static Font Font;

        private static bool built;

        public static void Build()
        {
            if (built)
                return;
            built = true;
            Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            White = Sliced(TabletArt.White, 0);
            Disc = Sliced(DiscTexture(64), 0);
            var plate = Sliced(TabletArt.PlateOff, 10);
            var on = Sliced(TabletArt.PlateOn, 10);
            var here = Sliced(TabletArt.Here, 10);
            var chip = Sliced(TabletArt.Chip, 10);
            var chipOn = Sliced(TabletArt.ChipOn, 10);
            var card = Sliced(TabletArt.Card, 10);
            var cardOn = Sliced(TabletArt.CardOn, 10);
            var well = Sliced(TabletArt.Well, 10);
            var panel = Sliced(TabletArt.Panel, 10);
            var screen = Sliced(TabletArt.ScreenBg, 10);

            Look Button(Sprite bg, Color text, int size, bool bold = false) => new Look { Sprite = bg, Text = text, Size = size, Bold = bold, Align = TextAnchor.MiddleCenter };
            Plate = Button(plate, Cream, 27);
            PlateOn = Button(on, Brass2, 27, true);
            Here = Button(here, Brass2, 27, true);
            Chip = Button(chip, Cream, 26);
            Chip.Align = TextAnchor.MiddleLeft;
            Chip.PadLeft = 40;
            ChipOn = Button(chipOn, Cream, 26);
            ChipOn.Align = TextAnchor.MiddleLeft;
            ChipOn.PadLeft = 40;

            Look Label(Color c, int size, bool bold = false) => new Look { Text = c, Size = size, Bold = bold, Align = TextAnchor.MiddleLeft, PadLeft = 0, PadRight = 0 };
            Text = Label(Cream, 28);
            Small = Label(Cream, 25);
            SoftText = Label(Soft, 26);
            Brass2Text = Label(Brass2, 26);
            Section = Label(Soft, 26, true);
            Big = Label(Cream, 32, true);
            Empty = Label(Soft, 26);
            Empty.Wrap = true;
            Empty.Align = TextAnchor.UpperLeft;

            Look Box(Sprite bg) => new Look { Sprite = bg };
            Card = Box(card);
            CardOn = Box(cardOn);
            Well = Box(well);
            Strip = Box(well);
            Panel = Box(panel);
            ScreenBg = Box(screen);
            Invisible = new Look();
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        private static Sprite Sliced(Texture2D t, int border)
        {
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static Texture2D DiscTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // ---- Drawing, onto the screen being drawn; all in canvas pixels ----

        public static void Fill(Rect r, Color c) => TabletSurface.Current?.Fill(r, c);

        public static void Lamp(float x, float y, float size, Color c) => TabletSurface.Current?.Lamp(new Rect(x, y, size, size), c);

        public static void Box(Rect r, Look look) => TabletSurface.Current?.Box(r, look);

        public static void Label(Rect r, string text, Look look) => TabletSurface.Current?.Label(r, text, look);

        public static void Picture(Rect r, Texture texture) => TabletSurface.Current?.Picture(r, texture);

        /// <summary>A plate: true when it was pressed. A disabled plate is drawn faint and takes no press.</summary>
        public static bool Button(Rect r, string text, Look look, bool enabled = true) => TabletSurface.Current != null && TabletSurface.Current.Button(r, text, look, enabled);

        /// <summary>After a press that changes what the page shows: the pass starts again on what it is now.</summary>
        public static void Changed() => throw new TabletSurface.Redraw();

        /// <summary>Two lines on a plate: a bold first, a smaller soft second.</summary>
        public static string Two(string top, string bottom) => $"<b>{top}</b>\n<size=22><color=#{ColorUtility.ToHtmlStringRGB(Soft)}>{bottom}</color></size>";

        public static string Tint(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        private static readonly Dictionary<Look, Look> centered = new Dictionary<Look, Look>();

        /// <summary>A look's text centred and bold, for numbers by a mark.</summary>
        public static Look Centered(Look look)
        {
            if (!centered.TryGetValue(look, out var c))
                centered[look] = c = look.With(TextAnchor.MiddleCenter, true);
            return c;
        }
    }
}
