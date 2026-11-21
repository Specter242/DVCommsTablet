using System;
using System.Collections.Generic;
using System.Linq;
using DV.Items;
using DV.RenderTextureSystem.BookletRender;
using DV.Utils;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The drawing's fields are filled from JSON.
#pragma warning disable 0649

namespace CommsTablet.Books
{
    /// <summary>
    /// Double Track's switches in the station booklet (item MapSchematic),
    /// from Books/booklet-normal.json. For Double Tracked Valley, until Double
    /// Track draws them itself (<see cref="DoubleTrackBooks"/>).
    ///
    /// - Stock pages whose yards Double Track changes get its switches: in the
    ///   render, the stock track tiles of the changed cells are hidden and the
    ///   new ones drawn, cloned from the booklet's own; a badge may move (Steel
    ///   Mill B's track 1, to the stub that now carries its name). In the
    ///   switch data, the page gets a touch cell for each.
    /// - The hump yard gets a page of its own, Steel Mill H, after Steel Mill
    ///   B: a copy of that page with the hump yard drawn on it. It goes into
    ///   the render's pages, the switch data's and the Steel Mill chapter, and
    ///   every later page (and chapter) moves one on, Interchange Yards' among
    ///   them: these patches run after its own.
    ///
    /// All or none, as the route map: only when the tablet does not stand
    /// down, every switch the pages show is found, and the booklet's pages are
    /// the ones drawn on.
    /// </summary>
    internal static class StationBooklet
    {
        internal sealed class Drawing
        {
            public sealed class Tile
            {
                public string sprite;
                public float x, y, rot;
                public float sx = 1f, sy = 1f;
            }

            public sealed class Cell
            {
                public string id;
                public float[] pos;
                public int[] cell;
            }

            public sealed class Move
            {
                public float[] from, to;
            }

            public sealed class Page
            {
                public int index;
                public string name;
                public List<int[]> clear;
                public List<Tile> tiles;
                public List<Move> moves;
                public Dictionary<string, Cell> switches;
            }

            public sealed class Badge
            {
                public float x, y;
                public string text, type;
            }

            public sealed class Insert
            {
                public int after, copyOf;
                public string yardId, yardType;
                public List<Tile> tiles;
                public List<Badge> badges;
                public Dictionary<string, Cell> switches;
            }

            public string layout;
            public int doubleTrackTotal;
            public List<Page> pages;
            public Insert insert;
        }

        private static Drawing data;
        private static bool loaded;

