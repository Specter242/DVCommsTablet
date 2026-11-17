using System;
using System.Collections.Generic;

namespace CommsTablet.Core.Tracks
{
    /// <summary>An end of a track: Start is its curve's first point (offset 0), End its last (offset = length).</summary>
    public enum TrackEnd : byte
    {
        Start = 0,
        End = 1,
    }

    /// <summary>A direction of travel along a track: Forward runs from Start to End, offsets increasing.</summary>
    public enum TrackDirection : sbyte
    {
        Backward = -1,
        Forward = 1,
    }

    public static class TrackEnds
    {
        public static TrackEnd Other(this TrackEnd end) => end == TrackEnd.Start ? TrackEnd.End : TrackEnd.Start;

        public static TrackDirection Reverse(this TrackDirection direction) =>
            direction == TrackDirection.Forward ? TrackDirection.Backward : TrackDirection.Forward;

        /// <summary>The end a train moving this way leaves the track by.</summary>
        public static TrackEnd ExitEnd(this TrackDirection direction) =>
            direction == TrackDirection.Forward ? TrackEnd.End : TrackEnd.Start;

        /// <summary>The direction a train takes along a track it enters by this end.</summary>
        public static TrackDirection DirectionInto(this TrackEnd entered) =>
            entered == TrackEnd.Start ? TrackDirection.Forward : TrackDirection.Backward;
    }

    /// <summary>A point on the network: a track and the distance along it from its Start end, in meters.</summary>
    public readonly struct TrackPosition : IEquatable<TrackPosition>
    {
        public readonly int Track;
        public readonly double Offset;

        public TrackPosition(int track, double offset)
        {
            Track = track;
            Offset = offset;
        }

        public bool Equals(TrackPosition other) => Track == other.Track && Offset == other.Offset;

        public override bool Equals(object obj) => obj is TrackPosition other && Equals(other);

        public override int GetHashCode() => Track * 397 ^ Offset.GetHashCode();

        public override string ToString() => $"track {Track} @ {Offset:F1} m";
    }

