using IfcIO.Bcf;

namespace IfcIO.Validation.Ids;

public sealed partial class IfcIdsValidationReport
{
    /// <summary>
    /// Exports all IDS compliance failures directly to a buildingSMART BCF 2.1 zip archive (.bcfzip)
    /// for issue tracking and resolution in Revit, Solibri, and BIMcollab.
    /// </summary>
    public void SaveBcf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var topics = new List<BcfTopic>();
        foreach (var failure in Failures)
        {
            var topic = new BcfTopic
            {
                Title = $"[IDS Violation] {failure.SpecificationName}: {failure.ElementType} #{failure.ElementId}",
                Description = $"{failure.Message} (Element: '{failure.ElementName}', GUID: {failure.ElementGuid ?? "N/A"})",
                TopicType = "Compliance",
                TopicStatus = "Active",
                CreationAuthor = Author ?? "IfcIO IDS Validator",
            };

            if (!string.IsNullOrEmpty(failure.ElementGuid))
            {
                var vp = new BcfViewpoint();
                vp.SelectedElementGuids.Add(failure.ElementGuid);
                topic.Viewpoints.Add(vp);
            }

            topics.Add(topic);
        }

        BcfZipExporter.Save(path, topics, Title);
    }
}
