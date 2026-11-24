using System.Xml.Linq;
using IfcIO.Core;
using Xunit;

namespace IfcIO.Tests;

public sealed class SurfaceStyleTests
{
    [Theory]
    [InlineData("IFC2X3", true, true, 0.75)]
    [InlineData("IFC2X3", true, false, 1.0)]
    [InlineData("IFC2X3", false, false, 1.0)]
    [InlineData("IFC2X3", false, true, 0.75)]
    [InlineData("IFC4", false, true, 0.75)]
    [InlineData("IFC4", false, false, 0.75)]
    public void Open_ReadsDirectAndWrappedSurfaceStyles(string schema, bool wrapped, bool rendering, double alpha)
    {
        var model = IfcModel.Open(new StringReader(StyledWall(schema, wrapped, rendering,
            itemStyle: true, materialStyle: false, associateMaterial: false)));

        var wall = Assert.Single(model.AllElements);
        Assert.NotNull(wall.Mesh);
        Assert.NotNull(wall.Material);
        Assert.Equal("Surface colour", wall.Material.Name);
        Assert.Equal(0.2, wall.Material.R);
        Assert.Equal(0.4, wall.Material.G);
        Assert.Equal(0.8, wall.Material.B);
        Assert.Equal(alpha, wall.Material.Alpha);
    }

    [Fact]
    public void Open_GeometryStyleTakesPrecedenceOverAColouredMaterialAssociation()
    {
        var model = IfcModel.Open(new StringReader(StyledWall("IFC2X3", wrapped: true, rendering: true, itemStyle: true, materialStyle: true)));

        var wall = Assert.Single(model.AllElements);
        Assert.Equal("Physical material", wall.Material!.Name);
        Assert.Equal("Surface colour", wall.GetEffectiveMaterial()!.Name);
        Assert.Equal(0.2, wall.GetEffectiveMaterial()!.R);
        var material = Assert.Single(model.Materials, candidate => candidate.Name == "Physical material");
        Assert.Equal(0.9, material.R);
        Assert.Equal(0.6, material.Alpha, 8);
    }

    [Fact]
    public void Open_UsesTheMaterialDefinitionRepresentationAsAFallback()
    {
        var model = IfcModel.Open(new StringReader(StyledWall("IFC2X3", wrapped: true, rendering: true, itemStyle: false, materialStyle: true)));

        var material = Assert.Single(model.AllElements).Material;
        Assert.NotNull(material);
        Assert.Equal("Physical material", material.Name);
        Assert.Equal(0.9, material.R);
        Assert.Equal(0.3, material.G);
        Assert.Equal(0.1, material.B);
        Assert.Equal(0.6, material.Alpha, 8);
    }

    [Fact]
    public void Open_PlainMaterialAssociationRemainsAvailableWithoutAnyStyle()
    {
        var model = IfcModel.Open(new StringReader(StyledWall("IFC2X3", wrapped: false, rendering: false, itemStyle: false, materialStyle: false)));

        var material = Assert.Single(model.AllElements).Material;
        Assert.NotNull(material);
        Assert.Equal("Physical material", material.Name);
        Assert.Equal(0.8, material.R);
        Assert.Equal(1.0, material.Alpha);
    }

    [Fact]
    public void Open_DoesNotBorrowAnUnrelatedStyleWithTheSameName()
    {
        var input = StyledWall("IFC2X3", wrapped: true, rendering: true, itemStyle: false, materialStyle: false)
            .Replace("IFCMATERIAL('Physical material')", "IFCMATERIAL('Surface colour')", StringComparison.Ordinal);
        var model = IfcModel.Open(new StringReader(input));

        var material = Assert.Single(model.AllElements).Material;
        Assert.NotNull(material);
        Assert.Equal(0.8, material.R);
        Assert.Equal(1.0, material.Alpha);
    }

    [Fact]
    public void SaveAndOpen_KeepsPhysicalMaterialAndVisualStyleSeparate()
    {
        var model = IfcModel.Open(new StringReader(StyledWall("IFC2X3", wrapped: true, rendering: true,
            itemStyle: true, materialStyle: true)));
        var specification = XDocument.Parse("""
            <ids xmlns="http://standards.buildingsmart.org/IDS">
              <specifications>
                <specification name="Wall material">
                  <applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability>
                  <requirements><material><value><simpleValue>Physical material</simpleValue></value></material></requirements>
                </specification>
              </specifications>
            </ids>
            """);

        for (int pass = 0; pass < 3; pass++)
        {
            Assert.True(model.ValidateIds(specification).IsPassed);
            var step = model.ToStepString();
            Assert.Single(StepReader.Read(new StringReader(step)).OfType("IFCMATERIAL"));
            model = IfcModel.Open(new StringReader(step));
            var wall = Assert.Single(model.AllElements);
            Assert.Equal("Physical material", wall.Material!.Name);
            Assert.Equal(0.6, wall.Material.Alpha, 8);
            var visual = wall.GetEffectiveMaterial();
            Assert.NotNull(visual);
            Assert.Equal("Surface colour", visual.Name);
            Assert.Equal(0.75, visual.Alpha);
        }
    }

