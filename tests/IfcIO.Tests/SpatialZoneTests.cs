using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class SpatialZoneTests
{
    [Fact]
    public void Zone_CanAddSpacesAndManageProperties()
    {
        var model = IfcModel.Create("Residential Complex");
        var space1 = model.AddSpace("Living Room", "101");
        var space2 = model.AddSpace("Kitchen", "102");

        var zone = model.AddZone("Thermal Zone 1", "ThermalZone", [space1, space2]);

        Assert.Equal("Thermal Zone 1", zone.Name);
        Assert.Equal("ThermalZone", zone.ZoneType);
        Assert.Equal(2, zone.Spaces.Count);
        Assert.Contains(space1, zone.Spaces);
        Assert.Contains(space2, zone.Spaces);
    }

    [Theory]
    [InlineData(IfcSchema.Ifc4)]
    [InlineData(IfcSchema.Ifc2x3)]
    public void Zone_StepRoundtrip_PreservesZonesAndSpaces(IfcSchema schema)
    {
        var model = IfcModel.Create("Office Tower", schema);
        var spaceA = model.AddSpace("Server Room", "SR-01");
        var spaceB = model.AddSpace("Electrical Room", "ER-01");

        var fireZone = model.AddZone("Fire Compartment A", "FireSafety", [spaceA, spaceB]);

        using var ms = new MemoryStream();
        model.Save(ms);
        ms.Position = 0;

        var loaded = IfcModel.Open(ms);
        Assert.Single(loaded.Zones);

        var loadedZone = loaded.Zones[0];
        Assert.Equal("Fire Compartment A", loadedZone.Name);
        Assert.Equal("FireSafety", loadedZone.ZoneType);
        Assert.Equal(2, loadedZone.Spaces.Count);
        Assert.Contains(loadedZone.Spaces, s => s.Name == "Server Room");
        Assert.Contains(loadedZone.Spaces, s => s.Name == "Electrical Room");
    }
}
