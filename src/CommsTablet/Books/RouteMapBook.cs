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
    /// The route map (item RouteMap), re-laid with Double Track's switches on
    /// its main lines: the game's own tiles at 80% on a 40 x 40 grid, which
    /// makes room for the passing loops and double-track sections. Drawn in
    /// Books/routemap-normal.json. For Double Tracked Valley, until Double
    /// Track draws them itself (<see cref="DoubleTrackBooks"/>).
    ///
    /// - The page: as its render prefab is rendered, the stock tiles are
    ///   hidden and the drawing's placed, cloned from the stock ones; the
    ///   stations' badges move to their cells. Tiles another mod added
    ///   (Interchange Yards' tie-ins) move with the stock cells they stand in.
    /// - The touch cells (the switch data's world page): every stock switch
    ///   moves to its cell in the drawing, every cell another mod added moves
    ///   with its stock cell, and Double Track's switches are added, each
    ///   naming its junction by its index in the registry's ordered junctions.
    /// - The touch grid is the drawing's (40 x 40).
    ///
    /// All of it or none: the map is re-laid only when the tablet does not
    /// stand down, the game's stock route map is the one the drawing was made
    /// from, and every Double Track switch the drawing shows is found (the
    /// Normal layout of Double Track 2.1.1). Otherwise the map stays as it
    /// is, so a touch never lands on the wrong switch.
    /// </summary>
    internal static class RouteMapBook
    {
        internal sealed class Drawing
        {
            public sealed class Tile
            {
                public string sprite;
                public float x, y, rot;
                public float sx = 1f, sy = 1f;
                public int[] stock;            // the stock cell an unchanged stock tile came from
            }

            public sealed class Switch
            {
                public string id;
                public float[] pos;            // in the map frame
            }

            public string layout;
            public int grid;
            public List<Tile> tiles;
            public Dictionary<string, int[]> badges;
            public Dictionary<string, int[]> switches;
            public Dictionary<string, Switch> doubleTrack;
            public Dictionary<string, string> bookletOnly;    // Double Track's switches only a station's booklet shows
            public Dictionary<string, int[]> stockCells;
            public int[] cols, rows;

            /// <summary>Canvas units per cell.</summary>
            public float Cell => 1600f / grid;
        }

        private static Drawing spec;
        private static bool loaded;

        internal static Drawing Spec
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    spec = DoubleTrackBooks.LoadDrawing<Drawing>("routemap-normal.json");
                }
                return spec;
            }
        }

        /// <summary>The drawing's Double Track switches found in this world, by key, and their indices.</summary>
        internal sealed class Plan
        {
            public RailTrackRegistryBase Registry;
            public bool Relaid;
            public string Why;
            public readonly BookSwitches Found = new BookSwitches();
            public Dictionary<string, Junction> Junctions => Found.Junctions;
        }

        private static readonly string[] ContainerNames = { "Tracks", "Switches", "Stations" };

        private static Plan plan;

        /// <summary>Whether this world's route map is re-laid, decided once per world (registry).</summary>
        internal static Plan Decide()
        {
            var registry = SingletonBehaviour<RailTrackRegistryBase>.Instance;
            if (registry == null || Spec == null || DoubleTrackBooks.Asking)
                return null;
            if (plan != null && plan.Registry == registry)
                return plan;
            if (SingletonBehaviour<LevelInfo>.Instance?.junctionData == null)
                return null;
            plan = new Plan { Registry = registry };
            try
            {
                plan.Why = DoubleTrackBooks.StandDown() ?? Check(plan);
                plan.Relaid = plan.Why == null;
            }
            catch (Exception e)
            {
                plan.Why = $"failed: {e}";
            }
            if (plan.Relaid)
                DoubleTrackBooks.Log($"route map re-laid: {plan.Junctions.Count} Double Track switches found");
            else if (BookSwitches.Loaded)
                DoubleTrackBooks.Log($"route map left as it is: {plan.Why}");
            return plan;
        }

        private static string Check(Plan p)
        {
            var data = SingletonBehaviour<LevelInfo>.Instance?.junctionData;
            if (data == null || data.junctionPageDataWorld == null || data.junctionPageDataWorld.Length != 1)
                return "no stock route map data";
            // The stock data must be the drawing's.
            var cells = data.junctionPageDataWorld[0].junctionDataCoords;
            if (cells == null || cells.Count != Spec.stockCells.Count)
                return $"the stock route map has {cells?.Count ?? 0} switches, the drawing {Spec.stockCells.Count}";
            foreach (var c in cells)
                if (!Spec.stockCells.TryGetValue(c.data.junctionIdLong ?? "", out var at) || at[0] != c.coord.x || at[1] != c.coord.y)
                    return $"stock switch {c.data.junctionIdLong} at ({c.coord.x}, {c.coord.y}) is not where the drawing has it";
            // Preflight the stock route map prefab's containers and tile prototypes before modifying touch cells.
            var stockPage = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.RouteMapRender)?.GetComponent<StaticPagesRender>()?.staticPages.FirstOrDefault();
            if (!TryGetContainers(stockPage, out var stockContainers, out var containerProblem))
                return containerProblem;
            var prototypes = CollectPrototypes(stockContainers, null);
            var missingSprites = MissingSprites(prototypes);
            if (missingSprites.Count > 0)
                return $"route map prefab is missing required tile sprites: {string.Join(", ", missingSprites)}";
            // Double Track's switches: all of them, and each the drawing shows found by id and place.
            var expected = Spec.doubleTrack.Count + (Spec.bookletOnly?.Count ?? 0);
            return p.Found.Find(p.Registry, expected,
                Spec.doubleTrack.Select(kv => new KeyValuePair<string, (string, float[])>(kv.Key, (kv.Value.id, kv.Value.pos))));
        }

        private static bool TryGetContainers(StaticPageTemplatePaper page, out Dictionary<string, Transform> containers, out string problem)
        {
            containers = null;
            problem = null;
            if (page == null)
            {
                problem = "no route map page";
                return false;
            }
            var wrapper = page.transform.Find(DoubleTrackBooks.WrapperPath);
            if (wrapper == null)
            {
                problem = $"route map page has no {DoubleTrackBooks.WrapperPath}";
                return false;
            }
            containers = new Dictionary<string, Transform>(ContainerNames.Length);
            foreach (var n in ContainerNames)
            {
                var child = wrapper.Find(n);
                if (child == null)
                {
                    problem = $"route map page has no {n} container";
                    containers = null;
                    return false;
                }
                containers[n] = child;
            }
            return true;
        }

        private static Dictionary<string, GameObject> CollectPrototypes(Dictionary<string, Transform> containers, HashSet<string> stockNames, Dictionary<string, Transform> fallbackContainers = null)
        {
            var prototypes = new Dictionary<string, GameObject>();
            foreach (var n in new[] { "Tracks", "Switches" })
                foreach (Transform t in containers[n])
                {
                    var s = SpriteOf(t);
                    if (s != null && !prototypes.ContainsKey(s) && (stockNames == null || stockNames.Contains($"{n}/{t.name}")))
                        prototypes[s] = t.gameObject;
                }
            if (fallbackContainers != null)
                foreach (var n in new[] { "Tracks", "Switches" })
                    foreach (Transform t in fallbackContainers[n])
                    {
                        var s = SpriteOf(t);
                        if (s != null && !prototypes.ContainsKey(s))
                            prototypes[s] = t.gameObject;
                    }
            var booklet = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.SchematicMapRender)?.GetComponent<StaticPagesRender>();
            if (booklet != null)
                foreach (var img in booklet.staticPages.SelectMany(sp => sp.GetComponentsInChildren<Image>(true)))
                    if (img.sprite != null && !prototypes.ContainsKey(img.sprite.name) && img.sprite.name.StartsWith("SM-Tracks", StringComparison.Ordinal))
                        prototypes[img.sprite.name] = img.gameObject;
            return prototypes;
        }

        private static List<string> MissingSprites(Dictionary<string, GameObject> prototypes) =>
            Spec.tiles.Select(t => t.sprite).Distinct().Where(s => string.IsNullOrEmpty(s) || !prototypes.ContainsKey(s)).ToList();

        private static bool Relaid
        {
            get
            {
                var p = Decide();
                return p != null && p.Relaid;
            }
        }

        // ------------------------------------------------------------ the touch cells

        internal static Vector2Int Remap(Vector2Int stock) =>
            stock.x >= 0 && stock.x < Spec.cols.Length && stock.y >= 0 && stock.y < Spec.rows.Length
                ? new Vector2Int(Spec.cols[stock.x], Spec.rows[stock.y])
                : stock;

        private static JunctionGeneratedDataRuntime.JunctionPageData[] WithWorld(JunctionGeneratedDataRuntime.JunctionPageData[] pages)
        {
            if (pages.Length == 0 || !Relaid)
                return pages;
            var page = pages[0];
            var coords = new List<JunctionGeneratedDataRuntime.DataCoord>();
            foreach (var c in page.junctionDataCoords ?? new List<JunctionGeneratedDataRuntime.DataCoord>())
            {
                var id = c.data.junctionIdLong ?? "";
                var moved = c;
                if (Spec.stockCells.TryGetValue(id, out var was) && was[0] == c.coord.x && was[1] == c.coord.y && Spec.switches.TryGetValue(id, out var now))
                    moved.coord = new Vector2Int(now[0], now[1]);
                else
                    moved.coord = Remap(c.coord);      // a cell another mod added: with its stock cell
                coords.Add(moved);
            }
            foreach (var key in plan.Junctions.Keys)
            {
                var cell = Spec.switches[key];
                coords.Add(new JunctionGeneratedDataRuntime.DataCoord { data = plan.Found.DataFor(key), coord = new Vector2Int(cell[0], cell[1]) });
            }
            page.junctionDataCoords = coords;
            pages[0] = page;
            return pages;
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
                    if (junctionMapType == JunctionMap.JunctionMapType.World)
                        __result = WithWorld(__result);
                    else if (junctionMapType == JunctionMap.JunctionMapType.All)
                    {
                        var world = __instance.junctionPageDataWorld.Length;
                        __result = WithWorld(__result.Take(world).ToArray()).Concat(__result.Skip(world)).ToArray();
                    }
                }
                catch (Exception e)
                {
                    DoubleTrackBooks.Error($"route map data: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(JunctionGeneratedDataRuntime), nameof(JunctionGeneratedDataRuntime.GetMapGridSize))]
        [HarmonyAfter("Interchanges", "DoubleTrack", "DoubleTrackMaps")]
        private static class GridSize
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(JunctionMap.JunctionMapType junctionMapType, ref Vector2Int __result)
            {
                if (junctionMapType != JunctionMap.JunctionMapType.World || DoubleTrackBooks.Asking)
                    return;
                try
                {
                    if (Relaid)
                        __result = new Vector2Int(Spec.grid, Spec.grid);
                }
                catch (Exception e)
                {
                    DoubleTrackBooks.Error($"route map grid: {e}");
                }
            }
        }

        // ------------------------------------------------------------ the page

        /// <summary>Marks a route map render the drawing is already in.</summary>
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
                    if (!__instance.gameObject.name.StartsWith("RouteMapStaticRender", StringComparison.Ordinal) || __instance.GetComponent<Drawn>() != null || !Relaid)
                        return;
                    if (Relay(__instance))
                        __instance.gameObject.AddComponent<Drawn>();
                }
                catch (Exception e)
                {
                    DoubleTrackBooks.Error($"route map page: {e}");
                }
            }
        }

        private static Vector2Int StockCell(RectTransform t)
        {
            var p = t.anchoredPosition;
            return new Vector2Int(Mathf.FloorToInt((p.x + 800f) / 50f), Mathf.FloorToInt((p.y + 800f) / 50f));
        }

        private static string SpriteOf(Transform t) => t.GetComponent<Image>()?.sprite?.name;

        private static string BadgeText(Transform badge)
        {
            var text = badge.Find("text-ID-Black")?.GetComponent<TMP_Text>()?.text ?? "";
            return text.Replace("\\n", "").Replace("\n", "").Trim();
        }

        private static bool Relay(StaticPagesRender render)
        {
            var page = render.staticPages.FirstOrDefault();
            var stockPage = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.RouteMapRender)?.GetComponent<StaticPagesRender>()?.staticPages.FirstOrDefault();
            if (!TryGetContainers(page, out var containers, out var pageProblem))
            {
                DoubleTrackBooks.Warn($"route map page: {pageProblem}");
                return false;
            }
            if (!TryGetContainers(stockPage, out var stockContainers, out var stockProblem))
            {
                DoubleTrackBooks.Warn($"route map page: {stockProblem}");
                return false;
            }
            var stockNames = new HashSet<string>(ContainerNames.SelectMany(n => stockContainers[n].Cast<Transform>().Select(t => $"{n}/{t.name}")));
            var k = Spec.Cell / 50f;
            Vector2 Centre(int x, int y) => new Vector2(-800f + Spec.Cell * (x + 0.5f), -800f + Spec.Cell * (y + 0.5f));

            // Prototypes of each tile, from the live page with fallback to the validated stock prefab (and the booklet's, for the end tile).
            var prototypes = CollectPrototypes(containers, stockNames, stockContainers);
            var missingSprites = MissingSprites(prototypes);
            if (missingSprites.Count > 0)
            {
                DoubleTrackBooks.Warn($"route map page: missing required tile sprites ({string.Join(", ", missingSprites)}); leaving page unchanged");
                return false;
            }

            // The stock tiles hidden; stock cells another mod had already emptied are left empty.
            var emptied = new HashSet<Vector2Int>();
            var hidden = 0;
            var moved = 0;
            foreach (var n in new[] { "Tracks", "Switches" })
                foreach (Transform t in containers[n].Cast<Transform>().ToList())
                {
                    if (stockNames.Contains($"{n}/{t.name}"))
                    {
                        if (!t.gameObject.activeSelf)
                            emptied.Add(StockCell((RectTransform)t));
                        else
                            hidden++;
                        t.gameObject.SetActive(false);
                    }
                    else
                        moved += Move((RectTransform)t, k) ? 1 : 0;
                }
            // The badges to their cells; another mod's badges with their stock cells.
            var badges = 0;
            foreach (Transform t in containers["Stations"].Cast<Transform>().ToList())
            {
                var rt = (RectTransform)t;
                if (stockNames.Contains($"Stations/{t.name}") && Spec.badges.TryGetValue(BadgeText(t), out var cell))
                {
                    rt.anchoredPosition = Centre(cell[0], cell[1]);
                    rt.localScale = new Vector3(rt.localScale.x * k, rt.localScale.y * k, 1f);
                    badges++;
                }
                else
                    moved += Move(rt, k) ? 1 : 0;
            }
            // The drawing.
            var made = 0;
            var skipped = 0;
            foreach (var tile in Spec.tiles)
            {
                if (tile.stock != null && emptied.Contains(new Vector2Int(tile.stock[0], tile.stock[1])))
                {
                    skipped++;
                    continue;
                }
                if (!prototypes.TryGetValue(tile.sprite, out var proto))
                {
                    DoubleTrackBooks.Warn($"route map page: no tile {tile.sprite} to copy");
                    continue;
                }
                var into = tile.sprite == "SM-Switch" ? containers["Switches"] : containers["Tracks"];
                var go = Object.Instantiate(proto, into, false);
                go.name = DoubleTrackBooks.OwnPrefix + tile.sprite;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(50f, 50f);
                rt.anchoredPosition = new Vector2(tile.x, tile.y);
                rt.localRotation = Quaternion.Euler(0f, 0f, tile.rot);
                rt.localScale = new Vector3(tile.sx * k, tile.sy * k, 1f);
                go.SetActive(true);
                made++;
            }
            DoubleTrackBooks.Log($"route map page: {hidden} stock tiles hidden, {made} drawn, {badges} badges placed, {moved} other tiles moved, " +
                                 $"{skipped} left out where another mod emptied the stock cell ({string.Join(" ", emptied.Select(c => $"({c.x},{c.y})"))})");
            return true;
        }

        /// <summary>Another mod's tile, moved with the stock cell it stands in (its place within the cell scaled with it).</summary>
        private static bool Move(RectTransform t, float k)
        {
            var cell = StockCell(t);
            var to = Remap(cell);
            if (to == cell && (cell.x < 0 || cell.x >= Spec.cols.Length || cell.y < 0 || cell.y >= Spec.rows.Length))
                return false;
            var within = t.anchoredPosition - new Vector2(-800f + 50f * (cell.x + 0.5f), -800f + 50f * (cell.y + 0.5f));
            t.anchoredPosition = new Vector2(-800f + Spec.Cell * (to.x + 0.5f), -800f + Spec.Cell * (to.y + 0.5f)) + within * k;
            t.localScale = new Vector3(t.localScale.x * k, t.localScale.y * k, 1f);
            return true;
        }
    }
}
