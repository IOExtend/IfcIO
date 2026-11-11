using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Model.Sequence;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class SequenceTests
{
    [Fact]
    public void WorkSchedule_CanCreateHierarchyAndSequences()
    {
        var schedule = new IfcWorkSchedule("Construction Master Schedule");

        var phase1 = schedule.AddTask("Substructure", "1.0", new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        var taskExcavation = phase1.AddSubTask("Bulk Excavation", "1.1", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var taskFoundations = phase1.AddSubTask("Reinforced Footings", "1.2", new DateTime(2026, 2, 1), new DateTime(2026, 3, 15));

        var rel = taskExcavation.AddSuccessor(taskFoundations, IfcSequenceType.FinishToStart, TimeSpan.FromDays(1));

        Assert.Single(schedule.RootTasks);
        Assert.Equal(3, schedule.AllTasks.Count());
        Assert.Equal("1.0", phase1.Identification);
        Assert.Equal("1.1", taskExcavation.Identification);
        Assert.Equal("1.2", taskFoundations.Identification);
        Assert.Single(taskExcavation.Successors);
        Assert.Single(taskFoundations.Predecessors);
        Assert.Equal(IfcSequenceType.FinishToStart, rel.SequenceType);
        Assert.Equal(TimeSpan.FromDays(1), rel.TimeLag);
    }

    [Fact]
    public void WorkSchedule_SimulationState_EvaluatesCorrectly()
    {
        var schedule = new IfcWorkSchedule("Simulation Schedule");
        var wallElement = new IfcElement { Id = 101, Name = "Bearing Wall 1" };

        var task = schedule.AddTask(
            "Erect Bearing Wall",
            "WBS-1",
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 10),
            IfcTaskType.Construction);
        task.AssignElement(wallElement);

        // Before start date
        var stateBefore = schedule.GetElementStateAt(wallElement, new DateTime(2026, 5, 20));
        Assert.Equal(IfcElementSimulationState.Unstarted, stateBefore);

        // During construction
        var stateDuring = schedule.GetElementStateAt(wallElement, new DateTime(2026, 6, 5));
        Assert.Equal(IfcElementSimulationState.InProgress, stateDuring);

        // After completion
        var stateAfter = schedule.GetElementStateAt(wallElement, new DateTime(2026, 6, 15));
        Assert.Equal(IfcElementSimulationState.Constructed, stateAfter);

        // Active tasks
        var active = schedule.GetActiveTasksAt(new DateTime(2026, 6, 5));
        Assert.Single(active);
        Assert.Equal(task, active[0]);

        // Progress percentage
        double progBefore = schedule.GetProgressAt(new DateTime(2026, 5, 1));
        double progMid = schedule.GetProgressAt(new DateTime(2026, 6, 5)); // halfway
        double progAfter = schedule.GetProgressAt(new DateTime(2026, 7, 1));

        Assert.Equal(0.0, progBefore);
        Assert.InRange(progMid, 40.0, 60.0);
        Assert.Equal(100.0, progAfter);
    }

    [Theory]
    [InlineData(IfcSchema.Ifc4)]
    [InlineData(IfcSchema.Ifc2x3)]
    public void WorkSchedule_StepRoundtrip_PreservesScheduleAndTasks(IfcSchema schema)
    {
        var model = IfcModel.Create("Hospital Project", schema);
        var storey = model.AddStorey("Level 1", 0.0);
        var wall = storey.AddWall("Exterior Wall East");

        var schedule = model.AddWorkSchedule("Hospital Schedule", "Main Construction Plan");
        schedule.StartTime = new DateTime(2026, 1, 1);
        schedule.FinishTime = new DateTime(2026, 12, 31);

        var taskA = schedule.AddTask("Foundation Work", "1.0", new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        var taskB = schedule.AddTask("Wall Framing", "2.0", new DateTime(2026, 4, 1), new DateTime(2026, 6, 30));
        taskB.AssignElement(wall);
        taskA.AddSuccessor(taskB, IfcSequenceType.FinishToStart);

        using var ms = new MemoryStream();
        model.Save(ms);
        ms.Position = 0;

        var loaded = IfcModel.Open(ms);
        Assert.Single(loaded.WorkSchedules);
        var loadedSchedule = loaded.WorkSchedules[0];
        Assert.Equal("Hospital Schedule", loadedSchedule.Name);
        Assert.Equal(2, loadedSchedule.RootTasks.Count);

        var loadedTaskA = loadedSchedule.FindTask("1.0");
        var loadedTaskB = loadedSchedule.FindTask("2.0");

        Assert.NotNull(loadedTaskA);
        Assert.NotNull(loadedTaskB);
        Assert.Equal("Foundation Work", loadedTaskA.Name);
        Assert.Equal("Wall Framing", loadedTaskB.Name);
        Assert.Single(loadedTaskB.AssignedElements);
        Assert.Equal(wall.Name, loadedTaskB.AssignedElements[0].Name);
        Assert.Single(loadedTaskA.Successors);
        Assert.Equal(loadedTaskB, loadedTaskA.Successors[0].RelatedProcess);
    }
}
