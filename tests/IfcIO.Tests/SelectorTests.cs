using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Query;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class SelectorTests
{
    [Fact]
    public void Evaluate_EntityType_MatchesExactAndPrefix()
    {
        var model = IfcModel.Create("Test Query", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wall1 = new IfcElement { Id = 1, Name = "Basic Wall", IfcType = "IfcWall" };
        var wallStd = new IfcElement { Id = 2, Name = "Standard Wall", IfcType = "IFCWALLSTANDARDCASE" };
        var col = new IfcElement { Id = 3, Name = "Steel Col", IfcType = "IfcColumn" };

        storey.Elements.Add(wall1);
        storey.Elements.Add(wallStd);
        storey.Elements.Add(col);

        var walls = model.Select("IfcWall").ToList();
        Assert.Equal(2, walls.Count);
        Assert.Contains(wall1, walls);
        Assert.Contains(wallStd, walls);
        Assert.DoesNotContain(col, walls);
    }

    [Fact]
    public void Evaluate_StoreyFilter_MatchesCorrectStorey()
    {
        var model = IfcModel.Create("MultiStorey Model", IfcSchema.Ifc4);
        var lvl1 = model.AddStorey("Level 1", 0.0);
        var lvl2 = model.AddStorey("Level 2", 3500.0);

        var wallL1 = new IfcElement { Id = 1, Name = "Wall L1", IfcType = "IfcWall" };
        var wallL2 = new IfcElement { Id = 2, Name = "Wall L2", IfcType = "IfcWall" };

        lvl1.Elements.Add(wallL1);
        lvl2.Elements.Add(wallL2);

        var resultsL1 = model.Select("IfcWall#Level 1").ToList();
        Assert.Single(resultsL1);
        Assert.Equal("Wall L1", resultsL1[0].Name);

        var resultsL2 = model.Select("@Level 2").ToList();
        Assert.Single(resultsL2);
        Assert.Equal("Wall L2", resultsL2[0].Name);
    }

    [Fact]
    public void Evaluate_PropertyConditions_BooleanAndNumericComparison()
    {
        var model = IfcModel.Create("Property Filter Model", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wallA = new IfcElement { Id = 1, Name = "Concrete Wall A", IfcType = "IfcWall" };
        var psetA = new IfcPropertySet { Name = "Pset_WallCommon" };
        psetA.Add("LoadBearing", true);
        psetA.Add("FireRating", 90);
        wallA.PropertySets.Add(psetA);

        var wallB = new IfcElement { Id = 2, Name = "Drywall Partition", IfcType = "IfcWall" };
        var psetB = new IfcPropertySet { Name = "Pset_WallCommon" };
        psetB.Add("LoadBearing", false);
        psetB.Add("FireRating", 30);
        wallB.PropertySets.Add(psetB);

        storey.Elements.Add(wallA);
        storey.Elements.Add(wallB);

        var loadBearingWalls = model.Select("IfcWall[LoadBearing=TRUE]").ToList();
        Assert.Single(loadBearingWalls);
        Assert.Equal("Concrete Wall A", loadBearingWalls[0].Name);

        var fireResistant = model.Select("IfcWall[FireRating>=60]").ToList();
        Assert.Single(fireResistant);
        Assert.Equal("Concrete Wall A", fireResistant[0].Name);

        var concWalls = model.Select("[Name*='Concrete']").ToList();
        Assert.Single(concWalls);
        Assert.Equal("Concrete Wall A", concWalls[0].Name);
    }

    [Fact]
    public void Evaluate_UnionSelector_MatchesEither()
    {
        var model = IfcModel.Create("Union Model", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wall = new IfcElement { Id = 1, Name = "Wall", IfcType = "IfcWall" };
        var col = new IfcElement { Id = 2, Name = "Column", IfcType = "IfcColumn" };
        var slab = new IfcElement { Id = 3, Name = "Slab", IfcType = "IfcSlab" };

        storey.Elements.Add(wall);
        storey.Elements.Add(col);
        storey.Elements.Add(slab);

        var structure = model.Select("IfcWall, IfcColumn").ToList();
        Assert.Equal(2, structure.Count);
        Assert.Contains(wall, structure);
        Assert.Contains(col, structure);
        Assert.DoesNotContain(slab, structure);
    }

    [Fact]
    public void Matches_ExtensionMethod_WorksDirectlyOnElement()
    {
        var elem = new IfcElement { Id = 42, Name = "Exterior Panel", IfcType = "IfcCurtainWall", Tag = "CW-101" };
        elem.PropertySets.Add(new IfcPropertySet
        {
            Name = "Pset_CurtainWallCommon",
            Properties = { new IfcPropertySingleValue { Name = "Status", Value = "Approved" } }
        });

        Assert.True(elem.Matches("IfcCurtainWall"));
        Assert.True(elem.Matches("[Tag='CW-101']"));
        Assert.True(elem.Matches("[Status='Approved']"));
        Assert.False(elem.Matches("IfcWall"));
        Assert.False(elem.Matches("[Status='Rejected']"));
    }
}
