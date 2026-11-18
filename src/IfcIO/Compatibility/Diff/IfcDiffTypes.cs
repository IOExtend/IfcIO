using System.Globalization;
using System.Text;
using System.Text.Json;
using IfcIO.Bcf;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Diff;

/// <summary>
/// Kind of modification detected for an IFC model element between revisions.
/// </summary>
public enum IfcChangeKind
{
    Added,
    Deleted,
    Modified,
    Unchanged,
}

/// <summary>
/// Options configuring IFC model revision comparison.
/// </summary>
public sealed class IfcDiffOptions
{
    /// <summary>
    /// Geometric displacement tolerance in meters below which changes in element centroid or size are ignored (default: 0.005m = 5mm).
    /// </summary>
    public double GeometricTolerance { get; set; } = 0.005;

    /// <summary>Whether to compare PropertySet parameter values (default: true).</summary>
    public bool CompareProperties { get; set; } = true;

    /// <summary>Whether to compare QuantitySet values (default: true).</summary>
    public bool CompareQuantities { get; set; } = true;

    /// <summary>Whether to detect changes in spatial building storey assignment (default: true).</summary>
    public bool CompareStoreys { get; set; } = true;

    /// <summary>Whether to detect changes in type product definitions (default: true).</summary>
    public bool CompareTypes { get; set; } = true;

    /// <summary>Optional selector filter to restrict comparison to matching elements (e.g. "IfcWall, IfcColumn").</summary>
    public string? FilterSelector { get; set; }
}

/// <summary>
/// Detail of a single property or quantity value change.
/// </summary>
public sealed record IfcPropertyChange(
    string PropertySet,
    string PropertyName,
    string? OldValue,
    string? NewValue,
    string ChangeType // "Added", "Removed", "Modified"
);

/// <summary>
/// Difference record for a specific IFC element between baseline and revision models.
/// </summary>
public sealed class IfcElementDiff
{
    public required string GlobalId { get; init; }
    public required string Name { get; init; }
    public required string IfcType { get; init; }
    public IfcChangeKind ChangeKind { get; set; }

    public IfcElement? BaselineElement { get; init; }
    public IfcElement? RevisionElement { get; init; }

    public bool GeometryChanged { get; set; }
    public IfcVector3? TranslationDelta { get; set; }
    public double? VolumeDelta { get; set; }

    public bool StoreyChanged { get; set; }
    public string? OldStorey { get; set; }
    public string? NewStorey { get; set; }

    public bool TypeChanged { get; set; }
    public string? OldType { get; set; }
    public string? NewType { get; set; }

    public List<IfcPropertyChange> PropertyChanges { get; } = [];

    public override string ToString() =>
        $"[{ChangeKind}] {IfcType} '{Name}' ({GlobalId}): Geo={GeometryChanged}, Props={PropertyChanges.Count}, Storey={StoreyChanged}";
}

/// <summary>
/// Complete model revision comparison report.
/// </summary>
public sealed class IfcDiffReport
{
    public string BaselineName { get; init; } = "Baseline";
    public string RevisionName { get; init; } = "Revision";
    public double GeometricTolerance { get; init; }
    public TimeSpan Elapsed { get; set; }

    public List<IfcElementDiff> Added { get; } = [];
    public List<IfcElementDiff> Deleted { get; } = [];
    public List<IfcElementDiff> Modified { get; } = [];
    public List<IfcElementDiff> Unchanged { get; } = [];

    public int TotalChangesCount => Added.Count + Deleted.Count + Modified.Count;

    public IfcDiffReport() { }

