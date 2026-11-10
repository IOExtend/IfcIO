using IfcIO.Model;
using IfcIO.Schema;
using IfcIO.Validation;
using Xunit;

namespace IfcIO.Tests;

public sealed class SystemTests
{
    [Fact]
    public void AddSystemAndGroupSerializeToStepRoundtrip()
    {
        var model = IfcModel.Create("MEP System Project", IfcSchema.Ifc2x3);

        var duct1 = model.AddMesh("Supply Duct 1", [new(0, 0, 0), new(2, 0, 0), new(2, 0.5, 0)], [0, 1, 2]);
        var duct2 = model.AddMesh("Supply Duct 2", [new(2, 0, 0), new(4, 0, 0), new(4, 0.5, 0)], [0, 1, 2]);

        var hvacSystem = model.AddSystem("Supply Air AHU-1", "HVAC", [duct1, duct2]);
        hvacSystem.Description = "Primary air distribution loop";

        var maintenanceGroup = model.AddGroup("Q1 Inspection Group", "Maintenance cohort", [duct1]);

        string stepContent = model.ToStepString();
        Assert.Contains("IFCSYSTEM", stepContent);
        Assert.Contains("Supply Air AHU-1", stepContent);
        Assert.Contains("IFCGROUP", stepContent);
        Assert.Contains("Q1 Inspection Group", stepContent);
        Assert.Contains("IFCRELASSIGNSTOGROUP", stepContent);

        // Deserialize back
        using var reader = new StringReader(stepContent);
        var loaded = IfcModel.Open(reader, "MEP System Project");

        Assert.Single(loaded.Systems);
        var loadedSys = loaded.Systems[0];
        Assert.Equal("Supply Air AHU-1", loadedSys.Name);
        Assert.Equal("HVAC", loadedSys.SystemType);
        Assert.Equal("Primary air distribution loop", loadedSys.Description);
        Assert.Equal(2, loadedSys.Elements.Count);

        Assert.Single(loaded.Groups);
        var loadedGroup = loaded.Groups[0];
        Assert.Equal("Q1 Inspection Group", loadedGroup.Name);
        Assert.Single(loadedGroup.Elements);
    }

    [Fact]
    public void SystemValidationCatchesEmptySystemAndMissingName()
    {
        var model = IfcModel.Create("Validation Project");
        var emptySys = model.AddSystem("Exhaust System", "HVAC"); // No elements assigned

        var result = model.Validate();
        Assert.Contains(result.Issues, i => i.RuleId == "IFC_SYSTEM_EMPTY" && i.Severity == IfcIssueSeverity.Warning);

        var unnamedSys = model.AddSystem("", "Plumbing");
        var result2 = model.Validate();
        Assert.Contains(result2.Issues, i => i.RuleId == "IFC_SYSTEM_NAME" && i.Severity == IfcIssueSeverity.Error);
    }
}
