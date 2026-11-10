using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class MaterialLayerTests
{
    [Fact]
    public void MaterialLayerSet_CalculatesTotalThickness()
    {
        var matConcrete = new IfcMaterial { Name = "Reinforced Concrete" };
        var matInsulation = new IfcMaterial { Name = "Mineral Wool" };
        var matPlaster = new IfcMaterial { Name = "Gypsum Plaster" };

        var layerSet = new IfcMaterialLayerSet("External Insulated Wall");
        layerSet.AddLayer(matPlaster, 15);
        layerSet.AddLayer(matConcrete, 200);
        layerSet.AddLayer(matInsulation, 100);
        layerSet.AddLayer(matPlaster, 15);

        Assert.Equal(4, layerSet.Layers.Count);
        Assert.Equal(330, layerSet.TotalThickness);
    }

    [Fact]
    public void MaterialProfileSet_ManagesProfiles()
    {
        var profile = new IfcIShapeProfileDef(200, 300, 10, 15, name: "HEA 300");
        var steel = new IfcMaterial { Name = "Steel S355" };

        var profileSet = new IfcMaterialProfileSet("Structural Steel Frame");
        profileSet.AddProfile(profile, steel, "Main Girder");

        Assert.Single(profileSet.MaterialProfiles);
        Assert.Equal("Main Girder", profileSet.MaterialProfiles[0].Name);
        Assert.Equal(steel, profileSet.MaterialProfiles[0].Material);
    }

    [Fact]
    public void MaterialSystems_SaveAndLoad_RoundtripsSuccessfully()
    {
        var model = IfcModel.Create("Material System Model");
        var wall = model.AddExtrudedRectangle("CavityWall", 300, 1000, 3000);

        var mBrick = new IfcMaterial { Name = "Facing Brick" };
        var mAir = new IfcMaterial { Name = "Air Cavity" };
        var mBlock = new IfcMaterial { Name = "Concrete Block" };

        var layerSet = model.AddMaterialLayerSet("Cavity Wall Construction");
        layerSet.AddLayer(mBrick, 102.5);
        layerSet.AddLayer(mAir, 50, isVentilated: true);
        layerSet.AddLayer(mBlock, 100);

        model.AssignMaterialLayerSet(wall, layerSet, offset: 0.0);

        string stepStr = model.ToStepString();
        Assert.Contains("IFCMATERIALLAYER", stepStr);
        Assert.Contains("IFCMATERIALLAYERSET", stepStr);
        Assert.Contains("IFCMATERIALLAYERSETUSAGE", stepStr);
        Assert.Contains("IFCRELASSOCIATESMATERIAL", stepStr);

        using var reader = new StringReader(stepStr);
        var loaded = IfcModel.Open(reader);

        var loadedWall = loaded.AllElements.FirstOrDefault(e => e.Name == "CavityWall");
        Assert.NotNull(loadedWall);
        Assert.NotNull(loadedWall.MaterialLayerSet);
        Assert.Equal(3, loadedWall.MaterialLayerSet.ForLayerSet.Layers.Count);
        Assert.Equal(252.5, loadedWall.MaterialLayerSet.ForLayerSet.TotalThickness);
        Assert.True(loadedWall.MaterialLayerSet.ForLayerSet.Layers[1].IsVentilated);
    }

    [Fact]
    public void Validator_DetectsProfileAndLayerViolations()
    {
        var model = IfcModel.Create("Validation Test");

        // 1. Invalid I-Shape profile: flange thickness 100 on depth 150 (2*100 = 200 >= 150)
        var invalidI = new IfcIShapeProfileDef(overallWidth: 100, overallDepth: 150, webThickness: 10, flangeThickness: 100);
        var solid = new IfcExtrudedAreaSolid(invalidI, 1000);
        var beam = model.AddExtrudedRectangle("Beam", 10, 10, 10);
        beam.Solid = solid;

        // 2. Invalid layer thickness: <= 0
        var mat = new IfcMaterial { Name = "Concrete" };
        var layerSet = model.AddMaterialLayerSet("Invalid Layers");
        layerSet.AddLayer(mat, -5.0);

        var result = model.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_PROFILE_SECTION");
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_LAYER_THICKNESS");
    }
}
