using System.Text;
using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public class SweptSolidTests
{
    [Fact]
    public void ExtrudedRectangle_GeneratesCorrectMesh()
    {
        var profile = new IfcRectangleProfileDef(200.0, 400.0);
        var solid = new IfcExtrudedAreaSolid(profile, 3000.0);

        var mesh = solid.ToMesh();

        Assert.NotNull(mesh);
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(12, mesh.TriangleCount); // 2 bottom, 2 top, 8 sides = 12

        // Verify bounding box
        Assert.Equal(-100.0, mesh.BoundingBox.Min.X, precision: 3);
        Assert.Equal(100.0, mesh.BoundingBox.Max.X, precision: 3);
        Assert.Equal(-200.0, mesh.BoundingBox.Min.Y, precision: 3);
        Assert.Equal(200.0, mesh.BoundingBox.Max.Y, precision: 3);
        Assert.Equal(0.0, mesh.BoundingBox.Min.Z, precision: 3);
        Assert.Equal(3000.0, mesh.BoundingBox.Max.Z, precision: 3);
    }

    [Fact]
    public void ExtrudedCircle_GeneratesCorrectMesh()
    {
        var profile = new IfcCircleProfileDef(150.0);
        var solid = new IfcExtrudedAreaSolid(profile, 2000.0);

        int segments = 16;
        var mesh = solid.ToMesh(segments);

        Assert.NotNull(mesh);
        Assert.Equal(segments * 2, mesh.Vertices.Count);
        // (16 - 2) * 2 cap triangles + 16 * 2 side triangles = 28 + 32 = 60 triangles
        Assert.Equal((segments - 2) * 2 + segments * 2, mesh.TriangleCount);

        Assert.Equal(-150.0, mesh.BoundingBox.Min.X, precision: 1);
        Assert.Equal(150.0, mesh.BoundingBox.Max.X, precision: 1);
        Assert.Equal(0.0, mesh.BoundingBox.Min.Z, precision: 3);
        Assert.Equal(2000.0, mesh.BoundingBox.Max.Z, precision: 3);
    }

    [Fact]
    public void ExtrudedArbitraryPolygon_GeneratesMesh()
    {
        var points = new List<IfcVector3>
        {
            new(0, 0, 0),
            new(500, 0, 0),
            new(500, 300, 0),
            new(250, 500, 0),
            new(0, 300, 0),
        };
        var profile = new IfcArbitraryClosedProfileDef(points);
        var solid = new IfcExtrudedAreaSolid(profile, 1000.0);

        var mesh = solid.ToMesh();

        Assert.NotNull(mesh);
        Assert.Equal(10, mesh.Vertices.Count);
        // 5-gon: 3 triangles bottom, 3 triangles top, 10 side triangles = 16
        Assert.Equal(16, mesh.TriangleCount);
    }

    [Fact]
    public void SweptSolid_RoundtripsThroughStepSerialization()
    {
        var model = IfcModel.Create("SweptSolidRoundtrip");
        var wall = model.AddExtrudedRectangle(
            name: "ParametricWall",
            xDim: 300.0,
            yDim: 5000.0,
            depth: 2800.0,
            position: new IfcVector3(100, 200, 0));

        var col = model.AddExtrudedCircle(
            name: "ParametricColumn",
            radius: 250.0,
            depth: 3500.0,
            position: new IfcVector3(1000, 2000, 0));

        using var ms = new MemoryStream();
        model.Save(ms);
        ms.Position = 0;

        string stepText = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("IFCEXTRUDEDAREASOLID", stepText);
        Assert.Contains("IFCRECTANGLEPROFILEDEF", stepText);
        Assert.Contains("IFCCIRCLEPROFILEDEF", stepText);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var reopened = IfcModel.Open(reader, "Reopened");

        var reopenedWall = Assert.Single(reopened.AllElements, e => e.Name == "ParametricWall");
        Assert.NotNull(reopenedWall.Solid);
        Assert.IsType<IfcRectangleProfileDef>(reopenedWall.Solid.Profile);
        var rectProfile = (IfcRectangleProfileDef)reopenedWall.Solid.Profile;
        Assert.Equal(300.0, rectProfile.XDim);
        Assert.Equal(5000.0, rectProfile.YDim);
        Assert.Equal(2800.0, reopenedWall.Solid.Depth);
        Assert.NotNull(reopenedWall.Mesh);
        Assert.Equal(8, reopenedWall.Mesh.Vertices.Count);

        var reopenedCol = Assert.Single(reopened.AllElements, e => e.Name == "ParametricColumn");
        Assert.NotNull(reopenedCol.Solid);
        Assert.IsType<IfcCircleProfileDef>(reopenedCol.Solid.Profile);
        var circProfile = (IfcCircleProfileDef)reopenedCol.Solid.Profile;
        Assert.Equal(250.0, circProfile.Radius);
        Assert.Equal(3500.0, reopenedCol.Solid.Depth);
        Assert.NotNull(reopenedCol.Mesh);
    }

    [Fact]
    public void SweptSolid_ExportsToGlbAndObj()
    {
        var model = IfcModel.Create("ExportTest");
        model.AddExtrudedRectangle("SolidBox", 100.0, 200.0, 300.0);

        using var glbMs = new MemoryStream();
        model.SaveGlb(glbMs);
        Assert.True(glbMs.Length > 0);

        // Verify GLB magic header "glTF"
        var glbBytes = glbMs.ToArray();
        Assert.Equal(0x67, glbBytes[0]); // 'g'
        Assert.Equal(0x6C, glbBytes[1]); // 'l'
        Assert.Equal(0x54, glbBytes[2]); // 'T'
        Assert.Equal(0x46, glbBytes[3]); // 'F'

        using var objWriter = new StringWriter();
        using var mtlWriter = new StringWriter();
        IfcIO.Exporters.IfcObjExporter.Write(model, objWriter, mtlWriter);

        string objText = objWriter.ToString();
        Assert.Contains("v ", objText);
        Assert.Contains("f ", objText);
        Assert.Contains("o SolidBox_1", objText);
    }

    [Fact]
    public void SweptSolid_ValidationRulesCatchInvalidDimensions()
    {
        var model = IfcModel.Create("InvalidSolid");
        var invalidWall = model.AddExtrudedRectangle("BadWall", -100.0, 200.0, -50.0);

        var result = model.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_SOLID_DEPTH");
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_PROFILE_DIMENSIONS");
    }
}
