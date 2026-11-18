using IfcIO.Exporters;

namespace IfcIO;

public sealed partial class IfcModel
{
    /// <summary>Spatial grid system (axes and levels) associated with the IFC model.</summary>
    public Grids.IfcGridSystem? GridSystem { get; set; }

    /// <summary>Imports a BCF 2.1 archive (.bcfzip) file from disk.</summary>
    public static List<Bcf.BcfTopic> ImportBcf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Bcf.BcfArchive.Open(path).Topics;
    }

    /// <summary>Exports a collection of BCF topics to a buildingSMART BCF 2.1 zip archive (.bcfzip).</summary>
    public void SaveBcf(string path, IEnumerable<Bcf.BcfTopic> topics)
    {
        Bcf.BcfZipExporter.Save(path, topics, ProjectName);
    }

    /// <summary>Exports a collection of BCF topics to a stream in BCF 2.1 format.</summary>
    public void SaveBcf(Stream stream, IEnumerable<Bcf.BcfTopic> topics)
    {
        Bcf.BcfZipExporter.Write(stream, topics, ProjectName);
    }

    /// <summary>
    /// Runs model validation and exports any detected errors or warnings as a BCF 2.1 archive (.bcfzip)
    /// with affected element GUIDs linked for cross-discipline issue coordination.
    /// </summary>
    public void ExportValidationBcf(string path)
    {
        var result = Validate();
        var topics = new List<Bcf.BcfTopic>();

        foreach (var err in result.Errors)
        {
            var topic = new Bcf.BcfTopic(
                title: $"Validation Error: {err.RuleId}",
                description: err.Message,
                topicType: "Error",
                priority: "High");

            string? targetGuid = err.GlobalId;
            if (string.IsNullOrEmpty(targetGuid) && err.ElementId.HasValue)
            {
                var elem = AllElements.FirstOrDefault(e => e.Id == err.ElementId.Value);
                if (elem is not null) targetGuid = elem.GlobalId;
                else
                {
                    var sp = AllSpaces.FirstOrDefault(s => s.Id == err.ElementId.Value);
                    if (sp is not null) targetGuid = sp.GlobalId;
                }
            }

            if (!string.IsNullOrEmpty(targetGuid))
            {
                var vp = new Bcf.BcfViewpoint();
                vp.SelectedElementGuids.Add(targetGuid);
                topic.Viewpoints.Add(vp);
            }
            topics.Add(topic);
        }

        foreach (var warn in result.Warnings)
        {
            var topic = new Bcf.BcfTopic(
                title: $"Validation Warning: {warn.RuleId}",
                description: warn.Message,
                topicType: "Warning",
                priority: "Normal");

            string? targetGuid = warn.GlobalId;
            if (string.IsNullOrEmpty(targetGuid) && warn.ElementId.HasValue)
            {
                var elem = AllElements.FirstOrDefault(e => e.Id == warn.ElementId.Value);
                if (elem is not null) targetGuid = elem.GlobalId;
                else
                {
                    var sp = AllSpaces.FirstOrDefault(s => s.Id == warn.ElementId.Value);
                    if (sp is not null) targetGuid = sp.GlobalId;
                }
            }

            if (!string.IsNullOrEmpty(targetGuid))
            {
                var vp = new Bcf.BcfViewpoint();
                vp.SelectedElementGuids.Add(targetGuid);
                topic.Viewpoints.Add(vp);
            }
            topics.Add(topic);
        }

        SaveBcf(path, topics);
    }

    /// <summary>Loads BCF topics from a BCF 2.1 zip archive (.bcfzip).</summary>
    public static List<Bcf.BcfTopic> LoadBcf(string path) => Bcf.BcfZipExporter.Open(path);

    /// <summary>Exports the model to binary glTF 2.0 (.glb) format.</summary>
    public void SaveGlb(string path) => IfcGlbExporter.Save(this, path);

    /// <summary>Exports the model to binary glTF 2.0 (.glb) stream.</summary>
    public void SaveGlb(Stream stream) => IfcGlbExporter.Write(this, stream);

    /// <summary>Exports the model to Wavefront OBJ format (.obj + .mtl).</summary>
    public void SaveObj(string path) => IfcObjExporter.Save(this, path);

    /// <summary>Performs geometric clash detection between this model and another IFC model.</summary>
    public Clash.IfcClashReport DetectClashes(IfcModel otherModel, Clash.IfcClashOptions? options = null) =>
        Clash.IfcClashDetector.DetectClashes(this, otherModel, options);

    /// <summary>Performs geometric self-clash detection within this IFC model.</summary>
    public Clash.IfcClashReport DetectClashes(Clash.IfcClashOptions? options = null) =>
        Clash.IfcClashDetector.DetectClashes(this, options);

    /// <summary>Compares this baseline model against a revision model, detecting added, deleted, and modified elements.</summary>
    public Diff.IfcDiffReport Compare(IfcModel revisionModel, Diff.IfcDiffOptions? options = null) =>
        Diff.IfcDiffEngine.Compare(this, revisionModel, options);

    /// <summary>Exports the model to a standalone, interactive 3D WebGL HTML viewer (.html) with zero dependencies.</summary>
    public void SaveHtmlViewer(string path, Exporters.IfcHtmlViewerOptions? options = null) =>
        Exporters.IfcHtmlViewerExporter.Save(path, this, options);

    /// <summary>Populates grid location tags across elements in this model using the attached or extracted grid system.</summary>
    public int AssignGridLocations(Grids.IfcGridSystem? gridSystem = null)
    {
        var grids = gridSystem ?? GridSystem ?? ExtractGridSystem();
        GridSystem = grids;
        return grids.AssignGridLocations(this);
    }

    /// <summary>Automatically extracts grid axes from IFCGRID elements or metadata in this model.</summary>
    public Grids.IfcGridSystem ExtractGridSystem() => Grids.IfcGridSystem.ExtractFromModel(this);
}
