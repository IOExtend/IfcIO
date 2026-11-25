using IfcIO.Geometry;
using Xunit;

namespace IfcIO.Tests;

public class SweptDiskImportTests
{
    private static IfcModel Open(
        string schema, string directrix, string points, string parameters, string inner = "$", bool useFallback = false, bool mapped = false)
    {
        string ifc = $$"""
            ISO-10303-21;
            HEADER;
            FILE_SCHEMA(('{{schema}}'));
            ENDSEC;
            DATA;
            #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Swept disks',$,$,$,$,(#7),#2);
            #2=IFCUNITASSIGNMENT((#3));
            #3=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
            #4=IFCCARTESIANPOINT((100.,200.,300.));
            #5=IFCAXIS2PLACEMENT3D(#4,$,$);
            #6=IFCLOCALPLACEMENT($,#5);
            #7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#16,$);
            #8=IFCSWEPTDISKSOLID(#20,2.,{{inner}},{{parameters}});
            #9=IFCSHAPEREPRESENTATION(#7,'Body','{{(mapped ? "MappedRepresentation" : "AdvancedSweptSolid")}}',
                ({{(mapped ? "#37" : useFallback ? "#8,#30" : "#8")}}));
            #10=IFCPRODUCTDEFINITIONSHAPE($,$,(#9{{(useFallback ? ",#31" : "")}}));
            #11=IFCBUILDINGELEMENTPROXY('1YvctVUKr0kugbFTf53O9L',$,'Disk',$,$,#6,#10,$,$);
            #15=IFCCARTESIANPOINT((0.,0.,0.));
            #16=IFCAXIS2PLACEMENT3D(#15,$,$);
            #20={{directrix}};
            {{points}}
            ENDSEC;
            END-ISO-10303-21;
            """;
        return IfcModel.Open(new StringReader(ifc));
    }

    private static IfcMesh? Mesh(IfcModel model) => Assert.Single(model.AllElements).Mesh;

    [Fact]
    public void Open_TrimmedIfc2x3Polyline_AppliesBothParametersInTheirSegmentDomain()
    {
        var model = Open("IFC2X3", "IFCPOLYLINE((#21,#22,#23))", """
            #21=IFCCARTESIANPOINT((0.,0.,0.));
            #22=IFCCARTESIANPOINT((0.,0.,10.));
            #23=IFCCARTESIANPOINT((0.,0.,30.));
            """, "0.5,1.25");
        var mesh = Mesh(model);
        Assert.NotNull(mesh);
        AssertBox(mesh, new(98, 198, 305), new(102, 202, 315));
        AssertClosed(mesh);
        AssertSameBoundsAfterExport(model, mesh);
    }

    [Fact]
    public void Open_IndexedPolyline_CreatesMiterAndHollowCaps()
    {
        var model = Open("IFC4", "IFCINDEXEDPOLYCURVE(#21,(IFCLINEINDEX((2,3)),IFCLINEINDEX((3,1))),.F.)",
            "#21=IFCCARTESIANPOINTLIST3D(((20.,0.,10.),(0.,0.,0.),(0.,0.,10.)));", "$,$", inner: "1.");
        var mesh = Mesh(model);
        Assert.NotNull(mesh);
        AssertBox(mesh, new(98, 198, 300), new(120, 202, 312));
        AssertClosed(mesh);

        // At the bend the section follows the half-angle plane x + z = 410.
        var bend = mesh.Vertices.Where(p => p.X < 110 && p.Z > 302).ToArray();
        Assert.NotEmpty(bend);
        Assert.All(bend, point => Assert.Equal(410, point.X + point.Z, 9));
        Assert.DoesNotContain(mesh.Vertices, p => p == new IfcVector3(100, 200, 300));
        AssertSameBoundsAfterExport(model, mesh);
    }

    [Theory]
    [InlineData("-1.,1.", "$")]
    [InlineData("0.,2.", "$")]
    [InlineData("0.,1.", "3.")]
    [InlineData("*,1.", "$")]
    [InlineData("0.,1.", "*")]
    public void Open_InvalidParameters_DoesNotInventGeometry(string parameters, string inner)
    {
        var mesh = Mesh(Open("IFC4", "IFCPOLYLINE((#21,#22))",
            "#21=IFCCARTESIANPOINT((0.,0.,0.)); #22=IFCCARTESIANPOINT((0.,0.,10.));", parameters, inner));
        Assert.Null(mesh);
    }

    [Fact]
    public void Open_Ifc2x3RequiresTrimParameters()
    {
        Assert.Null(Mesh(Open("IFC2X3", "IFCPOLYLINE((#21,#22))",
            "#21=IFCCARTESIANPOINT((0.,0.,0.)); #22=IFCCARTESIANPOINT((0.,0.,10.));", "$,$")));
    }

