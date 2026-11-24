using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IfcIO.Core;
using IfcIO.Exporters;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

/// <summary>
/// World placement, mapped items and length units of geometry read from IFC, checked through
/// the model meshes and the GLB/OBJ exports.
/// </summary>
public class PlacementGeometryTests
{
    private const string Millimetre = "#11=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);";
    private const string Metre = "#11=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);";
    private const string Centimetre = "#11=IFCSIUNIT(*,.LENGTHUNIT.,.CENTI.,.METRE.);";
    private const string Foot = """
        #11=IFCCONVERSIONBASEDUNIT(#12,.LENGTHUNIT.,'FOOT',#13);
        #12=IFCDIMENSIONALEXPONENTS(1,0,0,0,0,0,0);
        #13=IFCMEASUREWITHUNIT(IFCLENGTHMEASURE(0.3048),#14);
        #14=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
        """;

    // Site at (1000,2000,0); building rotated 90 degrees about Z and moved (100,0,0); storey raised 3000.
    // Panel: triangle (0,0,0),(1,0,0),(0,1,0) at (10,20,0) in the storey (IfcFaceBasedSurfaceModel).
    // Pipe: 20x10 rectangle (profile rotated 90 degrees) extruded 1000 along the element X axis.
    // Fittings: one representation map (origin (0,0,100), triangle (0,0,0),(10,0,0),(0,10,0)) used three times;
    // Fitting C mirrors it in X (Axis1 = -X with Axis2 omitted); Fitting D has only Axis3 = +X, so X defaults to +Y. Beam: an extrusion along world X inside a clipping result.
    private static string BuildIfc(string lengthUnit, string wcsOrigin = "0.,0.,0.") => $"""
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0]'),'2;1');
        FILE_NAME('placement.ifc','2026-01-01T00:00:00',(''),(''),'','','');
        FILE_SCHEMA(('IFC2X3'));
        ENDSEC;
        DATA;
        #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Placement',$,$,$,$,(#20),#10);
        #10=IFCUNITASSIGNMENT((#11));
        {lengthUnit}
        #20=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#21,$);
        #21=IFCAXIS2PLACEMENT3D(#22,$,$);
        #22=IFCCARTESIANPOINT(({wcsOrigin}));
        #23=IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#20,$,.MODEL_VIEW.,$);
        #30=IFCDIRECTION((0.,0.,1.));
        #31=IFCDIRECTION((0.,1.,0.));
        #32=IFCDIRECTION((1.,0.,0.));
        #33=IFCDIRECTION((0.,1.));
        #34=IFCDIRECTION((-1.,0.,0.));
        #40=IFCSITE('2YvctVUKr0kugbFTf53O9L',$,'Site',$,$,#41,$,$,.ELEMENT.,$,$,$,$,$);
        #41=IFCLOCALPLACEMENT($,#42);
        #42=IFCAXIS2PLACEMENT3D(#43,$,$);
        #43=IFCCARTESIANPOINT((1000.,2000.,0.));
        #50=IFCBUILDING('3YvctVUKr0kugbFTf53O9L',$,'Building',$,$,#51,$,$,.ELEMENT.,$,$,$);
        #51=IFCLOCALPLACEMENT(#41,#52);
        #52=IFCAXIS2PLACEMENT3D(#53,#30,#31);
        #53=IFCCARTESIANPOINT((100.,0.,0.));
        #60=IFCBUILDINGSTOREY('4YvctVUKr0kugbFTf53O9L',$,'Level 1',$,$,#61,$,$,.ELEMENT.,3000.);
        #61=IFCLOCALPLACEMENT(#51,#62);
        #62=IFCAXIS2PLACEMENT3D(#63,$,$);
        #63=IFCCARTESIANPOINT((0.,0.,3000.));
        #100=IFCBUILDINGELEMENTPROXY('1YvctVUKr0kugbFTf53O9L',$,'Panel',$,$,#101,#103,$,$);
        #101=IFCLOCALPLACEMENT(#61,#102);
        #102=IFCAXIS2PLACEMENT3D(#104,$,$);
        #104=IFCCARTESIANPOINT((10.,20.,0.));
        #103=IFCPRODUCTDEFINITIONSHAPE($,$,(#105));
        #105=IFCSHAPEREPRESENTATION(#23,'Body','SurfaceModel',(#106));
        #106=IFCFACEBASEDSURFACEMODEL((#107));
        #107=IFCCONNECTEDFACESET((#108));
        #108=IFCFACE((#109));
        #109=IFCFACEOUTERBOUND(#110,.T.);
        #110=IFCPOLYLOOP((#111,#112,#113));
        #111=IFCCARTESIANPOINT((0.,0.,0.));
        #112=IFCCARTESIANPOINT((1.,0.,0.));
        #113=IFCCARTESIANPOINT((0.,1.,0.));
        #200=IFCFLOWSEGMENT('5YvctVUKr0kugbFTf53O9L',$,'Pipe',$,$,#201,#203,$);
        #201=IFCLOCALPLACEMENT(#61,#202);
        #202=IFCAXIS2PLACEMENT3D(#204,$,$);
        #204=IFCCARTESIANPOINT((0.,0.,0.));
        #203=IFCPRODUCTDEFINITIONSHAPE($,$,(#205));
        #205=IFCSHAPEREPRESENTATION(#23,'Body','SweptSolid',(#206));
        #206=IFCEXTRUDEDAREASOLID(#207,#209,#30,1000.);
        #207=IFCRECTANGLEPROFILEDEF(.AREA.,$,#208,20.,10.);
        #208=IFCAXIS2PLACEMENT2D(#210,#33);
        #210=IFCCARTESIANPOINT((0.,0.));
        #209=IFCAXIS2PLACEMENT3D(#204,#32,#31);
        #300=IFCREPRESENTATIONMAP(#301,#303);
        #301=IFCAXIS2PLACEMENT3D(#302,$,$);
        #302=IFCCARTESIANPOINT((0.,0.,100.));
        #303=IFCSHAPEREPRESENTATION(#23,'Body','Brep',(#304));
        #304=IFCFACETEDBREP(#305);
        #305=IFCCLOSEDSHELL((#306));
        #306=IFCFACE((#307));
        #307=IFCFACEOUTERBOUND(#308,.T.);
        #308=IFCPOLYLOOP((#111,#309,#310));
        #309=IFCCARTESIANPOINT((10.,0.,0.));
        #310=IFCCARTESIANPOINT((0.,10.,0.));
        #320=IFCFLOWFITTING('6YvctVUKr0kugbFTf53O9L',$,'Fitting A',$,$,#321,#323,$);
        #321=IFCLOCALPLACEMENT($,#322);
        #322=IFCAXIS2PLACEMENT3D(#204,$,$);
        #323=IFCPRODUCTDEFINITIONSHAPE($,$,(#324));
        #324=IFCSHAPEREPRESENTATION(#23,'Body','MappedRepresentation',(#325));
        #325=IFCMAPPEDITEM(#300,#326);
        #326=IFCCARTESIANTRANSFORMATIONOPERATOR3D(#31,#34,#327,2.,$);
        #327=IFCCARTESIANPOINT((500.,0.,0.));
        #330=IFCFLOWFITTING('7YvctVUKr0kugbFTf53O9L',$,'Fitting B',$,$,#331,#333,$);
        #331=IFCLOCALPLACEMENT($,#332);
        #332=IFCAXIS2PLACEMENT3D(#334,$,$);
        #334=IFCCARTESIANPOINT((0.,0.,50.));
        #333=IFCPRODUCTDEFINITIONSHAPE($,$,(#335));
        #335=IFCSHAPEREPRESENTATION(#23,'Body','MappedRepresentation',(#336));
        #336=IFCMAPPEDITEM(#300,#337);
        #337=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#204,$,$);
        #340=IFCFLOWFITTING('BYvctVUKr0kugbFTf53O9L',$,'Fitting C',$,$,#321,#343,$);
        #343=IFCPRODUCTDEFINITIONSHAPE($,$,(#345));
        #345=IFCSHAPEREPRESENTATION(#23,'Body','MappedRepresentation',(#346));
        #346=IFCMAPPEDITEM(#300,#347);
        #347=IFCCARTESIANTRANSFORMATIONOPERATOR3D(#34,$,#204,$,$);
        #350=IFCFLOWFITTING('DYvctVUKr0kugbFTf53O9L',$,'Fitting D',$,$,#321,#353,$);
        #353=IFCPRODUCTDEFINITIONSHAPE($,$,(#355));
        #355=IFCSHAPEREPRESENTATION(#23,'Body','MappedRepresentation',(#356));
        #356=IFCMAPPEDITEM(#300,#357);
        #357=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#204,$,#32);
        #600=IFCBEAM('CYvctVUKr0kugbFTf53O9L',$,'Beam',$,$,#321,#603,$);
        #603=IFCPRODUCTDEFINITIONSHAPE($,$,(#605));
        #605=IFCSHAPEREPRESENTATION(#23,'Body','Clipping',(#606));
        #606=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#607,#610);
        #607=IFCEXTRUDEDAREASOLID(#608,#209,#30,1000.);
        #608=IFCRECTANGLEPROFILEDEF(.AREA.,$,#609,20.,10.);
        #609=IFCAXIS2PLACEMENT2D(#210,$);
        #610=IFCHALFSPACESOLID(#611,.F.);
        #611=IFCPLANE(#612);
        #612=IFCAXIS2PLACEMENT3D(#613,$,$);
        #613=IFCCARTESIANPOINT((0.,0.,5000.));
        #400=IFCDISTRIBUTIONPORT('8YvctVUKr0kugbFTf53O9L',$,'Pipe end',$,$,#401,$,.SOURCE.);
        #401=IFCLOCALPLACEMENT(#201,#402);
        #402=IFCAXIS2PLACEMENT3D(#403,#32,$);
        #403=IFCCARTESIANPOINT((1000.,0.,0.));
        #410=IFCRELCONNECTSPORTTOELEMENT('9YvctVUKr0kugbFTf53O9L',$,$,$,#400,#200);
        #500=IFCRELCONTAINEDINSPATIALSTRUCTURE('AYvctVUKr0kugbFTf53O9L',$,$,$,(#100,#200,#320,#330,#340,#350,#600),#60);
        ENDSEC;
        END-ISO-10303-21;
        """;