    /// <summary>A point in the horizontal plane of the map, in meters (the game's x and z).</summary>
    public readonly struct MapPoint
    {
        public readonly double X;
        public readonly double Z;

        public MapPoint(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double DistanceTo(MapPoint other)
        {
            var dx = X - other.X;
            var dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// A way from one track end onto another: the track and end it enters
    /// by, and the junction setting it needs (none for a plain joint).
    /// </summary>
    public readonly struct TrackLink
    {
        public const int NoJunction = -1;

        public readonly int Track;
        public readonly TrackEnd End;
        public readonly int Junction;
        public readonly int Branch;

        public TrackLink(int track, TrackEnd end, int junction, int branch)
        {
            Track = track;
            End = end;
            Junction = junction;
            Branch = branch;
        }

        public bool NeedsJunction => Junction != NoJunction;
    }

    /// <summary>A switch: its stem end, its branch ends in the game's branch order, and where it is.</summary>
    public sealed class JunctionInfo
    {
        public JunctionInfo(int stemTrack, TrackEnd stemEnd, (int Track, TrackEnd End)[] branches, MapPoint position)
        {
            StemTrack = stemTrack;
            StemEnd = stemEnd;
            Branches = branches;
            Position = position;
        }

        public int StemTrack { get; }
        public TrackEnd StemEnd { get; }
        public (int Track, TrackEnd End)[] Branches { get; }
        public MapPoint Position { get; }
    }

    /// <summary>
    /// The track network, fixed once built: tracks with their lengths, end
    /// positions, and speed limits, and the links between track ends. A
    /// train entering a switch at its stem may leave by any branch the
    /// switch is set to; one entering by a branch leaves by the stem, and
    /// the router sets the switch for that branch too, as a crew would,
    /// rather than run through it.
    /// </summary>
    public sealed class TrackGraph
    {
        private readonly double[] lengths;
        private readonly MapPoint[] endPoints;
        private readonly TrackLink[][] links;
        private readonly SpeedLimitProfile[] limits;
        private readonly JunctionInfo[] junctions;

        internal TrackGraph(double[] lengths, MapPoint[] endPoints, TrackLink[][] links, SpeedLimitProfile[] limits, JunctionInfo[] junctions)
        {
            this.lengths = lengths;
            this.endPoints = endPoints;
            this.links = links;
            this.limits = limits;
            this.junctions = junctions;
        }

        public int TrackCount => lengths.Length;

        public int JunctionCount => junctions.Length;

        public double Length(int track) => lengths[track];

        public MapPoint EndPoint(int track, TrackEnd end) => endPoints[track * 2 + (int)end];

        public double OffsetOf(int track, TrackEnd end) => end == TrackEnd.Start ? 0 : lengths[track];

        /// <summary>The links leaving the track by this end; empty at a dead end.</summary>
        public TrackLink[] Links(int track, TrackEnd end) => links[track * 2 + (int)end];

        public SpeedLimitProfile Limits(int track) => limits[track];

        public JunctionInfo Junction(int junction) => junctions[junction];

        /// <summary>The track end a link from this end arrives at, with the switch it needs, if one leads there.</summary>
        public bool TryFindLink(int fromTrack, TrackEnd fromEnd, int toTrack, out TrackLink link)
        {
            foreach (var l in Links(fromTrack, fromEnd))
            {
                if (l.Track == toTrack)
                {
                    link = l;
                    return true;
                }
            }
            link = default;
            return false;
        }
    }

    /// <summary>Builds a <see cref="TrackGraph"/> from tracks, plain joints, and switches.</summary>
    public sealed class TrackGraphBuilder
    {
        private readonly List<double> lengths = new List<double>();
        private readonly List<MapPoint> endPoints = new List<MapPoint>();
        private readonly List<SpeedLimitProfile> limits = new List<SpeedLimitProfile>();
        private readonly List<List<TrackLink>> links = new List<List<TrackLink>>();
        private readonly List<JunctionInfo> junctions = new List<JunctionInfo>();

        public int AddTrack(double length, MapPoint start, MapPoint end, SpeedLimitProfile limit = null)
        {
            if (!(length > 0))
                throw new ArgumentOutOfRangeException(nameof(length), length, "A track needs a positive length.");
            lengths.Add(length);
            endPoints.Add(start);
            endPoints.Add(end);
            limits.Add(limit ?? SpeedLimitProfile.Unlimited);
            links.Add(new List<TrackLink>());
            links.Add(new List<TrackLink>());
            return lengths.Count - 1;
        }

        /// <summary>Joins two track ends directly, as two rails butted together.</summary>
        public void Join(int trackA, TrackEnd endA, int trackB, TrackEnd endB)
        {
            CheckTrack(trackA);
            CheckTrack(trackB);
            AddUnique(trackA, endA, new TrackLink(trackB, endB, TrackLink.NoJunction, -1));
            AddUnique(trackB, endB, new TrackLink(trackA, endA, TrackLink.NoJunction, -1));
        }

        /// <summary>Adds a switch whose stem end leads to each branch end; returns its index.</summary>
        public int AddJunction(int stemTrack, TrackEnd stemEnd, (int Track, TrackEnd End)[] branches, MapPoint position)
        {
            CheckTrack(stemTrack);
            if (branches == null || branches.Length == 0)
                throw new ArgumentException("A junction needs at least one branch.", nameof(branches));
            var index = junctions.Count;
            junctions.Add(new JunctionInfo(stemTrack, stemEnd, (ValueTuple<int, TrackEnd>[])branches.Clone(), position));
            for (var b = 0; b < branches.Length; b++)
            {
                CheckTrack(branches[b].Track);
                AddUnique(stemTrack, stemEnd, new TrackLink(branches[b].Track, branches[b].End, index, b));
                AddUnique(branches[b].Track, branches[b].End, new TrackLink(stemTrack, stemEnd, index, b));
            }
            return index;
        }

        public TrackGraph Build()
        {
            var linkArrays = new TrackLink[links.Count][];
            for (var i = 0; i < links.Count; i++)
                linkArrays[i] = links[i].ToArray();
            return new TrackGraph(lengths.ToArray(), endPoints.ToArray(), linkArrays, limits.ToArray(), junctions.ToArray());
        }

        private void AddUnique(int track, TrackEnd end, TrackLink link)
        {
            var list = links[track * 2 + (int)end];
            foreach (var existing in list)
            {
                if (existing.Track == link.Track && existing.End == link.End && existing.Junction == link.Junction && existing.Branch == link.Branch)
                    return;
            }
            list.Add(link);
        }

        private void CheckTrack(int track)
        {
            if (track < 0 || track >= lengths.Count)
                throw new ArgumentOutOfRangeException(nameof(track), track, "No such track.");
        }
    }
}
