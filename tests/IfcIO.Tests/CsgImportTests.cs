using IfcIO.Core;
using IfcIO.Geometry;
using Xunit;

namespace IfcIO.Tests;

public class CsgImportTests
{
    private static string ClippedBeam(int firstOperand, string secondHalfSpace, string extra = "", bool ifc4 = false) => $$"""
        ISO-10303-21;
        HEADER;
        FILE_SCHEMA(('{{(ifc4 ? "IFC4" : "IFC2X3")}}'));
        ENDSEC;
        DATA;
        #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Nested clipping',$,$,$,$,(#30),#2);
        #2=IFCUNITASSIGNMENT((#3));
        #3=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
        #4=IFCCARTESIANPOINT((0.,0.,0.));
        #5=IFCCARTESIANPOINT((0.,0.));
        #6=IFCAXIS2PLACEMENT3D(#4,$,$);
        #7=IFCAXIS2PLACEMENT2D(#5,$);
        #8=IFCRECTANGLEPROFILEDEF(.AREA.,$,#7,20.,10.);
        #9=IFCDIRECTION((0.,0.,1.));
        #10=IFCEXTRUDEDAREASOLID(#8,#6,#9,100.);
        #11=IFCCARTESIANPOINT((0.,0.,25.));
        #12=IFCAXIS2PLACEMENT3D(#11,$,$);
        #13=IFCPLANE(#12);
        #14=IFCHALFSPACESOLID(#13,.T.);
        #15=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#10,#14);
        #16=IFCCARTESIANPOINT((0.,0.,75.));
        #17=IFCAXIS2PLACEMENT3D(#16,$,$);
        #18=IFCPLANE(#17);
        #19={{secondHalfSpace}};
        #20=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#{{firstOperand}},#19);
        #21=IFCSHAPEREPRESENTATION(#30,'Body','Clipping',(#20));
        #22=IFCPRODUCTDEFINITIONSHAPE($,$,(#21));
        #23=IFCBEAM('1YvctVUKr0kugbFTf53O9L',$,'Beam',$,$,$,#22,${{(ifc4 ? ",.NOTDEFINED." : "")}});
        #30=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#6,$);
        {{extra}}
        ENDSEC;
        END-ISO-10303-21;
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenAndSave_NestedClipping_AppliesBothPlanes(bool ifc4)
    {
        var model = IfcModel.Open(new StringReader(ClippedBeam(15, "IFCHALFSPACESOLID(#18,.F.)", ifc4: ifc4)));
        Assert.Empty(model.BooleanImportWarnings);
        var mesh = Assert.Single(model.AllElements).Mesh!;
        Assert.Equal(new IfcVector3(-10, -5, 25), mesh.BoundingBox.Min);
        Assert.Equal(new IfcVector3(10, 5, 75), mesh.BoundingBox.Max);

        string saved = model.ToStepString();
        Assert.Equal(2, StepReader.Read(new StringReader(saved)).OfType("IFCBOOLEANCLIPPINGRESULT").Count());
        var reopened = Assert.Single(IfcModel.Open(new StringReader(saved)).AllElements).Mesh!;
        Assert.Equal(mesh.BoundingBox, reopened.BoundingBox);
    }

    [Fact]
    public void Open_CyclicBooleanOperand_RejectsInvalidTree()
    {
        string ifc = ClippedBeam(20, "IFCHALFSPACESOLID(#18,.F.)");
        var error = Assert.Throws<InvalidDataException>(() => IfcModel.Open(new StringReader(ifc)));
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_ExcessiveBooleanDepth_RejectsDeepTree()
    {
        string chain = string.Join('\n', Enumerable.Range(100, 64).Select(id =>
            FormattableString.Invariant($"#{id}=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#{(id == 100 ? 10 : id - 1)},#14);")));
        var error = Assert.Throws<InvalidDataException>(() =>
            IfcModel.Open(new StringReader(ClippedBeam(163, "IFCHALFSPACESOLID(#18,.F.)", chain))));
        Assert.Contains("depth limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_SharedBooleanSubtrees_ChargeModelWideExpansion()
    {
        string tree = string.Join('\n', Enumerable.Range(100, 8).Select(id =>
            FormattableString.Invariant($"#{id}=IFCBOOLEANRESULT(.UNION.,#{id - 1},#{id - 1});")));
        string halfSpace = "IFCHALFSPACESOLID(#18,.F.)";
        Assert.Single(IfcModel.Open(new StringReader(ClippedBeam(107, halfSpace, tree))).AllElements);

        string ifc = ClippedBeam(107, halfSpace, tree + """

