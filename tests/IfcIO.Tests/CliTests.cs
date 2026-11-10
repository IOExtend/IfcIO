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
}