    [Theory]
    [InlineData("IFC2X3", ".F.")]
    [InlineData("IFC4", ".U.")]
    public void Open_RejectsIndexedCurvesOutsideTheirSchemaOrWithInvalidFlag(string schema, string flag)
    {
        Assert.Null(Mesh(Open(schema, $"IFCINDEXEDPOLYCURVE(#21,$,{flag})",
            "#21=IFCCARTESIANPOINTLIST3D(((0.,0.,0.),(0.,0.,10.)));", "0.,1.")));
    }

    [Fact]
    public void Open_IndexedCurveWithAnArc_DoesNotTreatTheArcAsALine()
    {
        var mesh = Mesh(Open("IFC4", "IFCINDEXEDPOLYCURVE(#21,(IFCARCINDEX((1,2,3))),.F.)",
            "#21=IFCCARTESIANPOINTLIST3D(((0.,0.,0.),(0.,10.,10.),(0.,20.,0.)));", "$,$"));
        Assert.Null(mesh);
    }

    [Fact]
    public void Open_RedundantCollinearVertex_DoesNotBreakMiterAfterTrimming()
    {
        var mesh = Mesh(Open("IFC4", "IFCPOLYLINE((#21,#22,#23,#24))", """
            #21=IFCCARTESIANPOINT((0.,0.,0.));
            #22=IFCCARTESIANPOINT((9.,0.,0.));
            #23=IFCCARTESIANPOINT((10.,0.,0.));
            #24=IFCCARTESIANPOINT((10.,10.,0.));
            """, "0.5,3."));
        Assert.NotNull(mesh);
        AssertBox(mesh, new(104.5, 198, 298), new(112, 210, 302));
        AssertClosed(mesh);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_PartialSweptDiskBody_PrefersCompleteBodyFallback(bool mapped)
    {
        var model = Open("IFC4", "IFCPOLYLINE((#21,#22))", """
            #21=IFCCARTESIANPOINT((0.,0.,0.));
            #22=IFCCARTESIANPOINT((0.,0.,10.));
            #30=IFCSWEPTDISKSOLID(#32,2.,$,$,$);
            #31=IFCSHAPEREPRESENTATION(#7,'Body-FallBack','Tessellation',(#34));
            #32=IFCINDEXEDPOLYCURVE(#33,(IFCARCINDEX((1,2,3))),.F.);
            #33=IFCCARTESIANPOINTLIST3D(((0.,0.,0.),(0.,10.,10.),(0.,20.,0.)));
            #34=IFCTRIANGULATEDFACESET(#33,$,.F.,((1,2,3)),$);
            #35=IFCSHAPEREPRESENTATION(#7,'Body','AdvancedSweptSolid',(#8,#30));
            #36=IFCREPRESENTATIONMAP(#16,#35);
            #37=IFCMAPPEDITEM(#36,#38);
            #38=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#15,$,$);
            """, "$,$", useFallback: true, mapped: mapped);
        var mesh = Mesh(model);
        Assert.NotNull(mesh);
        Assert.Equal(1, mesh.TriangleCount);
    }

    private static void AssertSameBoundsAfterExport(IfcModel model, IfcMesh before)
    {
        var after = Mesh(IfcModel.Open(new StringReader(model.ToStepString())));
        Assert.NotNull(after);
        Assert.Equal(before.TriangleCount, after.TriangleCount);
        AssertBox(after, before.BoundingBox.Min, before.BoundingBox.Max);
        AssertClosed(after);
    }

    private static void AssertBox(IfcMesh mesh, IfcVector3 min, IfcVector3 max)
    {
        Assert.True((mesh.BoundingBox.Min - min).Length < 1e-9, $"Min: {mesh.BoundingBox.Min}, expected {min}");
        Assert.True((mesh.BoundingBox.Max - max).Length < 1e-9, $"Max: {mesh.BoundingBox.Max}, expected {max}");
    }

    private static void AssertClosed(IfcMesh mesh)
    {
        var edges = new Dictionary<(int, int), (int Count, int Balance)>();
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Indices[i];
            var b = mesh.Indices[i + 1];
            var c = mesh.Indices[i + 2];
            Add(a, b);
            Add(b, c);
            Add(c, a);
        }
        Assert.All(edges.Values, edge => Assert.Equal((2, 0), edge));

        void Add(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            var (count, balance) = edges.GetValueOrDefault(key);
            edges[key] = (count + 1, balance + (a < b ? 1 : -1));
        }
    }
}
