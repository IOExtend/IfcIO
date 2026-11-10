using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using IfcIO.Validation;
using Xunit;

namespace IfcIO.Tests;

public class ValidatorTests
{
    [Fact]
    public void Validate_CleanModel_PassesWithZeroErrors()
    {
        var model = IfcModel.Create("Residential Complex A", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);
        var mat = new IfcMaterial { Name = "Concrete", R = 0.8, G = 0.8, B = 0.8 };

        model.AddMesh("Wall 1", [new(0, 0, 0), new(5000, 0, 0), new(5000, 3000, 0)], [0, 1, 2], mat, storey);

        var result = model.Validate();

        Assert.True(result.IsValid);
        Assert.Equal(0, result.ErrorCount);
        Assert.DoesNotContain(result.Issues, i => i.Severity == IfcIssueSeverity.Error);
    }

    [Fact]
    public void Validate_EmptyProjectName_ReportsError()
    {
        var model = IfcModel.Create("", IfcSchema.Ifc4);
        var result = model.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_PROJECT_NAME" && i.Severity == IfcIssueSeverity.Error);
    }

    [Fact]
    public void Validate_DuplicateGlobalId_ReportsError()
    {
        var model = IfcModel.Create("Hospital Block", IfcSchema.Ifc4);
        var storey = model.AddStorey("Ground Floor", 0.0);
        string sharedGuid = "0$A_B_C_D_E_F_G_H_I_J_K";

        var elem1 = new IfcElement { Id = 1, Name = "Beam A", GlobalId = sharedGuid };
        var elem2 = new IfcElement { Id = 2, Name = "Beam B", GlobalId = sharedGuid };

        storey.Elements.Add(elem1);
        storey.Elements.Add(elem2);

        var result = model.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_GLOBAL_ID_UNIQUE" && i.Severity == IfcIssueSeverity.Error);
    }

    [Fact]
    public void Validate_InvalidGlobalIdLength_ReportsError()
    {
        var model = IfcModel.Create("Office Tower", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 2", 3.0);
        var elem = new IfcElement { Id = 1, Name = "Door 1", GlobalId = "TooShort" };
        storey.Elements.Add(elem);

        var result = model.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_GLOBAL_ID_LENGTH" && i.Severity == IfcIssueSeverity.Error);
    }

    [Fact]
    public void Validate_MeshWithInvalidIndices_ReportsError()
    {
        var model = IfcModel.Create("Parking Garage", IfcSchema.Ifc4);
        var storey = model.AddStorey("B1", -3.5);

        // 3 vertices, but index 99 is out of bounds!
        var mesh = IfcMesh.Create(1, "Ramp", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 99]);
        var elem = new IfcElement { Id = 1, Name = "Ramp 1", Mesh = mesh };
        storey.Elements.Add(elem);

        var result = model.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_GEOMETRY_INDEX_OUT_OF_BOUNDS");
    }

    [Fact]
    public void Validate_MeshWithNonFiniteVertex_ReportsError()
    {
        var model = IfcModel.Create("Bridge Deck", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 0", 0.0);

        var mesh = IfcMesh.Create(1, "Segment", [new(0, 0, 0), new(double.NaN, 0, 0), new(0, 1, 0)], [0, 1, 2]);
        var elem = new IfcElement { Id = 1, Name = "Segment 1", Mesh = mesh };
        storey.Elements.Add(elem);

        var result = model.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_GEOMETRY_VERTEX_COORDINATES");
    }

    [Fact]
    public void Validate_MeshWithDegenerateFace_ReportsWarning()
    {
        var model = IfcModel.Create("Warehouse", IfcSchema.Ifc4);
        var storey = model.AddStorey("Floor", 0.0);

        // Face has duplicate indices (0, 0, 1) -> zero area
        var mesh = IfcMesh.Create(1, "DegenerateSlab", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 0, 1]);
        var elem = new IfcElement { Id = 1, Name = "Slab 1", Mesh = mesh };
        storey.Elements.Add(elem);

        var result = model.Validate();

        Assert.True(result.IsValid); // Warnings do not invalidate the model
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_GEOMETRY_DEGENERATE_FACES" && i.Severity == IfcIssueSeverity.Warning);
    }

    [Fact]
    public void Validate_DuplicatePropertyNameInPset_ReportsWarning()
    {
        var model = IfcModel.Create("School", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);
        var elem = new IfcElement { Id = 1, Name = "Window 1" };

        var pset = elem.GetOrCreatePropertySet("Pset_WindowCommon");
        pset.Add("FireRating", "EI 60");
        pset.Add("FireRating", "EI 90"); // duplicate property name!

        storey.Elements.Add(elem);

        var result = model.Validate();

        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_PROPERTY_NAME_DUPLICATE" && i.Severity == IfcIssueSeverity.Warning);
    }
}
