using IfcIO.Core;
using IfcIO.Geometry;
using Xunit;

namespace IfcIO.Tests;

public class ProductOccurrenceTests
{
    private static string BuildIfc(string schema, string products, string representation = "Body") => $$"""
        ISO-10303-21;
        HEADER;
        FILE_SCHEMA(('{{schema}}'));
        ENDSEC;
        DATA;
        #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Occurrences',$,$,$,$,(#7),#2);
        #2=IFCUNITASSIGNMENT((#3));
        #3=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
        #4=IFCCARTESIANPOINT((0.,0.,0.));
        #5=IFCAXIS2PLACEMENT3D(#4,$,$);
        #6=IFCLOCALPLACEMENT($,#5);
        #7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#5,$);
        #8=IFCCARTESIANPOINT((0.,0.));
        #9=IFCAXIS2PLACEMENT2D(#8,$);
        #10=IFCRECTANGLEPROFILEDEF(.AREA.,$,#9,20.,10.);
        #11=IFCDIRECTION((0.,0.,1.));
        #12=IFCEXTRUDEDAREASOLID(#10,#5,#11,100.);
        #13=IFCSHAPEREPRESENTATION(#7,'{{representation}}','SweptSolid',(#12));
        #14=IFCPRODUCTDEFINITIONSHAPE($,$,(#13));
        {{products}}
        ENDSEC;
        END-ISO-10303-21;
        """;

    [Theory]
    [InlineData("IFC2X3", "IFCCURTAINWALL", "")]
    [InlineData("IFC2X3", "IFCPILE", ",.NOTDEFINED.,$")]
    [InlineData("IFC4", "IFCFURNITURE", ",$")]
    [InlineData("IFC4", "IFCACTUATOR", ",$")]
    [InlineData("IFC4", "IFCRAMP", ",$")]
    [InlineData("IFC4X3_ADD2", "IFCCOURSE", ",$")]
    public void Open_ConcreteProductWithBody_KeepsTypeTagAndMesh(string schema, string type, string tail)
    {
        string ifc = BuildIfc(schema, $"#20={type}('1YvctVUKr0kugbFTf53O9L',$,'Product',$,$,#6,#14,'P-01'{tail});");
        var model = IfcModel.Open(new StringReader(ifc));
        var product = Assert.Single(model.AllElements);
        Assert.Equal(type, product.IfcType);
        Assert.Equal("P-01", product.Tag);
        Assert.Equal(new IfcBoundingBox(new(-10, -5, 0), new(10, 5, 100)), product.Mesh!.BoundingBox);
    }

    [Fact]
    public void Open_OtherProductsRequireBody_AndLegacyMetadataOnlyElementsRemain()
    {
        string ifc = BuildIfc("IFC4", """
            #20=IFCACTUATOR('1YvctVUKr0kugbFTf53O9L',$,'Axis only',$,$,#6,#14,$,$);
            #21=IFCWALL('2YvctVUKr0kugbFTf53O9L',$,'Wall metadata',$,$,#6,$,$,$);
            """, representation: "Axis");
        var model = IfcModel.Open(new StringReader(ifc));
        var wall = Assert.Single(model.AllElements);
        Assert.Equal("IFCWALL", wall.IfcType);
        Assert.Null(wall.Mesh);
    }

    [Fact]
    public void Open_ExcludeSpatialStructuresTypesAndWrongSchemaOccurrences()
    {
        string ifc = BuildIfc("IFC2X3", """
            #20=IFCBUILDING('1YvctVUKr0kugbFTf53O9L',$,'Building',$,$,#6,#14,$,.ELEMENT.,$,$,$);
            #21=IFCACTUATOR('2YvctVUKr0kugbFTf53O9L',$,'IFC4 only',$,$,#6,#14,$,$);
            #22=IFCCURTAINWALLTYPE('3YvctVUKr0kugbFTf53O9L',$,'Type',$,$,$,(#24),$,$,.NOTDEFINED.);
            #24=IFCREPRESENTATIONMAP(#5,#13);
            #25=IFCDISTRIBUTIONPORT('4YvctVUKr0kugbFTf53O9L',$,'Port',$,$,#6,#14,.SOURCE.);
            #26=IFCVENDORPROXY('5YvctVUKr0kugbFTf53O9L',$,'Unknown',$,$,#6,#14,$);
            """);
        Assert.Empty(IfcModel.Open(new StringReader(ifc)).AllElements);
    }

