using IfcIO.Cli;
using IfcIO.Core;
using IfcIO.Patch;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public sealed class SchemaDetectionTests
{
    private static string ModelText(string schemaDeclaration) => $"""
        ISO-10303-21;
        HEADER;
        {schemaDeclaration}
        ENDSEC;
        DATA;
        #1=IFCPROJECT('01vctVUKr0kugbFTf53O9L',$,'Schema test',$,$,$,$,(),$);
        #2=IFCWALL('02vctVUKr0kugbFTf53O9L',$,'Wall',$,$,$,$,$,$);
        ENDSEC;
        END-ISO-10303-21;
        """;

    [Theory]
    [InlineData("IFC2X3", IfcSchema.Ifc2x3)]
    [InlineData("ifc4", IfcSchema.Ifc4)]
    [InlineData("IFC4 { 2 999 1 }", IfcSchema.Ifc4)]
    public void Open_RecognizesExactWritableSchemaNames(string schema, IfcSchema expected)
    {
        var model = IfcModel.Open(new StringReader(ModelText($"FILE_SCHEMA(('{schema}'));")));
        Assert.Equal(expected, model.Schema);
        Assert.Equal(schema, model.SourceSchemaIdentifier);
        Assert.Single(model.AllElements);
        Assert.Contains(expected == IfcSchema.Ifc4 ? "FILE_SCHEMA(('IFC4'))" : "FILE_SCHEMA(('IFC2X3'))", model.ToStepString());
    }

    [Theory]
    [InlineData("IFC4X3")]
    [InlineData("IFC4X3_ADD1")]
    [InlineData("IFC4X3_ADD2")]
    [InlineData("IFC4X3_ADD2 { 2 999 1 }")]
    public void Open_RecognizesIfc4x3ButDoesNotEnableWriting(string schema)
    {
        var model = IfcModel.Open(new StringReader(ModelText($"FILE_SCHEMA(('{schema}'));")));
        Assert.Equal(IfcSchema.Ifc4x3, model.Schema);
        Assert.Equal(schema, model.SourceSchemaIdentifier);
        Assert.Single(model.AllElements);
        Assert.Contains(model.Validate().Warnings, warning => warning.RuleId == "IFC_SCHEMA_SUBSET");
        Assert.Throws<NotSupportedException>(() => model.ToStepString());
        Assert.Throws<NotSupportedException>(() => model.Extract(_ => true).ToStepString());

        model.Schema = IfcSchema.Ifc4;
        Assert.Throws<NotSupportedException>(() => model.ToStepString());
    }

    [Theory]
    [InlineData("not-IFC4")]
    [InlineData("IFC4X3_UNKNOWN")]
    [InlineData("IFC4 { unknown }")]
    [InlineData("IFC5")]
    public void Open_RejectsAnUnknownSchemaBeforeInterpretingTheModel(string schema)
    {
        var input = ModelText($"FILE_SCHEMA(('{schema}'));");
        var file = StepReader.Read(new StringReader(input));
        Assert.Equal(schema, file.Schema);

        var error = Assert.Throws<NotSupportedException>(() => IfcModel.Open(new StringReader(input)));
        Assert.Contains(schema, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_RejectsMissingAndAmbiguousSchemaDeclarations()
    {
        const string ambiguous = "FILE_SCHEMA(('IFC2X3','IFC4'));";
        Assert.Throws<InvalidDataException>(() => IfcModel.Open(new StringReader(ModelText(""))));
        var step = StepReader.Read(new StringReader(ModelText(ambiguous)));
        Assert.Collection(step.SchemaIdentifiers, first => Assert.Equal("IFC2X3", first), second => Assert.Equal("IFC4", second));
        Assert.Throws<NotSupportedException>(() =>
            IfcModel.Open(new StringReader(ModelText(ambiguous))));
        Assert.Throws<InvalidDataException>(() =>
            IfcModel.Open(new StringReader(ModelText("FILE_SCHEMA(('IFC4X3')); FILE_SCHEMA(('IFC4'));"))));
    }

    [Theory]
    [InlineData("FILE_SCHEMA((IFC4));")]
    [InlineData("FILE_SCHEMA(('IFC4',$));")]
    [InlineData("FILE_SCHEMA(('IFC4'),('IFC4X3_ADD2'));")]
    [InlineData("FILE_SCHEMA(('IFC4',));")]
    public void Open_RejectsMalformedSchemaDeclarations(string declaration)
    {
        Assert.Throws<InvalidDataException>(() => IfcModel.Open(new StringReader(ModelText(declaration))));
    }

    [Fact]
    public void Open_Ifc4x3OmitsGeometryUnderANonLocalPlacement()
    {
        const string text = """
            HEADER; FILE_SCHEMA(('IFC4X3_ADD2')); ENDSEC;
            DATA;
            #1=IFCBUILDINGELEMENTPROXY('01vctVUKr0kugbFTf53O9L',$,'Linear placement',$,$,#12,#30,$,$);
            #2=IFCBUILDINGELEMENTPROXY('02vctVUKr0kugbFTf53O9L',$,'Local below linear',$,$,#11,#30,$,$);
            #3=IFCBUILDINGELEMENTPROXY('03vctVUKr0kugbFTf53O9L',$,'Local placement',$,$,#19,#30,$,$);
            #11=IFCLOCALPLACEMENT(#12,#20);
            #12=IFCLINEARPLACEMENT($,#13,$);
            #13=IFCAXIS2PLACEMENTLINEAR(#14,$,$);
            #14=IFCPOINTBYDISTANCEEXPRESSION(IFCLENGTHMEASURE(0.),$,$,$,#15);
            #15=IFCPOLYLINE((#16,#17));
            #16=IFCCARTESIANPOINT((0.,0.,0.));
            #17=IFCCARTESIANPOINT((100.,0.,0.));
            #19=IFCLOCALPLACEMENT($,#20);
            #20=IFCAXIS2PLACEMENT3D(#16,$,$);
            #30=IFCPRODUCTDEFINITIONSHAPE($,$,(#31));
            #31=IFCSHAPEREPRESENTATION($,'Body','SweptSolid',(#32));
            #32=IFCEXTRUDEDAREASOLID(#33,#20,#34,10.);
            #33=IFCCIRCLEPROFILEDEF(.AREA.,$,$,2.);
            #34=IFCDIRECTION((0.,0.,1.));
            ENDSEC;
            """;
        var model = IfcModel.Open(new StringReader(text));

        Assert.Equal(2, model.UnsupportedPlacementCount);
        Assert.Single(model.AllElements, element => element.GetEffectiveMesh() is not null);
        Assert.Contains(model.Validate().Warnings, warning => warning.RuleId == "IFC_UNSUPPORTED_PLACEMENTS");
    }

    [Theory]
    [InlineData("existing.ifc")]
    [InlineData("existing.ifczip")]
    public void Save_DoesNotTruncateAFileWhenTheSchemaIsReadOnly(string name)
    {
        var model = IfcModel.Open(new StringReader(ModelText("FILE_SCHEMA(('IFC4X3_ADD2'));")));
        var dir = Directory.CreateTempSubdirectory("ifcio-schema-");
        try
        {
            string path = Path.Combine(dir.FullName, name);
            File.WriteAllText(path, "keep this");
            Assert.Throws<NotSupportedException>(() => model.Save(path));
            Assert.Equal("keep this", File.ReadAllText(path));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Cli_ReportsUnsupportedSchemaAndDoesNotRewriteIfc4x3()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-schema-cli-");
        try
        {
            string input = Path.Combine(dir.FullName, "model.ifc");
            string target = Path.Combine(dir.FullName, "target.ifc");
            File.WriteAllText(input, ModelText("FILE_SCHEMA(('IFC4X3_ADD2'));"));
            File.WriteAllText(target, "previous file");

            Assert.Equal(0, Program.Main(["info", input]));
            Assert.Equal(1, Program.Main(["export", input, "--ifc", target, "--schema", "ifc4"]));
            Assert.Equal(1, Program.Main(["patch", input, "--migrate", "ifc4", "-o", target]));
            Assert.Equal("previous file", File.ReadAllText(target));

            File.WriteAllText(input, ModelText("FILE_SCHEMA(('NOT_IFC4'));"));
            Assert.Equal(1, Program.Main(["info", input]));
            File.WriteAllText(input, ModelText("FILE_SCHEMA(('IFC4'));"));
            Assert.Equal(1, Program.Main(["export", input, "--ifc", target, "--schema", "ifc4x3"]));
            Assert.Equal(1, Program.Main(["patch", input, "--migrate", "ifc4x3", "-o", target]));
            Assert.Equal("previous file", File.ReadAllText(target));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Patch_RejectsMergingAndMigrationFromAnIfc4x3Subset()
    {
        var imported = IfcModel.Open(new StringReader(ModelText("FILE_SCHEMA(('IFC4X3_ADD1'));")));
        var writable = IfcModel.Create("Other model", IfcSchema.Ifc4);

        Assert.Throws<NotSupportedException>(() => IfcPatch.MigrateSchema(imported, IfcSchema.Ifc4));
        Assert.Throws<NotSupportedException>(() => IfcModel.Merge([imported, writable]));
        Assert.Equal(IfcSchema.Ifc4x3, imported.Schema);
    }
}
