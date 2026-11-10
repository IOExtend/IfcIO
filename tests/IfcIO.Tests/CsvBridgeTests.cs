using System.Text;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class CsvBridgeTests
{
    [Fact]
    public void ExportCsv_WritesHeaderAndPropertyValues()
    {
        var model = IfcModel.Create("CSV Export Test", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wall = new IfcElement
        {
            Id = 1,
            Name = "External Wall",
            IfcType = "IfcWall",
            GlobalId = "0X_WALL_GUID_12345"
        };
        var pset = new IfcPropertySet { Name = "Pset_WallCommon" };
        pset.Add("FireRating", 120);
        pset.Add("LoadBearing", true);
        wall.PropertySets.Add(pset);

        storey.Elements.Add(wall);

        using var sw = new StringWriter();
        model.ExportCsv(sw, ["FireRating", "LoadBearing"], ";");

        string csvOutput = sw.ToString();
        var lines = csvOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal("Id;GlobalId;Type;Name;Storey;FireRating;LoadBearing", lines[0]);
        Assert.Equal("1;0X_WALL_GUID_12345;IfcWall;External Wall;Level 1;120;.T.", lines[1]);
    }

    [Fact]
    public void ImportCsv_UpdatesPropertiesOnMatchingElements()
    {
        var model = IfcModel.Create("CSV Import Test", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var wall = new IfcElement
        {
            Id = 42,
            Name = "Original Name",
            IfcType = "IfcWall",
            GlobalId = "GUID_TARGET_001"
        };
        var pset = new IfcPropertySet { Name = "Pset_WallCommon" };
        pset.Add("FireRating", 30);
        wall.PropertySets.Add(pset);

        storey.Elements.Add(wall);

        string csvData = """
            Id;GlobalId;Type;Name;Storey;Pset_WallCommon.FireRating;Pset_WallCommon.AcousticRating
            42;GUID_TARGET_001;IfcWall;Original Name;Level 1;90;45dB
            """;

        using var sr = new StringReader(csvData);
        var (elementsUpdated, propertiesUpdated) = model.ImportCsv(sr, ";");

        Assert.Equal(1, elementsUpdated);
        Assert.Equal(2, propertiesUpdated);

        Assert.Equal("90", pset.GetValue("FireRating"));
        Assert.Equal("45dB", pset.GetValue("AcousticRating"));
    }

    [Fact]
    public void ExportAndImport_RoundtripsPropertyModifications()
    {
        var model = IfcModel.Create("Roundtrip Test", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1", 0.0);

        var col = new IfcElement
        {
            Id = 10,
            Name = "Column 10",
            IfcType = "IfcColumn",
            GlobalId = "COL_GUID_10"
        };
        var pset = new IfcPropertySet { Name = "CustomProps" };
        pset.Add("Manufacturer", "Acme Precast");
        col.PropertySets.Add(pset);
        storey.Elements.Add(col);

        // Export to CSV
        var sb = new StringBuilder();
        using (var sw = new StringWriter(sb))
        {
            model.ExportCsv(sw, ["CustomProps.Manufacturer"], ",");
        }

        // Edit CSV
        string modifiedCsv = sb.ToString().Replace("Acme Precast", "Global Steel Ltd");

        // Import back
        using (var sr = new StringReader(modifiedCsv))
        {
            var (elems, props) = model.ImportCsv(sr, ",");
            Assert.Equal(1, elems);
            Assert.Equal(1, props);
        }

        Assert.Equal("Global Steel Ltd", pset.GetValue("Manufacturer"));
    }
}