    /// <summary>Saves the diff report as a formatted JSON document.</summary>
    public void SaveJson(string path, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.Create(path);
        using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = indented });
        WriteJson(writer);
    }

    /// <summary>Writes the diff report into a <see cref="Utf8JsonWriter"/>.</summary>
    public void WriteJson(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("baseline", BaselineName);
        writer.WriteString("revision", RevisionName);
        writer.WriteNumber("geometricTolerance", GeometricTolerance);
        writer.WriteNumber("totalChanges", TotalChangesCount);
        writer.WriteNumber("addedCount", Added.Count);
        writer.WriteNumber("deletedCount", Deleted.Count);
        writer.WriteNumber("modifiedCount", Modified.Count);
        writer.WriteNumber("unchangedCount", Unchanged.Count);
        writer.WriteNumber("elapsedMs", Elapsed.TotalMilliseconds);

        // Added
        writer.WriteStartArray("added");
        foreach (var item in Added) WriteElementDiffJson(writer, item);
        writer.WriteEndArray();

        // Deleted
        writer.WriteStartArray("deleted");
        foreach (var item in Deleted) WriteElementDiffJson(writer, item);
        writer.WriteEndArray();

        // Modified
        writer.WriteStartArray("modified");
        foreach (var item in Modified) WriteElementDiffJson(writer, item);
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteElementDiffJson(Utf8JsonWriter writer, IfcElementDiff diff)
    {
        writer.WriteStartObject();
        writer.WriteString("globalId", diff.GlobalId);
        writer.WriteString("name", diff.Name);
        writer.WriteString("ifcType", diff.IfcType);
        writer.WriteString("changeKind", diff.ChangeKind.ToString());
        writer.WriteBoolean("geometryChanged", diff.GeometryChanged);

        if (diff.TranslationDelta.HasValue)
        {
            writer.WriteStartObject("translationDelta");
            writer.WriteNumber("dx", diff.TranslationDelta.Value.X);
            writer.WriteNumber("dy", diff.TranslationDelta.Value.Y);
            writer.WriteNumber("dz", diff.TranslationDelta.Value.Z);
            writer.WriteEndObject();
        }

        if (diff.StoreyChanged)
        {
            writer.WriteString("oldStorey", diff.OldStorey);
            writer.WriteString("newStorey", diff.NewStorey);
        }

        if (diff.TypeChanged)
        {
            writer.WriteString("oldType", diff.OldType);
            writer.WriteString("newType", diff.NewType);
        }

        if (diff.PropertyChanges.Count > 0)
        {
            writer.WriteStartArray("propertyChanges");
            foreach (var prop in diff.PropertyChanges)
            {
                writer.WriteStartObject();
                writer.WriteString("propertySet", prop.PropertySet);
                writer.WriteString("name", prop.PropertyName);
                if (prop.OldValue is not null) writer.WriteString("oldValue", prop.OldValue);
                if (prop.NewValue is not null) writer.WriteString("newValue", prop.NewValue);
                writer.WriteString("changeType", prop.ChangeType);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Exports changes to buildingSMART BCF 2.1 zip archive (.bcfzip)
    /// with focused perspective camera viewpoints framing modified, added, or deleted elements.
    /// </summary>
    public void SaveBcf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var topics = new List<BcfTopic>();

        // Added Elements
        foreach (var item in Added)
        {
            var elem = item.RevisionElement!;
            var mesh = elem.GetEffectiveMesh();
            var box = mesh?.BoundingBox ?? IfcBoundingBox.Empty;
            var center = box.IsEmpty ? IfcVector3.Zero : box.Center;

            var topic = new BcfTopic(
                title: $"[Added] {item.IfcType}: {item.Name}",
                description: $"New element introduced in revision {RevisionName}. GlobalId: {item.GlobalId}",
                topicType: "New",
                priority: "Normal")
            {
                CreationAuthor = "IfcDiffEngine",
            };

            topic.Viewpoints.Add(CreateFramingViewpoint(center, item.GlobalId));
            topics.Add(topic);
        }

        // Deleted Elements
        foreach (var item in Deleted)
        {
            var elem = item.BaselineElement!;
            var mesh = elem.GetEffectiveMesh();
            var box = mesh?.BoundingBox ?? IfcBoundingBox.Empty;
            var center = box.IsEmpty ? IfcVector3.Zero : box.Center;

            var topic = new BcfTopic(
                title: $"[Deleted] {item.IfcType}: {item.Name}",
                description: $"Element removed in revision {RevisionName}. Previous GlobalId: {item.GlobalId}",
                topicType: "Removed",
                priority: "Normal")
            {
                CreationAuthor = "IfcDiffEngine",
            };

            topic.Viewpoints.Add(CreateFramingViewpoint(center, item.GlobalId));
            topics.Add(topic);
        }

        // Modified Elements
        foreach (var item in Modified)
        {
            var elem = item.RevisionElement ?? item.BaselineElement!;
            var mesh = elem.GetEffectiveMesh();
            var box = mesh?.BoundingBox ?? IfcBoundingBox.Empty;
            var center = box.IsEmpty ? IfcVector3.Zero : box.Center;

            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Element {item.IfcType} '{item.Name}' modified in revision {RevisionName}:");
            if (item.GeometryChanged)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $" - 3D Geometry changed. Shift: {item.TranslationDelta}");
            }
            if (item.StoreyChanged)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $" - Storey re-assigned: '{item.OldStorey}' -> '{item.NewStorey}'");
            }
            if (item.TypeChanged)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $" - Type definition changed: '{item.OldType}' -> '{item.NewType}'");
            }
            if (item.PropertyChanges.Count > 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $" - Property modifications ({item.PropertyChanges.Count}):");
                foreach (var p in item.PropertyChanges.Take(10))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"   * {p.PropertySet}.{p.PropertyName}: '{p.OldValue}' -> '{p.NewValue}' ({p.ChangeType})");
                }
            }

            var topic = new BcfTopic(
                title: $"[Modified] {item.IfcType}: {item.Name}",
                description: sb.ToString().TrimEnd(),
                topicType: "Change",
                priority: "High")
            {
                CreationAuthor = "IfcDiffEngine",
            };

            topic.Viewpoints.Add(CreateFramingViewpoint(center, item.GlobalId));
            topics.Add(topic);
        }

        BcfZipExporter.Save(path, topics, projectName: $"{BaselineName} vs {RevisionName}");
    }

    private static BcfViewpoint CreateFramingViewpoint(IfcVector3 target, string globalId)
    {
        var offset = new IfcVector3(6.0, -6.0, 4.0);
        var eye = target + offset;
        var dir = (target - eye).Normalized();

        var vp = new BcfViewpoint
        {
            CameraPosition = eye,
            CameraDirection = dir,
            CameraUpVector = new IfcVector3(0, 0, 1),
            FieldOfView = 60.0,
        };
        if (!string.IsNullOrEmpty(globalId))
        {
            vp.SelectedElementGuids.Add(globalId);
        }
        return vp;
    }
}
