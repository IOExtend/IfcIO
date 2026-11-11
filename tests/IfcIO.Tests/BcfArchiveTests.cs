using System.Text;
using IfcIO.Bcf;
using IfcIO.Geometry;
using Xunit;

namespace IfcIO.Tests;

public class BcfArchiveTests
{
    [Fact]
    public void BcfArchive_RoundtripWithSnapshots_PreservesTopicsAndViewpoints()
    {
        var archive = new BcfArchive("MEP Coordination");

        var topic = archive.AddTopic("Pipe and Beam Interference", "Main chilled water pipe clashes with structural beam", "Clash", "Critical");
        topic.CreationAuthor = "BIM Manager";
        topic.AssignedTo = "HVAC Lead";
        topic.AddComment("Please lower the pipe run by 150mm.", "Structure Lead");

        var vp = new BcfViewpoint
        {
            CameraPosition = new IfcVector3(10.0, 5.0, 3.0),
            CameraDirection = new IfcVector3(0.0, 1.0, 0.0),
            CameraUpVector = new IfcVector3(0.0, 0.0, 1.0),
            FieldOfView = 60.0,
            SnapshotData = Encoding.UTF8.GetBytes("FAKE_PNG_BINARY_BYTES_FOR_TEST")
        };
        vp.SelectedElementGuids.Add("3$v9a8K2n1_abcDEF0123");
        vp.HiddenElementGuids.Add("1$a2b3c4d5_xyzUVW9876");
        topic.Viewpoints.Add(vp);

        using var ms = new MemoryStream();
        archive.Save(ms);
        ms.Position = 0;

        var loaded = BcfArchive.Open(ms);
        Assert.Single(loaded.Topics);
        var loadedTopic = loaded.Topics[0];

        Assert.Equal("Pipe and Beam Interference", loadedTopic.Title);
        Assert.Equal("Critical", loadedTopic.Priority);
        Assert.Equal("BIM Manager", loadedTopic.CreationAuthor);
        Assert.Equal("HVAC Lead", loadedTopic.AssignedTo);
        Assert.Single(loadedTopic.Comments);
        Assert.Equal("Please lower the pipe run by 150mm.", loadedTopic.Comments[0].Comment);

        Assert.Single(loadedTopic.Viewpoints);
        var loadedVp = loadedTopic.Viewpoints[0];
        Assert.NotNull(loadedVp.CameraPosition);
        Assert.Equal(10.0, loadedVp.CameraPosition.Value.X);
        Assert.Single(loadedVp.SelectedElementGuids);
        Assert.Equal("3$v9a8K2n1_abcDEF0123", loadedVp.SelectedElementGuids[0]);
        Assert.Single(loadedVp.HiddenElementGuids);
        Assert.Equal("1$a2b3c4d5_xyzUVW9876", loadedVp.HiddenElementGuids[0]);
        Assert.NotNull(loadedVp.SnapshotData);
        Assert.Equal("FAKE_PNG_BINARY_BYTES_FOR_TEST", Encoding.UTF8.GetString(loadedVp.SnapshotData));

        // Test archive search helpers
        var foundByElem = loaded.FindTopicsByElement("3$v9a8K2n1_abcDEF0123");
        Assert.Single(foundByElem);

        var foundByStatus = loaded.FindTopicsByStatus("Open");
        Assert.Single(foundByStatus);
    }
}