    private static readonly IfcVector3[] PanelWorld = [new(1080, 2010, 3000), new(1080, 2011, 3000), new(1079, 2010, 3000)];
    private static readonly IfcVector3[] FittingAWorld = [new(500, 0, 200), new(500, 20, 200), new(480, 0, 200)];
    private static readonly IfcVector3[] FittingBWorld = [new(0, 0, 150), new(10, 0, 150), new(0, 10, 150)];

    private static IfcModel Open(string ifc)
    {
        using var reader = new StringReader(ifc);
        return IfcModel.Open(reader);
    }

    private static IfcElement Element(IfcModel model, string name) => Assert.Single(model.AllElements, e => e.Name == name);

    private static void AssertPoints(IfcVector3[] expected, IReadOnlyList<IfcVector3> actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(
                (expected[i] - actual[i]).Length <= tolerance,
                $"Vertex {i}: expected {expected[i]}, actual {actual[i]}");
        }
    }

    [Fact]
    public void Open_NestedLocalPlacements_PutsVerticesInWorldCoordinates()
    {
        var model = Open(BuildIfc(Millimetre));

        var panel = Element(model, "Panel");
        Assert.NotNull(panel.Mesh);
        AssertPoints(PanelWorld, panel.Mesh.Vertices);
    }

    [Fact]
    public void Open_RotatedExtrusionAndProfile_PlacesPipeInWorldCoordinates()
    {
        var model = Open(BuildIfc(Millimetre));

        var pipe = Element(model, "Pipe");
        Assert.Equal("IFCFLOWSEGMENT", pipe.IfcType);
        Assert.NotNull(pipe.Mesh);
        Assert.Null(pipe.Solid);
        var box = pipe.Mesh.BoundingBox;
        AssertPoints([new(1095, 2000, 2990), new(1105, 3000, 3010)], [box.Min, box.Max]);
    }

    [Fact]
    public void Open_ScaledDirectionRatios_PreservePlacement()
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("#33=IFCDIRECTION((0.,1.));", "#33=IFCDIRECTION((0.,1.E-200));", StringComparison.Ordinal)
            .Replace("#31=IFCDIRECTION((0.,1.,0.));", "#31=IFCDIRECTION((0.,1.E200,0.));", StringComparison.Ordinal);
        var mesh = Element(Open(ifc), "Pipe").Mesh!;
        AssertPoints([new(1095, 2000, 2990), new(1105, 3000, 3010)], [mesh.BoundingBox.Min, mesh.BoundingBox.Max]);
    }

    [Theory]
    [InlineData("4.", 30, 38)]
    [InlineData("-4.", 22, 30)]
    public void Open_InclinedExtrusion_KeepsProfileInPlacementPlane(string zDirection, double minZ, double maxZ)
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("#201=IFCLOCALPLACEMENT(#61,#202);", "#201=IFCLOCALPLACEMENT($,#202);", StringComparison.Ordinal)
            .Replace("#206=IFCEXTRUDEDAREASOLID(#207,#209,#30,1000.);",
                $"#206=IFCEXTRUDEDAREASOLID(#207,#209,#211,10.); #211=IFCDIRECTION((0.,3.,{zDirection}));", StringComparison.Ordinal)
            .Replace("#209=IFCAXIS2PLACEMENT3D(#204,#32,#31);",
                "#209=IFCAXIS2PLACEMENT3D(#212,$,$); #212=IFCCARTESIANPOINT((10.,20.,30.));", StringComparison.Ordinal)
            .Replace("#208=IFCAXIS2PLACEMENT2D(#210,#33);", "#208=IFCAXIS2PLACEMENT2D(#210,$);", StringComparison.Ordinal);

        var mesh = Element(Open(ifc), "Pipe").Mesh!;
        AssertPoints([new(0, 15, minZ), new(20, 31, maxZ)], [mesh.BoundingBox.Min, mesh.BoundingBox.Max]);
        Assert.All(mesh.Vertices.Take(4), point => Assert.Equal(30, point.Z, 9));
        var cap = mesh.Indices.Skip(24).Take(3).Select(i => mesh.Vertices[i]).ToArray();
        Assert.True(IfcVector3.Dot(IfcVector3.Cross(cap[1] - cap[0], cap[2] - cap[0]),
            mesh.Vertices[4] - mesh.Vertices[0]) < 0);
    }

    [Theory]
    [InlineData(false, false, 13, 24, 11, 24, 28)]
    [InlineData(true, false, 6, 23, 6, 21, 28)]
    [InlineData(false, true, 16, 25, 11, 25, 25)]
    [InlineData(true, true, 5, 26, 5, 21, 25)]
    public void Open_Revolution_AppliesProfileAndSolidPlacementsBeforeObjectPlacement(
        bool rotateSolid, bool rotateProfile, double startX, double startY, double endX, double endY, double endZ)
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("#206=IFCEXTRUDEDAREASOLID(#207,#209,#30,1000.);",
                "#206=IFCREVOLVEDAREASOLID(#207,#214,#211,1.5707963267948966); #211=IFCAXIS1PLACEMENT(#213,#31);", StringComparison.Ordinal)
            .Replace("#207=IFCRECTANGLEPROFILEDEF(.AREA.,$,#208,20.,10.);", "#207=IFCRECTANGLEPROFILEDEF(.AREA.,$,#208,2.,4.);", StringComparison.Ordinal)
            .Replace("#208=IFCAXIS2PLACEMENT2D(#210,#33);",
                $"#208=IFCAXIS2PLACEMENT2D(#212,{(rotateProfile ? "#33" : "$")}); #212=IFCCARTESIANPOINT((4.,6.));", StringComparison.Ordinal)
            .Replace("#209=IFCAXIS2PLACEMENT3D(#204,#32,#31);",
                $"#209=IFCAXIS2PLACEMENT3D(#204,#32,#31); #213=IFCCARTESIANPOINT((1.,0.,0.)); #214=IFCAXIS2PLACEMENT3D(#215,$,{(rotateSolid ? "#31" : "$")}); #215=IFCCARTESIANPOINT((10.,20.,30.));",
                StringComparison.Ordinal);

        // The last ring is 90 degrees about the solid-local Y axis through (1,0,0).
        // The element's parent chain then takes (x,y,z) to (1100-y,2000+x,3000+z).
        var mesh = Element(Open(ifc), "Pipe").Mesh!;
        AssertPoints([new(1100 - startY, 2000 + startX, 3030), new(1100 - endY, 2000 + endX, 3000 + endZ)],
            [mesh.Vertices[0], mesh.Vertices[96]]);
        var cap = mesh.Indices.Skip(24 * 4 * 6).Take(3).Select(i => mesh.Vertices[i]).ToArray();
        Assert.True(IfcVector3.Cross(cap[1] - cap[0], cap[2] - cap[0]).Z > 0);
    }

    [Theory]
    [InlineData(IfcSchema.Ifc2x3)]
    [InlineData(IfcSchema.Ifc4)]
    public void SaveAndOpen_PlacedExtrusion_PreservesWorldGeometry(IfcSchema schema)
    {
        string ifc = BuildIfc(Millimetre);
        if (schema == IfcSchema.Ifc4)
            ifc = ifc.Replace("FILE_SCHEMA(('IFC2X3'));", "FILE_SCHEMA(('IFC4'));", StringComparison.Ordinal);
        var model = Open(ifc);

        var mesh = Element(Open(model.ToStepString()), "Pipe").Mesh!;
        Assert.Equal(12, mesh.Indices.Count / 3);
        AssertPoints([new(1095, 2000, 2990), new(1105, 3000, 3010)], [mesh.BoundingBox.Min, mesh.BoundingBox.Max]);
    }

    [Fact]
    public void SaveAndOpen_MultipleSweptItems_PreservesBothMeshes()
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("#201=IFCLOCALPLACEMENT(#61,#202);", "#201=IFCLOCALPLACEMENT($,#202);", StringComparison.Ordinal)
            .Replace("'SweptSolid',(#206));", "'SweptSolid',(#206,#211));", StringComparison.Ordinal)
            .Replace("#206=IFCEXTRUDEDAREASOLID(#207,#209,#30,1000.);",
                """
                #206=IFCEXTRUDEDAREASOLID(#207,#202,#30,1000.);
                #211=IFCEXTRUDEDAREASOLID(#207,#212,#30,1000.);
                #212=IFCAXIS2PLACEMENT3D(#213,$,$);
                #213=IFCCARTESIANPOINT((100.,0.,0.));
                """, StringComparison.Ordinal)
            .Replace("#208=IFCAXIS2PLACEMENT2D(#210,#33);", "#208=IFCAXIS2PLACEMENT2D(#210,$);", StringComparison.Ordinal);
        var model = Open(ifc);

        string saved = model.ToStepString();
        var step = StepReader.Read(new StringReader(saved));
        var rep = Assert.Single(step.OfType("IFCSHAPEREPRESENTATION"),
            e => e.GetString(2) == "Brep" && e.GetRefList(3).Count == 2);
        Assert.All(rep.GetRefList(3), id =>
        {
            var brep = step.Find(id)!;
            Assert.Equal("IFCFACETEDBREP", brep.Type);
            Assert.Equal(12, step.Find(brep.GetRef(0))!.GetRefList(0).Count);
        });
        var mesh = Element(Open(saved), "Pipe").Mesh!;
        Assert.Equal(24, mesh.TriangleCount);
        AssertPoints([new(-10, -5, 0), new(110, 5, 1000)], [mesh.BoundingBox.Min, mesh.BoundingBox.Max]);
    }

    [Fact]
    public void SaveAndOpen_TranslatedExtrusion_KeepsWorldSpaceParametricSolid()
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("#201=IFCLOCALPLACEMENT(#61,#202);", "#201=IFCLOCALPLACEMENT(#41,#202);", StringComparison.Ordinal)
            .Replace("IFCEXTRUDEDAREASOLID(#207,#209,#30,1000.)", "IFCEXTRUDEDAREASOLID(#207,#202,#30,1000.)", StringComparison.Ordinal)
            .Replace("#208=IFCAXIS2PLACEMENT2D(#210,#33);", "#208=IFCAXIS2PLACEMENT2D(#210,$);", StringComparison.Ordinal);
        var model = Open(ifc);

        var pipe = Element(model, "Pipe");
        Assert.Equal(new IfcVector3(1000, 2000, 0), pipe.Solid!.Position);
        AssertPoints(pipe.Mesh!.Vertices.ToArray(), Element(Open(model.ToStepString()), "Pipe").Mesh!.Vertices);
    }

    [Fact]
    public void SaveAndOpen_TranslatedClipping_KeepsSweptOperandAndPlane()
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("#321=IFCLOCALPLACEMENT($,#322);", "#321=IFCLOCALPLACEMENT(#41,#322);", StringComparison.Ordinal)
            .Replace("#607=IFCEXTRUDEDAREASOLID(#608,#209,#30,1000.);",
                "#607=IFCEXTRUDEDAREASOLID(#608,#202,#30,1000.);", StringComparison.Ordinal);

        var model = Open(ifc);
        var csg = Element(model, "Beam").BooleanResult!;
        Assert.Equal(new(1000, 2000, 0), csg.FirstSolid!.Position);
        Assert.Equal(new(1000, 2000, 5000), csg.SecondHalfSpace!.BaseSurface.Location);

        var reopened = Element(Open(model.ToStepString()), "Beam").BooleanResult!;
        Assert.True(reopened.IsClippingResult);
        Assert.Equal(csg.FirstSolid.Position, reopened.FirstSolid!.Position);
        Assert.Equal(csg.SecondHalfSpace.BaseSurface.Location, reopened.SecondHalfSpace!.BaseSurface.Location);
    }

    [Fact]
    public void Save_MultipleBooleanItems_RejectsLosingOperands()
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("'Clipping',(#606));", "'CSG',(#606,#614));", StringComparison.Ordinal)
            .Replace("#606=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#607,#610);",
                "#606=IFCBOOLEANRESULT(.DIFFERENCE.,#607,#206); #614=IFCBOOLEANRESULT(.UNION.,#206,#607);", StringComparison.Ordinal);
        var model = Open(ifc);
        Assert.True(Element(model, "Beam").Mesh!.TriangleCount > 0);
        var error = Assert.Throws<InvalidDataException>(() => model.ToStepString());
        Assert.Contains("losing operands", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_NestedBooleanWithMissingOperand_DoesNotWriteRefZero()
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("'Clipping',(#606));", "'Clipping',(#614));", StringComparison.Ordinal)
            .Replace("#606=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#607,#610);",
                "#606=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#999,#610); #614=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#606,#610);",
                StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => Open(ifc).ToStepString());
        Assert.Contains("without both operands", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".UNION.")]
    [InlineData(".DIFFERENCE.")]
    public void Open_BooleanWithRotatedSecondExtrusion_PlacesOperand(string operation)
    {
        string ifc = BuildIfc(Millimetre).Replace("#606=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#607,#610);",
            $"#606=IFCBOOLEANRESULT({operation},#607,#206);", StringComparison.Ordinal);
        var model = Open(ifc);
        var boolean = Element(model, "Beam").BooleanResult!;

        Assert.Null(boolean.SecondSolid);
        Assert.NotNull(boolean.SecondMesh);
        AssertPoints([new(0, -5, -10), new(1000, 5, 10)], [boolean.SecondMesh.BoundingBox.Min, boolean.SecondMesh.BoundingBox.Max]);

        string saved = model.ToStepString();
        var reopened = Element(Open(saved), "Beam").BooleanResult!;
        Assert.NotNull(reopened.SecondMesh);
        AssertPoints([new(0, -5, -10), new(1000, 5, 10)], [reopened.SecondMesh.BoundingBox.Min, reopened.SecondMesh.BoundingBox.Max]);
        Assert.Contains($"=IFCBOOLEANRESULT({operation},", saved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1e-8, 0)]
    public void SaveAndOpen_AuthoredExtrusion_WritesItsProfileFrame(double x, double y, double z)
    {
        var model = IfcModel.Create("Authored extrusion");
        var element = model.AddExtrudedRectangle("Extrusion", 20, 10, 30, new(10, 20, 30), new(x, y, z));
        string ifc = model.ToStepString();
        var step = StepReader.Read(new StringReader(ifc));
        var extrusion = Assert.Single(step.OfType("IFCEXTRUDEDAREASOLID"));
        var placement = step.Find(extrusion.GetRef(1))!;
        Assert.NotNull(placement.GetRef(1));
        if (y == 1e-8)
            Assert.Equal("(1.,1.E-08,0.)", step.Find(placement.GetRef(1))!.GetRaw(0));
        Assert.Equal("(0.,0.,1.)", step.Find(extrusion.GetRef(2))!.GetRaw(0));
        var reopened = Element(Open(ifc), "Extrusion");

        Assert.NotNull(reopened.Solid);
        AssertPoints(element.GetEffectiveMesh()!.Vertices.ToArray(), reopened.Mesh!.Vertices, 1e-9);
    }

    [Fact]
    public void Open_ClippingResult_AppliesRotatedOperandPosition()
    {
        string ifc = BuildIfc(Millimetre).Replace("#600=IFCBEAM('CYvctVUKr0kugbFTf53O9L',$,'Beam',$,$,#321",
            "#600=IFCBEAM('CYvctVUKr0kugbFTf53O9L',$,'Beam',$,$,#201", StringComparison.Ordinal)
            .Replace("#613=IFCCARTESIANPOINT((0.,0.,5000.));", "#613=IFCCARTESIANPOINT((500.,0.,0.));", StringComparison.Ordinal)
            .Replace("#612=IFCAXIS2PLACEMENT3D(#613,$,$);", "#612=IFCAXIS2PLACEMENT3D(#613,#32,$);", StringComparison.Ordinal);
        var model = Open(ifc);

        var beam = Element(model, "Beam");
        Assert.NotNull(beam.Mesh);
        var box = beam.Mesh.BoundingBox;
        AssertPoints([new(1090, 2000, 2995), new(1110, 2500, 3005)], [box.Min, box.Max]);
        Assert.NotNull(beam.BooleanResult);
        Assert.NotNull(beam.BooleanResult.FirstMesh);
        Assert.Null(beam.BooleanResult.FirstSolid);

        var plane = beam.BooleanResult.SecondHalfSpace!.BaseSurface;
        AssertPoints([new(1100, 2500, 3000), IfcVector3.UnitY], [plane.Location, plane.Normal]);
        string saved = model.ToStepString();
        var step = StepReader.Read(new StringReader(saved));
        var planePlacement = step.Find(Assert.Single(step.OfType("IFCPLANE")).GetRef(0))!;
        Assert.NotNull(planePlacement.GetRef(1));
        Assert.NotNull(planePlacement.GetRef(2));
        var reopened = Element(Open(saved), "Beam");
        var reopenedBox = reopened.Mesh!.BoundingBox;
        AssertPoints([box.Min, box.Max], [reopenedBox.Min, reopenedBox.Max]);
    }

    [Theory]
    [InlineData("$", "(3,1,2)", 2, 0, 1)]
    [InlineData("((0.,0.,1.))", "((1,1,1))", 0, 1, 2)]
    public void Open_TriangulatedFaceSet_ResolvesOneBasedPointNumbersAndSkipsInvalidFaces(
        string normals, string indexAttribute, int first, int second, int third)
    {
        string ifc = BuildIfc(Millimetre)
            .Replace("FILE_SCHEMA(('IFC2X3'));", "FILE_SCHEMA(('IFC4'));", StringComparison.Ordinal)
            .Replace("'SurfaceModel',(#106)", "'Tessellation',(#106)", StringComparison.Ordinal)
            .Replace("#106=IFCFACEBASEDSURFACEMODEL((#107));",
                $"""
                #106=IFCTRIANGULATEDFACESET(#114,{normals},.F.,((1,2,3), (1,0,2), (3,2,5)),{indexAttribute});
                #114=IFCCARTESIANPOINTLIST3D(((0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(1000000.,0.,0.)));
                """, StringComparison.Ordinal);

        var mesh = Element(Open(ifc), "Panel").Mesh!;
        AssertPoints(PanelWorld, mesh.Vertices);
        Assert.Equal([first, second, third], mesh.Indices);
    }

    [Fact]
    public void Open_MappedItem_AppliesMappingOriginAndTarget()
    {
        var model = Open(BuildIfc(Millimetre));

        var fittingA = Element(model, "Fitting A");
        var fittingB = Element(model, "Fitting B");
        Assert.NotNull(fittingA.Mesh);
        Assert.NotNull(fittingB.Mesh);
        AssertPoints(FittingAWorld, fittingA.Mesh.Vertices);
        AssertPoints(FittingBWorld, fittingB.Mesh.Vertices);
    }

    [Fact]
    public void Open_MappedItemWithAxis3AlongX_DefaultsXAxisToY()
    {
        var model = Open(BuildIfc(Millimetre));

        // IfcFirstProjAxis: with Z = (1,0,0) the defaulted X axis is (0,1,0), so Y = Z x X = (0,0,1).
        var fitting = Element(model, "Fitting D");
        Assert.NotNull(fitting.Mesh);
        AssertPoints([new(100, 0, 0), new(100, 10, 0), new(100, 0, 10)], fitting.Mesh.Vertices);
        Assert.Equal([0, 1, 2], fitting.Mesh.Indices);
    }

    [Fact]
    public void Open_UnitWithoutEnumValue_DropsParametricSolidsAndSavesRoundTrip()
    {
        var model = Open(BuildIfc(Centimetre));
        var pipe = Element(model, "Pipe");
        Assert.Null(pipe.Solid);
        var box = pipe.Mesh!.BoundingBox;

        var reopened = Open(model.ToStepString());

        Assert.Equal(IfcLengthUnit.Metre, reopened.LengthUnit);
        var reopenedBox = Element(reopened, "Pipe").Mesh!.BoundingBox;
        AssertPoints([box.Min, box.Max], [reopenedBox.Min, reopenedBox.Max], 1e-6);
        AssertPoints([new(10.95, 20, 29.9), new(11.05, 30, 30.1)], [box.Min, box.Max], 1e-9);
    }

    [Fact]
    public void Open_MappedItemWithoutAxis2_MirrorsAndKeepsFacesOutward()
    {
        var model = Open(BuildIfc(Millimetre));

        var fitting = Element(model, "Fitting C");
        Assert.NotNull(fitting.Mesh);
        AssertPoints([new(0, 0, 100), new(-10, 0, 100), new(0, 10, 100)], fitting.Mesh.Vertices);
        Assert.Equal([0, 2, 1], fitting.Mesh.Indices);
    }

    [Fact]
    public void Open_WorldCoordinateSystem_OffsetsAbsolutePlacements()
    {
        var model = Open(BuildIfc(Millimetre, wcsOrigin: "0.,0.,-50."));

        var down = new IfcVector3(0, 0, -50);
        AssertPoints(PanelWorld.Select(p => p + down).ToArray(), Element(model, "Panel").Mesh!.Vertices);
        AssertPoints(FittingBWorld.Select(p => p + down).ToArray(), Element(model, "Fitting B").Mesh!.Vertices);
    }

    [Fact]
    public void Open_PlacementCycle_ThrowsInvalidData()
    {
        string cyclic = BuildIfc(Millimetre).Replace("#61=IFCLOCALPLACEMENT(#51,#62);", "#61=IFCLOCALPLACEMENT(#101,#62);", StringComparison.Ordinal);

        var ex = Assert.Throws<InvalidDataException>(() => Open(cyclic));
        Assert.Contains("PlacementRelTo cycle", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_NestedMappedItemAmplification_ThrowsInsteadOfExhaustingMemory()
    {
        // 8 levels of representation maps, each holding 10 instances of the previous one: 3 * 10^8 vertices if expanded.
        var data = new StringBuilder("""
            #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Bomb',$,$,$,$,$,#10);
            #10=IFCUNITASSIGNMENT((#11));
            #11=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
            #20=IFCAXIS2PLACEMENT3D(#21,$,$);
            #21=IFCCARTESIANPOINT((0.,0.,0.));
            #22=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#21,$,$);
            #30=IFCCARTESIANPOINT((1.,0.,0.));
            #31=IFCCARTESIANPOINT((0.,1.,0.));
            #32=IFCPOLYLOOP((#21,#30,#31));
            #33=IFCFACEOUTERBOUND(#32,.T.);
            #34=IFCFACE((#33));
            #35=IFCCLOSEDSHELL((#34));
            #36=IFCFACETEDBREP(#35);
            #100=IFCSHAPEREPRESENTATION($,'Body','Brep',(#36));
            #101=IFCREPRESENTATIONMAP(#20,#100);

            """);
        for (int level = 1; level <= 8; level++)
        {
            int rep = 100 + (level * 100);
            var items = Enumerable.Range(1, 10).Select(i => $"#{rep + 1 + i}").ToList();
            foreach (var item in items)
            {
                data.AppendLine(CultureInfo.InvariantCulture, $"{item}=IFCMAPPEDITEM(#{rep - 99},#22);");
            }
            data.AppendLine(CultureInfo.InvariantCulture, $"#{rep}=IFCSHAPEREPRESENTATION($,'Body','MappedRepresentation',({string.Join(",", items)}));");
            data.AppendLine(CultureInfo.InvariantCulture, $"#{rep + 1}=IFCREPRESENTATIONMAP(#20,#{rep});");
        }
        data.AppendLine("#2000=IFCMAPPEDITEM(#901,#22);");
        data.AppendLine("#2001=IFCSHAPEREPRESENTATION($,'Body','MappedRepresentation',(#2000));");
        data.AppendLine("#2002=IFCPRODUCTDEFINITIONSHAPE($,$,(#2001));");
        data.AppendLine("#2003=IFCBUILDINGELEMENTPROXY('1YvctVUKr0kugbFTf53O9L',$,'Bomb',$,$,$,#2002,$,$);");
        string ifc = $"""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION((''),'2;1');
            FILE_NAME('bomb.ifc','2026-01-01T00:00:00',(''),(''),'','','');
            FILE_SCHEMA(('IFC2X3'));
            ENDSEC;
            DATA;
            {data}ENDSEC;
            END-ISO-10303-21;
            """;

        var ex = Assert.Throws<InvalidDataException>(() => Open(ifc));
        Assert.Contains("vertices", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_DistributionPort_UsesWorldPlacement()
    {
        var model = Open(BuildIfc(Millimetre));

        var port = Assert.Single(Element(model, "Pipe").Ports);
        AssertPoints([new(1100, 3000, 3000), IfcVector3.UnitY], [port.Position, port.Direction]);
    }

    [Theory]
    [InlineData(Millimetre, IfcLengthUnit.Millimetre, 1.0)]
    [InlineData(Metre, IfcLengthUnit.Metre, 1.0)]
    [InlineData(Foot, IfcLengthUnit.Foot, 1.0)]
    [InlineData(Centimetre, IfcLengthUnit.Metre, 0.01)]
    public void Open_LengthUnit_IsReadFromUnitAssignment(string lengthUnit, IfcLengthUnit expectedUnit, double coordinateScale)
    {
        var model = Open(BuildIfc(lengthUnit));

        Assert.Equal(expectedUnit, model.LengthUnit);
        AssertPoints(PanelWorld.Select(p => p * coordinateScale).ToArray(), Element(model, "Panel").Mesh!.Vertices);
        Assert.Equal(3000 * coordinateScale, Assert.Single(model.Storeys).Elevation, 9);
    }

    [Theory]
    [InlineData(Millimetre, 0.001)]
    [InlineData(Metre, 1.0)]
    [InlineData(Foot, 0.3048)]
    public void SaveGlb_WritesWorldVerticesInMetresYUp(string lengthUnit, double metresPerUnit)
    {
        var model = Open(BuildIfc(lengthUnit));
        using var ms = new MemoryStream();
        model.SaveGlb(ms);

        var glb = GlbReader.Parse(ms.ToArray());

        // Only the root node moves (to the model centre); everything below it is untransformed.
        var root = glb.Nodes.Last();
        Assert.All(glb.Nodes, n => Assert.False(
            n.TryGetProperty("matrix", out _) || n.TryGetProperty("rotation", out _) || n.TryGetProperty("scale", out _) ||
            (n.TryGetProperty("translation", out _) && !n.Equals(root))));
        var t = root.GetProperty("translation");
        var offset = new IfcVector3(t[0].GetDouble(), t[1].GetDouble(), t[2].GetDouble());

        // glTF is Y-up: IFC (x, y, z) becomes (x, z, -y).
        static IfcVector3 ToGltf(IfcVector3 p, double s) => new(p.X * s, p.Z * s, -p.Y * s);
        AssertPoints(
            PanelWorld.Select(p => ToGltf(p, metresPerUnit)).ToArray(),
            glb.Positions("Panel [IFCBUILDINGELEMENTPROXY]").Select(p => p + offset).ToArray(),
            1e-4);
        AssertPoints(
            FittingAWorld.Select(p => ToGltf(p, metresPerUnit)).ToArray(),
            glb.Positions("Fitting A [IFCFLOWFITTING]").Select(p => p + offset).ToArray(),
            1e-4);
    }

    [Fact]
    public void ObjExport_WritesWorldVerticesInModelUnits()
    {
        var model = Open(BuildIfc(Millimetre));
        var panel = Element(model, "Panel");
        using var obj = new StringWriter();
        using var mtl = new StringWriter();
        IfcObjExporter.Write(model, obj, mtl);

        var lines = obj.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int start = lines.IndexOf($"o Panel_{panel.Id}");
        Assert.True(start >= 0);
        var vertices = lines.Skip(start + 1)
            .SkipWhile(l => !l.StartsWith("v ", StringComparison.Ordinal))
            .TakeWhile(l => l.StartsWith("v ", StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .Select(p => new IfcVector3(
                double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture),
                double.Parse(p[3], CultureInfo.InvariantCulture)))
            .ToList();
        AssertPoints(PanelWorld, vertices, 1e-4);
    }

    private sealed class GlbReader
    {
        private readonly JsonElement _root;
        private readonly byte[] _bin;

        private GlbReader(JsonElement root, byte[] bin)
        {
            _root = root;
            _bin = bin;
        }

        public IEnumerable<JsonElement> Nodes => _root.GetProperty("nodes").EnumerateArray();

        public static GlbReader Parse(byte[] bytes)
        {
            Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 20, jsonLength));
            int binStart = 20 + jsonLength;
            int binLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binStart));
            return new GlbReader(doc.RootElement.Clone(), bytes.AsSpan(binStart + 8, binLength).ToArray());
        }

        public IfcVector3[] Positions(string nodeName)
        {
            var node = Assert.Single(Nodes, n => n.GetProperty("name").GetString() == nodeName);
            var mesh = _root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()];
            int accessorIndex = mesh.GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var accessor = _root.GetProperty("accessors")[accessorIndex];
            var view = _root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int offset = view.GetProperty("byteOffset").GetInt32() + accessor.GetProperty("byteOffset").GetInt32();

            var result = new IfcVector3[accessor.GetProperty("count").GetInt32()];
            for (int i = 0; i < result.Length; i++)
            {
                var span = _bin.AsSpan(offset + (i * 12));
                result[i] = new IfcVector3(
                    BinaryPrimitives.ReadSingleLittleEndian(span),
                    BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[8..]));
            }
            return result;
        }
    }
}
