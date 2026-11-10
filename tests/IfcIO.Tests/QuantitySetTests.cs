using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public class QuantitySetTests
{
    [Fact]
    public void StandardQtoSets_WallBaseQuantities_PopulatesCorrectly()
    {
        var qto = StandardQtoSets.WallBaseQuantities(
            length: 6000.0,
            width: 300.0,
            height: 3000.0,
            grossFootprintArea: 1.8,
            grossVolume: 5.4,
            netVolume: 5.0);

        Assert.Equal("Qto_WallBaseQuantities", qto.Name);
        Assert.Equal(6000.0, qto.GetLength("Length"));
        Assert.Equal(300.0, qto.GetLength("Width"));
        Assert.Equal(3000.0, qto.GetLength("Height"));
        Assert.Equal(1.8, qto.GetArea("GrossFootprintArea"));
        Assert.Equal(5.4, qto.GetVolume("GrossVolume"));
        Assert.Equal(5.0, qto.GetVolume("NetVolume"));
    }

    [Fact]
    public void QuantityCalculator_ExtrudedWall_CalculatesAccurateVolumes()
    {
        var model = IfcModel.Create("Quantities Test");
        var wall = model.AddExtrudedRectangle("Exterior Wall", xDim: 5000, yDim: 300, depth: 3000, ifcType: "IFCWALL");

        var qto = IfcQuantityCalculator.CalculateBaseQuantities(wall);

        Assert.Equal("Qto_WallBaseQuantities", qto.Name);
        Assert.Equal(5000.0, qto.GetLength("Length"));
        Assert.Equal(300.0, qto.GetLength("Width"));
        Assert.Equal(3000.0, qto.GetLength("Height"));

        // 5000 * 300 = 1,500,000 mm2
        Assert.Equal(1500000.0, qto.GetArea("GrossFootprintArea"));

        // 5000 * 300 * 3000 = 4,500,000,000 mm3
        Assert.Equal(4500000000.0, qto.GetVolume("GrossVolume"));
        Assert.Equal(4500000000.0, qto.GetVolume("NetVolume"));
    }

    [Fact]
    public void QuantityCalculator_WallWithOpening_DeductsOpeningVolume()
    {
        var model = IfcModel.Create("Opening Quantities");
        var wall = model.AddExtrudedRectangle("Wall with Window", xDim: 6000, yDim: 200, depth: 3000, ifcType: "IFCWALL");

        // Window opening: 1000 x 200 x 1500 (volume = 300,000,000)
        var openingProfile = new IfcRectangleProfileDef(1000, 200);
        var openingSolid = new IfcExtrudedAreaSolid(openingProfile, 1500);
        model.AddOpening(wall, "Window Cutout", solid: openingSolid);

        var qto = IfcQuantityCalculator.CalculateBaseQuantities(wall);

        // Gross = 6000 * 200 * 3000 = 3,600,000,000
        Assert.Equal(3600000000.0, qto.GetVolume("GrossVolume"));

        // Opening = 1000 * 200 * 1500 = 300,000,000
        // Net = 3,600,000,000 - 300,000,000 = 3,300,000,000
        Assert.Equal(3300000000.0, qto.GetVolume("NetVolume"));
    }

    [Fact]
    public void Model_CalculateAllBaseQuantities_AttachesQtoSets()
    {
        var model = IfcModel.Create("Bulk QTO");
        model.AddExtrudedRectangle("Wall 1", 4000, 200, 2800, ifcType: "IFCWALL");
        model.AddExtrudedCircle("Column 1", radius: 250, depth: 3500, ifcType: "IFCCOLUMN");

        model.CalculateAllBaseQuantities();

        var wall = Assert.Single(model.AllElements, e => e.IfcType == "IFCWALL");
        var column = Assert.Single(model.AllElements, e => e.IfcType == "IFCCOLUMN");

        var wallQto = Assert.Single(wall.QuantitySets, q => q.Name == "Qto_WallBaseQuantities");
        Assert.NotNull(wallQto.GetVolume("GrossVolume"));

        var colQto = Assert.Single(column.QuantitySets, q => q.Name == "Qto_ColumnBaseQuantities");
        Assert.NotNull(colQto.GetVolume("GrossVolume"));
    }

    [Fact]
    public void QuantitySets_StepRoundtrip_PreservesAllQuantities()
    {
        var model = IfcModel.Create("Roundtrip QTO");
        var wall = model.AddExtrudedRectangle("Partition Wall", 3000, 150, 2700, ifcType: "IFCWALL");

        var customQto = new IfcQuantitySet("Qto_CustomQuantities", description: "Custom measurements");
        customQto.AddLength("CustomLength", 3000.0, "Measured length");
        customQto.AddArea("PaintArea", 16.2, "Two sides paint area");
        customQto.AddVolume("GrossVolume", 1.215);
        customQto.AddCount("BrickCount", 450);
        customQto.AddWeight("TotalWeight", 2850.0);
        wall.QuantitySets.Add(customQto);

        string tempPath = Path.Combine(Path.GetTempPath(), $"qto_test_{Guid.NewGuid():N}.ifc");
        try
        {
            model.Save(tempPath);

            var loaded = IfcModel.Open(tempPath);
            var loadedElem = Assert.Single(loaded.AllElements);
            var loadedQto = Assert.Single(loadedElem.QuantitySets, q => q.Name == "Qto_CustomQuantities");

            Assert.Equal("Custom measurements", loadedQto.Description);
            Assert.Equal(3000.0, loadedQto.GetLength("CustomLength"));
            Assert.Equal(16.2, loadedQto.GetArea("PaintArea"));
            Assert.Equal(1.215, loadedQto.GetVolume("GrossVolume"));
            Assert.Equal(450.0, loadedQto.GetCount("BrickCount"));
            Assert.Equal(2850.0, loadedQto.GetWeight("TotalWeight"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Validator_NegativeQuantity_ReportsError()
    {
        var model = IfcModel.Create("Validation QTO");
        var wall = model.AddExtrudedRectangle("Wall", 3000, 200, 2500);

        var badQto = new IfcQuantitySet("Qto_Invalid");
        badQto.AddVolume("GrossVolume", -10.5);
        wall.QuantitySets.Add(badQto);

        var result = model.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_QUANTITY_POSITIVE");
    }
}