            #31=IFCBEAM('2YvctVUKr0kugbFTf53O9L',$,'Second beam',$,$,$,#22,$);
            #32=IFCBEAM('3YvctVUKr0kugbFTf53O9L',$,'Third beam',$,$,$,#22,$);
            """);
        var error = Assert.Throws<InvalidDataException>(() => IfcModel.Open(new StringReader(ifc)));
        Assert.Contains("model-wide expansion limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_NestedClipping_AppliesProductPlacementToBothPlanes()
    {
        string ifc = ClippedBeam(15, "IFCHALFSPACESOLID(#18,.F.)", """
            #24=IFCCARTESIANPOINT((100.,200.,300.));
            #25=IFCDIRECTION((1.,0.,0.));
            #26=IFCDIRECTION((0.,1.,0.));
            #27=IFCAXIS2PLACEMENT3D(#24,#25,#26);
            #28=IFCLOCALPLACEMENT($,#27);
            """).Replace("'Beam',$,$,$,#22", "'Beam',$,$,#28,#22", StringComparison.Ordinal);
        var model = IfcModel.Open(new StringReader(ifc));
        var mesh = Assert.Single(model.AllElements).Mesh!;
        Assert.Equal(new IfcVector3(125, 190, 295), mesh.BoundingBox.Min);
        Assert.Equal(new IfcVector3(175, 210, 305), mesh.BoundingBox.Max);
        var reopened = Assert.Single(IfcModel.Open(new StringReader(model.ToStepString())).AllElements).Mesh!;
        Assert.Equal(mesh.BoundingBox, reopened.BoundingBox);
    }

    [Fact]
    public void Open_BoundedHalfSpace_ReportsWhichOperandWasSkipped()
    {
        string ifc = ClippedBeam(15, "IFCPOLYGONALBOUNDEDHALFSPACE(#18,.F.,#17,#24)", """
            #24=IFCPOLYLINE((#5,#25,#26,#27,#5));
            #25=IFCCARTESIANPOINT((5.,0.));
            #26=IFCCARTESIANPOINT((5.,5.));
            #27=IFCCARTESIANPOINT((0.,5.));
            """);
        var model = IfcModel.Open(new StringReader(ifc));
        string warning = Assert.Single(model.BooleanImportWarnings);
        Assert.Contains("Boolean #20: skipped second operand (IFCPOLYGONALBOUNDEDHALFSPACE #19)", warning, StringComparison.Ordinal);
        Assert.Contains(model.Validate().Issues, i => i.RuleId == "IFC_BOOLEAN_IMPORT" && i.Message == warning);
        Assert.Throws<InvalidDataException>(() => model.ToStepString());
    }

    [Fact]
    public void Open_HalfSpaceWithMissingPlane_DoesNotInventACut()
    {
        var model = IfcModel.Open(new StringReader(ClippedBeam(10, "IFCHALFSPACESOLID(#999,.F.)")));
        Assert.Contains("skipped second operand", Assert.Single(model.BooleanImportWarnings), StringComparison.Ordinal);
        var mesh = Assert.Single(model.AllElements).Mesh!;
        Assert.Equal(100, mesh.BoundingBox.Max.Z);
    }

    [Fact]
    public void Open_NestedSecondOperand_PreservesTreeAndWarnsMeshIsIncomplete()
    {
        string ifc = ClippedBeam(10, "IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#10,#14)")
            .Replace("#20=IFCBOOLEANCLIPPINGRESULT", "#20=IFCBOOLEANRESULT", StringComparison.Ordinal)
            .Replace("'Clipping'", "'CSG'", StringComparison.Ordinal);

        var model = IfcModel.Open(new StringReader(ifc));
        Assert.Contains("not evaluated", Assert.Single(model.BooleanImportWarnings), StringComparison.Ordinal);
        var step = StepReader.Read(new StringReader(model.ToStepString()));
        var result = Assert.Single(step.OfType("IFCBOOLEANRESULT"));
        Assert.Equal(Assert.Single(step.OfType("IFCBOOLEANCLIPPINGRESULT")).Id, result.GetRef(2));
    }

    [Theory]
    [InlineData(".T.", 0, 75)]
    [InlineData(".F.", 75, 100)]
    public void Open_Intersection_KeepsHalfSpaceMaterial(string flag, int minZ, int maxZ)
    {
        string ifc = ClippedBeam(10, $"IFCHALFSPACESOLID(#18,{flag})", ifc4: true)
            .Replace("#20=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.", "#20=IFCBOOLEANRESULT(.INTERSECTION.", StringComparison.Ordinal)
            .Replace("'Clipping'", "'CSG'", StringComparison.Ordinal);

        var model = IfcModel.Open(new StringReader(ifc));
        var mesh = Assert.Single(model.AllElements).Mesh!;
        Assert.Equal(minZ, mesh.BoundingBox.Min.Z);
        Assert.Equal(maxZ, mesh.BoundingBox.Max.Z);
        Assert.Empty(model.BooleanImportWarnings);
    }

    [Fact]
    public void Open_UnionWithHalfSpace_WarnsWithoutReplacingSourceMeshWithAClip()
    {
        string ifc = ClippedBeam(10, "IFCHALFSPACESOLID(#18,.T.)", ifc4: true)
            .Replace("#20=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.", "#20=IFCBOOLEANRESULT(.UNION.", StringComparison.Ordinal)
            .Replace("'Clipping'", "'CSG'", StringComparison.Ordinal);
        var model = IfcModel.Open(new StringReader(ifc));
        var mesh = Assert.Single(model.AllElements).Mesh!;
        Assert.Equal(0, mesh.BoundingBox.Min.Z);
        Assert.Equal(100, mesh.BoundingBox.Max.Z);
        Assert.Contains("Union", Assert.Single(model.BooleanImportWarnings), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAndSave_SecondRevolution_PreservesParametricOperand()
    {
        string ifc = ClippedBeam(10, "IFCREVOLVEDAREASOLID(#24,#6,#27,1.57079632679)", """
            #24=IFCRECTANGLEPROFILEDEF(.AREA.,$,#25,2.,4.);
            #25=IFCAXIS2PLACEMENT2D(#26,$);
            #26=IFCCARTESIANPOINT((4.,6.));
            #27=IFCAXIS1PLACEMENT(#4,#28);
            #28=IFCDIRECTION((0.,1.,0.));
            """, ifc4: true)
            .Replace("#20=IFCBOOLEANCLIPPINGRESULT", "#20=IFCBOOLEANRESULT", StringComparison.Ordinal)
            .Replace("'Clipping'", "'CSG'", StringComparison.Ordinal);
        var model = IfcModel.Open(new StringReader(ifc));
        Assert.Contains("not evaluated", Assert.Single(model.BooleanImportWarnings), StringComparison.Ordinal);
        var result = Assert.Single(model.AllElements).BooleanResult!;
        Assert.NotNull(result.SecondRevolved);
        Assert.NotNull(result.SecondMesh);

        string saved = model.ToStepString();
        var step = StepReader.Read(new StringReader(saved));
        var operand = Assert.Single(step.OfType("IFCREVOLVEDAREASOLID"));
        Assert.Equal(operand.Id, Assert.Single(step.OfType("IFCBOOLEANRESULT")).GetRef(2));
        Assert.Single(IfcModel.Open(new StringReader(saved)).BooleanImportWarnings);
    }
}
