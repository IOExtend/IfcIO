using System.Diagnostics;
using System.Globalization;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Diff;

/// <summary>
/// Advanced semantic and geometric model comparison engine for IFC revisions (conforming to IfcOpenShell ifcdiff).
/// Detects added, deleted, and modified elements, capturing geometric shifts, property mutations, and spatial re-assignments.
/// </summary>
public static class IfcDiffEngine
{
    /// <summary>
    /// Compares two revisions of an IFC model (<paramref name="baseline"/> vs <paramref name="revision"/>).
    /// </summary>
    public static IfcDiffReport Compare(IfcModel baseline, IfcModel revision, IfcDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(revision);

        options ??= new IfcDiffOptions();
        var sw = Stopwatch.StartNew();

        var report = new IfcDiffReport
        {
            BaselineName = baseline.ProjectName,
            RevisionName = revision.ProjectName,
            GeometricTolerance = options.GeometricTolerance,
        };

        var baseElems = GetParticipatingElements(baseline, options.FilterSelector);
        var revElems = GetParticipatingElements(revision, options.FilterSelector);

        var baseStoreyMap = BuildStoreyMap(baseline);
        var revStoreyMap = BuildStoreyMap(revision);

        var baseMap = new Dictionary<string, IfcElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var elem in baseElems)
        {
            string key = GetElementKey(elem);
            baseMap[key] = elem;
        }

