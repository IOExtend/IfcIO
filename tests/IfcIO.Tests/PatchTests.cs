using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Patch;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class PatchTests
{
    [Fact]
    public void Purge_RemovesEmptyPropertySetsAndEmptyStoreys()
    {
        var model = IfcModel.Create("Purge Test", IfcSchema.Ifc4);
        var activeStorey = model.AddStorey("Level 1", 0.0);
        var emptyStorey = model.AddStorey("Unused Level", 3000.0);

        var wall = new IfcElement { Id = 1, Name = "Wall", IfcType = "IfcWall" };

        // Valid pset
        var validPset = new IfcPropertySet { Name = "Pset_WallCommon" };
        validPset.Add("LoadBearing", true);
        wall.PropertySets.Add(validPset);

        // Empty pset
        var emptyPset = new IfcPropertySet { Name = "Pset_Empty" };
        wall.PropertySets.Add(emptyPset);

        // Duplicate pset
        var dupPset = new IfcPropertySet { Name = "Pset_WallCommon" };
        dupPset.Add("LoadBearing", true);
        wall.PropertySets.Add(dupPset);

        activeStorey.Elements.Add(wall);

        var stats = model.Purge();

        Assert.Equal(2, stats.RemovedPropertySets);
        Assert.Equal(1, stats.RemovedEmptyStoreys);
        Assert.Single(wall.PropertySets);
        Assert.Equal("Pset_WallCommon", wall.PropertySets[0].Name);
        Assert.DoesNotContain(model.Storeys, s => s.Name == "Unused Level");
    }

    [Fact]
    public void ResetCoordinates_RecentersUTMCoordinatesAndPreservesZ()
    {
        var model = IfcModel.Create("Civil Model", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        // UTM coordinates around (500,000, 6,000,000)
        var mat = new IfcMaterial { Name = "Concrete" };
        var vertices = new List<IfcVector3>
        {
            new(500000.0, 6000000.0, 15.0),
            new(500010.0, 6000000.0, 15.0),
            new(500010.0, 6000020.0, 15.0),
        };
        var mesh = IfcMesh.Create(1, "Mesh", vertices, [0, 1, 2]);
        var elem = new IfcElement { Id = 1, Name = "Road Segment", IfcType = "IfcCivilElement", Mesh = mesh };
        storey.Elements.Add(elem);

        bool recentered = model.ResetCoordinates(out var offset);

        Assert.True(recentered);
        Assert.True(Math.Abs(offset.X + 500005.0) < 1.0);
        Assert.True(Math.Abs(offset.Y + 6000010.0) < 1.0);
        Assert.Equal(0.0, offset.Z);

        // Verify element coordinates are now centered around (0, 0)
        var updatedVerts = elem.Mesh!.Vertices;
        Assert.True(Math.Abs(updatedVerts[0].X) < 20.0);
        Assert.True(Math.Abs(updatedVerts[0].Y) < 20.0);
        Assert.Equal(15.0, updatedVerts[0].Z);

        // Second call should detect model is already near origin
        bool secondCall = model.ResetCoordinates(out _);
        Assert.False(secondCall);
    }

    [Fact]
    public void OffsetCoordinates_TranslatesAllVertices()
    {
        var model = IfcModel.Create("Offset Test", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var mesh = IfcMesh.Create(1, "Box", [new(10, 20, 30), new(40, 50, 60)], [0, 1]);
        var elem = new IfcElement { Id = 1, Name = "Box Element", IfcType = "IfcBuildingElementProxy", Mesh = mesh };
        storey.Elements.Add(elem);

        model.OffsetCoordinates(new IfcVector3(5, -10, 15));

        var v = elem.Mesh!.Vertices;
        Assert.Equal(15.0, v[0].X);
        Assert.Equal(10.0, v[0].Y);
        Assert.Equal(45.0, v[0].Z);
        Assert.Equal(45.0, v[1].X);
        Assert.Equal(40.0, v[1].Y);
        Assert.Equal(75.0, v[1].Z);
    }

    [Fact]
    public void Extract_CreatesIsolatedSubModel()
    {
        var model = IfcModel.Create("Full Model", IfcSchema.Ifc4);
        var lvl1 = model.AddStorey("Level 1", 0.0);
        var lvl2 = model.AddStorey("Level 2", 3000.0);

        lvl1.Elements.Add(new IfcElement { Id = 1, Name = "Wall L1", IfcType = "IfcWall" });
        lvl1.Elements.Add(new IfcElement { Id = 2, Name = "Col L1", IfcType = "IfcColumn" });
        lvl2.Elements.Add(new IfcElement { Id = 3, Name = "Wall L2", IfcType = "IfcWall" });

        var wallsOnly = model.Extract("IfcWall", "Walls Package");

        Assert.Equal("Walls Package", wallsOnly.ProjectName);
        Assert.Equal(2, wallsOnly.Storeys.Count);
        Assert.Equal(2, wallsOnly.AllElements.Count());
        Assert.All(wallsOnly.AllElements, e => Assert.Equal("IfcWall", e.IfcType));
    }

    [Fact]
    public void Merge_CombinesModelsAndReconcilesStoreys()
    {
        var modelArch = IfcModel.Create("Architecture", IfcSchema.Ifc4);
        var archL1 = modelArch.AddStorey("Ground Floor", 0.0);
        archL1.Elements.Add(new IfcElement { Id = 10, Name = "Exterior Wall", IfcType = "IfcWall" });

        var modelStruct = IfcModel.Create("Structure", IfcSchema.Ifc4);
        var structL1 = modelStruct.AddStorey("Ground Floor", 0.0);
        structL1.Elements.Add(new IfcElement { Id = 10, Name = "Concrete Column", IfcType = "IfcColumn" });
        var structL2 = modelStruct.AddStorey("Second Floor", 4000.0);
        structL2.Elements.Add(new IfcElement { Id = 20, Name = "Roof Slab", IfcType = "IfcSlab" });

        var merged = IfcPatch.Merge([modelArch, modelStruct], "Combined Campus");

        Assert.Equal("Combined Campus", merged.ProjectName);
        Assert.Equal(2, merged.Storeys.Count);

        var groundStorey = merged.Storeys.FirstOrDefault(s => s.Name == "Ground Floor");
        Assert.NotNull(groundStorey);
        Assert.Equal(2, groundStorey.Elements.Count);

        // Check IDs are sequential and unique
        var allIds = merged.AllElements.Select(e => e.Id).ToList();
        Assert.Equal(3, allIds.Count);
        Assert.Equal(allIds.Distinct().Count(), allIds.Count);
        Assert.Equal([1, 2, 3], allIds);
    }
}
