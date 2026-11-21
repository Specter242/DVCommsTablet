using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.Items;
using DV.RenderTextureSystem.BookletRender;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace CommsTablet.Books
{
    /// <summary>
    /// Double Track's switches in the game's two switch books, the route map
    /// (<see cref="RouteMapBook"/>) and the station booklet
    /// (<see cref="StationBooklet"/>), so the paper books, the Switches app,
    /// and AI Engineer's map all show them and throw them by touch.
    ///
    /// These maps are for Double Tracked Valley (Chump_the_Lump's Double
    /// Track, Nexus mod 1487). They are the standalone Double Track Maps
    /// mod's patches, carried in the tablet until Double Track draws its
    /// switches in the books itself (Chump-the-Lump/DV-DoubleTrack pull
    /// request 1), and they are removed when that ships or at its author's
    /// request. The drawings (Books/*.json) are the tablet author's own,
    /// made from a survey; Double Track's switches are read from it at run
    /// time, and none of its files are here.
    ///
    /// The tablet stands down, leaving the books to another mod or as they
    /// are, when Double Track or Double Track Maps patches the same methods,
    /// when Double Track Maps is running, or when the books already hold
    /// Double Track's switches (checked in the switch data). Otherwise it
    /// draws a whole book or none of it: only for Double Track's Normal layout
    /// (its 114 switches, each found by id and place) on the game's unchanged
    /// books.
    /// </summary>
    internal static class DoubleTrackBooks
    {
        /// <summary>Double Track's and the standalone maps' ids: their mod, Harmony, and assembly names.</summary>
        internal static readonly string[] Drawers = { "DoubleTrack", "DoubleTrackMaps" };

        /// <summary>The standalone mod the tablet's patches come from.</summary>
        internal const string StandaloneId = "DoubleTrackMaps";

        /// <summary>Where a book page's contents sit in its render prefab.</summary>
        internal const string WrapperPath = "Template Canvas/BackgroundImage/OuterWrapper";

        /// <summary>Names the tiles and badges the tablet adds (the standalone mod's are "doubletrackmaps ", Double Track's own "doubletrack ").</summary>
        internal const string OwnPrefix = "commstablet dt ";

        /// <summary>True while the tablet reads the books as the other mods leave them: its own patches stay out of the way.</summary>
        internal static bool Asking { get; private set; }

        private static RailTrackRegistryBase decidedFor;
        private static string standDown;

        /// <summary>
        /// Why the tablet leaves the books alone in this world, or null when it
        /// may draw them (its books' own checks then decide). Decided once per
        /// world.
        /// </summary>
        internal static string StandDown()
        {
            var registry = SingletonBehaviour<RailTrackRegistryBase>.Instance;
            if (registry == null)
                return "no railway yet";
            if (decidedFor == registry)
                return standDown;
            if (SingletonBehaviour<LevelInfo>.Instance?.junctionData == null)
                return "no junction data yet";
            decidedFor = registry;
            try
            {
                standDown = Decide();
            }
            catch (Exception e)
            {
                standDown = $"the check failed: {e.GetType().Name}: {e.Message}";
            }
            if (standDown != null && BookSwitches.Loaded)
                Log($"left to the books as they are: {standDown}");
            return standDown;
        }

        private static string Decide()
        {
            if (UnityModManager.FindMod(StandaloneId)?.Active == true)
                return "Double Track Maps is running and draws them";
            foreach (var (method, label) in Targets())
            {
                if (method == null)
                    continue;
                var info = Harmony.GetPatchInfo(method);
                if (info == null)
                    continue;
                var by = DrawnBy(info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                    .Select(p => (p.owner, p.PatchMethod?.DeclaringType?.Assembly.GetName().Name)));
                if (by != null)
                    return $"{by} patches {label}";
            }
            var data = SingletonBehaviour<LevelInfo>.Instance?.junctionData;
            var added = BookSwitches.Added();
            if (data == null || added == null || added.Count == 0)
                return null;
            Asking = true;
            try
            {
                var grid = data.GetMapGridSize(JunctionMap.JunctionMapType.World);
                if (grid != data.junctionWorldGridSize)
                    return $"another mod has changed the route map's grid ({grid.x} x {grid.y})";
                return HoldsAny(data.GetPageData(JunctionMap.JunctionMapType.All), added, SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedJunctions)
                    ? "the books' switch data already names Double Track's switches"
                    : null;
            }
            finally
            {
                Asking = false;
            }
        }

        /// <summary>The methods the books are drawn through, which another drawer would patch too.</summary>
        internal static IEnumerable<(MethodBase Method, string Label)> Targets()
        {
            yield return (AccessTools.DeclaredMethod(typeof(JunctionGeneratedDataRuntime), nameof(JunctionGeneratedDataRuntime.GetPageData)), "the switch data");
            yield return (AccessTools.DeclaredMethod(typeof(JunctionGeneratedDataRuntime), nameof(JunctionGeneratedDataRuntime.GetMapGridSize)), "the route map's grid");
            yield return (AccessTools.DeclaredMethod(typeof(StaticPagesRender), "GetStaticTemplatePaperData"), "the books' pages");
            yield return (AccessTools.DeclaredMethod(typeof(ChapterSelector), "Start"), "the booklet's chapters");
        }

 internal static string DrawnBy(IEnumerable<(string Owner, string Assembly)> patches) =>
            patches.SelectMany(p => new[] { p.Owner, p.Assembly }).FirstOrDefault(n => n != null && Drawers.Contains(n));

        /// <summary>Whether any page's touch cell names one of Double Track's switches: by its index in the ordered junctions and by its id.</summary>
        internal static bool HoldsAny(JunctionGeneratedDataRuntime.JunctionPageData[] pages, IList<Junction> added, Junction[] ordered)
        {
            if (pages == null || ordered == null)
                return false;
            var theirs = new HashSet<Junction>(added);
            foreach (var page in pages)
                foreach (var c in page.junctionDataCoords ?? new List<JunctionGeneratedDataRuntime.DataCoord>())
                {
                    var i = c.data.junctionIndex;
                    if (i >= 0 && i < ordered.Length && ordered[i] != null && theirs.Contains(ordered[i])
                        && ordered[i].junctionData.junctionIdLong == c.data.junctionIdLong)
                        return true;
                }
            return false;
        }

        /// <summary>A drawing kept in the tablet's assembly (Books/*.json); null if it is missing or unreadable.</summary>
        internal static T LoadDrawing<T>(string name) where T : class
        {
            try
            {
                using (var stream = typeof(DoubleTrackBooks).Assembly.GetManifestResourceStream("CommsTablet.Books." + name))
                {
                    if (stream == null)
                    {
                        Warn($"no drawing {name}");
                        return null;
                    }
                    using (var reader = new StreamReader(stream))
                        return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
                }
            }
            catch (Exception e)
            {
                Warn($"the drawing {name} could not be read: {e.Message}");
                return null;
            }
        }

        internal static void Log(string text) => Main.Mod?.Logger.Log("Double Track maps: " + text);
        internal static void Warn(string text) => Main.Mod?.Logger.Warning("Double Track maps: " + text);
        internal static void Error(string text) => Main.Mod?.Logger.Error("Double Track maps: " + text);
    }
}
