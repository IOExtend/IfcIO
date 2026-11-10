using IfcIO.Clash;
using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class ClashGrouperTests
{
    private static IfcClashReport CreateSampleReport(out IfcModel model)
    {
        model = IfcModel.Create("Sample Model");
        var l1 = model.AddStorey("Level 1", 0.0);
        var l2 = model.AddStorey("Level 2", 4.0);

        var pipe1 = new IfcElement { Id = 1, Name = "Pipe 100mm", IfcType = "IfcFlowSegment", GlobalId = "GUID_PIPE1" };
        var beam1 = new IfcElement { Id = 2, Name = "Beam W12x26", IfcType = "IfcBeam", GlobalId = "GUID_BEAM1" };
        var pipe2 = new IfcElement { Id = 3, Name = "Pipe 50mm", IfcType = "IfcFlowSegment", GlobalId = "GUID_PIPE2" };
        var beam2 = new IfcElement { Id = 4, Name = "Beam W14x30", IfcType = "IfcBeam", GlobalId = "GUID_BEAM2" };

        l1.Elements.Add(pipe1);
        l1.Elements.Add(beam1);
        l2.Elements.Add(pipe2);
        l2.Elements.Add(beam2);

        var report = new IfcClashReport
        {
            ModelAName = "MEP",
            ModelBName = "Structure",
            Clashes =
            {
                // Cluster 1 near (0, 0, 1)
                new IfcClashResult
                {
                    ElementA = pipe1,
                    ElementB = beam1,
                    ClashPoint = new IfcVector3(0.1, 0.2, 1.0),
                    Distance = 0.05,
                    GridLocation = "A / 1"
                },
                new IfcClashResult
                {
                    ElementA = pipe1,
                    ElementB = beam1,
                    ClashPoint = new IfcVector3(0.3, 0.4, 1.1),
                    Distance = 0.04,
                    GridLocation = "A / 1"
                },
                // Cluster 2 far away at (20, 20, 5)
                new IfcClashResult
                {
                    ElementA = pipe2,
                    ElementB = beam2,
                    ClashPoint = new IfcVector3(20.0, 20.0, 5.0),
                    Distance = 0.02,
                    GridLocation = "B / 2"
                },
                new IfcClashResult
                {
                    ElementA = pipe2,
                    ElementB = beam2,
                    ClashPoint = new IfcVector3(20.2, 20.1, 5.1),
                    Distance = 0.03,
                    GridLocation = "B / 2"
                },
                // Standalone isolated clash at (100, 100, 0)
                new IfcClashResult
                {
                    ElementA = pipe1,
                    ElementB = beam2,
                    ClashPoint = new IfcVector3(100.0, 100.0, 0.0),
                    Distance = 0.01,
                    GridLocation = "C / 3"
                }
            }
        };

        return report;
    }

    [Fact]
    public void GroupByProximity_ClustersNearbyClashes()
    {
        var report = CreateSampleReport(out _);
        Assert.Equal(5, report.Clashes.Count);

        int groupsCreated = report.GroupByProximity(clusterDistance: 1.0, minGroupSize: 2);

        Assert.Equal(2, groupsCreated);
        Assert.Equal(2, report.Groups.Count);
        Assert.Single(report.Clashes); // 1 standalone remaining

        Assert.Equal(2, report.Groups[0].Clashes.Count);
        Assert.Equal(2, report.Groups[1].Clashes.Count);
    }

    [Fact]
    public void GroupByPrimaryElement_BundlesClashesByElementA()
    {
        var report = CreateSampleReport(out _);

        int groupsCreated = report.GroupByPrimaryElement(minGroupSize: 2);

        // Pipe 1 has 3 clashes, Pipe 2 has 2 clashes
        Assert.Equal(2, groupsCreated);
        Assert.Equal(2, report.Groups.Count);
        Assert.Empty(report.Clashes); // all bundled

        var pipe1Group = report.Groups.FirstOrDefault(g => g.Name.Contains("GUID_PIPE1"));
        Assert.NotNull(pipe1Group);
        Assert.Equal(3, pipe1Group.Clashes.Count);
    }

    [Fact]
    public void GroupByStorey_BundlesClashesByBuildingStorey()
    {
        var report = CreateSampleReport(out var model);

        int groupsCreated = report.GroupByStorey(model, minGroupSize: 2);

        Assert.Equal(2, groupsCreated);
        Assert.Equal(2, report.Groups.Count);

        var lvl1Group = report.Groups.FirstOrDefault(g => g.Name.Contains("Level 1"));
        Assert.NotNull(lvl1Group);
        Assert.True(lvl1Group.Clashes.Count >= 2);
    }

    [Fact]
    public void GroupByGrid_BundlesClashesByGridLocation()
    {
        var report = CreateSampleReport(out _);

        int groupsCreated = report.GroupByGrid(minGroupSize: 2);

        // A / 1 has 2, B / 2 has 2, C / 3 has 1
        Assert.Equal(2, groupsCreated);
        Assert.Equal(2, report.Groups.Count);
        Assert.Single(report.Clashes); // C / 3 isolated
    }

    [Fact]
    public void UngroupAll_RestoresClashesToMainList()
    {
        var report = CreateSampleReport(out _);
        report.GroupByProximity(clusterDistance: 1.0);
        Assert.Equal(2, report.Groups.Count);
        Assert.Single(report.Clashes);

        int dissolved = report.UngroupAll();

        Assert.Equal(4, dissolved);
        Assert.Empty(report.Groups);
        Assert.Equal(5, report.Clashes.Count);
    }

    [Fact]
    public void SaveBcf_WithGroups_ProducesValidArchive()
    {
        var report = CreateSampleReport(out _);
        report.GroupByProximity(clusterDistance: 1.0);

        string tempBcf = Path.Combine(Path.GetTempPath(), $"IfcClash_{Guid.NewGuid():N}.bcfzip");
        try
        {
            report.SaveBcf(tempBcf);
            Assert.True(File.Exists(tempBcf));
            var fi = new FileInfo(tempBcf);
            Assert.True(fi.Length > 0);
        }
        finally
        {
            if (File.Exists(tempBcf)) File.Delete(tempBcf);
        }
    }
}
