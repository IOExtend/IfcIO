using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class CsgTests
{
    [Fact]
    public void Plane_SignedDistance_ComputesCorrectly()
    {
        var plane = new IfcPlane(new IfcVector3(0, 0, 10), IfcVector3.UnitZ);
        double distAbove = plane.SignedDistanceTo(new IfcVector3(0, 0, 15));
        double distBelow = plane.SignedDistanceTo(new IfcVector3(0, 0, 5));
        double distOn = plane.SignedDistanceTo(new IfcVector3(10, 20, 10));

        Assert.Equal(5.0, distAbove, 4);
        Assert.Equal(-5.0, distBelow, 4);
        Assert.Equal(0.0, distOn, 4);
    }

    [Fact]
    public void HalfSpaceSolid_ContainsPoint_RespectsAgreementFlag()
    {
        var plane = new IfcPlane(new IfcVector3(0, 0, 10), IfcVector3.UnitZ);
        var hsPositive = new IfcHalfSpaceSolid(plane, agreementFlag: true);
        var hsNegative = new IfcHalfSpaceSolid(plane, agreementFlag: false);

        Assert.True(hsPositive.ContainsPoint(new IfcVector3(0, 0, 15)));
        Assert.False(hsPositive.ContainsPoint(new IfcVector3(0, 0, 5)));

        Assert.False(hsNegative.ContainsPoint(new IfcVector3(0, 0, 15)));
        Assert.True(hsNegative.ContainsPoint(new IfcVector3(0, 0, 5)));
    }

    [Fact]
    public void BooleanClipping_ClipsExtrudedSolidWithPlane()
    {
        var profile = new IfcRectangleProfileDef(10, 10);
        var solid = new IfcExtrudedAreaSolid(profile, 10, position: new IfcVector3(0, 0, 0), direction: IfcVector3.UnitZ);

        // Plane cuts horizontally at Z = 5 pointing Up
        var plane = new IfcPlane(new IfcVector3(0, 0, 5), IfcVector3.UnitZ);
        var halfSpace = new IfcHalfSpaceSolid(plane, agreementFlag: true);

        var csg = new IfcBooleanResult(solid, halfSpace, IfcBooleanOperator.Difference);
        var mesh = csg.ToMesh("ClippedBox");

        Assert.NotNull(mesh);
        Assert.True(mesh.TriangleCount > 0);
        // All vertices must be at or above Z=5 (retained half-space)
        foreach (var v in mesh.Vertices)
        {
            Assert.True(v.Z >= 5.0 - 1e-5, $"Vertex Z was {v.Z}, expected >= 5.0");
        }
    }

    [Fact]
    public void StructuralSteelProfiles_ComputeBoundaryPoints()
    {
        var iBeam = new IfcIShapeProfileDef(overallWidth: 200, overallDepth: 300, webThickness: 10, flangeThickness: 15, name: "IPE300");
        var iPts = iBeam.GetBoundaryPoints();
        Assert.Equal(12, iPts.Count);

        var lAngle = new IfcLShapeProfileDef(depth: 100, width: 75, thickness: 10, name: "L100x75x10");
        var lPts = lAngle.GetBoundaryPoints();
        Assert.Equal(6, lPts.Count);

        var uChannel = new IfcUShapeProfileDef(depth: 200, flangeWidth: 80, webThickness: 8, flangeThickness: 12, name: "UNP200");
        var uPts = uChannel.GetBoundaryPoints();
        Assert.Equal(8, uPts.Count);
    }

    [Fact]
    public void ClippedSolid_SaveAndLoad_RoundtripsSuccessfully()
    {
        var model = IfcModel.Create("CSG Clipping Model", Schema.IfcSchema.Ifc2x3);
        var profile = new IfcIShapeProfileDef(200, 300, 10, 15, name: "HEB200");
        var solid = new IfcExtrudedAreaSolid(profile, 1000, IfcVector3.Zero, IfcVector3.UnitZ);
        var plane = new IfcPlane(new IfcVector3(0, 0, 500), IfcVector3.UnitZ);

        var elem = model.AddClippedSolid("RafterBeam", solid, plane, agreementFlag: true);
        Assert.NotNull(elem.BooleanResult);

        string stepStr = model.ToStepString();
        Assert.Contains("IFCBOOLEANCLIPPINGRESULT", stepStr);
        Assert.Contains("IFCHALFSPACESOLID", stepStr);
        Assert.Contains("IFCPLANE", stepStr);
        Assert.Contains("IFCISHAPEPROFILEDEF", stepStr);

        using var reader = new StringReader(stepStr);
        var loaded = IfcModel.Open(reader);

        var loadedElem = loaded.AllElements.FirstOrDefault(e => e.Name == "RafterBeam");
        Assert.NotNull(loadedElem);
        Assert.NotNull(loadedElem.BooleanResult);
        Assert.NotNull(loadedElem.BooleanResult.FirstSolid);
        Assert.IsType<IfcIShapeProfileDef>(loadedElem.BooleanResult.FirstSolid.Profile);
        Assert.NotNull(loadedElem.BooleanResult.SecondHalfSpace);
    }
}
