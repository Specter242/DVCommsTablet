using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using CommsTablet.Core.Booklet;
using DV.RenderTextureSystem.BookletRender;
using DV.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CommsTablet.Map
{
    /// <summary>
    /// The game's station booklet and route map, read page by page from their
    /// render prefabs (LevelInfo's world-specific prefabs) for the Switches
    /// app's map: each page's schematic tiles and badges, in the schematic's
    /// own units, and the switches its touch cells name (the level's generated
    /// junction data), as the network's indices.
    ///
    /// Other mods draw on the books as the game renders them (Double Track
    /// Maps and Interchange Yards patch the render's page list and the
    /// junction data), so the pages are read from a copy of each render
    /// prefab that has been through that step. The copies are kept, inactive
    /// and far below the world, for <see cref="BookletRenderer"/> to draw the
    /// pages from. Read once per network.
    ///
    /// AI Engineer's <c>BookletReader</c> (c6e6207,
    /// src/AIEngineer/Game/BookletReader.cs), with its export left out, the
    /// cells the touch maps skip (invalid, or excluded from the junction
    /// map) skipped as the game's map items do, and its copies held at a
    /// place of their own, apart from AI Engineer's.
    /// </summary>
    internal static class BookletReader
    {
        private const float Cell = 50f;

        private static readonly Regex PageName = new Regex(@"^\[(\w+)-(\w+)\]$", RegexOptions.CultureInvariant);

        private static TrackNetwork readFor;
        private static Booklet booklet;
        private static List<SchematicSource> sources;
        private static GameObject holder;

        /// <summary>The copies of the two renders the pages were read from; null until read.</summary>
        public static StaticPagesRender BookletCopy { get; private set; }

        public static StaticPagesRender RouteCopy { get; private set; }

        /// <summary>What holds the copies: inactive but while a page is drawn.</summary>
        public static GameObject Holder => holder;

        /// <summary>
        /// Where the copies stand: far below the world, out of any other
        /// camera's way, and 2 km below AI Engineer's (at y -30000), so the
        /// two mods' copies never share a place.
        /// </summary>
        public static readonly Vector3 Below = new Vector3(0, -32000, 0);

        /// <summary>The booklet as last read, or null (not read yet, or it could not be).</summary>
        public static Booklet Read => readFor != null ? booklet : null;

        /// <summary>The booklet matched to the network; null if the game's couldn't be read (the reason is logged).</summary>
        public static Booklet Get(TrackNetwork network)
        {
            if (network == null)
                return null;
            if (readFor == network)
                return booklet;
            Forget();
            readFor = network;
            var watch = Stopwatch.StartNew();
            try
            {
                sources = ReadPages(network, out var problem);
                if (sources == null)
                {
                    Main.Mod?.Logger.Warning($"Map: the station booklet was not read: {problem}");
                    return null;
                }
                var read = watch.ElapsedMilliseconds;
                booklet = Booklet.Build(sources, network.Graph, t => network.Tracks[t].name);
                Main.Mod?.Logger.Log($"Map: booklet of {booklet.Stations.Count} stations, {sources.Count} pages, read in {read} ms, matched in {watch.ElapsedMilliseconds - read} ms: {booklet.Stats}");
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException("Map: the station booklet was not read", e);
                booklet = null;
            }
            return booklet;
        }

        /// <summary>Lets the booklet and its copies go: at unload, or before reading another world's.</summary>
        public static void Forget()
        {
            BookletRenderer.Hide();
            if (holder != null)
                Object.Destroy(holder);
            holder = null;
            BookletCopy = RouteCopy = null;
            readFor = null;
            booklet = null;
            sources = null;
        }

        /// <summary>The pages as read; null until <see cref="Get"/> has read them.</summary>
        public static IReadOnlyList<SchematicSource> Sources => sources;

        private static List<SchematicSource> ReadPages(TrackNetwork network, out string problem)
        {
            problem = null;
            var data = SingletonBehaviour<LevelInfo>.Instance != null ? SingletonBehaviour<LevelInfo>.Instance.junctionData : null;
            if (data == null)
            {
                problem = "the level has no junction data";
                return null;
            }
            var result = new List<SchematicSource>();
            holder = new GameObject("[CommsTablet booklet]");
            holder.SetActive(false);
            holder.transform.position = Below;
            Object.DontDestroyOnLoad(holder);
            var stationPages = data.GetPageData(DV.Items.JunctionMap.JunctionMapType.Station);
            var worldPages = data.GetPageData(DV.Items.JunctionMap.JunctionMapType.World);
            var book = BookletCopy = Copy(LevelInfo.WorldSpecificPrefabs.SchematicMapRender, holder.transform);
            if (book == null)
            {
                problem = "the level has no station booklet render";
                return null;
            }
            if (stationPages == null || stationPages.Length != book.staticPages.Count)
            {
                problem = $"the booklet's render has {book.staticPages.Count} pages and its switch data {stationPages?.Length ?? 0}";
                return null;
            }
            var grid = data.GetMapGridSize(DV.Items.JunctionMap.JunctionMapType.Station);
            for (var i = 0; i < book.staticPages.Count; i++)
            {
                var page = book.staticPages[i];
                var m = PageName.Match(page.gameObject.name);
                if (!m.Success || page.gameObject.name == "[Cover]" || page.gameObject.name == "[Legend]")
                    continue;
                var roots = page.GetComponentsInChildren<RectTransform>(true).Where(t => t.name == "SMap" && ActiveUnder(t, page.transform)).ToList();
                if (roots.Count == 0)
                    continue;
                var src = ReadPage(network, page.transform, roots, stationPages[i], grid, false);
                if (src != null)
                    result.Add(src);
            }
            var route = RouteCopy = Copy(LevelInfo.WorldSpecificPrefabs.RouteMapRender, holder.transform);
            if (route != null && route.staticPages.Count > 0 && worldPages != null && worldPages.Length > 0)
            {
                var page = route.staticPages[0];
                var root = page.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "OuterWrapper");
                if (root != null)
                {
                    var src = ReadPage(network, page.transform, new List<RectTransform> { root }, worldPages[0], data.GetMapGridSize(DV.Items.JunctionMap.JunctionMapType.World), true);
                    if (src != null)
                        result.Add(src);
                }
            }
            if (result.Count == 0)
            {
                problem = "no pages";
                return null;
            }
            return result;
        }

        /// <summary>A copy of a render prefab, inactive under the holder, through the step where the game (and other mods) fill its pages.</summary>
        private static StaticPagesRender Copy(LevelInfo.WorldSpecificPrefabs which, Transform under)
        {
            var prefab = LevelInfo.GetWorldSpecificPrefab(which);
            if (prefab == null)
                return null;
            var copy = Object.Instantiate(prefab, under);
            var render = copy.GetComponent<StaticPagesRender>();
            if (render == null)
                return null;
            try
            {
                GameMembers.RenderPageStep?.Invoke(render, null);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Warning($"Map: the render's page step failed on a copy, reading it as it is: {e.InnerException?.Message ?? e.Message}");
            }
            return render;
        }

        private static bool ActiveUnder(Transform t, Transform top)
        {
            for (var x = t; x != null && x != top; x = x.parent)
                if (!x.gameObject.activeSelf)
                    return false;
            return true;
        }

        private static string TextOf(Transform page, string name)
        {
            var t = page.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => x.gameObject.name == name);
            return t != null ? t.text.Trim() : null;
        }

        private static SchematicSource ReadPage(TrackNetwork network, Transform page, List<RectTransform> roots, JunctionGeneratedDataRuntime.JunctionPageData cells, Vector2Int grid, bool isRoute)
        {
            var schematic = roots[0];
            var tiles = new List<SchematicTile>();
            var badges = new List<SchematicBadge>();
            foreach (var root in roots)
            {
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                {
                    var rt = image.rectTransform;
                    if (rt == root || image.sprite == null || !image.enabled || !ActiveUnder(rt, root) || InBadge(rt, root))
                        continue;
                    var at = schematic.InverseTransformPoint(rt.position);
                    if (image.sprite.name == "SM-ID")
                    {
                        badges.Add(ReadBadge(rt, at, image.color));
                        continue;
                    }
                    var turn = (Quaternion.Inverse(schematic.rotation) * rt.rotation).eulerAngles.z;
                    var scale = new Vector2(rt.lossyScale.x / schematic.lossyScale.x, rt.lossyScale.y / schematic.lossyScale.y);
                    tiles.Add(new SchematicTile(image.sprite.name, at.x, at.y, turn, rt.rect.width * scale.x, rt.rect.height * scale.y));
                }
            }
            // Each touch cell's middle on the page, then on the schematic: the page's canvas holds the grid, centred.
            var canvas = page.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Template Canvas") ?? (RectTransform)page;
            var size = canvas.rect.size;
            float left = (size.x - grid.x * Cell) / 2, bottom = (size.y - grid.y * Cell) / 2;
            var switches = new List<SchematicSwitch>();
            var ordered = RailTrackRegistryBase.Junctions;
            foreach (var c in cells.junctionDataCoords ?? new List<JunctionGeneratedDataRuntime.DataCoord>())
            {
                // As the game's touch maps: a cell the junction map leaves out is no switch to press.
                if (!c.data.isValid || c.data.excludeFromJunctionMap)
                    continue;
                var index = c.data.junctionIndex;
                if (index < 0 || index >= ordered.Length || ordered[index] == null || !network.TryGetJunction(ordered[index], out var j))
                    continue;
                var onCanvas = new Vector3(left + (c.coord.x + 0.5f) * Cell - size.x / 2, bottom + (c.coord.y + 0.5f) * Cell - size.y / 2, 0);
                // The canvas's own pivot is its middle on the game's pages; allow for one that isn't.
                onCanvas += new Vector3((0.5f - canvas.pivot.x) * size.x, (0.5f - canvas.pivot.y) * size.y, 0);
                var at = schematic.InverseTransformPoint(canvas.TransformPoint(onCanvas));
                switches.Add(new SchematicSwitch(at.x, at.y, j));
            }
            if (isRoute)
                return new SchematicSource("[Route]", null, null, null, "ROUTE MAP", null, true, tiles, badges, switches);
            var color = page.GetComponentsInChildren<Image>(true).FirstOrDefault(x => x.gameObject.name == "StationBg");
            var code = TextOf(page, "Text-StationID");
            var m = PageName.Match(page.gameObject.name);
            return new SchematicSource(page.gameObject.name, code ?? m.Groups[1].Value, TextOf(page, "Text-StationName"), TextOf(page, "Text-YardID") ?? m.Groups[2].Value,
                                       TextOf(page, "Text-YardType"), color != null ? "#" + ColorUtility.ToHtmlStringRGBA(color.color) : null, false, tiles, badges, switches);
        }

        private static bool InBadge(Transform t, Transform root)
        {
            for (var x = t.parent; x != null && x != root; x = x.parent)
            {
                var image = x.GetComponent<Image>();
                if (image != null && image.sprite != null && image.sprite.name == "SM-ID")
                    return true;
            }
            return false;
        }

        /// <summary>A badge's label as drawn: a two-digit number's first digit is a text of its own, before the rest; its kind is the one kind plate shown.</summary>
        private static SchematicBadge ReadBadge(RectTransform badge, Vector3 at, Color color)
        {
            string first = "", rest = "", kind = null;
            foreach (Transform child in badge)
            {
                if (!child.gameObject.activeSelf)
                    continue;
                var name = child.name;
                var text = child.GetComponent<TMP_Text>();
                if (name.StartsWith("text-2digit", StringComparison.Ordinal) && text != null && first.Length == 0)
                    first = text.text.Trim();
                else if (name.StartsWith("text-ID", StringComparison.Ordinal) && text != null && rest.Length == 0)
                    rest = text.text.Trim();
                else if (name.StartsWith("Type-", StringComparison.Ordinal))
                    kind = name.Substring("Type-".Length);
            }
            return new SchematicBadge(at.x, at.y, first + rest, kind, "#" + ColorUtility.ToHtmlStringRGBA(color));
        }
    }
}
