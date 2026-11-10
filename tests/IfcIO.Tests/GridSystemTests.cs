using IfcIO.Clash;
using IfcIO.Geometry;
using IfcIO.Grids;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class GridSystemTests
{
    [Fact]
    public void GridAxis_CalculatesDistanceAndIntersection()
    {
        // Axis A along Y-axis at X = 0
        var axisA = new IfcGridAxis("A", new IfcVector3(0, -10, 0), new IfcVector3(0, 10, 0));
        // Axis 1 along X-axis at Y = 0
        var axis1 = new IfcGridAxis("1", new IfcVector3(-10, 0, 0), new IfcVector3(10, 0, 0));

        Assert.Equal(20.0, axisA.Length, 3);
        Assert.Equal(20.0, axis1.Length, 3);

        double distToA = axisA.DistanceToPoint2D(new IfcVector3(2.5, 5, 0));
        Assert.Equal(2.5, distToA, 3);

        bool intersects = axisA.Intersect2D(axis1, out var inter);
        Assert.True(intersects);
        Assert.Equal(0.0, inter.X, 3);
        Assert.Equal(0.0, inter.Y, 3);
    }

    [Fact]
    public void GridSystem_FindsNearestAndFormatsLocation()
    {
        var grids = new IfcGridSystem("Building Grids");
        grids.AddAxis("A", new IfcVector3(0, -20, 0), new IfcVector3(0, 20, 0));
        grids.AddAxis("B", new IfcVector3(6, -20, 0), new IfcVector3(6, 20, 0));
        grids.AddAxis("1", new IfcVector3(-10, 0, 0), new IfcVector3(20, 0, 0));
        grids.AddAxis("2", new IfcVector3(-10, 8, 0), new IfcVector3(20, 8, 0));

        var inters = grids.GetIntersections();
        Assert.Equal(4, inters.Count); // A/1, A/2, B/1, B/2

        // Exact snap at (0, 0)
        string locExact = grids.FormatLocation(new IfcVector3(0, 0, 0));
        Assert.Equal("A / 1", locExact);

        // Near (0, 0) with small offset
        string locOffset = grids.FormatLocation(new IfcVector3(0.5, -0.2, 0));
        Assert.Contains("A (+0.50m)", locOffset);
        Assert.Contains("1 (-0.20m)", locOffset);

        // Nearest intersection
        var nearestInter = grids.FindNearestIntersection(new IfcVector3(5.8, 8.1, 0), out double dInter);
        Assert.NotNull(nearestInter);
        Assert.Equal("B / 2", nearestInter.DisplayName);
        Assert.True(dInter < 0.5);
    }

    [Fact]
    public void AssignGridLocations_PopulatesClashesAndElements()
    {
        var grids = new IfcGridSystem("Test Grids");
        grids.AddAxis("A", new IfcVector3(0, -10, 0), new IfcVector3(0, 10, 0));
        grids.AddAxis("B", new IfcVector3(10, -10, 0), new IfcVector3(10, 10, 0));
        grids.AddAxis("1", new IfcVector3(-5, 0, 0), new IfcVector3(15, 0, 0));

        var model = IfcModel.Create("Grid Test Model");
        var storey = model.AddStorey("Level 1", 0.0);

        var elemA = new IfcElement
        {
            Id = 1,
            Name = "Column A",
            IfcType = "IfcColumn",
            Mesh = IfcMesh.Create(1, "ColMesh", [new IfcVector3(-0.2, -0.2, 0), new IfcVector3(0.2, 0.2, 3)], [0, 1])
        };
        storey.Elements.Add(elemA);

        int elemAssigned = model.AssignGridLocations(grids);
        Assert.Equal(1, elemAssigned);
        Assert.Equal("A / 1", elemA.GridLocation);

        var report = new IfcClashReport
        {
            ModelAName = "Test",
            Clashes =
            {
                new IfcClashResult
                {
                    ElementA = elemA,
                    ElementB = elemA,
                    ClashPoint = new IfcVector3(0, 0, 1.5)
                },
                new IfcClashResult
                {
                    ElementA = elemA,
                    ElementB = elemA,
                    ClashPoint = new IfcVector3(10, 0, 1.5)
                }
            }
        };

        int clashAssigned = report.AssignGridLocations(grids);
        Assert.Equal(2, clashAssigned);
        Assert.Equal("A / 1", report.Clashes[0].GridLocation);
        Assert.Equal("B / 1", report.Clashes[1].GridLocation);
    }

    [Fact]
    public void GridSystem_XmlRoundtrip_PreservesData()
    {
        var original = new IfcGridSystem("Hospital Grid Project");
        original.AddAxis("X1", new IfcVector3(0, 0, 0), new IfcVector3(100, 0, 0), elevation: 3.5, gridName: "Tower A");
        original.AddAxis("Y1", new IfcVector3(0, 0, 0), new IfcVector3(0, 80, 0), elevation: 3.5, gridName: "Tower A");

        using var ms = new MemoryStream();
        original.SaveXml(ms);
        ms.Position = 0;

        var loaded = IfcGridSystem.ReadXml(ms);
        Assert.Equal("Hospital Grid Project", loaded.Name);
        Assert.Equal(2, loaded.Axes.Count);

        var l1 = loaded.Axes.First(l => l.Tag == "X1");
        Assert.Equal(0.0, l1.Start.X, 3);
        Assert.Equal(100.0, l1.End.X, 3);
        Assert.Equal(3.5, l1.Elevation, 3);
        Assert.Equal("Tower A", l1.GridName);
    }

    [Fact]
    public void ExtractGridSystem_ExtractsGridAxesFromModel()
    {
        var model = IfcModel.Create("Model With Grids");
        var storey = model.AddStorey("Site", 0.0);

        var gridElem1 = new IfcElement
        {
            Id = 1,
            Name = "Grid 1",
            IfcType = "IfcGrid",
            Mesh = IfcMesh.Create(1, "Mesh", [new IfcVector3(0, 0, 0), new IfcVector3(50, 0, 0)], [0, 1])
        };
        var gridElemA = new IfcElement
        {
            Id = 2,
            Name = "Grid A",
            IfcType = "IfcGrid",
            Mesh = IfcMesh.Create(2, "Mesh", [new IfcVector3(0, 0, 0), new IfcVector3(0, 50, 0)], [0, 1])
        };

        storey.Elements.Add(gridElem1);
        storey.Elements.Add(gridElemA);

        var extracted = model.ExtractGridSystem();
        Assert.Equal(2, extracted.Axes.Count);
        Assert.Contains(extracted.Axes, a => a.Tag == "1");
        Assert.Contains(extracted.Axes, a => a.Tag == "A");
    }
}
