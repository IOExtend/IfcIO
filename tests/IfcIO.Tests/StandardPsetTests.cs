using System.Text;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public class StandardPsetTests
{
    [Fact]
    public void StandardPsets_PopulateCorrectValues()
    {
        var wallPset = StandardPsets.WallCommon(
            reference: "WALL-EXT-01",
            loadBearing: true,
            isExternal: true,
            fireRating: "REI 60",
            thermalTransmittance: 0.28);

        Assert.Equal("Pset_WallCommon", wallPset.Name);
        Assert.Equal("WALL-EXT-01", wallPset.GetValue("Reference"));
        Assert.Equal(".T.", wallPset.GetValue("LoadBearing"));
        Assert.Equal(".T.", wallPset.GetValue("IsExternal"));
        Assert.Equal("REI 60", wallPset.GetValue("FireRating"));
        Assert.Equal("0.28", wallPset.GetValue("ThermalTransmittance"));

        var slabPset = StandardPsets.SlabCommon(
            reference: "SLAB-01",
            loadBearing: true,
            isExternal: false,
            pitchAngle: 0.0);

        Assert.Equal("Pset_SlabCommon", slabPset.Name);
        Assert.Equal("SLAB-01", slabPset.GetValue("Reference"));

        var colPset = StandardPsets.ColumnCommon(
            reference: "COL-01",
            loadBearing: true,
            slope: 90.0);

        Assert.Equal("Pset_ColumnCommon", colPset.Name);
        Assert.Equal("COL-01", colPset.GetValue("Reference"));

        var beamPset = StandardPsets.BeamCommon(
            reference: "BEAM-01",
            span: 6000.0);

        Assert.Equal("Pset_BeamCommon", beamPset.Name);
        Assert.Equal("6000", beamPset.GetValue("Span"));

        var windowPset = StandardPsets.WindowCommon(
            isExternal: true,
            overallHeight: 1500.0,
            overallWidth: 1200.0);

        Assert.Equal("Pset_WindowCommon", windowPset.Name);
        Assert.Equal(".T.", windowPset.GetValue("IsExternal"));

        var doorPset = StandardPsets.DoorCommon(
            isExternal: true,
            handicapAccessible: true,
            selfClosing: true);

        Assert.Equal("Pset_DoorCommon", doorPset.Name);
        Assert.Equal(".T.", doorPset.GetValue("HandicapAccessible"));
    }

    [Fact]
    public void StandardPsets_RoundtripThroughStepSerialization()
    {
        var model = IfcModel.Create("PsetRoundtrip");
        var wall = model.AddExtrudedRectangle("WallWithPset", 200, 3000, 2800);

        var pset = StandardPsets.WallCommon(
            reference: "W-100",
            loadBearing: true,
            isExternal: false,
            fireRating: "EI 45");

        wall.PropertySets.Add(pset);

        using var ms = new MemoryStream();
        model.Save(ms);
        ms.Position = 0;

        string step = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Pset_WallCommon", step);
        Assert.Contains("EI 45", step);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var reopened = IfcModel.Open(reader, "ReopenedWithPsets");

        var reopenedWall = Assert.Single(reopened.AllElements);
        var reopenedPset = Assert.Single(reopenedWall.PropertySets, p => p.Name == "Pset_WallCommon");
        Assert.Equal("W-100", reopenedPset.GetValue("Reference"));
        Assert.Equal("EI 45", reopenedPset.GetValue("FireRating"));
    }
}
