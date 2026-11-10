using System.IO.Compression;
using IfcIO.Diff;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public sealed class DiffEngineTests
{
    [Fact]
    public void Compare_DetectsAddedDeletedAndModifiedElements()
    {
        var baseline = IfcModel.Create("Building Revision A", IfcSchema.Ifc4);
        var wall1 = baseline.AddExtrudedRectangle("Wall-101", 5.0, 0.3, 3.0, position: new IfcVector3(0, 0, 0), ifcType: "IFCWALL");
        wall1.GlobalId = "1111111111111111111111";
        wall1.GetOrCreatePropertySet("Pset_WallCommon").Add("FireRating", "60");

        var col1 = baseline.AddExtrudedRectangle("Column-201", 0.4, 0.4, 3.5, position: new IfcVector3(5.0, 0, 0), ifcType: "IFCCOLUMN");
        col1.GlobalId = "2222222222222222222222";

        var slab1 = baseline.AddExtrudedRectangle("Slab-301", 10.0, 10.0, 0.2, position: new IfcVector3(0, 0, 0), ifcType: "IFCSLAB");
        slab1.GlobalId = "3333333333333333333333";

        var revision = IfcModel.Create("Building Revision B", IfcSchema.Ifc4);

        // Wall 1: Property modified (FireRating changed from 60 to 90)
        var revWall = revision.AddExtrudedRectangle("Wall-101", 5.0, 0.3, 3.0, position: new IfcVector3(0, 0, 0), ifcType: "IFCWALL");
        revWall.GlobalId = "1111111111111111111111";
        revWall.GetOrCreatePropertySet("Pset_WallCommon").Add("FireRating", "90");

        // Column 1: Geometry modified (shifted along X by 1.0m to position (6.0, 0, 0))
        var revCol = revision.AddExtrudedRectangle("Column-201", 0.4, 0.4, 3.5, position: new IfcVector3(6.0, 0, 0), ifcType: "IFCCOLUMN");
        revCol.GlobalId = "2222222222222222222222";

        // Beam 1: Added!
        var revBeam = revision.AddExtrudedRectangle("Beam-401", 6.0, 0.3, 0.5, position: new IfcVector3(0, 0, 3.0), ifcType: "IFCBEAM");
        revBeam.GlobalId = "4444444444444444444444";

        // (Slab 1 is deleted in revision!)

        var report = baseline.Compare(revision, new IfcDiffOptions { GeometricTolerance = 0.005 });

        Assert.Single(report.Added);
        Assert.Equal("4444444444444444444444", report.Added[0].GlobalId);
        Assert.Equal("Beam-401", report.Added[0].Name);

        Assert.Single(report.Deleted);
        Assert.Equal("3333333333333333333333", report.Deleted[0].GlobalId);
        Assert.Equal("Slab-301", report.Deleted[0].Name);

        Assert.Equal(2, report.Modified.Count);
        Assert.Equal(4, report.TotalChangesCount);

        var wallDiff = report.Modified.First(m => m.GlobalId == "1111111111111111111111");
        Assert.Single(wallDiff.PropertyChanges);
        Assert.Equal("FireRating", wallDiff.PropertyChanges[0].PropertyName);
        Assert.Equal("60", wallDiff.PropertyChanges[0].OldValue);
        Assert.Equal("90", wallDiff.PropertyChanges[0].NewValue);

        var colDiff = report.Modified.First(m => m.GlobalId == "2222222222222222222222");
        Assert.True(colDiff.GeometryChanged);
        Assert.NotNull(colDiff.TranslationDelta);
        Assert.True(Math.Abs(colDiff.TranslationDelta.Value.X - 1.0) < 0.01);
    }

    [Fact]
    public void DiffReport_SaveJson_OutputsValidJson()
    {
        var baseline = IfcModel.Create("RevA", IfcSchema.Ifc4);
        var w = baseline.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0);
        w.GlobalId = "1111111111111111111111";

        var revision = IfcModel.Create("RevB", IfcSchema.Ifc4);
        var b = revision.AddExtrudedRectangle("Beam", 4.0, 0.3, 0.4);
        b.GlobalId = "2222222222222222222222";

        var report = baseline.Compare(revision);

        string tempJson = Path.Combine(Path.GetTempPath(), $"diff_{Guid.NewGuid():N}.json");
        try
        {
            report.SaveJson(tempJson);
            Assert.True(File.Exists(tempJson));
            string json = File.ReadAllText(tempJson);
            Assert.Contains("\"baseline\": \"RevA\"", json, StringComparison.Ordinal);
            Assert.Contains("\"revision\": \"RevB\"", json, StringComparison.Ordinal);
            Assert.Contains("\"added\"", json, StringComparison.Ordinal);
            Assert.Contains("\"deleted\"", json, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    [Fact]
    public void DiffReport_SaveBcf_CreatesValidBcfZip()
    {
        var baseline = IfcModel.Create("RevA", IfcSchema.Ifc4);
        var w = baseline.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0);
        w.GlobalId = "1111111111111111111111";

        var revision = IfcModel.Create("RevB", IfcSchema.Ifc4);
        var wRev = revision.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0);
        wRev.GlobalId = "1111111111111111111111";
        wRev.GetOrCreatePropertySet("Pset_WallCommon").Add("Status", "Approved");

        var report = baseline.Compare(revision);

        string tempBcf = Path.Combine(Path.GetTempPath(), $"diff_{Guid.NewGuid():N}.bcfzip");
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
            Assert.Contains("TopicType=\"Change\"", xml, StringComparison.Ordinal);
            Assert.Contains("[Modified] IFCWALL: Wall", xml, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempBcf)) File.Delete(tempBcf);
        }
    }

    [Fact]
    public void Cli_Diff_Execution_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ifc_cli_diff_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string ifcBase = Path.Combine(tempDir, "base.ifc");
            string ifcRev = Path.Combine(tempDir, "rev.ifc");
            string outJson = Path.Combine(tempDir, "diff.json");
            string outBcf = Path.Combine(tempDir, "diff.bcfzip");

            var mBase = IfcModel.Create("Base", IfcSchema.Ifc4);
            var w = mBase.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0);
            w.GlobalId = "1111111111111111111111";
            mBase.Save(ifcBase);

            var mRev = IfcModel.Create("Rev", IfcSchema.Ifc4);
            var w2 = mRev.AddExtrudedRectangle("Wall", 5.0, 0.3, 3.0, position: new IfcVector3(2, 0, 0));
            w2.GlobalId = "1111111111111111111111";
            mRev.Save(ifcRev);

            int exitCode = Cli.Program.Main([
                "diff",
                ifcBase,
                ifcRev,
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
