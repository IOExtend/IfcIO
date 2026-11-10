using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using IfcIO.Model;
using IfcIO.Schema;
using IfcIO.Validation.Ids;
using Xunit;

namespace IfcIO.Tests;

public class IdsValidatorTests
{
    private const string SampleIdsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ids xmlns="http://standards.buildingsmart.org/IDS">
          <info>
            <title>Architectural Compliance Audit</title>
            <version>1.0</version>
          </info>
          <specifications>
            <specification name="Wall Fire Rating Requirement">
              <applicability>
                <entity>
                  <name><simpleValue>IFCWALL</simpleValue></name>
                </entity>
              </applicability>
              <requirements>
                <property minOccurs="1">
                  <propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet>
                  <baseName><simpleValue>FireRating</simpleValue></baseName>
                </property>
              </requirements>
            </specification>
          </specifications>
        </ids>
        """;

    [Fact]
    public void Validate_CompliantModel_PassesWithZeroFailures()
    {
        var model = IfcModel.Create("Compliance Model", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wall = new IfcElement { Id = 1, Name = "Fire Wall", IfcType = "IfcWall", GlobalId = "0a1b2c3d4e5f6g7h8i9j0k" };
        var pset = new IfcPropertySet { Name = "Pset_WallCommon" };
        pset.Add("FireRating", 60);
        wall.PropertySets.Add(pset);

        storey.Elements.Add(wall);

        var doc = XDocument.Parse(SampleIdsXml);
        var report = model.ValidateIds(doc);

        Assert.True(report.IsPassed);
        Assert.Equal(0, report.FailedSpecifications);
        Assert.Equal(1, report.PassedSpecifications);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void Validate_NonCompliantModel_DetectsMissingProperty()
    {
        var model = IfcModel.Create("Audit Target", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        // Wall without Pset_WallCommon.FireRating
        var wall = new IfcElement { Id = 1, Name = "Non-Rated Partition", IfcType = "IfcWall", GlobalId = "1a2b3c4d5e6f7g8h9i0j1k" };
        var column = new IfcElement { Id = 2, Name = "Steel Column", IfcType = "IfcColumn", GlobalId = "2a2b3c4d5e6f7g8h9i0j2k" };

        storey.Elements.Add(wall);
        storey.Elements.Add(column);

        var doc = XDocument.Parse(SampleIdsXml);
        var report = model.ValidateIds(doc);

        Assert.False(report.IsPassed);
        Assert.Equal(1, report.FailedSpecifications);
        Assert.Single(report.Failures);

        var failure = report.Failures[0];
        Assert.Equal("Wall Fire Rating Requirement", failure.SpecificationName);
        Assert.Equal(1, failure.ElementId);
        Assert.Equal("Non-Rated Partition", failure.ElementName);
        Assert.Contains("FireRating", failure.Message);
    }

    [Fact]
    public void SaveBcf_ProducesValidBcfZipWithFailures()
    {
        var model = IfcModel.Create("Audit Target", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wall = new IfcElement { Id = 101, Name = "Defective Wall", IfcType = "IfcWall", GlobalId = "3a3b3c3d3e3f3g3h3i3j3k" };
        storey.Elements.Add(wall);

        var doc = XDocument.Parse(SampleIdsXml);
        var report = model.ValidateIds(doc);

        string tempPath = Path.Combine(Path.GetTempPath(), $"ids_audit_{Guid.NewGuid():N}.bcfzip");
        try
        {
            report.SaveBcf(tempPath);

            Assert.True(File.Exists(tempPath));
            using var zip = ZipFile.OpenRead(tempPath);
            Assert.Contains(zip.Entries, e => e.FullName == "bcf.version");
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("/markup.bcf", StringComparison.Ordinal));
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("/viewpoint.bcfv", StringComparison.Ordinal));

            var markupEntry = zip.Entries.First(e => e.FullName.EndsWith("/markup.bcf", StringComparison.Ordinal));
            using var stream = markupEntry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string markupXml = reader.ReadToEnd();
            Assert.Contains("Defective Wall", markupXml);
            Assert.Contains("3a3b3c3d3e3f3g3h3i3j3k", markupXml);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
