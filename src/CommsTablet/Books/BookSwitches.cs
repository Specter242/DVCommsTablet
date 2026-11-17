using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CommsTablet.Books
{
    /// <summary>
    /// Double Track's switches found in this world: each one a drawing names
    /// by key, found by its id and its place (the map frame, within 3 m), with
    /// its index in the registry's ordered junctions (what a touched cell
    /// names). The standalone Double Track Maps' <c>Switches</c>.
    /// </summary>
    internal sealed class BookSwitches
    {
        public readonly Dictionary<string, Junction> Junctions = new Dictionary<string, Junction>();
        public readonly Dictionary<string, int> Indices = new Dictionary<string, int>();
        public RailTrackRegistryBase Registry;

        /// <summary>Whether Double Track is loaded (its list of the switches it built exists).</summary>
        internal static bool Loaded => AddedField() != null;

        private static System.Reflection.FieldInfo AddedField()
        {
            var type = AccessTools.TypeByName("DoubleTrack.AllTracksPatch");
            return type == null ? null : AccessTools.Field(type, "AddedJunctions");
        }

        /// <summary>
        /// What Double Tracked Valley built, from its own list
        /// (<c>DoubleTrack.AllTracksPatch.AddedJunctions</c>, filled as it
        /// edits the railway scene); null without Double Track. Read by
        /// reflection, so the tablet loads without it.
        /// </summary>
        internal static List<Junction> Added() =>
            (AddedField()?.GetValue(null) as IEnumerable<Junction>)?.Where(j => j != null).ToList();

        internal static Vector3 MapOf(RailTrackRegistryBase registry, Vector3 world)
        {
            var root = registry.TrackRootParent;
            return root.parent != null ? root.parent.InverseTransformPoint(world) : world;
        }

        /// <summary>Finds every switch in <paramref name="wanted"/> (key: id and map position); null when all are found, else why not.</summary>
        public string Find(RailTrackRegistryBase registry, int expectedTotal, IEnumerable<KeyValuePair<string, (string Id, float[] Pos)>> wanted)
        {
            Registry = registry;
            var added = Added();
            if (added == null)
                return "Double Track is not loaded";
            if (added.Count != expectedTotal)
                return $"Double Track built {added.Count} switches, the drawing knows {expectedTotal} (another layout or version)";
            var ordered = registry.OrderedJunctions;
            var index = new Dictionary<Junction, int>();
            for (var i = 0; i < ordered.Length; i++)
                index[ordered[i]] = i;
            foreach (var kv in wanted)
            {
                var want = new Vector3(kv.Value.Pos[0], kv.Value.Pos[1], kv.Value.Pos[2]);
                Junction best = null;
                var bestGap = float.MaxValue;
                foreach (var j in added)
                {
                    if (j.junctionData.junctionIdLong != kv.Value.Id)
                        continue;
                    var gap = Vector3.Distance(MapOf(registry, j.transform.position), want);
                    if (gap < bestGap)
                    {
                        best = j;
                        bestGap = gap;
                    }
                }
                if (best == null || bestGap > 3f)
                    return $"Double Track switch {kv.Key} not found{(best != null ? $" (nearest {bestGap:F1} m away)" : "")}";
                if (!index.TryGetValue(best, out var at))
                    return $"Double Track switch {kv.Key} is not among the ordered junctions";
                if (Junctions.TryGetValue(kv.Key, out var had))
                {
                    if (had != best)
                        return $"Double Track switch {kv.Key} found twice";
                    continue;
                }
                if (Junctions.Values.Contains(best))
                    return $"Double Track switch {kv.Key} is another key's too";
                Junctions[kv.Key] = best;
                Indices[kv.Key] = at;
            }
            return null;
        }

        /// <summary>A switch's data for a touch cell: its index, its place, shown on the map.</summary>
        public Junction.JunctionData DataFor(string key)
        {
            var j = Junctions[key];
            var data = j.junctionData;
            data.junctionIndex = Indices[key];
            data.position = MapOf(Registry, j.transform.position);
            data.excludeFromJunctionMap = false;
            data.isValid = true;
            data.linkedJunctions = new List<int>();
            return data;
        }
    }
}
