using System.Globalization;
using System.Reflection;
using IfcIO.Model;
using IfcIO.Model.Sequence;
using IfcIO.Schema;
using IOScene;

namespace IfcIO.Scene;

/// <summary>
/// Converts an IFC file, or an <see cref="IfcModel"/> read by IfcIO, into a <see cref="SceneDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The scene has one <see cref="SceneModel"/>: <c>Key</c> is <see cref="IfcSceneOptions.ModelKey"/>, else the file
/// name without directory; <c>SourceFormat</c> is <c>ifc</c>. Geometry is IfcIO's world mesh of each element
/// (<see cref="IfcElement.GetEffectiveMesh"/>), scaled from <see cref="IfcModel.LengthUnit"/> to metres by an exact
/// factor; IFC is Z up like the scene, so axes are not swapped. Each mesh becomes one geometry placed once, without
/// a transform.
/// </para>
/// <para>
/// Each storey becomes a level (elevation in metres) and a tree node of kind <see cref="SceneNodeKind.Level"/>.
/// Each element of the storey (and each space with <see cref="IfcSceneOptions.IncludeSpaces"/>) becomes a child of
/// that node, except the type objects (for example <c>IFCWALLTYPE</c>, <c>IFCDOORSTYLE</c>) and lining and panel
/// properties that IfcIO also lists as elements. <see cref="SceneElement.Id"/> is <see cref="IfcElement.Id"/>, which
/// for a model opened from a file is the STEP entity id (<c>#123</c> gives 123); <see cref="SceneElement.SourceId"/>
/// and <see cref="SceneElement.IfcGuid"/> are the GlobalId; <see cref="SceneElement.Category"/> is the IFC class as
/// IfcIO reports it (upper case, for example <c>IFCWALL</c>). IfcIO places an element in a storey only through
/// <c>IfcRelContainedInSpatialStructure</c>, so elements contained in the site or building, and spaces aggregated to
/// a storey, are in the first storey.
/// </para>
/// <para>
/// Property sets and quantity sets become property sets keyed <c>ifc:&lt;SetName&gt;</c>, their properties
/// <c>ifc:&lt;SetName&gt;.&lt;Name&gt;</c>; a set that IfcIO shares between elements is one set in the scene.
/// Property values are text as IfcIO reads them. Quantity values are numbers in the file's units, so their
/// <see cref="SceneValue.Unit"/> is null. Colour and transparency come from <see cref="IfcElement.Material"/>;
/// elements without one get a colour for their IFC class. Work schedules become schedules. Openings are not converted.
/// </para>
/// </remarks>
public static class IfcSceneReader
{
    private const int ModelId = 1;

    /// <summary>
    /// Reads the IFC file at <paramref name="path"/> (<c>.ifc</c> or <c>.ifczip</c>) with <see cref="IfcModel.Open(string)"/>
    /// and converts it.
    /// </summary>
    /// <param name="path">The IFC file.</param>
    /// <param name="options">Conversion options; null uses the defaults.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="IfcSceneOptions.ModelKey"/> is empty, or two converted elements have the same <see cref="IfcElement.Id"/>.
    /// </exception>
    public static IfcSceneResult Read(string path, IfcSceneOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= new IfcSceneOptions();
        if (options.ModelKey is "")
        {
            throw new ArgumentException("IfcSceneOptions.ModelKey must not be empty; leave it null to use the file name.", nameof(options));
        }
        return Convert(IfcModel.Open(path), options.ModelKey ?? Path.GetFileName(path), path, options);
    }

