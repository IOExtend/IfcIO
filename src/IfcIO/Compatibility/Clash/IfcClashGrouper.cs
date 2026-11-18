using System.Globalization;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Clash;

/// <summary>
/// Provides intelligent grouping and spatial clustering algorithms for IFC clash detection results,
/// bundling collisions by 3D proximity, primary MEP/structural element, building storey, or grid intersection.
/// </summary>
public static class IfcClashGrouper
{
    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public DisjointSet(int size)
        {
            _parent = new int[size];
            _rank = new int[size];
            for (int i = 0; i < size; i++) _parent[i] = i;
        }

        public int Find(int i)
        {
            if (_parent[i] != i) _parent[i] = Find(_parent[i]);
            return _parent[i];
        }

        public void Union(int x, int y)
        {
            int rx = Find(x);
            int ry = Find(y);
            if (rx == ry) return;

            if (_rank[rx] < _rank[ry]) _parent[rx] = ry;
            else if (_rank[rx] > _rank[ry]) _parent[ry] = rx;
            else
            {
                _parent[ry] = rx;
                _rank[rx]++;
            }
        }
    }

    /// <summary>
    /// Groups clashes by spatial 3D proximity using a Disjoint-Set (Union-Find) clustering algorithm.
    /// </summary>
    public static int GroupByProximity(
        this IfcClashReport report,
        double clusterDistance = 1.0,
        string groupPrefix = "Cluster",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Clashes.Count < minGroupSize) return 0;

        int n = report.Clashes.Count;
        var ds = new DisjointSet(n);
        double distSqThreshold = clusterDistance * clusterDistance;

        for (int i = 0; i < n; i++)
        {
            var ptA = report.Clashes[i].ClashPoint;
            for (int j = i + 1; j < n; j++)
            {
                var ptB = report.Clashes[j].ClashPoint;
                double dx = ptA.X - ptB.X;
                double dy = ptA.Y - ptB.Y;
                double dz = ptA.Z - ptB.Z;
                double dSq = dx * dx + dy * dy + dz * dz;

                if (dSq <= distSqThreshold)
                {
                    ds.Union(i, j);
                }
            }
        }

        var clusters = new Dictionary<int, List<IfcClashResult>>();
        for (int i = 0; i < n; i++)
        {
            int root = ds.Find(i);
            if (!clusters.TryGetValue(root, out var list))
            {
                list = [];
                clusters[root] = list;
            }
            list.Add(report.Clashes[i]);
        }

        int groupsCreated = 0;
        var toRemoveFromClashes = new HashSet<IfcClashResult>();

        foreach (var cluster in clusters.Values)
        {
            if (cluster.Count >= minGroupSize)
            {
                groupsCreated++;
                string groupName = $"{groupPrefix} {report.Groups.Count + 1}";
                var group = new IfcClashGroup(groupName, cluster[0].Status)
                {
                    Description = $"Spatial cluster of {cluster.Count} clashes within {clusterDistance:F2} m proximity.",
                    GridLocation = cluster[0].GridLocation,
                };

                foreach (var clash in cluster)
                {
                    group.AddClash(clash);
                    toRemoveFromClashes.Add(clash);
                }

                report.Groups.Add(group);
            }
        }

        report.Clashes.RemoveAll(toRemoveFromClashes.Contains);
        return groupsCreated;
    }

    /// <summary>
    /// Groups clashes by primary element (ElementA), bundling collisions involving the same MEP run or structural beam.
    /// </summary>
    public static int GroupByPrimaryElement(
        this IfcClashReport report,
        string groupPrefix = "Element",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Clashes.Count < minGroupSize) return 0;

        var buckets = new Dictionary<string, List<IfcClashResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var clash in report.Clashes)
        {
            string key = !string.IsNullOrEmpty(clash.ElementA.GlobalId)
                ? clash.ElementA.GlobalId
                : (!string.IsNullOrEmpty(clash.ElementA.Name) ? clash.ElementA.Name : $"Element_{clash.ElementA.Id}");

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(clash);
        }

        return ApplyBuckets(report, buckets, groupPrefix, minGroupSize);
    }

    /// <summary>
    /// Groups clashes by building storey / level.
    /// </summary>
    public static int GroupByStorey(
        this IfcClashReport report,
        IfcModel? model = null,
        string groupPrefix = "Storey",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Clashes.Count < minGroupSize) return 0;

        // Build element to storey map if model is available
        var elemStoreyMap = new Dictionary<int, string>();
        if (model is not null)
        {
            foreach (var s in model.Storeys)
            {
                foreach (var e in s.Elements)
                {
                    elemStoreyMap[e.Id] = s.Name;
                }
            }
        }

        var buckets = new Dictionary<string, List<IfcClashResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var clash in report.Clashes)
        {
            string storey = "Default";
            if (elemStoreyMap.TryGetValue(clash.ElementA.Id, out var sA))
            {
                storey = sA;
            }
            else if (elemStoreyMap.TryGetValue(clash.ElementB.Id, out var sB))
            {
                storey = sB;
            }

            if (!buckets.TryGetValue(storey, out var list))
            {
                list = [];
                buckets[storey] = list;
            }
            list.Add(clash);
        }

        return ApplyBuckets(report, buckets, groupPrefix, minGroupSize);
    }

    /// <summary>
    /// Groups clashes by structural grid location (e.g. "A / 1", "B / 4").
    /// </summary>
    public static int GroupByGrid(
        this IfcClashReport report,
        string groupPrefix = "Grid",
        int minGroupSize = 2)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Clashes.Count < minGroupSize) return 0;

        var buckets = new Dictionary<string, List<IfcClashResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var clash in report.Clashes)
        {
            string key = !string.IsNullOrEmpty(clash.GridLocation) ? clash.GridLocation : "Unassigned";

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(clash);
        }

        return ApplyBuckets(report, buckets, groupPrefix, minGroupSize);
    }

    /// <summary>
    /// Dissolves all groups in the report, returning their clashes back to the unbundled list.
    /// </summary>
    public static int UngroupAll(this IfcClashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        int dissolvedClashes = 0;

        foreach (var group in report.Groups)
        {
            dissolvedClashes += group.Clashes.Count;
            report.Clashes.AddRange(group.Clashes);
        }

        report.Groups.Clear();
        return dissolvedClashes;
    }

    private static int ApplyBuckets(
        IfcClashReport report,
        Dictionary<string, List<IfcClashResult>> buckets,
        string groupPrefix,
        int minGroupSize)
    {
        int groupsCreated = 0;
        var toRemoveFromClashes = new HashSet<IfcClashResult>();

        foreach (var kvp in buckets)
        {
            var bucket = kvp.Value;
            if (bucket.Count >= minGroupSize)
            {
                groupsCreated++;
                string groupName = $"{groupPrefix}: {kvp.Key}";
                var group = new IfcClashGroup(groupName, bucket[0].Status)
                {
                    Description = $"Clashes grouped by {groupPrefix} '{kvp.Key}' ({bucket.Count} items).",
                    GridLocation = bucket[0].GridLocation,
                };

                foreach (var clash in bucket)
                {
                    group.AddClash(clash);
                    toRemoveFromClashes.Add(clash);
                }

                report.Groups.Add(group);
            }
        }

        report.Clashes.RemoveAll(toRemoveFromClashes.Contains);
        return groupsCreated;
    }
}
