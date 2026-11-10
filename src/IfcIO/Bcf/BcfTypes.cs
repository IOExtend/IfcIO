namespace IfcIO.Bcf;

/// <summary>
/// A comment recorded on a BCF topic.
/// </summary>
public sealed class BcfComment
{
    public Guid CommentGuid { get; set; } = Guid.NewGuid();
    public DateTimeOffset Date { get; set; } = DateTimeOffset.UtcNow;
    public string Author { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public Guid? ViewpointGuid { get; set; }
}

/// <summary>
/// A 3D camera viewpoint associated with a BCF issue, including camera position,
/// viewing vector, up vector, and list of selected/highlighted IFC element GUIDs.
/// </summary>
public sealed class BcfViewpoint
{
    public Guid ViewpointGuid { get; set; } = Guid.NewGuid();
    public Geometry.IfcVector3? CameraPosition { get; set; }
    public Geometry.IfcVector3? CameraDirection { get; set; }
    public Geometry.IfcVector3? CameraUpVector { get; set; }
    public double? FieldOfView { get; set; }
    public List<string> SelectedElementGuids { get; } = [];
    public List<string> VisibleElementGuids { get; } = [];
}

/// <summary>
/// A collaboration topic, clash, or issue conforming to the buildingSMART BIM Collaboration Format (BCF 2.1).
/// </summary>
public sealed class BcfTopic
{
    public Guid TopicGuid { get; set; } = Guid.NewGuid();
    public string TopicType { get; set; } = "Issue";
    public string TopicStatus { get; set; } = "Open";
    public string Title { get; set; } = string.Empty;
    public string? Priority { get; set; } = "Normal";
    public string? Description { get; set; }
    public DateTimeOffset CreationDate { get; set; } = DateTimeOffset.UtcNow;
    public string CreationAuthor { get; set; } = "IfcIO";
    public string? AssignedTo { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public List<BcfComment> Comments { get; } = [];
    public List<BcfViewpoint> Viewpoints { get; } = [];

    public BcfTopic() { }

    public BcfTopic(string title, string? description = null, string topicType = "Issue", string priority = "Normal")
    {
        Title = title;
        Description = description;
        TopicType = topicType;
        Priority = priority;
    }

    public void AddComment(string comment, string? author = null, Guid? viewpointGuid = null)
    {
        Comments.Add(new BcfComment
        {
            Comment = comment,
            Author = author ?? CreationAuthor,
            ViewpointGuid = viewpointGuid,
        });
    }
}
