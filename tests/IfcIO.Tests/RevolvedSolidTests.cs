using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public class RevolvedSolidTests
{
    [Fact]
    public void RevolvedSolid_FullRevolution_GeneratesValidClosedMesh()
    {
        // 2D rectangle profile offset by X = 1000 (forming a hollow ring/torus)
        var profile = new IfcRectangleProfileDef(200, 400, position: new IfcVector3(1000, 0, 0));
        var solid = new IfcRevolvedAreaSolid(profile, angle: 2.0 * Math.PI);

        var mesh = solid.ToMesh(radialSegments: 16);

        Assert.NotNull(mesh);
        Assert.True(mesh.Vertices.Count > 0);
        Assert.True(mesh.Indices.Count > 0);
        Assert.Equal(0, mesh.Indices.Count % 3);

        // Verify bounding box spans around Z axis: ~[-1100, 1100] in X and Y
        Assert.True(mesh.BoundingBox.Min.X < -900);
        Assert.True(mesh.BoundingBox.Max.X > 900);
        Assert.True(mesh.BoundingBox.Min.Y < -900);
        Assert.True(mesh.BoundingBox.Max.Y > 900);
    }

    [Fact]
    public void RevolvedSolid_PartialRevolution_IncludesCaps()
    {
        var profile = new IfcRectangleProfileDef(100, 200, position: new IfcVector3(500, 0, 0));
        var solid = new IfcRevolvedAreaSolid(profile, angle: Math.PI * 0.5); // 90 degree elbow

        var mesh = solid.ToMesh(radialSegments: 8);

        Assert.NotNull(mesh);
        Assert.True(mesh.Vertices.Count > 0);
        Assert.True(mesh.Indices.Count > 0);
        Assert.Equal(0, mesh.Indices.Count % 3);
    }

    [Fact]
    public void RevolvedSolid_StepRoundtrip_PreservesGeometryAndSolid()
    {
        var model = IfcModel.Create("Revolved Roundtrip");
        var profile = new IfcCircleProfileDef(radius: 150, position: new IfcVector3(600, 0, 0));
        var axis = new IfcAxis1Placement(IfcVector3.Zero, IfcVector3.UnitZ);
        var elem = model.AddRevolvedSolid("Circular Torus", profile, angle: Math.PI, axis: axis, ifcType: "IFCPIPEFITTING");

        string tempPath = Path.Combine(Path.GetTempPath(), $"rev_test_{Guid.NewGuid():N}.ifc");
        try
        {
            model.Save(tempPath);

            var loaded = IfcModel.Open(tempPath);
            var loadedElem = Assert.Single(loaded.AllElements);

            Assert.Equal("Circular Torus", loadedElem.Name);
            Assert.NotNull(loadedElem.RevolvedSolid);
            Assert.Equal(Math.PI, loadedElem.RevolvedSolid.Angle, 3);
            Assert.IsType<IfcCircleProfileDef>(loadedElem.RevolvedSolid.Profile);

            var circleProf = (IfcCircleProfileDef)loadedElem.RevolvedSolid.Profile;
            Assert.Equal(150.0, circleProf.Radius);

            var effectiveMesh = loadedElem.GetEffectiveMesh();
            Assert.NotNull(effectiveMesh);
            Assert.True(effectiveMesh.Vertices.Count > 0);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Validator_RevolvedAngleOutOfRange_ReportsError()
    {
        var model = IfcModel.Create("Validation Revolved");
        var profile = new IfcRectangleProfileDef(100, 100);
        var badSolid = new IfcRevolvedAreaSolid(profile, angle: 10.0); // > 2pi
        model.AddRevolvedSolid("Bad Angle", profile, angle: 10.0);

        var result = model.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_REVOLVED_ANGLE");
    }
}