    [Theory]
    [InlineData(false, "Surface colour")]
    [InlineData(true, "Material colour")]
    public void Open_UsesTheStyleOfTheClippingResultBeforeTheFirstOperand(bool styleResult, string expectedStyle)
    {
        const string clipping = """
            #50=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#22,#51);
            #51=IFCHALFSPACESOLID(#52,.T.);
            #52=IFCPLANE(#53);
            #53=IFCAXIS2PLACEMENT3D(#54,$,$);
            #54=IFCCARTESIANPOINT((0.,0.,5.));
            """;
        string extra = clipping + (styleResult ? "\n#55=IFCSTYLEDITEM(#50,(#45),$);" : "");
        string input = StyledWall("IFC2X3", wrapped: true, rendering: true, itemStyle: true, materialStyle: true, extra: extra)
            .Replace("'SweptSolid',(#22)", "'Clipping',(#50)", StringComparison.Ordinal);
        var wall = Assert.Single(IfcModel.Open(new StringReader(input)).AllElements);

        Assert.NotNull(wall.Mesh);
        Assert.Equal(expectedStyle, wall.GetEffectiveMaterial()!.Name);
    }

    [Fact]
    public void Open_SingleLayerMaterialProvidesAnAppearanceWhenNoGeometryStyleExists()
    {
        const string layer = """
            #50=IFCMATERIALLAYER(#40,2.,$);
            #51=IFCMATERIALLAYERSET((#50),'Single layer');
            #52=IFCMATERIALLAYERSETUSAGE(#51,.AXIS2.,.POSITIVE.,0.);
            """;
        var input = StyledWall("IFC2X3", wrapped: true, rendering: true, itemStyle: false, materialStyle: true, extra: layer)
            .Replace("(#2),#40);", "(#2),#52);", StringComparison.Ordinal);
        var wall = Assert.Single(IfcModel.Open(new StringReader(input)).AllElements);

        Assert.NotNull(wall.MaterialLayerSet);
        Assert.Null(wall.SurfaceMaterial);
        Assert.Equal(0.9, wall.GetEffectiveMaterial()!.R);
    }

    private static string StyledWall(string schema, bool wrapped, bool rendering, bool itemStyle, bool materialStyle,
        bool associateMaterial = true, string extra = "")
    {
        string assignments = wrapped ? "#33=IFCPRESENTATIONSTYLEASSIGNMENT((#30));" : "";
        string shading = rendering
            ? "#31=IFCSURFACESTYLERENDERING(#32,0.25,$,$,$,$,$,$,.NOTDEFINED.);"
            : schema == "IFC4" ? "#31=IFCSURFACESTYLESHADING(#32,0.25);" : "#31=IFCSURFACESTYLESHADING(#32);";
        string materialRepresentations = materialStyle ? """
            #42=IFCMATERIALDEFINITIONREPRESENTATION($,$,(#43),#40);
            #43=IFCSTYLEDREPRESENTATION(#10,'Style','Material',(#44));
            #44=IFCSTYLEDITEM($,(#45),$);
            #45=IFCPRESENTATIONSTYLEASSIGNMENT((#46));
            #46=IFCSURFACESTYLE('Material colour',.BOTH.,(#47));
            #47=IFCSURFACESTYLERENDERING(#48,0.4,$,$,$,$,$,$,.NOTDEFINED.);
            #48=IFCCOLOURRGB($,0.9,0.3,0.1);
            """ : "";
        return $"""
            ISO-10303-21;
            HEADER; FILE_SCHEMA(('{schema}')); ENDSEC;
            DATA;
            #1=IFCPROJECT('01vctVUKr0kugbFTf53O9L',$,'Colours',$,$,$,$,(#10),$);
            #2=IFCWALL('02vctVUKr0kugbFTf53O9L',$,'Wall',$,$,$,#20,${(schema == "IFC4" ? ",$" : "")});
            #10=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#11,$);
            #11=IFCAXIS2PLACEMENT3D(#12,$,$);
            #12=IFCCARTESIANPOINT((0.,0.,0.));
            #20=IFCPRODUCTDEFINITIONSHAPE($,$,(#21));
            #21=IFCSHAPEREPRESENTATION(#10,'Body','SweptSolid',(#22));
            #22=IFCEXTRUDEDAREASOLID(#23,#11,#24,10.);
            #23=IFCRECTANGLEPROFILEDEF(.AREA.,$,#25,2.,3.);
            #24=IFCDIRECTION((0.,0.,1.));
            #25=IFCAXIS2PLACEMENT2D(#26,$);
            #26=IFCCARTESIANPOINT((0.,0.));
            #30=IFCSURFACESTYLE('Surface colour',.BOTH.,(#31));
            {shading}
            #32=IFCCOLOURRGB($,0.2,0.4,0.8);
            {assignments}
            {(itemStyle ? $"#34=IFCSTYLEDITEM(#22,(#{(wrapped ? 33 : 30)}),$);" : "")}
            #40=IFCMATERIAL('Physical material'{(schema == "IFC4" ? ",$,$" : "")});
            {(associateMaterial ? "#41=IFCRELASSOCIATESMATERIAL('03vctVUKr0kugbFTf53O9L',$,$,$,(#2),#40);" : "")}
            {materialRepresentations}
            {extra}
            ENDSEC;
            END-ISO-10303-21;
            """;
    }
}
