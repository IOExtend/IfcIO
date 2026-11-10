using System.IO.Compression;
using IfcIO.Clash;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public sealed class ClashDetectorTests
{
    [Fact]
    public void DetectClashes_CrossModel_IdentifiesInterpenetratingElements()
    {
        var modelA = IfcModel.Create("Architectural Model", IfcSchema.Ifc4);
        var wall = modelA.AddExtrudedRectangle(
            name: "Exterior Wall",
            xDim: 6.0,
            yDim: 0.4,
            depth: 3.0,
            position: new IfcVector3(0, 0, 0),
            ifcType: "IFCWALL"
        );
        wall.GlobalId = "1111111111111111111111";

        var modelB = IfcModel.Create("HVAC Model", IfcSchema.Ifc4);
        var duct = modelB.AddExtrudedRectangle(
            name: "Supply Duct",
            xDim: 0.6,
            yDim: 2.0,
            depth: 0.5,
            position: new IfcVector3(2.5, -0.8, 1.2),
            ifcType: "IFCFLOWSEGMENT"
        );
        duct.GlobalId = "2222222222222222222222";

        var report = modelA.DetectClashes(modelB, new IfcClashOptions { Tolerance = 0.001 });

        Assert.Equal(1, report.TotalClashesCount);
        var clash = report.Clashes[0];
        Assert.Equal("Exterior Wall", clash.ElementA.Name);
        Assert.Equal("Supply Duct", clash.ElementB.Name);
        Assert.True(clash.Distance < 0, "Hard clash distance should be negative penetration depth.");
        Assert.True(Math.Abs(clash.ClashPoint.X - 2.8) < 1.0);
    }

    [Fact]
    public void DetectClashes_ReturnsEmpty_ForDisjointElements()
    {
        var modelA = IfcModel.Create("Arch", IfcSchema.Ifc4);
        modelA.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0, position: new IfcVector3(0, 0, 0));

        var modelB = IfcModel.Create("MEP", IfcSchema.Ifc4);
        modelB.AddExtrudedRectangle("Duct", 0.5, 0.5, 0.5, position: new IfcVector3(50, 50, 10));

        var report = modelA.DetectClashes(modelB);

        Assert.Equal(0, report.TotalClashesCount);
        Assert.Empty(report.Clashes);
    }

    [Fact]
    public void DetectClashes_SelfClash_FindsCollisionsWithinSameModel()
    {
        var model = IfcModel.Create("Structural Model", IfcSchema.Ifc4);
        model.AddExtrudedRectangle("Beam 1", 6.0, 0.3, 0.5, position: new IfcVector3(0, 0, 3.0));
        model.AddExtrudedRectangle("Column 1", 0.4, 0.4, 4.0, position: new IfcVector3(2.0, 0, 0));

        var report = model.DetectClashes();

        Assert.Equal(1, report.TotalClashesCount);
        Assert.Equal("Beam 1", report.Clashes[0].ElementA.Name);
        Assert.Equal("Column 1", report.Clashes[0].ElementB.Name);
    }

    [Fact]
    public void ClashReport_SaveJson_OutputsValidJson()
    {
        var modelA = IfcModel.Create("Arch", IfcSchema.Ifc4);
        modelA.AddExtrudedRectangle("Wall", 4.0, 0.3, 3.0, position: new IfcVector3(0, 0, 0));

        var modelB = IfcModel.Create("Str", IfcSchema.Ifc4);
        modelB.AddExtrudedRectangle("Column", 0.5, 0.5, 4.0, position: new IfcVector3(2.0, -0.1, 0));

        var report = modelA.DetectClashes(modelB);
        Assert.True(report.TotalClashesCount > 0);

        string tempJson = Path.Combine(Path.GetTempPath(), $"clashes_{Guid.NewGuid():N}.json");
        try
        {
            report.SaveJson(tempJson);
            Assert.True(File.Exists(tempJson));
            string json = File.ReadAllText(tempJson);
            Assert.Contains("\"totalClashes\": 1", json, StringComparison.Ordinal);
            Assert.Contains("\"elementA\"", json, StringComparison.Ordinal);
            Assert.Contains("\"elementB\"", json, StringComparison.Ordinal);
            Assert.Contains("\"clashPoint\"", json, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    [Fact]
    public void ClashReport_SaveBcf_CreatesValidBcfZip()
    {
        var modelA = IfcModel.Create("Arch", IfcSchema.Ifc4);
        var wall = modelA.AddExtrudedRectangle("Wall", 4.0, 0.3, 3.0, position: new IfcVector3(0, 0, 0));
        wall.GlobalId = "03X9Wk_0f8pQ123456789A";

        var modelB = IfcModel.Create("Str", IfcSchema.Ifc4);
        var col = modelB.AddExtrudedRectangle("Column", 0.5, 0.5, 4.0, position: new IfcVector3(2.0, -0.1, 0));
        col.GlobalId = "15Y9Wk_0f8pQ123456789B";

        var report = modelA.DetectClashes(modelB);

        string tempBcf = Path.Combine(Path.GetTempPath(), $"clashes_{Guid.NewGuid():N}.bcfzip");
        try
        {
            report.SaveBcf(tempBcf);
            Assert.True(File.Exists(tempBcf));

            using var zip = ZipFile.OpenRead(tempBcf);
            Assert.NotNull(zip.GetEntry("bcf.version"));

            var markup = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(markup);

            using var reader = new StreamReader(markup.Open());
            string xml = reader.ReadToEnd();
            Assert.Contains("TopicType=\"Clash\"", xml, StringComparison.Ordinal);
            Assert.Contains("[Clash] Wall vs Column", xml, StringComparison.Ordinal);

            var viewpoint = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("viewpoint.bcfv", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(viewpoint);
        }
        finally
        {
            if (File.Exists(tempBcf)) File.Delete(tempBcf);
        }
    }

    [Fact]
    public void Cli_Clash_Execution_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ifc_cli_clash_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string ifcA = Path.Combine(tempDir, "arch.ifc");
            string ifcB = Path.Combine(tempDir, "mep.ifc");
            string outJson = Path.Combine(tempDir, "clashes.json");
            string outBcf = Path.Combine(tempDir, "clashes.bcfzip");

            var modelA = IfcModel.Create("Arch", IfcSchema.Ifc4);
            modelA.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0, position: new IfcVector3(0, 0, 0));
            modelA.Save(ifcA);

            var modelB = IfcModel.Create("MEP", IfcSchema.Ifc4);
            modelB.AddExtrudedRectangle("Duct", 0.4, 2.0, 0.4, position: new IfcVector3(2.0, -0.5, 1.0));
            modelB.Save(ifcB);

            int exitCode = Cli.Program.Main([
                "clash",
                ifcA,
                ifcB,
                "-o", outJson,
                "--bcf", outBcf,
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outJson));
            Assert.True(File.Exists(outBcf));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
