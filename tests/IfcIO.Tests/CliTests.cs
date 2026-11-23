using System.IO.Compression;
using IfcIO.Cli;
using IfcIO.Geometry;
using IfcIO.Grids;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class CliTests
{
    private static string CreateSampleIfcFile()
    {
        var model = IfcModel.Create("Sample Complex", IfcSchema.Ifc4);
        var floor1 = model.AddStorey("Level 1", 0.0);
        var mat = new IfcMaterial { Name = "Concrete", R = 0.8, G = 0.8, B = 0.8 };

        model.AddMesh("Стена 1", [new(0, 0, 0), new(3000, 0, 0), new(0, 200, 0)], [0, 1, 2], mat, floor1);
        model.AddMesh("Колонна 1", [new(100, 100, 0), new(500, 100, 0), new(100, 500, 0)], [0, 1, 2], mat, floor1);

        string path = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.ifc");
        model.Save(path);
        return path;
    }

    [Fact]
    public void Cli_Info_ReturnsZero()
    {
        string samplePath = CreateSampleIfcFile();
        try
        {
            int code = Program.Main(["info", samplePath, "--report"]);
            Assert.Equal(0, code);
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
        }
    }

    [Fact]
    public void Cli_Validate_PassesOnValidModel()
    {
        string samplePath = CreateSampleIfcFile();
        try
        {
            int code = Program.Main(["validate", samplePath]);
            Assert.Equal(0, code);
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
        }
    }

    [Fact]
    public void Cli_Validate_ReturnsFailureWhenRequestedBcfCannotBeWritten()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-validate-");
        try
        {
            string input = CopyPlacementFixture(dir.FullName);
            string blocked = Path.Combine(dir.FullName, "not-a-directory");
            File.WriteAllText(blocked, "keep this file");
            string destination = Path.Combine(blocked, "report.bcfzip");

            Assert.Equal(1, Program.Main(["validate", input, "--bcf", destination]));
            Assert.False(File.Exists(destination));
            Assert.Equal("keep this file", File.ReadAllText(blocked));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Cli_Validate_WritesAReadableBcfForAValidModel()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-validate-");
        try
        {
            string input = CopyPlacementFixture(dir.FullName);
            string destination = Path.Combine(dir.FullName, "report.bcfzip");
            File.WriteAllText(destination, "previous report");

            Assert.Equal(0, Program.Main(["validate", input, "--bcf", destination]));
            using var archive = ZipFile.OpenRead(destination);
            Assert.NotNull(archive.GetEntry("bcf.version"));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Cli_Validate_CannotReplaceTheInputWithABcfArchive()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-validate-");
        try
        {
            string input = CopyPlacementFixture(dir.FullName);
            byte[] before = File.ReadAllBytes(input);
            string alias = Path.Combine(dir.FullName, ".", Path.GetFileName(input));

            Assert.Equal(1, Program.Main(["validate", input, "--bcf", alias]));
            Assert.Equal(before, File.ReadAllBytes(input));
            Assert.Single(dir.GetFiles());
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Cli_Validate_RejectsAValidModelWithAnIncompleteStepTail()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-validate-");
        try
        {
            string input = CopyPlacementFixture(dir.FullName);
            File.AppendAllText(input, "\n/* incomplete comment");
            string destination = Path.Combine(dir.FullName, "report.bcfzip");

            Assert.Equal(1, Program.Main(["validate", input, "--bcf", destination]));
            Assert.False(File.Exists(destination));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Cli_Validate_ReturnsFailureWhenModelIngestionCannotComplete()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-validate-");
        try
        {
            string input = CopyPlacementFixture(dir.FullName);
            string source = File.ReadAllText(input);
            const string placement = "#101=IFCLOCALPLACEMENT(#61,#102);";
            Assert.Contains(placement, source);
            File.WriteAllText(input, source.Replace(placement, "#101=IFCLOCALPLACEMENT(#101,#102);", StringComparison.Ordinal));
            string destination = Path.Combine(dir.FullName, "report.bcfzip");

            Assert.Equal(1, Program.Main(["validate", input]));
            Assert.Equal(1, Program.Main(["validate", input, "--bcf", destination]));
            Assert.False(File.Exists(destination));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Cli_Validate_RejectsBcfOptionWithoutOutputPath()
    {
        var dir = Directory.CreateTempSubdirectory("ifcio-validate-");
        try
        {
            string input = CopyPlacementFixture(dir.FullName);

            Assert.Equal(1, Program.Main(["validate", input, "--bcf"]));
        }
        finally { dir.Delete(recursive: true); }
    }

    private static string CopyPlacementFixture(string directory)
    {
        string path = Path.Combine(directory, "placement.ifc");
        using var fixture = typeof(CliTests).Assembly.GetManifestResourceStream("IfcIO.Tests.placement.ifc")!;
        using var destination = File.Create(path);
        fixture.CopyTo(destination);
        return path;
    }

    [Fact]
    public void Cli_Export_GlbAndObj_GeneratesFiles()
    {
        string samplePath = CreateSampleIfcFile();
        string tempGlb = Path.Combine(Path.GetTempPath(), $"cli_{Guid.NewGuid():N}.glb");
        string tempObj = Path.Combine(Path.GetTempPath(), $"cli_{Guid.NewGuid():N}.obj");
        try
        {
            int code = Program.Main(["export", samplePath, "--glb", tempGlb, "--obj", tempObj]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(tempGlb));
            Assert.True(File.Exists(tempObj));
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
            if (File.Exists(tempGlb)) File.Delete(tempGlb);
            if (File.Exists(tempObj)) File.Delete(tempObj);
            string mtl = Path.ChangeExtension(tempObj, ".mtl");
            if (File.Exists(mtl)) File.Delete(mtl);
        }
    }

    [Fact]
    public void Cli_PackAndUnpack_Roundtrip()
    {
        string samplePath = CreateSampleIfcFile();
        string tempZip = Path.Combine(Path.GetTempPath(), $"cli_{Guid.NewGuid():N}.ifczip");
        string tempUnpacked = Path.Combine(Path.GetTempPath(), $"cli_unpacked_{Guid.NewGuid():N}.ifc");
        try
        {
            int packCode = Program.Main(["pack", samplePath, "-o", tempZip]);
            Assert.Equal(0, packCode);
            Assert.True(File.Exists(tempZip));

            int unpackCode = Program.Main(["unpack", tempZip, "-o", tempUnpacked]);
            Assert.Equal(0, unpackCode);
            Assert.True(File.Exists(tempUnpacked));

            string content = File.ReadAllText(tempUnpacked);
            Assert.Contains("IFCPROJECT", content);
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (File.Exists(tempUnpacked)) File.Delete(tempUnpacked);
        }
    }

    [Fact]
    public void Cli_Query_FindsMatchingElements()
    {
        string samplePath = CreateSampleIfcFile();
        try
        {
            int code = Program.Main(["query", samplePath, "IfcWall"]);
            Assert.Equal(0, code);
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
        }
    }

    [Fact]
    public void Cli_Patch_Purge_SavesCleanedModel()
    {
        string samplePath = CreateSampleIfcFile();
        string tempOut = Path.Combine(Path.GetTempPath(), $"cli_patch_{Guid.NewGuid():N}.ifc");
        try
        {
            int code = Program.Main(["patch", samplePath, "-o", tempOut, "--purge"]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(tempOut));
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1,2")]
    [InlineData("1,no,3")]
    [InlineData("1,NaN,3")]
    [InlineData("1,2,1e999")]
    public void Cli_Patch_RejectsInvalidOffsetsWithoutWritingOutput(string? offset)
    {
        string input = CreateSampleIfcFile();
        string output = Path.Combine(Path.GetTempPath(), $"cli_patch_{Guid.NewGuid():N}.ifc");
        try
        {
            string[] args = ["patch", input, "-o", output, "--offset"];
            if (offset is not null) args = [.. args, offset];
            Assert.Equal(1, Program.Main(args));
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData("clash", ".json")]
    [InlineData("view", ".html")]
    public void Cli_GridOptions_FailOnMissingInputAndAcceptExistingGrid(string command, string extension)
    {
        string input = CreateSampleIfcFile();
        var dir = Directory.CreateTempSubdirectory("ifcio-grids-");
        try
        {
            string grid = Path.Combine(dir.FullName, "grid.xml");
            string savedGrid = Path.Combine(dir.FullName, "saved-grid.xml");
            string output = Path.Combine(dir.FullName, "report" + extension);

            Assert.Equal(1, Program.Main([command, input, "-o", output, "--grids"]));
            Assert.Equal(1, Program.Main([command, input, "-o", output, "--grids", grid]));
            Assert.Equal(1, Program.Main([command, input, "--export-grids", "-o", output]));
            Assert.False(File.Exists(output));

            var system = new IfcGridSystem("Test grid");
            system.AddAxis("A", new(0, 0, 0), new(10, 0, 0));
            system.SaveXml(grid);
            Assert.Equal(0, Program.Main([command, input, "--grids", grid, "--export-grids", savedGrid, "-o", output]));
            Assert.True(File.Exists(output));
            Assert.Equal("A", Assert.Single(IfcGridSystem.LoadXml(savedGrid).Axes).Tag);
        }
        finally
        {
            File.Delete(input);
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Cli_Clash_RecognizesTheSecondModelAfterOptions()
    {
        string input = CreateSampleIfcFile();
        var dir = Directory.CreateTempSubdirectory("ifcio-clash-");
        try
        {
            string second = Path.Combine(dir.FullName, "second.ifc");
            string output = Path.Combine(dir.FullName, "report.json");
            Assert.Equal(1, Program.Main(["clash", input, "-o", output, second]));
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(input);
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Cli_Schedule_RejectsAnInvalidSimulationDateEvenWithoutSchedules()
    {
        string input = CreateSampleIfcFile();
        try
        {
            Assert.Equal(1, Program.Main(["schedule", input, "--sim-date", "not-a-date"]));
        }
        finally { File.Delete(input); }
    }

    [Theory]
    [InlineData("--grids")]
    [InlineData("--export-grids")]
    public void Cli_Info_RejectsUnsupportedGridOptions(string option)
    {
        string input = CreateSampleIfcFile();
        try
        {
            Assert.Equal(1, Program.Main(["info", input, option, "grid.xml"]));
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public void Cli_CsvRoundtrip_ExportsAndImports()
    {
        string samplePath = CreateSampleIfcFile();
        string tempCsv = Path.Combine(Path.GetTempPath(), $"cli_export_{Guid.NewGuid():N}.csv");
        string tempIfc = Path.Combine(Path.GetTempPath(), $"cli_import_{Guid.NewGuid():N}.ifc");
        try
        {
            int codeExp = Program.Main(["export-csv", samplePath, "-o", tempCsv, "-p", "FireRating", "LoadBearing"]);
            Assert.Equal(0, codeExp);
            Assert.True(File.Exists(tempCsv));

            int codeImp = Program.Main(["import-csv", samplePath, "--csv", tempCsv, "-o", tempIfc]);
            Assert.Equal(0, codeImp);
            Assert.True(File.Exists(tempIfc));
        }
        finally
        {
            if (File.Exists(samplePath)) File.Delete(samplePath);
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
            if (File.Exists(tempIfc)) File.Delete(tempIfc);
        }
    }
}
