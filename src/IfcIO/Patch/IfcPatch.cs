using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Query;

namespace IfcIO.Patch;

/// <summary>
/// Statistics report returned after running <see cref="IfcPatch.Purge"/>.
/// </summary>
public sealed record IfcPurgeStats
{
    public int RemovedPropertySets { get; init; }
    public int RemovedQuantitySets { get; init; }
    public int RemovedEmptyStoreys { get; init; }
}

/// <summary>
/// Provides powerful model repair, cleanup, coordinate shifting, extraction, and merging recipes
/// for Industry Foundation Classes (IFC) models, conforming to the IfcOpenShell `ifcpatch` recipe architecture.
/// </summary>
public static class IfcPatch
{
    /// <summary>
    /// Purges unreferenced, empty, or duplicate property sets, quantity sets, and empty building storeys.
    /// </summary>
    public static IfcPurgeStats Purge(IfcModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        int removedPsets = 0;
        int removedQtos = 0;
        int removedStoreys = 0;

        foreach (var element in model.AllElements)
        {
            // 1. Remove empty and duplicate property sets
            var seenPsets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = element.PropertySets.Count - 1; i >= 0; i--)
            {
                var pset = element.PropertySets[i];
                if (pset.Properties.Count == 0 || !seenPsets.Add(pset.Name))
                {
                    element.PropertySets.RemoveAt(i);
                    removedPsets++;
                }
            }

            // 2. Remove empty and duplicate quantity sets
            var seenQtos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = element.QuantitySets.Count - 1; i >= 0; i--)
            {
                var qto = element.QuantitySets[i];
                if (qto.Quantities.Count == 0 || !seenQtos.Add(qto.Name))
                {
                    element.QuantitySets.RemoveAt(i);
                    removedQtos++;
                }
            }
        }

        // 3. Remove storeys that have neither elements nor spaces
        for (int s = model.Storeys.Count - 1; s >= 0; s--)
        {
            var storey = model.Storeys[s];
            if (storey.Elements.Count == 0 && storey.Spaces.Count == 0)
            {
                model.Storeys.RemoveAt(s);
                removedStoreys++;
            }
        }

        return new IfcPurgeStats
        {
            RemovedPropertySets = removedPsets,
            RemovedQuantitySets = removedQtos,
            RemovedEmptyStoreys = removedStoreys,
        };
    }

    /// <summary>
    /// Offsets all 3D mesh vertices across all elements in the model by a specified translation vector.
    /// </summary>
    public static void OffsetCoordinates(IfcModel model, IfcVector3 offset)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Math.Abs(offset.X) < 1e-9 && Math.Abs(offset.Y) < 1e-9 && Math.Abs(offset.Z) < 1e-9)
        {
            return;
        }

        foreach (var element in model.AllElements)
        {
            if (element.Mesh is not null && element.Mesh.Vertices.Count > 0)
            {
                var newVerts = new List<IfcVector3>(element.Mesh.Vertices.Count);
                for (int i = 0; i < element.Mesh.Vertices.Count; i++)
                {
                    newVerts.Add(element.Mesh.Vertices[i] + offset);
                }

                element.Mesh = IfcMesh.Create(
                    element.Mesh.Id,
                    element.Mesh.Name,
                    newVerts,
                    element.Mesh.Indices);
            }
        }
    }

    /// <summary>
    /// Detects large absolute coordinate offsets (e.g. Revit/Civil UTM coordinates exceeding <paramref name="thresholdDistance"/> meters)
    /// and shifts model geometry back to the local origin (0, 0, 0) to eliminate 32-bit/64-bit floating point precision jitter.
    /// </summary>
    /// <param name="model">The IFC model to check and recenter.</param>
    /// <param name="offset">Output parameter receiving the negative offset vector that was applied.</param>
    /// <param name="thresholdDistance">Distance from origin above which coordinates are recentered (default: 10,000 meters).</param>
    /// <returns>True if coordinates were recentered; otherwise false.</returns>
    public static bool ResetCoordinates(IfcModel model, out IfcVector3 offset, double thresholdDistance = 10000.0)
    {
        ArgumentNullException.ThrowIfNull(model);

        offset = IfcVector3.Zero;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        int vertexCount = 0;

        foreach (var element in model.AllElements)
        {
            var mesh = element.GetEffectiveMesh();
            if (mesh is null) continue;
            foreach (var v in mesh.Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z > maxZ) maxZ = v.Z;
                vertexCount++;
            }
        }

        if (vertexCount == 0) return false;

        var center = new IfcVector3(
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            (minZ + maxZ) * 0.5);

        double distFromOrigin = Math.Sqrt(center.X * center.X + center.Y * center.Y);
        if (distFromOrigin < thresholdDistance)
        {
            return false;
        }

        // Shift X and Y towards local origin (preserve Z elevation)
        offset = new IfcVector3(-center.X, -center.Y, 0.0);
        OffsetCoordinates(model, offset);
        return true;
    }

    /// <summary>
    /// Extracts a subset of elements matching an <see cref="IfcSelector"/> query string into a new, self-contained IFC model.
    /// </summary>
    public static IfcModel Extract(IfcModel source, string query, string? newProjectName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        var selector = IfcSelector.Parse(query);
        return Extract(source, e => selector.Matches(e, source), newProjectName);
    }

    /// <summary>
    /// Extracts a subset of elements matching a predicate function into a new, self-contained IFC model.
    /// Preserves spatial storeys, project units, materials, and property sets of extracted elements.
    /// </summary>
    public static IfcModel Extract(IfcModel source, Func<IfcElement, bool> predicate, string? newProjectName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        var target = new IfcModel
        {
            ProjectName = newProjectName ?? $"{source.ProjectName} (Extracted)",
            BuildingName = source.BuildingName,
            Author = source.Author,
            Organization = source.Organization,
            Application = source.Application,
            LengthUnit = source.LengthUnit,
            AngleUnit = source.AngleUnit,
            Schema = source.Schema,
        };
        target.Storeys.Clear();

        int nextId = 1;

        foreach (var srcStorey in source.Storeys)
        {
            var matchedElements = srcStorey.Elements.Where(predicate).ToList();
            if (matchedElements.Count == 0) continue;

            var targetStorey = new IfcBuildingStorey
            {
                Name = srcStorey.Name,
                Elevation = srcStorey.Elevation,
            };

            foreach (var elem in matchedElements)
            {
                var clone = CloneElement(elem, nextId++);
                targetStorey.Elements.Add(clone);
            }

            target.Storeys.Add(targetStorey);
        }

        return target;
    }

    /// <summary>
    /// Merges multiple IFC models into a single coordinated model, reconciling storeys by name/elevation
    /// and assigning unique, non-colliding entity IDs.
    /// </summary>
    public static IfcModel Merge(IEnumerable<IfcModel> models, string mergedProjectName = "Merged Project")
    {
        ArgumentNullException.ThrowIfNull(models);

        var modelList = models.ToList();
        if (modelList.Count == 0)
        {
            throw new ArgumentException("At least one model must be provided for merge.", nameof(models));
        }

        if (modelList.Count == 1)
        {
            return modelList[0];
        }

        var baseModel = modelList[0];
        var merged = new IfcModel
        {
            ProjectName = mergedProjectName,
            BuildingName = baseModel.BuildingName,
            Author = baseModel.Author,
            Organization = baseModel.Organization,
            Application = baseModel.Application,
            LengthUnit = baseModel.LengthUnit,
            AngleUnit = baseModel.AngleUnit,
            Schema = modelList.Any(m => m.Schema == Schema.IfcSchema.Ifc4) ? Schema.IfcSchema.Ifc4 : Schema.IfcSchema.Ifc2x3,
        };
        merged.Storeys.Clear();

        int nextId = 1;

        foreach (var model in modelList)
        {
            foreach (var storey in model.Storeys)
            {
                // Find matching storey by name or elevation within 50mm tolerance
                var targetStorey = merged.Storeys.FirstOrDefault(s =>
                    s.Name.Equals(storey.Name, StringComparison.OrdinalIgnoreCase) ||
                    Math.Abs(s.Elevation - storey.Elevation) < 0.05);

                if (targetStorey is null)
                {
                    targetStorey = new IfcBuildingStorey
                    {
                        Name = storey.Name,
                        Elevation = storey.Elevation,
                    };
                    merged.Storeys.Add(targetStorey);
                }

                foreach (var elem in storey.Elements)
                {
                    var clone = CloneElement(elem, nextId++);
                    targetStorey.Elements.Add(clone);
                }
            }
        }

        // Sort storeys by elevation ascending
        merged.Storeys.Sort((a, b) => a.Elevation.CompareTo(b.Elevation));
        return merged;
    }

    private static IfcElement CloneElement(IfcElement source, int newId)
    {
        var clone = new IfcElement
        {
            Id = newId,
            Name = source.Name,
            IfcType = source.IfcType,
            GlobalId = source.GlobalId ?? Guid.NewGuid().ToString("N")[..22],
            Tag = source.Tag,
            TypeProduct = source.TypeProduct,
            Material = source.Material,
            Mesh = source.Mesh is not null ? IfcMesh.Create(
                newId,
                source.Mesh.Name,
                new List<IfcVector3>(source.Mesh.Vertices),
                new List<int>(source.Mesh.Indices)) : null,
            Solid = source.Solid,
            RevolvedSolid = source.RevolvedSolid,
            BooleanResult = source.BooleanResult,
        };

        foreach (var pset in source.PropertySets)
        {
            var psetClone = new IfcPropertySet(pset.Name);
            foreach (var p in pset.Properties)
            {
                psetClone.Properties.Add(new IfcPropertySingleValue { Name = p.Name, Value = p.Value, Type = p.Type });
            }
            clone.PropertySets.Add(psetClone);
        }

        foreach (var qto in source.QuantitySets)
        {
            var qtoClone = new IfcQuantitySet(qto.Name, qto.Description, qto.MethodOfMeasurement);
            foreach (var q in qto.Quantities)
            {
                switch (q)
                {
                    case IfcQuantityLength l: qtoClone.AddLength(l.Name, l.LengthValue, l.Description); break;
                    case IfcQuantityArea a: qtoClone.AddArea(a.Name, a.AreaValue, a.Description); break;
                    case IfcQuantityVolume v: qtoClone.AddVolume(v.Name, v.VolumeValue, v.Description); break;
                    case IfcQuantityCount c: qtoClone.AddCount(c.Name, c.CountValue, c.Description); break;
                    case IfcQuantityWeight w: qtoClone.AddWeight(w.Name, w.WeightValue, w.Description); break;
                }
            }
            clone.QuantitySets.Add(qtoClone);
        }

        return clone;
    }
}
