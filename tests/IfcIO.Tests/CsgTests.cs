using IfcIO.Core;
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

    [Theory]
    [InlineData(Schema.IfcSchema.Ifc2x3, 8, 11, 11, false)]
    [InlineData(Schema.IfcSchema.Ifc4, 10, 9, 10, false)]
    [InlineData(Schema.IfcSchema.Ifc2x3, 8, 11, 11, true)]
    [InlineData(Schema.IfcSchema.Ifc4, 10, 9, 10, true)]
    public void WriteProfiles_EmitSchemaSpecificAttributesAndPlacement(
        Schema.IfcSchema schema, int iCount, int lCount, int uCount, bool translated)
    {
        var model = IfcModel.Create("Structural profiles", schema);
        IfcVector3 position = translated ? new(5, -7, 0) : IfcVector3.Zero;
        var cut = new IfcPlane(new IfcVector3(0, 0, 500), IfcVector3.UnitZ);
        model.AddClippedSolid("Beam", new IfcExtrudedAreaSolid(new IfcIShapeProfileDef(200, 300, 10, 15,
            filletRadius: 0, position: position), 1000), cut);
        model.AddClippedSolid("Angle", new IfcExtrudedAreaSolid(new IfcLShapeProfileDef(150, 80, 8,
            filletRadius: 6, position: position), 1000), cut);
        model.AddClippedSolid("Channel", new IfcExtrudedAreaSolid(new IfcUShapeProfileDef(200, 80, 8, 12,
            filletRadius: 1e-7, position: position), 1000), cut);
        model.AddClippedSolid("Rectangle", new IfcExtrudedAreaSolid(new IfcRectangleProfileDef(100, 50, position),
            1000), cut);
        model.AddClippedSolid("Circle", new IfcExtrudedAreaSolid(new IfcCircleProfileDef(50, position),
            1000), cut);

        var step = StepReader.Read(new StringReader(model.ToStepString()));
        var iShape = Assert.Single(step.OfType("IFCISHAPEPROFILEDEF"));
        var lShape = Assert.Single(step.OfType("IFCLSHAPEPROFILEDEF"));
        var uShape = Assert.Single(step.OfType("IFCUSHAPEPROFILEDEF"));
        var rectangle = Assert.Single(step.OfType("IFCRECTANGLEPROFILEDEF"));
        var circle = Assert.Single(step.OfType("IFCCIRCLEPROFILEDEF"));
        Assert.Equal(iCount, iShape.Arguments.Count);
        Assert.Equal(lCount, lShape.Arguments.Count);
        Assert.Equal(uCount, uShape.Arguments.Count);
        Assert.Equal(schema == Schema.IfcSchema.Ifc4 ? 0.0 : null, iShape.GetReal(7));
        Assert.Equal(6.0, lShape.GetReal(6));
        Assert.Equal(1e-7, uShape.GetReal(7));
        Assert.Equal("1.0E-7", uShape.GetRaw(7));
        Assert.All(lShape.Arguments.Skip(7), arg => Assert.Equal("$", arg));
        Assert.All(uShape.Arguments.Skip(8), arg => Assert.Equal("$", arg));
        Assert.All(iShape.Arguments.Skip(8), arg => Assert.Equal("$", arg));

        foreach (var profile in new[] { iShape, lShape, uShape, rectangle, circle })
        {
            if (schema == Schema.IfcSchema.Ifc4 && !translated)
            {
                Assert.Null(profile.GetRef(2));
                continue;
            }
            var placement = step.Find(profile.GetRef(2));
            Assert.NotNull(placement);
            Assert.Equal("IFCAXIS2PLACEMENT2D", placement.Type);
            var point = step.Find(placement.GetRef(0));
            Assert.NotNull(point);
            Assert.Equal("IFCCARTESIANPOINT", point.Type);
            Assert.Equal(translated ? "(5.,-7.)" : "(0.,0.)", point.GetRaw(0));
        }
    }
}