        var revMap = new Dictionary<string, IfcElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var elem in revElems)
        {
            string key = GetElementKey(elem);
            revMap[key] = elem;
        }

        // 1. Detect Added and Modified Elements
        foreach (var (key, revElem) in revMap)
        {
            if (!baseMap.TryGetValue(key, out var baseElem))
            {
                // Added
                report.Added.Add(new IfcElementDiff
                {
                    GlobalId = key,
                    Name = revElem.Name,
                    IfcType = revElem.IfcType,
                    ChangeKind = IfcChangeKind.Added,
                    RevisionElement = revElem,
                });
            }
            else
            {
                // Element exists in both revisions: check for modifications
                var diff = CompareElement(baseElem, revElem, baseStoreyMap, revStoreyMap, options);
                if (diff.ChangeKind == IfcChangeKind.Modified)
                {
                    report.Modified.Add(diff);
                }
                else
                {
                    report.Unchanged.Add(diff);
                }
            }
        }

        // 2. Detect Deleted Elements
        foreach (var (key, baseElem) in baseMap)
        {
            if (!revMap.ContainsKey(key))
            {
                report.Deleted.Add(new IfcElementDiff
                {
                    GlobalId = key,
                    Name = baseElem.Name,
                    IfcType = baseElem.IfcType,
                    ChangeKind = IfcChangeKind.Deleted,
                    BaselineElement = baseElem,
                });
            }
        }

        sw.Stop();
        report.Elapsed = sw.Elapsed;
        return report;
    }

    private static IfcElementDiff CompareElement(
        IfcElement baseElem,
        IfcElement revElem,
        Dictionary<int, IfcBuildingStorey> baseStoreys,
        Dictionary<int, IfcBuildingStorey> revStoreys,
        IfcDiffOptions options)
    {
        var diff = new IfcElementDiff
        {
            GlobalId = GetElementKey(revElem),
            Name = revElem.Name,
            IfcType = revElem.IfcType,
            BaselineElement = baseElem,
            RevisionElement = revElem,
            ChangeKind = IfcChangeKind.Unchanged,
        };

        bool hasChange = false;

        // 1. Geometry Comparison
        var meshA = baseElem.GetEffectiveMesh();
        var meshB = revElem.GetEffectiveMesh();

        if ((meshA is null) != (meshB is null))
        {
            diff.GeometryChanged = true;
            hasChange = true;
        }
        else if (meshA is not null && meshB is not null)
        {
            var boxA = meshA.BoundingBox;
            var boxB = meshB.BoundingBox;

            var translation = boxB.Center - boxA.Center;
            double moveDist = translation.Length;

            var sizeDelta = boxB.Size - boxA.Size;
            double sizeChange = sizeDelta.Length;

            if (moveDist > options.GeometricTolerance || sizeChange > options.GeometricTolerance || meshA.Vertices.Count != meshB.Vertices.Count)
            {
                diff.GeometryChanged = true;
                diff.TranslationDelta = translation;
                hasChange = true;
            }
        }

        // 2. Storey Comparison
        if (options.CompareStoreys)
        {
            baseStoreys.TryGetValue(baseElem.Id, out var sA);
            revStoreys.TryGetValue(revElem.Id, out var sB);

            string storeyA = sA?.Name ?? string.Empty;
            string storeyB = sB?.Name ?? string.Empty;

            if (!storeyA.Equals(storeyB, StringComparison.OrdinalIgnoreCase))
            {
                diff.StoreyChanged = true;
                diff.OldStorey = storeyA;
                diff.NewStorey = storeyB;
                hasChange = true;
            }
        }

        // 3. Type Comparison
        if (options.CompareTypes)
        {
            string typeA = baseElem.TypeProduct?.Name ?? string.Empty;
            string typeB = revElem.TypeProduct?.Name ?? string.Empty;

            if (!typeA.Equals(typeB, StringComparison.OrdinalIgnoreCase))
            {
                diff.TypeChanged = true;
                diff.OldType = typeA;
                diff.NewType = typeB;
                hasChange = true;
            }
        }

        // 4. PropertySets Comparison
        if (options.CompareProperties)
        {
            var propsA = FlattenProperties(baseElem);
            var propsB = FlattenProperties(revElem);

            foreach (var (pKey, bVal) in propsB)
            {
                int dotIdx = pKey.IndexOf('.', StringComparison.Ordinal);
                string pset = dotIdx >= 0 ? pKey[..dotIdx] : "PropertySet";
                string pName = dotIdx >= 0 ? pKey[(dotIdx + 1)..] : pKey;

                if (!propsA.TryGetValue(pKey, out var aVal))
                {
                    diff.PropertyChanges.Add(new IfcPropertyChange(pset, pName, null, bVal, "Added"));
                    hasChange = true;
                }
                else if (!string.Equals(aVal, bVal, StringComparison.Ordinal))
                {
                    diff.PropertyChanges.Add(new IfcPropertyChange(pset, pName, aVal, bVal, "Modified"));
                    hasChange = true;
                }
            }

            foreach (var (pKey, aVal) in propsA)
            {
                if (!propsB.ContainsKey(pKey))
                {
                    int dotIdx = pKey.IndexOf('.', StringComparison.Ordinal);
                    string pset = dotIdx >= 0 ? pKey[..dotIdx] : "PropertySet";
                    string pName = dotIdx >= 0 ? pKey[(dotIdx + 1)..] : pKey;

                    diff.PropertyChanges.Add(new IfcPropertyChange(pset, pName, aVal, null, "Removed"));
                    hasChange = true;
                }
            }
        }

        // 5. QuantitySets Comparison
        if (options.CompareQuantities)
        {
            var qtoA = FlattenQuantities(baseElem);
            var qtoB = FlattenQuantities(revElem);

            foreach (var (qKey, bVal) in qtoB)
            {
                int dotIdx = qKey.IndexOf('.', StringComparison.Ordinal);
                string qset = dotIdx >= 0 ? qKey[..dotIdx] : "QuantitySet";
                string qName = dotIdx >= 0 ? qKey[(dotIdx + 1)..] : qKey;

                if (!qtoA.TryGetValue(qKey, out double aVal))
                {
                    diff.PropertyChanges.Add(new IfcPropertyChange(qset, qName, null, bVal.ToString("F3", CultureInfo.InvariantCulture), "Added"));
                    hasChange = true;
                }
                else if (Math.Abs(aVal - bVal) > 1e-4)
                {
                    diff.PropertyChanges.Add(new IfcPropertyChange(qset, qName, aVal.ToString("F3", CultureInfo.InvariantCulture), bVal.ToString("F3", CultureInfo.InvariantCulture), "Modified"));
                    hasChange = true;
                }
            }

            foreach (var (qKey, aVal) in qtoA)
            {
                if (!qtoB.ContainsKey(qKey))
                {
                    int dotIdx = qKey.IndexOf('.', StringComparison.Ordinal);
                    string qset = dotIdx >= 0 ? qKey[..dotIdx] : "QuantitySet";
                    string qName = dotIdx >= 0 ? qKey[(dotIdx + 1)..] : qKey;

                    diff.PropertyChanges.Add(new IfcPropertyChange(qset, qName, aVal.ToString("F3", CultureInfo.InvariantCulture), null, "Removed"));
                    hasChange = true;
                }
            }
        }

        if (hasChange)
        {
            diff.ChangeKind = IfcChangeKind.Modified;
        }

        return diff;
    }

    private static string GetElementKey(IfcElement elem)
    {
        if (!string.IsNullOrEmpty(elem.GlobalId)) return elem.GlobalId;
        if (!string.IsNullOrEmpty(elem.Tag)) return elem.Tag;
        return string.Create(CultureInfo.InvariantCulture, $"{elem.IfcType}#{elem.Id}_{elem.Name}");
    }

    private static Dictionary<string, string> FlattenProperties(IfcElement elem)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pset in elem.PropertySets)
        {
            foreach (var prop in pset.Properties)
            {
                string key = $"{pset.Name}.{prop.Name}";
                dict[key] = prop.Value;
            }
        }
        return dict;
    }

    private static Dictionary<string, double> FlattenQuantities(IfcElement elem)
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var qset in elem.QuantitySets)
        {
            foreach (var q in qset.Quantities)
            {
                string key = $"{qset.Name}.{q.Name}";
                dict[key] = q.Value;
            }
        }
        return dict;
    }

    private static List<IfcElement> GetParticipatingElements(IfcModel model, string? selector)
    {
        return !string.IsNullOrEmpty(selector)
            ? model.Select(selector).ToList()
            : model.AllElements.ToList();
    }

    private static Dictionary<int, IfcBuildingStorey> BuildStoreyMap(IfcModel model)
    {
        var map = new Dictionary<int, IfcBuildingStorey>();
        foreach (var storey in model.Storeys)
        {
            foreach (var elem in storey.Elements)
            {
                map[elem.Id] = storey;
            }
        }
        return map;
    }
}
