using System.Collections.Generic;
using System.Linq;
using CommsTablet.Pages;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// An app on the home screen (decision 15): a few pages behind tabs along
    /// the screen's top, so the home screen holds a handful of icons, not one
    /// for each radio mode. Opening an app shows the page last shown in it.
    /// </summary>
    internal sealed class TabletApp
    {
        internal readonly string Name;
        /// <summary>The game's own interface glyph for the icon, by its sprite's name (<see cref="TabletIcons"/>), if it has one.</summary>
        internal readonly string Art;
        /// <summary>The drawn symbol, in the same style, where the game has none or it is not loaded (<see cref="TabletArt.Glyph"/>).</summary>
        internal readonly string Glyph;
        internal readonly TabletPage[] Pages;

        /// <summary>The page shown last in this app.</summary>
        internal TabletPage Last;

        /// <summary>For a tile that switches something instead of opening (the light): what a press does, and whether it is on.</summary>
        internal readonly System.Action Toggle;
        internal readonly System.Func<bool> On;
        private readonly System.Func<bool> offered;

        internal TabletApp(string name, string art, string glyph, params TabletPage[] pages)
        {
            Name = name;
            Art = art;
            Glyph = glyph;
            Pages = pages;
        }

 internal readonly Guests.GuestApp Guest;

        internal TabletApp(GuestPage page) : this(page.Guest.Name, null, null, page) => Guest = page.Guest;

        /// <summary>A tile that switches something on and off with a press, as a phone's flashlight tile.</summary>
        internal TabletApp(string name, string art, string glyph, System.Action toggle, System.Func<bool> on, System.Func<bool> offered)
            : this(name, art, glyph)
        {
            Toggle = toggle;
            On = on;
            this.offered = offered;
        }

        /// <summary>Its pages the game offers now, in tab order.</summary>
        internal List<TabletPage> Shown => Pages.Where(p => p.Available).ToList();

        internal bool Available => Toggle != null ? offered() : Pages.Any(p => p.Available);

        /// <summary>The page to open on: the last one shown, if still offered, else the first.</summary>
        internal TabletPage Opening
        {
            get
            {
                var shown = Shown;
                return Last != null && shown.Contains(Last) ? Last : shown.FirstOrDefault();
            }
        }

        internal bool Holds(TabletPage page) => page != null && Pages.Contains(page);
    }

    /// <summary>
    /// The game's own interface glyphs for the app icons, found by name among
    /// the sprites the game has loaded (and the item catalog's icons); none
    /// are shipped with the mod. A name not found falls back to a drawn symbol.
    /// </summary>
    internal static class TabletIcons
    {
        private const int FastMisses = 3;
        private const float RetrySeconds = 30f, LongRetrySeconds = 120f;

        private static readonly Dictionary<string, Sprite> found = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, (float at, int count)> missed = new Dictionary<string, (float, int)>();
        private static bool sceneHooked;

        internal static Sprite Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (found.TryGetValue(name, out var sprite) && sprite != null)
                return sprite;
            if (!sceneHooked)
            {
                sceneHooked = true;
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            }
            // A miss is retried a few times at RetrySeconds, then falls back to LongRetrySeconds
            // (reset on scene load) so Resources.FindObjectsOfTypeAll does not hitch frequently.
            var missCount = 0;
            if (missed.TryGetValue(name, out var miss))
            {
                missCount = miss.count;
                var wait = missCount >= FastMisses ? LongRetrySeconds : RetrySeconds;
                if (Time.unscaledTime - miss.at < wait)
                    return null;
            }
            sprite = FromItems(name) ?? FromLoaded(name);
            if (sprite != null)
            {
                found[name] = sprite;
                missed.Remove(name);
            }
            else
                missed[name] = (Time.unscaledTime, missCount + 1);
            return sprite;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene _, UnityEngine.SceneManagement.LoadSceneMode __) => missed.Clear();

        private static Sprite FromLoaded(string name)
        {
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            for (var i = 0; i < sprites.Length; i++)
            {
                var s = sprites[i];
                if (s != null && s.name == name)
                    return s;
            }
            return null;
        }

        private static Sprite FromItems(string name)
        {
            var items = DV.Globals.G?.Items?.items;
            if (items == null)
                return null;
            foreach (var spec in items)
            {
                if (spec == null)
                    continue;
                if (spec.ItemIconSprite != null && spec.ItemIconSprite.name == name)
                    return spec.ItemIconSprite;
                if (spec.ItemIconSpriteSimple != null && spec.ItemIconSpriteSimple.name == name)
                    return spec.ItemIconSpriteSimple;
            }
            return null;
        }
    }
}