        internal static Drawing Data
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    data = DoubleTrackBooks.LoadDrawing<Drawing>("booklet-normal.json");
                }
                return data;
            }
        }

        internal sealed class Plan
        {
            public RailTrackRegistryBase Registry;
            public bool Applied;
            public string Why;
            public readonly BookSwitches Found = new BookSwitches();
        }

        private static Plan plan;

        internal static Plan Decide()
        {
            var registry = SingletonBehaviour<RailTrackRegistryBase>.Instance;
            if (registry == null || Data == null || DoubleTrackBooks.Asking)
                return null;
            if (plan != null && plan.Registry == registry)
                return plan;
            if (SingletonBehaviour<LevelInfo>.Instance?.junctionData == null)
                return null;
            plan = new Plan { Registry = registry };
            try
            {
                plan.Why = DoubleTrackBooks.StandDown() ?? Check(plan);
                plan.Applied = plan.Why == null;
            }
            catch (Exception e)
            {
                plan.Why = $"failed: {e}";
            }
            if (plan.Applied)
                DoubleTrackBooks.Log($"booklet: Double Track's {plan.Found.Junctions.Count} switches drawn in");
            else if (BookSwitches.Loaded)
                DoubleTrackBooks.Log($"booklet left as it is: {plan.Why}");
            return plan;
        }

        private static string Check(Plan p)
        {
            var level = SingletonBehaviour<LevelInfo>.Instance?.junctionData;
            var stations = level?.junctionPageDataStations;
            if (stations == null)
                return "no stock booklet data";
            // The pages drawn on must be where the drawing has them, with every container and prototype present.
            var render = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.SchematicMapRender)?.GetComponent<StaticPagesRender>();
            if (render == null || render.staticPages.Count != stations.Length)
                return "the booklet's render and switch data disagree";
            if (!TryCollectAssets(render.staticPages, null, out _, out _, out var assetProblem))
                return assetProblem;
            var wanted = Data.pages.SelectMany(pg => pg.switches).Concat(Data.insert.switches)
                .Select(kv => new KeyValuePair<string, (string, float[])>(kv.Key, (kv.Value.id, kv.Value.pos)));
            return p.Found.Find(p.Registry, Data.doubleTrackTotal, wanted);
        }

        private static bool TryCollectAssets(IList<StaticPageTemplatePaper> pages, IList<StaticPageTemplatePaper> fallbackPages,
            out Dictionary<string, GameObject> prototypes, out GameObject badge, out string problem)
        {
            prototypes = new Dictionary<string, GameObject>();
            badge = null;
            problem = null;
            if (pages == null)
            {
                problem = "no booklet pages";
                return false;
            }
            foreach (var pg in Data.pages)
            {
                if (pg.index < 0 || pg.index >= pages.Count || pages[pg.index] == null || pages[pg.index].gameObject.name != pg.name)
                {
                    problem = $"page {pg.index} is not {pg.name}";
                    return false;
                }
                if (pages[pg.index].transform.Find(DoubleTrackBooks.WrapperPath + "/SMap") == null)
                {
                    problem = $"page {pg.name} has no SMap container";
                    return false;
                }
            }
            var ins = Data.insert;
            if (ins == null || ins.copyOf < 0 || ins.copyOf >= pages.Count || pages[ins.copyOf] == null || ins.after < 0 || ins.after >= pages.Count)
            {
                problem = "no page to copy for the hump yard";
                return false;
            }
            var copyWrapper = pages[ins.copyOf].transform.Find(DoubleTrackBooks.WrapperPath);
            if (copyWrapper == null || copyWrapper.Find("SMap") == null)
            {
                problem = "hump yard source page has no SMap container";
                return false;
            }

            var foundPrototypes = new Dictionary<string, GameObject>();
            GameObject foundBadge = null;

            void Scan(IList<StaticPageTemplatePaper> list)
            {
                if (list == null)
                    return;
                foreach (var page in list)
                {
                    if (page == null)
                        continue;
                    var smap = page.transform.Find(DoubleTrackBooks.WrapperPath + "/SMap");
                    if (smap == null)
                        continue;
                    foreach (Transform t in smap)
                    {
                        var s = SpriteOf(t);
                        if (s == null)
                            continue;
                        if (TrackSprites.Contains(s) && !foundPrototypes.ContainsKey(s))
                            foundPrototypes[s] = t.gameObject;
                        if (s == "SM-ID" && foundBadge == null && t.Find("Type-S") != null)
                            foundBadge = t.gameObject;
                    }
                }
            }

            Scan(pages);
            Scan(fallbackPages);
            var route = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.RouteMapRender)?.GetComponent<StaticPagesRender>();
            if (route != null)
                foreach (var img in route.staticPages.SelectMany(sp => sp.GetComponentsInChildren<Image>(true)))
                    if (img.sprite != null && TrackSprites.Contains(img.sprite.name) && !foundPrototypes.ContainsKey(img.sprite.name))
                        foundPrototypes[img.sprite.name] = img.gameObject;

            var missing = Data.pages.SelectMany(pg => pg.tiles).Concat(ins.tiles)
                .Select(t => t.sprite).Distinct()
                .Where(s => string.IsNullOrEmpty(s) || !foundPrototypes.ContainsKey(s))
                .ToList();
            if (missing.Count > 0)
            {
                problem = $"booklet is missing required tile sprites: {string.Join(", ", missing)}";
                return false;
            }
            if (ins.badges != null && ins.badges.Count > 0 && foundBadge == null)
            {
                problem = "booklet is missing SM-ID badge prototype";
                return false;
            }
            prototypes = foundPrototypes;
            badge = foundBadge;
            return true;
        }

        private static bool On
        {
            get
            {
                var p = Decide();
                return p != null && p.Applied;
            }
        }

        // ------------------------------------------------------------ the switch data

        private static JunctionGeneratedDataRuntime.DataCoord Coord(string key, Drawing.Cell c) =>
            new JunctionGeneratedDataRuntime.DataCoord { data = plan.Found.DataFor(key), coord = new Vector2Int(c.cell[0], c.cell[1]) };

        private static JunctionGeneratedDataRuntime.JunctionPageData[] WithStations(JunctionGeneratedDataRuntime.JunctionPageData[] pages)
        {
            if (!On)
                return pages;
            var list = pages.ToList();
            var ins = Data.insert;
            if (ins == null || ins.after < 0 || ins.after >= list.Count)
            {
                DoubleTrackBooks.Warn($"booklet data: cannot insert after page {ins?.after} of {list.Count}");
                return pages;
            }
            foreach (var pg in Data.pages)
            {
                if (pg.index < 0 || pg.index >= list.Count)
                    continue;
                var page = list[pg.index];
                var existing = page.junctionDataCoords ?? new List<JunctionGeneratedDataRuntime.DataCoord>();
                page.junctionDataCoords = existing.Concat(pg.switches.Select(kv => Coord(kv.Key, kv.Value))).ToList();
                list[pg.index] = page;
            }
            list.Insert(ins.after + 1, new JunctionGeneratedDataRuntime.JunctionPageData
            {
                categoryId = list[ins.after].categoryId,
                junctionDataCoords = ins.switches.Select(kv => Coord(kv.Key, kv.Value)).ToList(),
            });
            for (var i = 0; i < list.Count; i++)
            {
                var page = list[i];
                page.pageNumber = i;
                list[i] = page;
            }
            return list.ToArray();
        }

        [HarmonyPatch(typeof(JunctionGeneratedDataRuntime), nameof(JunctionGeneratedDataRuntime.GetPageData))]
        [HarmonyAfter("Interchanges", "DoubleTrack", "DoubleTrackMaps")]
        private static class PageData
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(JunctionGeneratedDataRuntime __instance, JunctionMap.JunctionMapType junctionMapType,
                                        ref JunctionGeneratedDataRuntime.JunctionPageData[] __result)
            {
                if (__result == null || DoubleTrackBooks.Asking)
                    return;
                try
                {
                    if (junctionMapType == JunctionMap.JunctionMapType.Station)
                        __result = WithStations(__result);
                    else if (junctionMapType == JunctionMap.JunctionMapType.All)
                    {
                        var world = __instance.junctionPageDataWorld.Length;
                        __result = __result.Take(world).Concat(WithStations(__result.Skip(world).ToArray())).ToArray();
                    }
                }
                catch (Exception e)
                {
                    DoubleTrackBooks.Error($"booklet data: {e}");
                }
            }
        }

        // ------------------------------------------------------------ the chapters

        /// <summary>Marks a booklet whose chapters already have the hump yard's page.</summary>
        internal sealed class Chaptered : MonoBehaviour
        {
        }

        /// <summary>The hump yard's page in the Steel Mill chapter; every later chapter a page on.</summary>
        [HarmonyPatch(typeof(ChapterSelector), "Start")]
        [HarmonyAfter("Interchanges", "DoubleTrack", "DoubleTrackMaps")]
        private static class Chapters
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(ChapterSelector __instance, List<ChapterSelector.PageBookChapter> ___allChapters)
            {
                try
                {
                    var map = __instance.GetComponent<JunctionMap>();
                    if (map == null || map.junctionMapType != JunctionMap.JunctionMapType.Station || ___allChapters == null ||
                        __instance.GetComponent<Chaptered>() != null || !On)
                        return;
                    var after = Data.insert.after;
                    var holder = ___allChapters.FindIndex(c => c.startingPage <= after && after <= c.endingPage);
                    if (holder < 0)
                    {
                        DoubleTrackBooks.Warn($"booklet: no chapter holds page {after}");
                        return;
                    }
                    for (var i = 0; i < ___allChapters.Count; i++)
                    {
                        var c = ___allChapters[i];
                        if (i == holder)
                            c.endingPage++;
                        else if (c.startingPage > after)
                        {
                            c.startingPage++;
                            c.endingPage++;
                        }
                        ___allChapters[i] = c;
                    }
                    __instance.gameObject.AddComponent<Chaptered>();
                }
                catch (Exception e)
                {
                    DoubleTrackBooks.Error($"booklet chapters: {e}");
                }
            }
        }

        // ------------------------------------------------------------ the pages

        /// <summary>Marks a booklet render the drawing is already in.</summary>
        internal sealed class Drawn : MonoBehaviour
        {
        }

        [HarmonyPatch(typeof(StaticPagesRender), "GetStaticTemplatePaperData")]
        [HarmonyAfter("Interchanges", "DoubleTrack", "DoubleTrackMaps")]
        private static class Render
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(StaticPagesRender __instance)
            {
                try
                {
                    if (!__instance.gameObject.name.StartsWith("MapSchematicStaticRender", StringComparison.Ordinal) ||
                        __instance.GetComponent<Drawn>() != null || !On)
                        return;
                    if (Draw(__instance))
                        __instance.gameObject.AddComponent<Drawn>();
                }
                catch (Exception e)
                {
                    DoubleTrackBooks.Error($"booklet pages: {e}");
                }
            }
        }

        private static readonly HashSet<string> TrackSprites = new HashSet<string>
        {
            "SM-TracksStraight", "SM-TracksDiagonal", "SM-TracksCrossover", "SM-TracksSwitchDivergingL", "SM-TracksSwitchDivergingR",
            "SM-TracksSwitchSpread", "SM-TracksExit", "SM-TracksEnd", "SM-Switch",
        };

        private static string SpriteOf(Transform t) => t.GetComponent<Image>()?.sprite?.name;

        private static Vector2Int CellOf(RectTransform t) =>
            new Vector2Int(Mathf.RoundToInt(t.anchoredPosition.x / 50f), Mathf.RoundToInt(t.anchoredPosition.y / 50f));

        private static bool Draw(StaticPagesRender render)
        {
            var pages = render.staticPages;
            var stockPages = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.SchematicMapRender)?.GetComponent<StaticPagesRender>()?.staticPages;
            if (!TryCollectAssets(pages, stockPages, out var prototypes, out var badge, out var problem))
            {
                DoubleTrackBooks.Warn($"booklet pages: {problem}; leaving booklet unchanged");
                return false;
            }
            var made = 0;
            var hidden = 0;
            foreach (var pg in Data.pages)
            {
                var smap = pages[pg.index].transform.Find(DoubleTrackBooks.WrapperPath + "/SMap");
                var clear = new HashSet<Vector2Int>(pg.clear.Select(c => new Vector2Int(c[0], c[1])));
                foreach (Transform t in smap.Cast<Transform>().ToList())
                {
                    var s = SpriteOf(t);
                    if (s != null && TrackSprites.Contains(s) && t.gameObject.activeSelf && clear.Contains(CellOf((RectTransform)t)))
                    {
                        t.gameObject.SetActive(false);
                        hidden++;
                    }
                }
                foreach (var m in pg.moves)
                {
                    var from = new Vector2(m.from[0], m.from[1]);
                    var t = smap.Cast<Transform>().FirstOrDefault(c => SpriteOf(c) == "SM-ID" && (((RectTransform)c).anchoredPosition - from).sqrMagnitude < 1f);
                    if (t != null)
                        ((RectTransform)t).anchoredPosition = new Vector2(m.to[0], m.to[1]);
                    else
                        DoubleTrackBooks.Warn($"booklet {pg.name}: no badge at ({from.x}, {from.y}) to move");
                }
                made += Place(smap, pg.tiles, prototypes);
            }
            // The hump yard's page: a copy of the page it follows, renamed, its schematic redrawn.
            var ins = Data.insert;
            var source = pages[ins.copyOf];
            var copy = Object.Instantiate(source.gameObject, source.transform.parent, false);
            copy.name = $"[{SourceStation(source)}-{ins.yardId}]";
            var wrapper = copy.transform.Find(DoubleTrackBooks.WrapperPath);
            SetText(wrapper, "Text-YardID", ins.yardId);
            SetText(wrapper, "Text-YardType", ins.yardType);
            var csmap = wrapper.Find("SMap");
            foreach (Transform t in csmap.Cast<Transform>().ToList())
            {
                var s = SpriteOf(t);
                if (s != "SM-Compass" && s != "SM-CompassOffice")
                    Object.DestroyImmediate(t.gameObject);
            }
            made += Place(csmap, ins.tiles, prototypes);
            foreach (var b in ins.badges)
            {
                if (badge == null)
                    break;
                var go = Object.Instantiate(badge, csmap, false);
                go.name = DoubleTrackBooks.OwnPrefix + "SM-ID";
                ((RectTransform)go.transform).anchoredPosition = new Vector2(b.x, b.y);
                foreach (Transform child in go.transform)
                {
                    if (child.name.StartsWith("Type-", StringComparison.Ordinal))
                        child.gameObject.SetActive(child.name == "Type-" + b.type);
                    else if (child.name == "text-ID-White")
                    {
                        child.gameObject.SetActive(true);
                        SetTextOn(child, b.text);
                    }
                    else if (child.name.StartsWith("text-", StringComparison.Ordinal))
                        child.gameObject.SetActive(false);
                }
                go.SetActive(true);
            }
            pages.Insert(ins.after + 1, copy.GetComponent<StaticPageTemplatePaper>());
            DoubleTrackBooks.Log($"booklet pages: {hidden} stock tiles hidden, {made} drawn on {Data.pages.Count} pages; page {copy.name} added at {ins.after + 1} of {pages.Count}");
            return true;
        }

        private static string SourceStation(StaticPageTemplatePaper page)
        {
            var n = page.gameObject.name;
            var dash = n.LastIndexOf('-');
            return n.StartsWith("[", StringComparison.Ordinal) && dash > 1 ? n.Substring(1, dash - 1) : "SM";
        }

        private static int Place(Transform smap, IEnumerable<Drawing.Tile> tiles, Dictionary<string, GameObject> prototypes)
        {
            var made = 0;
            foreach (var tile in tiles)
            {
                if (!prototypes.TryGetValue(tile.sprite, out var proto))
                {
                    DoubleTrackBooks.Warn($"booklet: no tile {tile.sprite} to copy");
                    continue;
                }
                var go = Object.Instantiate(proto, smap, false);
                go.name = DoubleTrackBooks.OwnPrefix + tile.sprite;
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = new Vector2(tile.x, tile.y);
                rt.localRotation = Quaternion.Euler(0f, 0f, tile.rot);
                rt.localScale = new Vector3(tile.sx, tile.sy, 1f);
                // The red markers lie under the track, as on the stock pages.
                if (tile.sprite == "SM-Switch")
                    go.transform.SetAsFirstSibling();
                go.SetActive(true);
                made++;
            }
            return made;
        }

        private static void SetText(Transform wrapper, string name, string text)
        {
            var t = wrapper.Find(name);
            if (t != null)
                SetTextOn(t, text);
        }

        /// <summary>Sets a text, taking off the localisation that would put the stock one back.</summary>
        private static void SetTextOn(Transform t, string text)
        {
            foreach (var l in t.GetComponents<DV.Localization.Localize>())
                Object.DestroyImmediate(l);
            foreach (var l in t.GetComponents<I2.Loc.Localize>())
                Object.DestroyImmediate(l);
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
                tmp.text = text;
        }
    }
}
