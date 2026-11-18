namespace IfcIO.Bcf;

/// <summary>
/// A managed buildingSMART BIM Collaboration Format (BCF 2.1) archive container holding issues, viewpoints, and comments.
/// </summary>
public sealed class BcfArchive
{
    /// <summary>The collection of coordination topics contained in this archive.</summary>
    public List<BcfTopic> Topics { get; } = [];

    /// <summary>Optional project name or document title associated with this issue archive.</summary>
    public string? ProjectName { get; set; }

    public BcfArchive() { }

    public BcfArchive(string? projectName = null)
    {
        ProjectName = projectName;
    }

    /// <summary>Opens and parses a BCF 2.1 (.bcfzip) file from disk.</summary>
    public static BcfArchive Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return Open(fs);
    }

    /// <summary>Opens and parses a BCF 2.1 (.bcfzip) zip archive from a stream.</summary>
    public static BcfArchive Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var topics = BcfZipExporter.Read(stream);
        var archive = new BcfArchive();
        archive.Topics.AddRange(topics);
        return archive;
    }

    /// <summary>Saves all topics into a BCF 2.1 (.bcfzip) file on disk.</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        BcfZipExporter.Save(path, Topics, ProjectName);
    }

    /// <summary>Saves all topics into a stream as a BCF 2.1 (.bcfzip) zip archive.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        BcfZipExporter.Write(stream, Topics, ProjectName);
    }

    /// <summary>Creates and adds a new topic to the archive.</summary>
    public BcfTopic AddTopic(string title, string? description = null, string topicType = "Issue", string priority = "Normal")
    {
        var topic = new BcfTopic(title, description, topicType, priority);
        Topics.Add(topic);
        return topic;
    }

    /// <summary>Finds all topics that reference or select a given building element by its IFC GlobalId.</summary>
    public IReadOnlyList<BcfTopic> FindTopicsByElement(string ifcGuid)
    {
        ArgumentNullException.ThrowIfNull(ifcGuid);
        return Topics.Where(t => t.Viewpoints.Any(v => v.SelectedElementGuids.Contains(ifcGuid, StringComparer.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Finds all topics matching a given status (e.g. "Open", "Resolved", "Closed").</summary>
    public IReadOnlyList<BcfTopic> FindTopicsByStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return Topics.Where(t => string.Equals(t.TopicStatus, status, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
