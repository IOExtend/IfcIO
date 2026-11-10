using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using IfcIO.Geometry;

namespace IfcIO.Bcf;

/// <summary>
/// Reads and writes buildingSMART BIM Collaboration Format (BCF 2.1) zip archives (.bcfzip).
/// </summary>
public static class BcfZipExporter
{
    private static readonly XmlWriterSettings XmlSettings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(false),
        OmitXmlDeclaration = false,
    };

    /// <summary>
    /// Writes a collection of BCF topics as a BCF 2.1 (.bcfzip) file to disk.
    /// </summary>
    public static void Save(string path, IEnumerable<BcfTopic> topics, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(topics);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        Write(fs, topics, projectName);
    }

    /// <summary>
    /// Writes a collection of BCF topics into a stream as a BCF 2.1 (.bcfzip) zip archive.
    /// </summary>
    public static void Write(Stream stream, IEnumerable<BcfTopic> topics, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(topics);

        var topicList = topics.ToList();
        projectName = string.IsNullOrWhiteSpace(projectName) ? "IfcIO Coordination" : projectName;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // 1. Root bcf.version
        var versionEntry = archive.CreateEntry("bcf.version", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(versionEntry.Open(), new UTF8Encoding(false)))
        {
            writer.WriteLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            writer.WriteLine("""<Version VersionId="2.1" DetailedVersion="2.1" />""");
        }

        // 2. Per-topic folders
        foreach (var topic in topicList)
        {
            string topicFolder = topic.TopicGuid.ToString("D");

            // Write markup.bcf
            var markupEntry = archive.CreateEntry($"{topicFolder}/markup.bcf", CompressionLevel.Optimal);
            using (var s = markupEntry.Open())
            using (var xml = XmlWriter.Create(s, XmlSettings))
            {
                WriteMarkupXml(xml, topic, projectName);
            }

            // Write viewpoint files
            for (int vIdx = 0; vIdx < topic.Viewpoints.Count; vIdx++)
            {
                var vp = topic.Viewpoints[vIdx];
                string vpFileName = vIdx == 0 ? "viewpoint.bcfv" : $"viewpoint_{vp.ViewpointGuid:D}.bcfv";
                var vpEntry = archive.CreateEntry($"{topicFolder}/{vpFileName}", CompressionLevel.Optimal);
                using (var s = vpEntry.Open())
                using (var xml = XmlWriter.Create(s, XmlSettings))
                {
                    WriteViewpointXml(xml, vp);
                }
            }
        }
    }

    /// <summary>
    /// Reads BCF topics from a BCF 2.1 (.bcfzip) file on disk.
    /// </summary>
    public static List<BcfTopic> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    /// <summary>
    /// Reads BCF topics from a BCF 2.1 (.bcfzip) zip archive stream.
    /// </summary>
    public static List<BcfTopic> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var result = new List<BcfTopic>();

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // Group entries by topic folder
        var topicFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            int slash = entry.FullName.IndexOf('/');
            if (slash > 0)
            {
                topicFolders.Add(entry.FullName[..slash]);
            }
        }

        foreach (var folder in topicFolders)
        {
            var markupEntry = archive.GetEntry($"{folder}/markup.bcf");
            if (markupEntry is null) continue;

            using var s = markupEntry.Open();
            var doc = XDocument.Load(s);
            var topicElem = doc.FindDescendants("Topic").FirstOrDefault();
            if (topicElem is null) continue;

            string guidStr = (string?)topicElem.Attribute("Guid") ?? folder;
            _ = Guid.TryParse(guidStr, out var topicGuid);

            var topic = new BcfTopic
            {
                TopicGuid = topicGuid != Guid.Empty ? topicGuid : Guid.NewGuid(),
                TopicType = (string?)topicElem.Attribute("TopicType") ?? "Issue",
                TopicStatus = (string?)topicElem.Attribute("TopicStatus") ?? "Open",
                Title = topicElem.ChildValue("Title") ?? "Untitled Topic",
                Priority = topicElem.ChildValue("Priority") ?? "Normal",
                Description = topicElem.ChildValue("Description"),
                CreationAuthor = topicElem.ChildValue("CreationAuthor") ?? "IfcIO",
                AssignedTo = topicElem.ChildValue("AssignedTo"),
            };

            if (DateTimeOffset.TryParse(topicElem.ChildValue("CreationDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var cDate))
            {
                topic.CreationDate = cDate;
            }

            // Comments
            var comments = doc.Root?.Elements().Where(e => e.Name.LocalName.Equals("Comment", StringComparison.OrdinalIgnoreCase)) ?? [];
            foreach (var cElem in comments)
            {
                string cGuidStr = (string?)cElem.Attribute("Guid") ?? Guid.NewGuid().ToString();
                _ = Guid.TryParse(cGuidStr, out var cGuid);

                var comment = new BcfComment
                {
                    CommentGuid = cGuid,
                    Author = cElem.ChildValue("Author") ?? string.Empty,
                    Comment = cElem.ChildValue("Comment") ?? string.Empty,
                };
                if (DateTimeOffset.TryParse(cElem.ChildValue("Date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                {
                    comment.Date = dt;
                }
                topic.Comments.Add(comment);
            }

            // Viewpoint files
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith($"{folder}/", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.EndsWith(".bcfv", StringComparison.OrdinalIgnoreCase))
                {
                    using var vpStream = entry.Open();
                    var vpDoc = XDocument.Load(vpStream);
                    var vp = ParseViewpointXml(vpDoc);
                    if (vp is not null)
                    {
                        topic.Viewpoints.Add(vp);
                    }
                }
            }

            result.Add(topic);
        }

        return result;
    }

    private static void WriteMarkupXml(XmlWriter xml, BcfTopic topic, string projectName)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("Markup", "http://www.buildingsmart-tech.org/specifications/bcf-releases");
        xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

        xml.WriteStartElement("Header");
        xml.WriteStartElement("File");
        xml.WriteElementString("Filename", projectName);
        xml.WriteElementString("Date", topic.CreationDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        xml.WriteEndElement(); // File
        xml.WriteEndElement(); // Header

        xml.WriteStartElement("Topic");
        xml.WriteAttributeString("Guid", topic.TopicGuid.ToString("D"));
        xml.WriteAttributeString("TopicType", topic.TopicType);
        xml.WriteAttributeString("TopicStatus", topic.TopicStatus);

        xml.WriteElementString("Title", topic.Title);
        if (!string.IsNullOrEmpty(topic.Priority)) xml.WriteElementString("Priority", topic.Priority);
        if (!string.IsNullOrEmpty(topic.Description)) xml.WriteElementString("Description", topic.Description);
        xml.WriteElementString("CreationDate", topic.CreationDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        xml.WriteElementString("CreationAuthor", topic.CreationAuthor);
        if (!string.IsNullOrEmpty(topic.AssignedTo)) xml.WriteElementString("AssignedTo", topic.AssignedTo);
        if (topic.DueDate.HasValue) xml.WriteElementString("DueDate", topic.DueDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        for (int i = 0; i < topic.Viewpoints.Count; i++)
        {
            var vp = topic.Viewpoints[i];
            string vpFileName = i == 0 ? "viewpoint.bcfv" : $"viewpoint_{vp.ViewpointGuid:D}.bcfv";
            xml.WriteStartElement("Viewpoints");
            xml.WriteAttributeString("Guid", vp.ViewpointGuid.ToString("D"));
            xml.WriteElementString("Viewpoint", vpFileName);
            xml.WriteEndElement();
        }

        xml.WriteEndElement(); // Topic

        if (topic.Comments.Count > 0)
        {
            foreach (var comment in topic.Comments)
            {
                xml.WriteStartElement("Comment");
                xml.WriteAttributeString("Guid", comment.CommentGuid.ToString("D"));
                xml.WriteElementString("Date", comment.Date.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                xml.WriteElementString("Author", comment.Author);
                xml.WriteElementString("Comment", comment.Comment);
                if (comment.ViewpointGuid.HasValue)
                {
                    xml.WriteStartElement("Viewpoint");
                    xml.WriteAttributeString("Guid", comment.ViewpointGuid.Value.ToString("D"));
                    xml.WriteEndElement();
                }
                xml.WriteEndElement(); // Comment
            }
        }

        xml.WriteEndElement(); // Markup
        xml.WriteEndDocument();
    }

    private static void WriteViewpointXml(XmlWriter xml, BcfViewpoint vp)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("VisualizationInfo", "http://www.buildingsmart-tech.org/specifications/bcf-releases");
        xml.WriteAttributeString("Guid", vp.ViewpointGuid.ToString("D"));

        if (vp.CameraPosition.HasValue)
        {
            xml.WriteStartElement("PerspectiveCamera");
            var pos = vp.CameraPosition.Value;
            var dir = vp.CameraDirection ?? new IfcVector3(0, 1, 0);
            var up = vp.CameraUpVector ?? new IfcVector3(0, 0, 1);

            xml.WriteStartElement("CameraViewPoint");
            xml.WriteElementString("X", pos.X.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteElementString("Y", pos.Y.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteElementString("Z", pos.Z.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteEndElement();

            xml.WriteStartElement("CameraDirection");
            xml.WriteElementString("X", dir.X.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteElementString("Y", dir.Y.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteElementString("Z", dir.Z.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteEndElement();

            xml.WriteStartElement("CameraUpVector");
            xml.WriteElementString("X", up.X.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteElementString("Y", up.Y.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteElementString("Z", up.Z.ToString("0.######", CultureInfo.InvariantCulture));
            xml.WriteEndElement();

            xml.WriteElementString("FieldOfView", (vp.FieldOfView ?? 60.0).ToString("0.##", CultureInfo.InvariantCulture));
            xml.WriteEndElement(); // PerspectiveCamera
        }

        if (vp.SelectedElementGuids.Count > 0)
        {
            xml.WriteStartElement("Components");
            xml.WriteStartElement("Selection");
            foreach (var guid in vp.SelectedElementGuids)
            {
                xml.WriteStartElement("Component");
                xml.WriteAttributeString("IfcGuid", guid);
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // Selection
            xml.WriteEndElement(); // Components
        }

        xml.WriteEndElement(); // VisualizationInfo
        xml.WriteEndDocument();
    }

    private static BcfViewpoint? ParseViewpointXml(XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return null;

        var vp = new BcfViewpoint();
        string guidStr = (string?)root.Attribute("Guid") ?? Guid.NewGuid().ToString();
        if (Guid.TryParse(guidStr, out var vpGuid))
        {
            vp.ViewpointGuid = vpGuid;
        }

        var cam = root.FindDescendants("PerspectiveCamera").FirstOrDefault();
        if (cam is not null)
        {
            var pElem = cam.FindChild("CameraViewPoint");
            if (pElem is not null &&
                double.TryParse(pElem.ChildValue("X"), NumberStyles.Float, CultureInfo.InvariantCulture, out double px) &&
                double.TryParse(pElem.ChildValue("Y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double py) &&
                double.TryParse(pElem.ChildValue("Z"), NumberStyles.Float, CultureInfo.InvariantCulture, out double pz))
            {
                vp.CameraPosition = new IfcVector3(px, py, pz);
            }

            var dElem = cam.FindChild("CameraDirection");
            if (dElem is not null &&
                double.TryParse(dElem.ChildValue("X"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dx) &&
                double.TryParse(dElem.ChildValue("Y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dy) &&
                double.TryParse(dElem.ChildValue("Z"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dz))
            {
                vp.CameraDirection = new IfcVector3(dx, dy, dz);
            }

            var uElem = cam.FindChild("CameraUpVector");
            if (uElem is not null &&
                double.TryParse(uElem.ChildValue("X"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ux) &&
                double.TryParse(uElem.ChildValue("Y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double uy) &&
                double.TryParse(uElem.ChildValue("Z"), NumberStyles.Float, CultureInfo.InvariantCulture, out double uz))
            {
                vp.CameraUpVector = new IfcVector3(ux, uy, uz);
            }

            if (double.TryParse(cam.ChildValue("FieldOfView"), NumberStyles.Float, CultureInfo.InvariantCulture, out double fov))
            {
                vp.FieldOfView = fov;
            }
        }

        foreach (var c in root.FindDescendants("Component"))
        {
            string? ifcGuid = (string?)c.Attribute("IfcGuid");
            if (!string.IsNullOrEmpty(ifcGuid))
            {
                vp.SelectedElementGuids.Add(ifcGuid);
            }
        }

        return vp;
    }

    private static XElement? FindChild(this XContainer parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> FindDescendants(this XContainer parent, string localName) =>
        parent.Descendants().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static string? ChildValue(this XContainer parent, string localName) =>
        parent.FindChild(localName)?.Value;
}
