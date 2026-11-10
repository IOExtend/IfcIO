using IfcIO.Cli;
using IfcIO.Geometry;
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
