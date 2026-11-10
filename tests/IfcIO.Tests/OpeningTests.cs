using System.Text;
using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public class OpeningTests
{
    [Fact]
    public void AddOpening_WithFilling_CreatesRelationships()
    {
        var model = IfcModel.Create("OpeningTest");
        var wall = model.AddExtrudedRectangle(
            name: "HostWall",
            xDim: 300.0,
            yDim: 6000.0,
            depth: 3000.0);

        var openingSolid = new IfcExtrudedAreaSolid(
            new IfcRectangleProfileDef(300.0, 1200.0),
            depth: 1500.0,
            position: new IfcVector3(0, 2000, 900));

        var windowSolid = new IfcExtrudedAreaSolid(
            new IfcRectangleProfileDef(100.0, 1180.0),
            depth: 1480.0,
            position: new IfcVector3(0, 2010, 910));

        var window = new IfcElement
        {
            Id = 55,
            Name = "Window1",
            IfcType = "IFCWINDOW",
            Solid = windowSolid,
        };

        var opening = model.AddOpening(wall, "WallOpening1", solid: openingSolid, filling: window);

        Assert.Single(wall.Openings);
        Assert.Same(opening, wall.Openings[0]);
        Assert.Same(window, opening.Filling);
    }

    [Fact]
    public void OpeningAndFilling_RoundtripThroughStepSerialization()
    {
        var model = IfcModel.Create("OpeningRoundtrip");
        var wall = model.AddExtrudedRectangle(
            name: "ExteriorWall",
            xDim: 300.0,
            yDim: 4000.0,
            depth: 2700.0);

        var openingSolid = new IfcExtrudedAreaSolid(
            new IfcRectangleProfileDef(300.0, 1000.0),
            depth: 2100.0);

        var doorSolid = new IfcExtrudedAreaSolid(
            new IfcRectangleProfileDef(80.0, 980.0),
            depth: 2080.0);

        var door = new IfcElement
        {
            Id = 88,
            Name = "EntranceDoor",
            IfcType = "IFCDOOR",
            Solid = doorSolid,
        };

        model.AddOpening(wall, "DoorOpening", solid: openingSolid, filling: door);

        using var ms = new MemoryStream();
        model.Save(ms);
        ms.Position = 0;

        string step = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("IFCOPENINGELEMENT", step);
        Assert.Contains("IFCRELVOIDSELEMENT", step);
        Assert.Contains("IFCRELFILLSELEMENT", step);
        Assert.Contains("IFCDOOR", step);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var reopened = IfcModel.Open(reader, "ReopenedWithOpenings");

        var reopenedWall = Assert.Single(reopened.AllElements, e => e.Name == "ExteriorWall");
        Assert.Single(reopenedWall.Openings);

        var reopenedOpening = reopenedWall.Openings[0];
        Assert.Equal("DoorOpening", reopenedOpening.Name);
        Assert.NotNull(reopenedOpening.Solid);
        Assert.NotNull(reopenedOpening.Filling);
        Assert.Equal("EntranceDoor", reopenedOpening.Filling.Name);
        Assert.Equal("IFCDOOR", reopenedOpening.Filling.IfcType);
    }
}