    /// <summary>
    /// Converts <paramref name="model"/>, which has no file name, so <see cref="IfcSceneOptions.ModelKey"/> is required.
    /// </summary>
    /// <param name="model">The model, for example from <see cref="IfcModel.Open(Stream, string?)"/>.</param>
    /// <param name="options">Conversion options with <see cref="IfcSceneOptions.ModelKey"/>.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="IfcSceneOptions.ModelKey"/> is empty, or two converted elements have the same <see cref="IfcElement.Id"/>.
    /// </exception>
    public static IfcSceneResult Read(IfcModel model, IfcSceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.ModelKey))
        {
            throw new ArgumentException("A model converted from memory needs IfcSceneOptions.ModelKey.", nameof(options));
        }
        return Convert(model, options.ModelKey, null, options);
    }

    private static IfcSceneResult Convert(IfcModel model, string key, string? path, IfcSceneOptions options)
    {
        double scale = MetresPerUnit(model.LengthUnit);
        var doc = new SceneDocument { Title = model.ProjectName };
        doc.Models.Add(new SceneModel
        {
            Id = ModelId,
            Key = key,
            Name = model.ProjectName,
            Path = path,
            SourceFormat = "ifc",
            SourceLengthUnit = UnitSymbol(model.LengthUnit),
            Producer = Producer,
        });

        var diagnostics = new List<SceneDiagnostic>();
        var materials = new Dictionary<IfcMaterial, int>(ReferenceEqualityComparer.Instance);
        var sets = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var converted = new Dictionary<IfcElement, long>(ReferenceEqualityComparer.Instance);
        var ids = new HashSet<long>();
        int withGeometry = 0;

        // Storey nodes take ids above every element id, which are STEP ids for a model read from a file.
        long nextNodeId = model.AllElements.Concat(model.AllSpaces).Select(e => (long)e.Id).DefaultIfEmpty(0).Max() + 1;

        foreach (var storey in model.Storeys)
        {
            int levelId = doc.Levels.Count + 1;
            doc.Levels.Add(new SceneLevel { Id = levelId, ModelId = ModelId, Name = storey.Name, Elevation = storey.Elevation * scale });
            long nodeId = nextNodeId++;
            doc.Elements.Add(new SceneElement
            {
                Id = nodeId,
                ModelId = ModelId,
                SourceId = storey.GlobalId,
                IfcGuid = storey.GlobalId,
                Kind = SceneNodeKind.Level,
                Name = storey.Name,
                Category = "IFCBUILDINGSTOREY",
                LevelId = levelId,
            });

            var elements = options.IncludeSpaces ? storey.Elements.Concat(storey.Spaces) : storey.Elements;
            foreach (var element in elements)
            {
                if (IsNotProduct(element.IfcType)) continue;

                // Instances, 4D tasks and clashes all key on Id, so a repeat would merge two elements.
                if (!ids.Add(element.Id))
                {
                    throw new ArgumentException(
                        $"Element id {element.Id} ('{element.Name}') is used twice. Ids are unique in models read with IfcModel.Open; "
                        + "a model built in memory numbers elements per storey, so save and reopen it first.",
                        nameof(model));
                }

                converted.Add(element, element.Id);
                var issue = AddElement(doc, element, nodeId, levelId, scale, materials, sets);
                if (issue is null) withGeometry++;
                else diagnostics.Add(issue);
            }
        }

        int skippedTasks = AddSchedules(doc, model.WorkSchedules, converted);
        return new IfcSceneResult(doc, diagnostics, model.LengthUnit, scale, converted.Count, withGeometry, skippedTasks);
    }

    private static readonly string Producer =
        "IfcIO.Scene " + (typeof(IfcSceneReader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(IfcSceneReader).Assembly.GetName().Version?.ToString()
            ?? "0.0.0").Split('+')[0];

    // shortcut: IfcIO picks products by class-name prefix (IFCWALL, IFCDOOR, ...), so type objects such as IfcWallType or
    // IfcDoorStyle and the door and window lining and panel properties arrive as elements without geometry; no product
    // class it matches ends like these. Remove once IfcIO reads only IfcProduct subtypes.
    private static bool IsNotProduct(string ifcType) =>
        ifcType.EndsWith("TYPE", StringComparison.OrdinalIgnoreCase)
        || ifcType.EndsWith("STYLE", StringComparison.OrdinalIgnoreCase)
        || ifcType.EndsWith("PROPERTIES", StringComparison.OrdinalIgnoreCase);

    // Exact lengths of the units IfcIO reports; it already converts any other file unit to metres while reading.
    private static double MetresPerUnit(IfcLengthUnit unit) => unit switch
    {
        IfcLengthUnit.Millimetre => 0.001,
        IfcLengthUnit.Metre => 1.0,
        IfcLengthUnit.Foot => 0.3048,
        IfcLengthUnit.Inch => 0.0254,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown IFC length unit."),
    };

    private static string UnitSymbol(IfcLengthUnit unit) => unit switch
    {
        IfcLengthUnit.Millimetre => "mm",
        IfcLengthUnit.Metre => "m",
        IfcLengthUnit.Foot => "ft",
        IfcLengthUnit.Inch => "in",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown IFC length unit."),
    };

    private static SceneDiagnostic? AddElement(
        SceneDocument doc,
        IfcElement element,
        long parentId,
        int levelId,
        double scale,
        Dictionary<IfcMaterial, int> materials,
        Dictionary<object, int> sets)
    {
        var sceneElement = new SceneElement
        {
            Id = element.Id,
            ModelId = ModelId,
            SourceId = element.GlobalId,
            IfcGuid = element.GlobalId,
            ParentId = parentId,
            Kind = SceneNodeKind.Element,
            Name = element.Name,
            Category = element.IfcType,
            TypeName = element.TypeProduct?.Name ?? string.Empty,
            LevelId = levelId,
        };
        foreach (var pset in element.PropertySets)
        {
            sceneElement.PropertySetIds.Add(SetId(doc, sets, pset, pset.Name,
                pset.Properties.Select(p => (p.Name, SceneValue.FromText(p.Value)))));
        }
        foreach (var qset in element.QuantitySets)
        {
            sceneElement.PropertySetIds.Add(SetId(doc, sets, qset, qset.Name,
                qset.Quantities.Select(q => (q.Name, QuantityValue(q.Value)))));
        }
        doc.Elements.Add(sceneElement);

        var source = element.GetEffectiveMesh();
        if (source is null) return Issue(SceneSeverity.Info, "no-geometry", "IfcIO returned no geometry", element);
        if (source.Indices.Count == 0 || source.Vertices.Count == 0) return Issue(SceneSeverity.Warning, "empty-mesh", "The mesh has no triangles", element);

        var vertices = new Vector3d[source.Vertices.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = source.Vertices[i];
            if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
            {
                return Issue(SceneSeverity.Warning, "invalid-mesh", "The mesh has a non-finite coordinate", element);
            }
            vertices[i] = new Vector3d(v.X * scale, v.Y * scale, v.Z * scale);
        }
        if (source.Indices.Count % 3 != 0 || source.Indices.Any(index => (uint)index >= (uint)vertices.Length))
        {
            return Issue(SceneSeverity.Warning, "invalid-mesh", "The mesh has an index out of range or an index count not divisible by 3", element);
        }

        int geometryId = doc.Geometries.Count + 1;
        doc.Geometries.Add(new SceneGeometry { Id = geometryId, Topology = SceneTopology.Triangles, Vertices = vertices, Indices = [.. source.Indices] });
        var instance = new SceneInstance { ElementId = element.Id, GeometryId = geometryId };
        if (element.Material is { } material) instance.MaterialId = MaterialId(doc, material, materials);
        else instance.Color = DefaultColor(element);
        doc.Instances.Add(instance);
        return null;
    }

    private static SceneDiagnostic Issue(SceneSeverity severity, string code, string reason, IfcElement element) =>
        new(severity, code, $"{reason}; element #{element.Id} '{element.Name}' ({element.IfcType}) has no geometry in the scene.", element.GlobalId);

    // Quantities are doubles in IfcIO, in the file's units (not always IfcModel.LengthUnit), so they carry no unit.
    private static SceneValue QuantityValue(double value)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        return double.IsFinite(value) ? SceneValue.FromNumber(value, null, text) : SceneValue.FromText(text);
    }

    private static int SetId(
        SceneDocument doc, Dictionary<object, int> sets, object source, string name, IEnumerable<(string Name, SceneValue Value)> values)
    {
        if (sets.TryGetValue(source, out int id)) return id;

        id = doc.PropertySets.Count + 1;
        var set = new ScenePropertySet { Id = id, Name = name, Key = "ifc:" + name };
        foreach (var (propertyName, value) in values)
        {
            set.Properties.Add(new SceneProperty { Name = propertyName, Key = $"ifc:{name}.{propertyName}", Value = value });
        }
        doc.PropertySets.Add(set);
        sets.Add(source, id);
        return id;
    }

    private static int MaterialId(SceneDocument doc, IfcMaterial material, Dictionary<IfcMaterial, int> materials)
    {
        if (materials.TryGetValue(material, out int id)) return id;

        id = doc.Materials.Count + 1;
        doc.Materials.Add(new SceneMaterial
        {
            Id = id,
            Name = material.Name,
            Color = new RgbaColor(Channel(material.R), Channel(material.G), Channel(material.B), Channel(material.Alpha)),
        });
        materials.Add(material, id);
        return id;
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    // Fallback colours by IFC class family, matched on the class name prefix as IfcIO lists the classes it reads.
    private static RgbaColor DefaultColor(IfcElement element)
    {
        string type = element.IfcType.ToUpperInvariant();
        if (element is IfcSpace) return new RgbaColor(150, 200, 235, Channel(0.3));
        if (type.StartsWith("IFCWINDOW", StringComparison.Ordinal) || type.StartsWith("IFCCURTAINWALL", StringComparison.Ordinal))
        {
            return new RgbaColor(140, 190, 225, Channel(0.5));
        }
        if (type.StartsWith("IFCWALL", StringComparison.Ordinal)) return new RgbaColor(215, 213, 205);
        if (type.StartsWith("IFCSLAB", StringComparison.Ordinal) || type.StartsWith("IFCROOF", StringComparison.Ordinal)
            || type.StartsWith("IFCFOOTING", StringComparison.Ordinal))
        {
            return new RgbaColor(190, 190, 190);
        }
        if (type.StartsWith("IFCCOLUMN", StringComparison.Ordinal) || type.StartsWith("IFCBEAM", StringComparison.Ordinal)
            || type.StartsWith("IFCMEMBER", StringComparison.Ordinal))
        {
            return new RgbaColor(120, 140, 185);
        }
        if (type.StartsWith("IFCDOOR", StringComparison.Ordinal)) return new RgbaColor(150, 105, 60);
        if (type.StartsWith("IFCPIPE", StringComparison.Ordinal) || type.StartsWith("IFCFLOW", StringComparison.Ordinal))
        {
            return new RgbaColor(225, 110, 60);
        }
        if (type.StartsWith("IFCDUCT", StringComparison.Ordinal)) return new RgbaColor(70, 190, 110);
        return new RgbaColor(180, 180, 180);
    }

    private static int AddSchedules(SceneDocument doc, List<IfcWorkSchedule> schedules, Dictionary<IfcElement, long> converted)
    {
        int skipped = 0;
        foreach (var schedule in schedules)
        {
            var sceneSchedule = new SceneSchedule { ModelId = ModelId, Name = schedule.Name };
            foreach (var task in schedule.RootTasks) AddTask(task, sceneSchedule.Tasks);
            if (sceneSchedule.Tasks.Count > 0) doc.Schedules.Add(sceneSchedule);
        }
        return skipped;

        void AddTask(IfcTask task, List<SceneTask> target)
        {
            if (task.Time?.PlannedStartDate is not { } start || task.Time.PlannedFinishDate is not { } finish)
            {
                skipped++;
                foreach (var sub in task.SubTasks) AddTask(sub, target);
                return;
            }

            var sceneTask = new SceneTask
            {
                Id = task.GlobalId,
                Name = task.Name,
                TaskType = task.TaskType == IfcTaskType.Demolition ? SceneTaskType.Demolish : SceneTaskType.Construct,
                PlannedStart = Instant(start),
                PlannedEnd = Instant(finish),
                ActualStart = task.Time.ActualStartDate is { } actualStart ? Instant(actualStart) : null,
                ActualEnd = task.Time.ActualFinishDate is { } actualEnd ? Instant(actualEnd) : null,
            };
            foreach (var element in task.AssignedElements)
            {
                if (converted.TryGetValue(element, out long id)) sceneTask.ElementIds.Add(id);
            }
            target.Add(sceneTask);
            foreach (var sub in task.SubTasks) AddTask(sub, sceneTask.Children);
        }
    }

    // IfcIO reads IFC date-times as UTC (converted to local time); a date set in memory without a kind is taken as UTC too.
    private static DateTimeOffset Instant(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(value, TimeSpan.Zero) : new DateTimeOffset(value).ToUniversalTime();
}
