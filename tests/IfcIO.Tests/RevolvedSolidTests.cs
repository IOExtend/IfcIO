using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
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

    [Theory]
    [InlineData(IfcSchema.Ifc2x3, IfcLengthUnit.Foot, 0.3048, IfcPlaneAngleUnit.Degree)]
    [InlineData(IfcSchema.Ifc2x3, IfcLengthUnit.Inch, 0.0254, IfcPlaneAngleUnit.Radian)]
    [InlineData(IfcSchema.Ifc4, IfcLengthUnit.Foot, 0.3048, IfcPlaneAngleUnit.Radian)]
    [InlineData(IfcSchema.Ifc4, IfcLengthUnit.Inch, 0.0254, IfcPlaneAngleUnit.Degree)]
    public void SaveAndOpen_PreservesImperialLengthsAndUsesTheDeclaredAngleUnit(
        IfcSchema schema, IfcLengthUnit length, double metresPerUnit, IfcPlaneAngleUnit angle)
    {
        var model = IfcModel.Create("Imperial", schema);
        model.LengthUnit = length;
        model.AngleUnit = angle;
        var storey = model.AddStorey("Raised floor", 12);
        var profile = new IfcCircleProfileDef(radius: 2, position: new IfcVector3(12, 0, 0));
        var element = model.AddRevolvedSolid("Bend", profile, angle: Math.PI / 2,
            axis: new IfcAxis1Placement(IfcVector3.Zero, IfcVector3.UnitY),
            storey: storey, ifcType: "IFCPIPEFITTING");

        string text = model.ToStepString();
        var step = StepReader.Read(new StringReader(text));
        var assignment = Assert.Single(step.OfType("IFCUNITASSIGNMENT"));
        var lengthUnit = Assert.Single(assignment.GetRefList(0).Select(id => step.Find(id)!),
            unit => unit.GetRaw(1) == ".LENGTHUNIT.");
        Assert.Equal("IFCCONVERSIONBASEDUNIT", lengthUnit.Type);
        Assert.Equal(length == IfcLengthUnit.Foot ? "FOOT" : "INCH", lengthUnit.GetString(2));
        var factor = step.Find(lengthUnit.GetRef(3))!;
        Assert.Equal(FormattableString.Invariant($"IFCLENGTHMEASURE({metresPerUnit})"), factor.GetRaw(0));
        Assert.Equal(".METRE.", step.Find(factor.GetRef(1))!.GetRaw(3));

        var angleUnit = Assert.Single(assignment.GetRefList(0).Select(id => step.Find(id)!),
            unit => unit.GetRaw(1) == ".PLANEANGLEUNIT.");
        if (angle == IfcPlaneAngleUnit.Degree)
        {
            Assert.Equal("IFCCONVERSIONBASEDUNIT", angleUnit.Type);
            Assert.Equal("DEGREE", angleUnit.GetString(2));
        }
        else
        {
            Assert.Equal("IFCSIUNIT", angleUnit.Type);
            Assert.Equal(".RADIAN.", angleUnit.GetRaw(3));
        }
        var revolution = Assert.Single(step.OfType("IFCREVOLVEDAREASOLID"));
        Assert.Equal(angle == IfcPlaneAngleUnit.Degree ? 90.0 : Math.PI / 2, revolution.GetReal(3)!.Value, 5);

        var opened = IfcModel.Open(new StringReader(text));
        Assert.Equal(length, opened.LengthUnit);
        Assert.Equal(angle, opened.AngleUnit);
        Assert.Equal(12.0, Assert.Single(opened.Storeys).Elevation);
        var reopened = Assert.Single(opened.AllElements);
        Assert.Equal(Math.PI / 2, reopened.RevolvedSolid!.Angle, 5);
        Assert.Equal(2.0, Assert.IsType<IfcCircleProfileDef>(reopened.RevolvedSolid.Profile).Radius);
        var originalBox = element.GetEffectiveMesh()!.BoundingBox;
        var reopenedBox = reopened.GetEffectiveMesh()!.BoundingBox;
        Assert.Equal(originalBox.Min.X, reopenedBox.Min.X, 4);
        Assert.Equal(originalBox.Max.Z, reopenedBox.Max.Z, 4);
    }

    [Fact]
    public void LegacyMislabeledAngle_RequiresExplicitOptInAndSavesWithCorrectUnits()
    {
        const string legacy = """
            ISO-10303-21;
            HEADER; FILE_SCHEMA(('IFC4')); ENDSEC;
            DATA;
            #1=IFCPROJECT('01vctVUKr0kugbFTf53O9L',$,'Legacy',$,$,$,$,(),#2);
            #2=IFCUNITASSIGNMENT((#3,#5));
            #3=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
            #4=IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.);
            #5=IFCCONVERSIONBASEDUNIT(#6,.PLANEANGLEUNIT.,'DEGREE',#7);
            #6=IFCDIMENSIONALEXPONENTS(0,0,0,0,0,0,0);
            #7=IFCMEASUREWITHUNIT(IFCPLANEANGLEMEASURE(0.017453293),#4);
            #8=IFCCARTESIANPOINT((0.,0.,0.));
            #9=IFCDIRECTION((0.,1.,0.));
            #10=IFCAXIS1PLACEMENT(#8,#9);
            #11=IFCRECTANGLEPROFILEDEF(.AREA.,'Profile',$,2.,3.);
            #12=IFCREVOLVEDAREASOLID(#11,$,#10,1.570796);
            #13=IFCSHAPEREPRESENTATION($,'Body','SweptSolid',(#12));
            #14=IFCPRODUCTDEFINITIONSHAPE($,$,(#13));
            #15=IFCBUILDINGELEMENTPROXY('02vctVUKr0kugbFTf53O9L',$,'Sweep',$,$,$,#14,$,.NOTDEFINED.);
            ENDSEC;
            END-ISO-10303-21;
            """;

        var standardRead = IfcModel.Open(new StringReader(legacy));
        Assert.Equal(IfcPlaneAngleUnit.Degree, standardRead.AngleUnit);
        Assert.Equal(1.570796 * 0.017453293, Assert.Single(standardRead.AllElements).RevolvedSolid!.Angle, 12);

        var repaired = IfcModel.Open(new StringReader(legacy), modelName: null,
            options: new IfcReadOptions { LegacyIfcIoRevolvedAnglesInRadians = true });
        Assert.Equal(1.570796, Assert.Single(repaired.AllElements).RevolvedSolid!.Angle);

        var reopened = IfcModel.Open(new StringReader(repaired.ToStepString()));
        Assert.Equal(IfcPlaneAngleUnit.Degree, reopened.AngleUnit);
        Assert.Equal(1.570796, Assert.Single(reopened.AllElements).RevolvedSolid!.Angle, 7);
    }

    [Fact]
    public void Save_PreservesSmallPositiveRevolutionAngles()
    {
        var model = IfcModel.Create("Small angle", IfcSchema.Ifc4);
        model.AngleUnit = IfcPlaneAngleUnit.Radian;
        model.AddRevolvedSolid("Short sweep", new IfcCircleProfileDef(2), angle: 1e-8);

        var step = StepReader.Read(new StringReader(model.ToStepString()));

        Assert.Equal("1.0E-8", Assert.Single(step.OfType("IFCREVOLVEDAREASOLID")).GetRaw(3));
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
