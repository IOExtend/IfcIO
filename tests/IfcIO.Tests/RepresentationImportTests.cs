using IfcIO.Validation;
using Xunit;

namespace IfcIO.Tests;

public class RepresentationImportTests
{
    private static IfcModel Read(string bodyItems, string additional = "", string representations = "#10", string? sourceName = null) =>
        IfcModel.Open(new StringReader($$"""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('Synthetic geometry diagnostics'),'2;1');
            FILE_NAME('body.ifc','2026-11-25T00:00:00',(''),(''),'','','');
            FILE_SCHEMA(('IFC4'));
            ENDSEC;
            DATA;
            #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Geometry diagnostics',$,$,$,$,(#7),#2);
            #2=IFCUNITASSIGNMENT((#3));
            #3=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
            #4=IFCCARTESIANPOINT((0.,0.,0.));
            #5=IFCAXIS2PLACEMENT3D(#4,$,$);
            #7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#5,$);
            #9=IFCCARTESIANPOINTLIST3D(((0.,0.,0.),(10.,0.,0.),(0.,10.,0.)));
            #10=IFCSHAPEREPRESENTATION(#7,'Body',$,({{bodyItems}}));
            #11=IFCPRODUCTDEFINITIONSHAPE($,$,({{representations}}));
            #12=IFCWALL('1YvctVUKr0kugbFTf53O9L',$,'Wall',$,$,$,#11,$,$);
            #13=IFCTRIANGULATEDFACESET(#9,$,.F.,((1,2,3)),$);
            #14=IFCBOUNDINGBOX(#4,10.,10.,10.);
            {{additional}}
            ENDSEC;
            END-ISO-10303-21;
            """), sourceName);

    [Fact]
    public void Open_PartiallyReadBody_IdentifiesMissingAndUnsupportedItems()
    {
        var model = Read("#13,#14,#999");
        Assert.Equal(1, Assert.Single(model.AllElements).Mesh!.TriangleCount);
        Assert.Collection(model.RepresentationImportWarnings,
            first =>
            {
                Assert.Equal(10, first.RepresentationId);
                Assert.Equal(14, first.ItemId);
                Assert.Equal("IFCBOUNDINGBOX", first.ItemType);
                Assert.Equal("body.ifc", first.SourceName);
            },
            second =>
            {
                Assert.Equal(10, second.RepresentationId);
                Assert.Equal(999, second.ItemId);
                Assert.Null(second.ItemType);
            });
        var issues = model.Validate().Issues.Where(issue => issue.RuleId == "IFC_GEOMETRY_IMPORT");
        Assert.Equal(2, issues.Count());
        Assert.All(issues, issue => Assert.Equal(IfcIssueSeverity.Warning, issue.Severity));
    }

    [Fact]
    public void Open_InstancedPartialBody_ReportsSourceItemOnce()
    {
        var model = Read("#23,#24", """
            #20=IFCSHAPEREPRESENTATION(#7,'Body',$,(#13,#14));
            #21=IFCREPRESENTATIONMAP(#5,#20);
            #22=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#4,$,$);
            #23=IFCMAPPEDITEM(#21,#22);
            #24=IFCMAPPEDITEM(#21,#22);
            """);
        Assert.Equal(2, Assert.Single(model.AllElements).Mesh!.TriangleCount);
        var skipped = Assert.Single(model.RepresentationImportWarnings);
        Assert.Equal(20, skipped.RepresentationId);
        Assert.Equal(14, skipped.ItemId);
    }

    [Fact]
    public void Open_AlternativeRepresentationNotAttempted_IsNotReported()
    {
        var model = Read("#13", "#20=IFCSHAPEREPRESENTATION(#7,'Box','BoundingBox',(#14));", "#10,#20");
        Assert.Empty(model.RepresentationImportWarnings);
    }

    [Fact]
    public void Open_ExplicitFallbackPreservesGeometryAndWarningAboutPrimaryItem()
    {
        var model = Read("#14", "#20=IFCSHAPEREPRESENTATION(#7,'Body-FallBack','Tessellation',(#13));", "#10,#20");
        Assert.Equal(1, Assert.Single(model.AllElements).Mesh!.TriangleCount);
        Assert.Equal(new IfcRepresentationImportWarning(10, 14, "IFCBOUNDINGBOX") { SourceName = "body.ifc" },
            Assert.Single(model.RepresentationImportWarnings));
    }

    [Fact]
    public void Open_MissingRepresentation_IsReportedWithItsStepId()
    {
        var model = Read("#13", representations: "#999,#10");
        Assert.NotNull(Assert.Single(model.AllElements).Mesh);
        Assert.Equal(new IfcRepresentationImportWarning(999, null, null) { SourceName = "body.ifc" },
            Assert.Single(model.RepresentationImportWarnings));
    }

    [Fact]
    public void Open_BooleanWithoutReadableBase_ReportsTheItemInAPartialBody()
    {
        var model = Read("#13,#20", """
            #20=IFCBOOLEANRESULT(.DIFFERENCE.,#21,#13);
            #21=IFCBLOCK(#5,1.,1.,1.);
            """);
        Assert.Equal(1, Assert.Single(model.AllElements).Mesh!.TriangleCount);
        Assert.Equal(new IfcRepresentationImportWarning(10, 20, "IFCBOOLEANRESULT") { SourceName = "body.ifc" },
            Assert.Single(model.RepresentationImportWarnings));
    }

    [Fact]
    public void Merge_RetainsSourceLabelsWhenStepIdsAreReused()
    {
        var a = Read("#14", sourceName: "A.ifc");
        var b = Read("#14", sourceName: "B.ifc");
        var merged = IfcIO.Patch.IfcPatch.Merge([a, b]);
        Assert.Equal(["A.ifc", "B.ifc"], merged.RepresentationImportWarnings.Select(warning => warning.SourceName));
    }
}
