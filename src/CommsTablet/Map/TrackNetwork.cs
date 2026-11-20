using System.Collections.Generic;
using System.Diagnostics;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Map
{
    /// <summary>
    /// The game's track network as the engine-free graph the station
    /// booklet's pages are matched to (<see cref="Core.Booklet.Booklet"/>):
    /// tracks and switches indexed both ways. AI Engineer's <c>Network</c>
    /// (c6e6207, src/AIEngineer/Game/Network.cs) stripped to the graph:
    /// tracks, plain joints, turntable bridges left unjoined, and switches;
    /// its speed curves, map lines, nearest-track grid, and export are
    /// left out. Built once per loaded world (<see cref="MapWorld"/>).
    /// </summary>
    internal sealed class TrackNetwork
    {
        private readonly Dictionary<RailTrack, int> trackIndex = new Dictionary<RailTrack, int>();
        private readonly Dictionary<Junction, int> junctionIndex = new Dictionary<Junction, int>();

        private TrackNetwork() { }

        public TrackGraph Graph { get; private set; }

        public RailTrack[] Tracks { get; private set; }

        public Junction[] Junctions { get; private set; }

        private TrackShapes shapes;

        /// <summary>The tracks' heights, grades, and limits, each read the first time it is wanted.</summary>
        public TrackShapes Shapes => shapes ?? (shapes = new TrackShapes(this));

        public bool TryGetTrack(RailTrack track, out int index) => trackIndex.TryGetValue(track, out index);

        public bool TryGetJunction(Junction junction, out int index) => junctionIndex.TryGetValue(junction, out index);

        public static TrackNetwork Build()
        {
            var watch = Stopwatch.StartNew();
            var network = new TrackNetwork();
            var tracks = RailTrackRegistryBase.RailTracks;
            var junctions = RailTrackRegistryBase.Junctions;
            var builder = new TrackGraphBuilder();
            var usable = new List<RailTrack>(tracks.Length);

            foreach (var track in tracks)
            {
                if (track == null)
                    continue;
                var set = track.GetKinkedPointSet();
                if (set == null || set.points == null || set.points.Length < 2 || !(set.span > 0.01))
                    continue;
                var first = set.points[0].position;
                var last = set.points[set.points.Length - 1].position;
                var index = builder.AddTrack(set.span, new MapPoint(first.x, first.z), new MapPoint(last.x, last.z));
                network.trackIndex[track] = index;
                usable.Add(track);
            }

            // A turntable's bridge rejoins whichever tracks it is turned to, so
            // the graph built now would go stale: bridges are left unjoined, as
            // dead ends both ways.
            var bridges = new HashSet<RailTrack>();
            foreach (var turntable in UnityEngine.Object.FindObjectsOfType<TurntableRailTrack>())
            {
                if (turntable != null && turntable.Track != null)
                    bridges.Add(turntable.Track);
            }

            // Plain joints: each track end tied straight to another's.
            foreach (var track in usable)
            {
                if (bridges.Contains(track))
                    continue;
                var index = network.trackIndex[track];
                if (track.inJunction == null && track.inBranch?.track != null && !bridges.Contains(track.inBranch.track)
                    && network.trackIndex.TryGetValue(track.inBranch.track, out var other))
                    builder.Join(index, TrackEnd.Start, other, track.inBranch.first ? TrackEnd.Start : TrackEnd.End);
                if (track.outJunction == null && track.outBranch?.track != null && !bridges.Contains(track.outBranch.track)
                    && network.trackIndex.TryGetValue(track.outBranch.track, out other))
                    builder.Join(index, TrackEnd.End, other, track.outBranch.first ? TrackEnd.Start : TrackEnd.End);
            }

            // Switches: the stem to each branch, in the game's branch order.
            var usableJunctions = new List<Junction>();
            foreach (var junction in junctions)
            {
                if (junction == null || junction.inBranch?.track == null || junction.outBranches == null || junction.outBranches.Count == 0)
                    continue;
                if (!network.trackIndex.TryGetValue(junction.inBranch.track, out var stem))
                    continue;
                var branches = new (int, TrackEnd)[junction.outBranches.Count];
                var ok = true;
                for (var b = 0; b < branches.Length; b++)
                {
                    var branch = junction.outBranches[b];
                    if (branch?.track == null || !network.trackIndex.TryGetValue(branch.track, out var bt))
                    {
                        ok = false;
                        break;
                    }
                    branches[b] = (bt, branch.first ? TrackEnd.Start : TrackEnd.End);
                }
                if (!ok)
                    continue;
                var pos = junction.position - WorldMover.currentMove;
                var j = builder.AddJunction(stem, junction.inBranch.first ? TrackEnd.Start : TrackEnd.End, branches, new MapPoint(pos.x, pos.z));
                network.junctionIndex[junction] = j;
                usableJunctions.Add(junction);
            }

            network.Graph = builder.Build();
            network.Tracks = usable.ToArray();
            network.Junctions = usableJunctions.ToArray();
            Main.Mod?.Logger.Log($"Map: track network of {usable.Count} tracks, {usableJunctions.Count} switches, {bridges.Count} turntables left out, built in {watch.ElapsedMilliseconds} ms");
            return network;
        }
    }
}