    [Fact]
    public void Open_SiteWithBody_ImportsTerrainButNotMetadataOnlySite()
    {
        string ifc = BuildIfc("IFC2X3", """
            #20=IFCSITE('1YvctVUKr0kugbFTf53O9L',$,'Terrain',$,$,#6,#14,$,.ELEMENT.,$,$,$,$,$);
            #21=IFCSITE('2YvctVUKr0kugbFTf53O9L',$,'No terrain',$,$,#6,$,$,.ELEMENT.,$,$,$,$,$);
            """);
        var terrain = Assert.Single(IfcModel.Open(new StringReader(ifc)).AllElements);
        Assert.Equal("IFCSITE", terrain.IfcType);
        Assert.Null(terrain.Tag);
        Assert.Equal(12, terrain.Mesh!.TriangleCount);
    }

    [Fact]
    public void Open_NestedRamp_InheritsTheStoreyOfItsContainer()
    {
        string ifc = BuildIfc("IFC4", """
            #20=IFCBUILDINGSTOREY('1YvctVUKr0kugbFTf53O9L',$,'Ground',$,$,#6,$,$,.ELEMENT.,0.);
            #21=IFCBUILDINGSTOREY('2YvctVUKr0kugbFTf53O9L',$,'Upper',$,$,#6,$,$,.ELEMENT.,3000.);
            #22=IFCRAMP('3YvctVUKr0kugbFTf53O9L',$,'Parent ramp',$,$,#6,$,$,$);
            #23=IFCRAMP('4YvctVUKr0kugbFTf53O9L',$,'Ramp body',$,$,#6,#14,'Ramp-01',$);
            #24=IFCRELCONTAINEDINSPATIALSTRUCTURE('5YvctVUKr0kugbFTf53O9L',$,$,$,(#22),#21);
            #25=IFCRELAGGREGATES('6YvctVUKr0kugbFTf53O9L',$,$,$,#22,(#23));
            """);
        var model = IfcModel.Open(new StringReader(ifc));
        Assert.Empty(model.Storeys[0].Elements);
        var ramp = Assert.Single(model.Storeys[1].Elements);
        Assert.Equal("Ramp-01", ramp.Tag);
        Assert.NotNull(ramp.Mesh);
    }

    [Fact]
    public void Open_UnreadableBody_DoesNotUseClearanceAsTheProductMesh()
    {
        string ifc = BuildIfc("IFC4", """
            #20=IFCACTUATOR('1YvctVUKr0kugbFTf53O9L',$,'Actuator',$,$,#6,#14,$,$);
            #21=IFCSWEPTDISKSOLIDPOLYGONAL(#22,1.,$,$,$,4.);
            #22=IFCPOLYLINE((#4,#23,#25));
            #23=IFCCARTESIANPOINT((0.,0.,100.));
            #24=IFCSHAPEREPRESENTATION(#7,'Clearance','SweptSolid',(#12));
            #25=IFCCARTESIANPOINT((20.,0.,100.));
            """)
            .Replace("'Body','SweptSolid',(#12)", "'Body','AdvancedSweptSolid',(#21)", StringComparison.Ordinal)
            .Replace("$,$,(#13)", "$,$,(#13,#24)", StringComparison.Ordinal);

        var product = Assert.Single(IfcModel.Open(new StringReader(ifc)).AllElements);
        Assert.Equal("IFCACTUATOR", product.IfcType);
        Assert.Null(product.Mesh);
        var step = StepReader.Read(new StringReader(IfcModel.Open(new StringReader(ifc)).ToStepString()));
        Assert.Null(Assert.Single(step.OfType("IFCBUILDINGELEMENTPROXY")).GetRef(6));
        Assert.Empty(step.OfType("IFCPRODUCTDEFINITIONSHAPE"));
    }

    [Fact]
    public void Open_SubtractionFeature_KeepsItsMetadataOnlyHost()
    {
        string ifc = BuildIfc("IFC4", """
            #20=IFCELEMENTASSEMBLY('1YvctVUKr0kugbFTf53O9L',$,'Assembly',$,$,#6,$,$,$,$);
            #21=IFCVOIDINGFEATURE('2YvctVUKr0kugbFTf53O9L',$,'Opening',$,$,#6,#14,$,$);
            #22=IFCRELVOIDSELEMENT('3YvctVUKr0kugbFTf53O9L',$,$,$,#20,#21);
            """);
        var model = IfcModel.Open(new StringReader(ifc));
        var assembly = Assert.Single(model.AllElements);
        Assert.Equal("IFCELEMENTASSEMBLY", assembly.IfcType);
        Assert.Null(assembly.Mesh);
        Assert.NotNull(Assert.Single(assembly.Openings).Mesh);
    }

