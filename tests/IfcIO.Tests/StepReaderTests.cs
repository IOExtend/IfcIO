using IfcIO.Core;
using Xunit;

namespace IfcIO.Tests;

public class StepReaderTests
{
    [Theory]
    [InlineData(@"\S\D", "\u00C4")]
    [InlineData(@"\PA\\S\D", "\u00C4")]
    [InlineData(@"\PB\\S\!", "\u0104")]
    [InlineData(@"\PC\\S\!", "\u0126")]
    [InlineData(@"\PD\\S\" + "\"", "\u0138")]
    [InlineData(@"\PE\\S\0", "\u0410")]
    [InlineData(@"\PF\\S\G", "\u0627")]
    [InlineData(@"\PG\\S\P", "\u03A0")]
    [InlineData(@"\PH\\S\`", "\u05D0")]
    [InlineData(@"\PI\\S\]", "\u0130")]
    [InlineData(@"\PE\\S\0\X\C4\PA\\S\D", "\u0410\u00C4\u00C4")]
    [InlineData(@"\X4\0001F6000001F638\X0\", "\U0001F600\U0001F638")]
    [InlineData(@"\X2\03C0\X0\\X4\0001F600\X0\", "\u03C0\U0001F600")]
    [InlineData(@"\X4\00110000\X0\", @"\X4\00110000\X0\")]
    [InlineData(@"\X4\0000D800\X0\", @"\X4\0000D800\X0\")]
    [InlineData(@"\X4\123\X0\", @"\X4\123\X0\")]
    [InlineData(@"\X4\ABCDEF", @"\X4\ABCDEF")]
    [InlineData(@"\S\'", "\u00A7")]
    [InlineData(@"a\N\b\F\c", "abc")]
    public void Unescape_DecodesStepPageAndUnicodeDirectives(string source, string expected)
    {
        Assert.Equal(expected, StepWriter.Unescape(source));
    }

    [Fact]
    public void Read_ResetsTheCodePageForEachString()
    {
        var file = StepReader.Read(new StringReader(
            @"DATA; #1=IFCA('\PE\\S\0','\S\0','\X4\0001F600\X0\'); ENDSEC;"));

        var entity = file.Find(1)!;
        Assert.Equal("\u0410", entity.GetString(0));
        Assert.Equal("\u00B0", entity.GetString(1));
        Assert.Equal("\U0001F600", entity.GetString(2));
    }

    [Fact]
    public void Read_UsesStatementBoundariesRatherThanLineBoundaries()
    {
        const string text = """
            ISO-10303-21; HEADER; FILE_SCHEMA(('IFC4')); ENDSEC;
            DATA; /* ; #99=IFCFAKE(); */ #1=IFCA('a;') /* before ; */; #2=/* inside */IFCB(#1,
              /* comment across
              two lines */(2,3)); #3=IFCC('It''s /* literal */;');
            ENDSEC; END-ISO-10303-21;
            """;

        var file = StepReader.Read(new StringReader(text));

        Assert.Equal("IFC4", file.Schema);
        Assert.Equal(3, file.Entities.Count);
        Assert.Equal("a;", file.Find(1)!.GetString(0));
        Assert.Equal("IFCB", file.Find(2)!.Type);
        Assert.Equal(1, file.Find(2)!.GetRef(0));
        Assert.Equal("(2,3)", file.Find(2)!.GetRaw(1));
        Assert.Equal("It's /* literal */;", file.Find(3)!.GetString(0));
    }

    [Fact]
    public void Read_IgnoresPhysicalBreaksButPreservesSpacesAndEncodedNewlines()
    {
        const string text = "ISO-10303-21;\nDATA;\n#1=IFCA('first;\r\n  second;\\X\\0A\nthird  ',#2);#2=IF\nCB();\nENDSEC;\n";

        var file = StepReader.Read(new StringReader(text));

        Assert.Equal("first;  second;\nthird  ", file.Find(1)!.GetString(0));
        Assert.Equal(2, file.Find(1)!.GetRef(1));
        Assert.Equal("IFCB", file.Find(2)!.Type);
    }

    [Fact]
    public void Read_UsesReadLineAndKeepsCommentAndStringStateAcrossPhysicalLines()
    {
        using var text = new LineReader(
            "DATA; /", "* #9=IFCFAKE(); *", "/ #1=IFCA('a'", "';b\\X2\\03", "C0\\X0\\'); ENDSEC;");
        var file = StepReader.Read(text);

        Assert.Single(file.Entities);
        Assert.Equal("a';b\u03C0", file.Find(1)!.GetString(0));
    }

    private sealed class LineReader(params string[] lines) : TextReader
    {
        private int _index;
        public override string? ReadLine() => _index < lines.Length ? lines[_index++] : null;
    }

    [Fact]
    public void Read_ApostropheUsedAsShiftedByteDoesNotEndTheString()
    {
        using var text = new StringReader(@"DATA;#1=IFCA('\S\'',#2);#2=IFCB('\PG\\S\P\S\'');ENDSEC;");
        var file = StepReader.Read(text);

        Assert.Equal(2, file.Entities.Count);
        Assert.Equal("\u00A7", file.Find(1)!.GetString(0));
        Assert.Equal(2, file.Find(1)!.GetRef(1));
        Assert.Equal("\u03A0\u00A7", file.Find(2)!.GetString(0));
    }

    [Theory]
    [InlineData("DATA; #1=IFCA('unfinished")]
    [InlineData("DATA; #1=IFCA(); ENDSEC; /* unfinished")]
    [InlineData("DATA; #1=IFCA(); #2=IFCB()")]
    [InlineData("DATA; #1=IFCA();")]
    public void Read_ReportsIncompleteInput(string input)
    {
        Assert.Throws<InvalidDataException>(() => StepReader.Read(new StringReader(input)));
    }

    [Fact]
    public void Read_PopulatesHeaderFieldsFromMultilineStatements()
    {
        const string text = """
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(
                ('a view; it''s valid', 'secondary'),
                '2;1');
            FILE_NAME ('owner''s model.ifc',
                '2026-11-23T15:30:00',
                ('A, First', 'B. Last'),
                ('Org (BIM)', 'Other org'),
                'Processor 1', 'Source 2', 'Approved');
            FILE_SCHEMA (('IFC2X3'));
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """;

        var file = StepReader.Read(new StringReader(text));

        Assert.Equal("a view; it's valid", file.Description);
        Assert.Collection(file.Descriptions, first => Assert.Equal("a view; it's valid", first),
            second => Assert.Equal("secondary", second));
        Assert.Equal("2;1", file.ImplementationLevel);
        Assert.Equal("owner's model.ifc", file.FileName);
        Assert.Equal("2026-11-23T15:30:00", file.TimeStamp);
        Assert.Equal("A, First, B. Last", file.Author);
        Assert.Equal("Org (BIM), Other org", file.Organization);
        Assert.Equal("Processor 1", file.PreprocessorVersion);
        Assert.Equal("Source 2", file.OriginatingSystem);
        Assert.Equal("Approved", file.Authorization);
        Assert.Equal("IFC2X3", file.Schema);
    }
}
