using IfcIO.Api;
using IfcIO.Model.Cost;
using IfcIO.Model.Sequence;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class IfcApiTests
{
    [Fact]
    public void IfcApi_CanBuildFullBimModelWithSequenceAndCost()
    {
        // 1. Root & Spatial
        var model = IfcApi.Root.CreateProject("API Modern Campus", IfcSchema.Ifc4);
        var storey1 = IfcApi.Root.CreateStorey(model, "Ground Level", 0.0);

        var wall1 = IfcApi.Root.CreateWall(storey1, "Perimeter Wall 1");
        var slab1 = IfcApi.Root.CreateSlab(storey1, "Ground Slab");
        var col1 = IfcApi.Root.CreateColumn(storey1, "Corner Column");
        var beam1 = IfcApi.Root.CreateBeam(storey1, "Transfer Beam");

        Assert.Equal(4, storey1.Elements.Count);

        // 2. Spatial Zones
        var space = model.AddSpace("Lobby", "101");
        var zone = IfcApi.Spatial.AddZone(model, "Ground Thermal Zone", "Thermal", space);
        Assert.Single(zone.Spaces);

        // 3. 4D Sequencing
        var schedule = IfcApi.Sequence.AddWorkSchedule(model, "Master Construction Schedule");
        var taskSlab = IfcApi.Sequence.AddTask(schedule, "Pour Slab", "1.1", new DateTime(2026, 1, 1), new DateTime(2026, 1, 10));
        var taskWalls = IfcApi.Sequence.AddTask(schedule, "Erect Walls", "1.2", new DateTime(2026, 1, 11), new DateTime(2026, 1, 20));

        IfcApi.Sequence.AssignProcess(taskSlab, slab1);
        IfcApi.Sequence.AssignProcess(taskWalls, wall1, col1, beam1);
        IfcApi.Sequence.AddSequence(taskSlab, taskWalls, IfcSequenceType.FinishToStart);

        Assert.Single(taskSlab.AssignedElements);
        Assert.Equal(3, taskWalls.AssignedElements.Count);
        Assert.Single(taskSlab.Successors);

        // 4. 5D Cost Planning
        var costPlan = IfcApi.Cost.AddCostSchedule(model, "Baseline Cost Plan", "USD", IfcCostScheduleType.Budget);
        var itemConcrete = IfcApi.Cost.AddCostItem(costPlan, "Concrete Structures", "03.00.00", unitRate: 350.0, quantity: 80.0, unitBasis: "m3");
        IfcApi.Cost.AssignCostControl(itemConcrete, slab1, wall1, col1, beam1);

        Assert.Equal(28000.0, itemConcrete.TotalCost);
        Assert.Equal(28000.0, costPlan.GetTotalCost());
        Assert.Equal(28000.0, model.CalculateTotalCost());
    }
}