    [Fact]
    public void Open_NonElementProductWithBody_DoesNotTreatExtraAttributeAsTag()
    {
        var model = IfcModel.Open(new StringReader(BuildIfc("IFC4", """
            #20=IFCANNOTATION('1YvctVUKr0kugbFTf53O9L',$,'Annotation',$,$,#6,#14);
            """)));
        var annotation = Assert.Single(model.AllElements);
        Assert.Equal("IFCANNOTATION", annotation.IfcType);
        Assert.Null(annotation.Tag);
        Assert.Equal(12, annotation.Mesh!.TriangleCount);
    }

    [Fact]
    public void Open_Proxy_ReadsItsOwnTagAttribute()
    {
        var model = IfcModel.Open(new StringReader(BuildIfc("IFC4", """
            #20=IFCPROXY('1YvctVUKr0kugbFTf53O9L',$,'Proxy',$,$,#6,#14,.PRODUCT.,'Proxy tag');
            """)));
        Assert.Equal("Proxy tag", Assert.Single(model.AllElements).Tag);
    }

    [Theory]
    [InlineData("IFCOPENINGSTANDARDCASE", ".OPENING.")]
    [InlineData("IFCVOIDINGFEATURE", "$")]
    public void Open_SubtractionFeature_IsAttachedToHostAndNotDuplicated(string type, string predefinedType)
    {
        var model = IfcModel.Open(new StringReader(BuildIfc("IFC4", $"""
            #20=IFCWALL('1YvctVUKr0kugbFTf53O9L',$,'Wall',$,$,#6,#14,$,$);
            #21={type}('2YvctVUKr0kugbFTf53O9L',$,'Opening',$,$,#6,#14,$,{predefinedType});
            #22=IFCRELVOIDSELEMENT('3YvctVUKr0kugbFTf53O9L',$,$,$,#20,#21);
            """)));
        var wall = Assert.Single(model.AllElements);
        var opening = Assert.Single(wall.Openings);
        Assert.Equal(type, opening.IfcType);
        Assert.NotNull(opening.Mesh);
    }

    [Theory]
    [InlineData("IFC2X3", "IFCCURTAINWALL", "", ".ELEMENT.")]
    [InlineData("IFC4", "IFCFURNITURE", ",$", ".NOTDEFINED.")]
    public void SaveAndOpen_ProductsWithoutDedicatedWriter_UseTheSchemaProxyKind(
        string schema, string type, string tail, string expectedProxyKind)
    {
        string ifc = BuildIfc(schema, $"#20={type}('1YvctVUKr0kugbFTf53O9L',$,'Product',$,$,#6,#14,'P-01'{tail});");
        var model = IfcModel.Open(new StringReader(ifc));
        var original = Assert.Single(model.AllElements);
        string saved = model.ToStepString();
        var step = StepReader.Read(new StringReader(saved));
        Assert.Equal(expectedProxyKind, Assert.Single(step.OfType("IFCBUILDINGELEMENTPROXY")).GetRaw(8));
        var proxy = Assert.Single(IfcModel.Open(new StringReader(saved)).AllElements);
        Assert.Equal(original.GlobalId, proxy.GlobalId);
        Assert.Equal(original.Tag, proxy.Tag);
        Assert.Equal(original.Mesh!.BoundingBox, proxy.Mesh!.BoundingBox);
    }

    [Fact]
    public void Save_Ifc2x3OpeningHasNoPredefinedTypeArgument()
    {
        string ifc = BuildIfc("IFC2X3", """
            #20=IFCWALL('1YvctVUKr0kugbFTf53O9L',$,'Wall',$,$,#6,#14,$);
            #21=IFCOPENINGELEMENT('2YvctVUKr0kugbFTf53O9L',$,'Opening',$,$,#6,#14,$);
            #22=IFCRELVOIDSELEMENT('3YvctVUKr0kugbFTf53O9L',$,$,$,#20,#21);
            """);
        string saved = IfcModel.Open(new StringReader(ifc)).ToStepString();
        var opening = Assert.Single(StepReader.Read(new StringReader(saved)).OfType("IFCOPENINGELEMENT"));
        Assert.Equal(8, opening.Arguments.Count);
    }
}
