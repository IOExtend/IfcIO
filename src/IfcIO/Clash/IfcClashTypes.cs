using System.Globalization;
using System.Text.Json;
using IfcIO.Bcf;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Clash;

/// <summary>
/// Type of geometric conflict or clearance test.
/// </summary>
public enum IfcClashType
{
    Hard,
    Clearance,
    Duplicates,
}

/// <summary>
/// Options configuring IFC clash detection and interference analysis.
/// </summary>
public sealed class IfcClashOptions
{
    /// <summary>Tolerance / clearance threshold in meters (default: 0.001m = 1mm).</summary>
    public double Tolerance { get; set; } = 0.001;

    /// <summary>Type of clash detection to perform.</summary>
    public IfcClashType ClashType { get; set; } = IfcClashType.Hard;

    /// <summary>Optional selector query filtering participating elements in Model A (e.g. "IfcWall, IfcColumn").</summary>
    public string? SelectorA { get; set; }

    /// <summary>Optional selector query filtering participating elements in Model B (e.g. "IfcFlowSegment, IfcFlowFitting").</summary>
    public string? SelectorB { get; set; }

    /// <summary>Whether to ignore clashes between elements on the same building storey (default: false).</summary>
    public bool ExcludeSameStorey { get; set; }

    /// <summary>Whether to ignore clashes between elements of the same IFC entity type (default: false).</summary>
    public bool ExcludeSameType { get; set; }
}

/// <summary>
/// An individual geometric collision or clearance violation between two IFC elements.
/// </summary>
public sealed class IfcClashResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required IfcElement ElementA { get; init; }
    public required IfcElement ElementB { get; init; }
    public required IfcVector3 ClashPoint { get; init; }
    public double Distance { get; init; }
    public IfcClashType ClashType { get; init; } = IfcClashType.Hard;
    public string? Description { get; set; }

    public override string ToString() =>
        $"[{ClashType}] {ElementA.Name} (#{ElementA.Id}) vs {ElementB.Name} (#{ElementB.Id}) at {ClashPoint} (Penetration: {Distance:F3}m)";
}

/// <summary>
/// Summary report containing all detected clashes, execution statistics, and export bridges.
/// </summary>
public sealed class IfcClashReport
{
    public string ModelAName { get; init; } = "Model A";
    public string? ModelBName { get; init; }
    public double Tolerance { get; init; }
    public List<IfcClashResult> Clashes { get; } = [];
    public TimeSpan Elapsed { get; set; }

    public int TotalClashesCount => Clashes.Count;

    public IfcClashReport() { }

    /// <summary>Saves the clash report as a formatted JSON report.</summary>
    public void SaveJson(string path, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.Create(path);
        using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = indented });
        WriteJson(writer);
    }

    /// <summary>Writes the clash report into a <see cref="Utf8JsonWriter"/>.</summary>
    public void WriteJson(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("modelA", ModelAName);
        if (!string.IsNullOrEmpty(ModelBName))
        {
            writer.WriteString("modelB", ModelBName);
        }
        writer.WriteNumber("tolerance", Tolerance);
        writer.WriteNumber("totalClashes", Clashes.Count);
        writer.WriteNumber("elapsedMs", Elapsed.TotalMilliseconds);

        writer.WriteStartArray("clashes");
        foreach (var c in Clashes)
        {
            writer.WriteStartObject();
            writer.WriteString("guid", c.Id.ToString("D"));
            writer.WriteString("type", c.ClashType.ToString());
            writer.WriteNumber("distance", c.Distance);

            writer.WriteStartObject("elementA");
            writer.WriteNumber("id", c.ElementA.Id);
            writer.WriteString("name", c.ElementA.Name);
            writer.WriteString("ifcType", c.ElementA.IfcType);
            if (!string.IsNullOrEmpty(c.ElementA.GlobalId))
            {
                writer.WriteString("globalId", c.ElementA.GlobalId);
            }
            writer.WriteEndObject();

            writer.WriteStartObject("elementB");
            writer.WriteNumber("id", c.ElementB.Id);
            writer.WriteString("name", c.ElementB.Name);
            writer.WriteString("ifcType", c.ElementB.IfcType);
            if (!string.IsNullOrEmpty(c.ElementB.GlobalId))
            {
                writer.WriteString("globalId", c.ElementB.GlobalId);
            }
            writer.WriteEndObject();

            writer.WriteStartObject("clashPoint");
            writer.WriteNumber("x", c.ClashPoint.X);
            writer.WriteNumber("y", c.ClashPoint.Y);
            writer.WriteNumber("z", c.ClashPoint.Z);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>
    /// Exports all detected clashes to a buildingSMART BCF 2.1 zip archive (.bcfzip)
    /// with focused perspective camera viewpoints and selected IFC GUIDs.
    /// </summary>
    public void SaveBcf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var topics = new List<BcfTopic>();
        foreach (var clash in Clashes)
        {
            string title = string.Create(CultureInfo.InvariantCulture, $"[Clash] {clash.ElementA.Name} vs {clash.ElementB.Name}");
            string desc = string.Create(CultureInfo.InvariantCulture,
                $"Geometric conflict between {clash.ElementA.IfcType} '{clash.ElementA.Name}' (#{clash.ElementA.Id}) and {clash.ElementB.IfcType} '{clash.ElementB.Name}' (#{clash.ElementB.Id}). Penetration depth: {clash.Distance:F3} m.");

            var topic = new BcfTopic(title, desc, topicType: "Clash", priority: "High")
            {
                CreationAuthor = "IfcClashDetector",
            };

            var target = clash.ClashPoint;
            var offset = new IfcVector3(5.0, -5.0, 3.5);
            var eye = target + offset;
            var dir = (target - eye).Normalized();

            var vp = new BcfViewpoint
            {
                CameraPosition = eye,
                CameraDirection = dir,
                CameraUpVector = new IfcVector3(0, 0, 1),
                FieldOfView = 60.0,
            };

            if (!string.IsNullOrEmpty(clash.ElementA.GlobalId))
            {
                vp.SelectedElementGuids.Add(clash.ElementA.GlobalId);
            }
            if (!string.IsNullOrEmpty(clash.ElementB.GlobalId))
            {
                vp.SelectedElementGuids.Add(clash.ElementB.GlobalId);
            }

            topic.Viewpoints.Add(vp);
            topics.Add(topic);
        }

        BcfZipExporter.Save(path, topics, projectName: ModelAName);
    }
}
