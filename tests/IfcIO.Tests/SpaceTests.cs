using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Validation;
using Xunit;

namespace IfcIO.Tests;

public sealed class SpaceTests
{
    [Fact]
    public void AddSpaceAndSerializeToStepRoundtrip()
    {
        var model = IfcModel.Create("Space Project");
        var storey = model.Storeys[0];

        // Add a host wall
        var wall = model.AddMesh(
            "Exterior Wall",
            [new(0, 0, 0), new(5, 0, 0), new(5, 0, 3), new(0, 0, 3)],
            [0, 1, 2, 0, 2, 3]);

        // Add an architectural space
        var space = model.AddSpace(
            name: "Office 101",
            spaceNumber: "101",
            longName: "Open Plan Office",
            grossFloorArea: 45.5,
            height: 3.2,
            storey: storey);

        space.Boundaries.Add(new IfcSpaceBoundary
        {
            Name = "North Wall Boundary",
            Description = "Enclosing wall boundary",
            PhysicalOrVirtual = IfcPhysicalOrVirtual.Physical,
            InternalOrExternal = IfcInternalOrExternal.Internal,
            RelatedBuildingElement = wall,
        });

        space.GetOrCreatePropertySet("Pset_SpaceCommon").Add("OccupancyType", "Office");

        string stepContent = model.ToStepString();
        Assert.Contains("IFCSPACE", stepContent);
        Assert.Contains("Office 101", stepContent);
        Assert.Contains("Open Plan Office", stepContent);
        Assert.Contains("IFCRELSPACEBOUNDARY", stepContent);

        // Deserialize back
        using var reader = new StringReader(stepContent);
        var loaded = IfcModel.Open(reader, "Space Project");

        Assert.Single(loaded.AllSpaces);
        var loadedSpace = loaded.AllSpaces.First();
        Assert.Equal("Office 101", loadedSpace.Name);
        Assert.Equal("Open Plan Office", loadedSpace.LongName);
        Assert.Equal("101", loadedSpace.Tag);

        Assert.Single(loadedSpace.Boundaries);
        var loadedBoundary = loadedSpace.Boundaries[0];
        Assert.Equal("North Wall Boundary", loadedBoundary.Name);
        Assert.NotNull(loadedBoundary.RelatedBuildingElement);
        Assert.Contains("Exterior Wall", loadedBoundary.RelatedBuildingElement.Name);

        var pset = loadedSpace.PropertySets.Find(p => p.Name == "Pset_SpaceCommon");
        Assert.NotNull(pset);
        Assert.Equal("Office", pset.GetValue("OccupancyType"));
    }

    [Fact]
    public void SpaceValidationCatchesNegativeAreaAndHeight()
    {
        var model = IfcModel.Create("Invalid Space Project");
        var space = model.AddSpace("Corridor", grossFloorArea: -10.0, height: -1.0);

        var result = model.Validate();
        Assert.False(result.IsValid);

        Assert.Contains(result.Issues, i => i.RuleId == "IFC_SPACE_AREA" && i.Severity == IfcIssueSeverity.Error);
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_SPACE_HEIGHT" && i.Severity == IfcIssueSeverity.Error);
    }
}
