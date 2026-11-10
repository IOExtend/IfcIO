using IfcIO.Bcf;
using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class BcfTests
{
    [Fact]
    public void BcfArchiveSaveAndReadRoundtrip()
    {
        var topic = new BcfTopic(
            title: "Clash between HVAC and Structural Beam",
            description: "Duct penetrates bottom flange of W12x26 beam without penetration sleeve.",
            topicType: "Clash",
            priority: "High")
        {
            CreationAuthor = "engineer@bim.org",
            AssignedTo = "mep-contractor@bim.org",
        };

        topic.AddComment("Reviewed on site. Need penetration detail.", "lead-architect@bim.org");

        var vp = new BcfViewpoint
        {
            CameraPosition = new IfcVector3(12.5, -4.0, 3.5),
            CameraDirection = new IfcVector3(0.0, 1.0, 0.0),
            CameraUpVector = new IfcVector3(0.0, 0.0, 1.0),
            FieldOfView = 60.0,
        };
        vp.SelectedElementGuids.Add("2O2Fr$t4X7Zf8NOew3FL9r");
        vp.SelectedElementGuids.Add("3O2Fr$t4X7Zf8NOew3FL9s");
        topic.Viewpoints.Add(vp);

        using var ms = new MemoryStream();
        BcfZipExporter.Write(ms, [topic], "Commercial Tower BCF");
        ms.Position = 0;

        var loaded = BcfZipExporter.Read(ms);

        Assert.Single(loaded);
        var loadedTopic = loaded[0];
        Assert.Equal("Clash between HVAC and Structural Beam", loadedTopic.Title);
        Assert.Equal("Clash", loadedTopic.TopicType);
        Assert.Equal("High", loadedTopic.Priority);
        Assert.Equal("engineer@bim.org", loadedTopic.CreationAuthor);
        Assert.Equal("mep-contractor@bim.org", loadedTopic.AssignedTo);

        Assert.Single(loadedTopic.Comments);
        Assert.Equal("lead-architect@bim.org", loadedTopic.Comments[0].Author);
        Assert.Contains("penetration detail", loadedTopic.Comments[0].Comment, StringComparison.OrdinalIgnoreCase);

        Assert.Single(loadedTopic.Viewpoints);
        var loadedVp = loadedTopic.Viewpoints[0];
        Assert.NotNull(loadedVp.CameraPosition);
        Assert.Equal(12.5, loadedVp.CameraPosition.Value.X, 3);
        Assert.Equal(2, loadedVp.SelectedElementGuids.Count);
        Assert.Contains("2O2Fr$t4X7Zf8NOew3FL9r", loadedVp.SelectedElementGuids);
    }

    [Fact]
    public void ExportValidationBcfGeneratesIssuesWithGuids()
    {
        string tempBcf = Path.Combine(Path.GetTempPath(), $"IfcIO_ValBcf_{Guid.NewGuid():N}.bcfzip");
        try
        {
            var model = IfcModel.Create("Validation Model");
            // Add a space with invalid negative height to trigger validation error
            var space = model.AddSpace("Invalid Room", grossFloorArea: 25.0, height: -2.0);
            space.GlobalId = "1O2Fr$t4X7Zf8NOew3FL9a";

            model.ExportValidationBcf(tempBcf);

            Assert.True(File.Exists(tempBcf));
            var topics = BcfZipExporter.Open(tempBcf);
            Assert.NotEmpty(topics);

            var spaceTopic = topics.FirstOrDefault(t => t.Title.Contains("IFC_SPACE_HEIGHT"));
            Assert.NotNull(spaceTopic);
            Assert.Single(spaceTopic.Viewpoints);
            Assert.Contains("1O2Fr$t4X7Zf8NOew3FL9a", spaceTopic.Viewpoints[0].SelectedElementGuids);
        }
        finally
        {
            if (File.Exists(tempBcf))
            {
                File.Delete(tempBcf);
            }
        }
    }
}
