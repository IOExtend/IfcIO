using System.Globalization;

namespace IfcIO.Model.Sequence;

/// <summary>
/// Predefined classification for construction schedule tasks conforming to buildingSMART standard IFCTASKTYPEENUM.
/// </summary>
public enum IfcTaskType
{
    Construction,
    Demolition,
    Installation,
    Maintenance,
    Move,
    Operation,
    Renovation,
    Safety,
    UserDefined,
    NotDefined
}

/// <summary>
/// Predefined sequence dependency relationship between construction tasks conforming to buildingSMART IFCRELSEQUENCE.
/// </summary>
public enum IfcSequenceType
{
    StartToStart,
    StartToFinish,
    FinishToStart,
    FinishToFinish,
    UserDefined,
    NotDefined
}

/// <summary>
/// Temporal state of a building element during 4D construction schedule simulation.
/// </summary>
public enum IfcElementSimulationState
{
    /// <summary>Element construction has not yet started.</summary>
    Unstarted,

    /// <summary>Element is actively being constructed/installed at the target simulation date.</summary>
    InProgress,

    /// <summary>Element construction is fully complete.</summary>
    Constructed,

    /// <summary>Element was demolished as part of demolition tasks.</summary>
    Demolished
}

/// <summary>
/// Captures planned and actual scheduling time attributes for an IFC task.
/// Conforms to buildingSMART IFCTASKTIME (IFC4) and IFCSCHEDULETIMECONTROL (IFC2x3).
/// </summary>
public sealed class IfcTaskTime
{
    public string? Name { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ScheduleDuration { get; set; }
    public DateTime? EarlyStartDate { get; set; }
    public DateTime? EarlyFinishDate { get; set; }
    public DateTime? LateStartDate { get; set; }
    public DateTime? LateFinishDate { get; set; }
    public DateTime? PlannedStartDate { get; set; }
    public DateTime? PlannedFinishDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualFinishDate { get; set; }

    /// <summary>Task completion ratio ranging from 0.0 (unstarted) to 1.0 (completed).</summary>
    public double? Completion { get; set; }

    /// <summary>True if this task represents a zero-duration project milestone.</summary>
    public bool IsMilestone => Duration.HasValue && Duration.Value == TimeSpan.Zero;

    public IfcTaskTime() { }

    public IfcTaskTime(DateTime? plannedStart, DateTime? plannedFinish, double? completion = null)
    {
        PlannedStartDate = plannedStart;
        PlannedFinishDate = plannedFinish;
        if (plannedStart.HasValue && plannedFinish.HasValue && plannedFinish.Value >= plannedStart.Value)
        {
            Duration = plannedFinish.Value - plannedStart.Value;
        }
        Completion = completion;
    }
}

/// <summary>
/// Dependency sequence connection between two tasks conforming to buildingSMART IFCRELSEQUENCE.
/// </summary>
public sealed class IfcTaskRelationship
{
    public required IfcTask RelatingProcess { get; init; }
    public required IfcTask RelatedProcess { get; init; }
    public IfcSequenceType SequenceType { get; set; } = IfcSequenceType.FinishToStart;
    public TimeSpan? TimeLag { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// A scheduled construction or operational task within a work schedule conforming to buildingSMART IFCTASK.
/// </summary>
public sealed class IfcTask
{
    public string GlobalId { get; set; } = Core.IfcGuid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Work Breakdown Structure (WBS) identification code (e.g. "1.2.3").</summary>
    public string? Identification { get; set; }

    public string? Status { get; set; }
    public int? Priority { get; set; }
    public IfcTaskType TaskType { get; set; } = IfcTaskType.Construction;
    public IfcTaskTime? Time { get; set; }

    /// <summary>Hierarchical subtasks subordinate to this task.</summary>
    public List<IfcTask> SubTasks { get; } = [];

    /// <summary>Building elements assigned to be constructed or demolished by this task.</summary>
    public List<IfcElement> AssignedElements { get; } = [];

    /// <summary>Predecessor sequence dependencies leading into this task.</summary>
    public List<IfcTaskRelationship> Predecessors { get; } = [];

    /// <summary>Successor sequence dependencies leading out of this task.</summary>
    public List<IfcTaskRelationship> Successors { get; } = [];

    public IfcTask() { }

    public IfcTask(string name, string? identification = null, IfcTaskType taskType = IfcTaskType.Construction)
    {
        Name = name;
        Identification = identification;
        TaskType = taskType;
    }

    /// <summary>Adds a nested subtask under this task.</summary>
    public IfcTask AddSubTask(string name, string? identification = null, DateTime? start = null, DateTime? finish = null)
    {
        var subTask = new IfcTask(name, identification, TaskType)
        {
            Time = (start.HasValue || finish.HasValue) ? new IfcTaskTime(start, finish) : null
        };
        SubTasks.Add(subTask);
        return subTask;
    }

    /// <summary>Assigns a physical building element to this task.</summary>
    public void AssignElement(IfcElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!AssignedElements.Contains(element))
        {
            AssignedElements.Add(element);
        }
    }

    /// <summary>Assigns multiple building elements to this task.</summary>
    public void AssignElements(IEnumerable<IfcElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        foreach (var el in elements)
        {
            AssignElement(el);
        }
    }

    /// <summary>Links a successor task with an optional sequence type and lag.</summary>
    public IfcTaskRelationship AddSuccessor(IfcTask successor, IfcSequenceType sequenceType = IfcSequenceType.FinishToStart, TimeSpan? lag = null)
    {
        ArgumentNullException.ThrowIfNull(successor);
        var rel = new IfcTaskRelationship
        {
            RelatingProcess = this,
            RelatedProcess = successor,
            SequenceType = sequenceType,
            TimeLag = lag
        };
        Successors.Add(rel);
        successor.Predecessors.Add(rel);
        return rel;
    }
}

/// <summary>
/// A collection of scheduled tasks representing a construction or operational schedule.
/// Conforms to buildingSMART standard IFCWORKSCHEDULE.
/// </summary>
public sealed class IfcWorkSchedule
{
    public string GlobalId { get; set; } = Core.IfcGuid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public DateTime? StartTime { get; set; }
    public DateTime? FinishTime { get; set; }
    public TimeSpan? TotalFloat { get; set; }

    /// <summary>Root-level tasks in the schedule Work Breakdown Structure (WBS).</summary>
    public List<IfcTask> RootTasks { get; } = [];

    public IfcWorkSchedule() { }

    public IfcWorkSchedule(string name, string? description = null)
    {
        Name = name;
        Description = description;
    }

    /// <summary>Enumerates all tasks in the schedule across the hierarchical WBS tree.</summary>
    public IEnumerable<IfcTask> AllTasks
    {
        get
        {
            var stack = new Stack<IfcTask>(RootTasks);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                for (int i = current.SubTasks.Count - 1; i >= 0; i--)
                {
                    stack.Push(current.SubTasks[i]);
                }
            }
        }
    }

    /// <summary>Adds a new root task to the schedule.</summary>
    public IfcTask AddTask(string name, string? identification = null, DateTime? start = null, DateTime? finish = null, IfcTaskType taskType = IfcTaskType.Construction)
    {
        var task = new IfcTask(name, identification, taskType)
        {
            Time = (start.HasValue || finish.HasValue) ? new IfcTaskTime(start, finish) : null
        };
        RootTasks.Add(task);
        return task;
    }

    /// <summary>Finds a task by its WBS identification code or task name.</summary>
    public IfcTask? FindTask(string identificationOrName)
    {
        ArgumentNullException.ThrowIfNull(identificationOrName);
        return AllTasks.FirstOrDefault(t =>
            string.Equals(t.Identification, identificationOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, identificationOrName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all active tasks at the specified target simulation date.
    /// </summary>
    public IReadOnlyList<IfcTask> GetActiveTasksAt(DateTime date)
    {
        var result = new List<IfcTask>();
        foreach (var task in AllTasks)
        {
            var start = task.Time?.ActualStartDate ?? task.Time?.PlannedStartDate;
            var finish = task.Time?.ActualFinishDate ?? task.Time?.PlannedFinishDate;

            if (start.HasValue && finish.HasValue && date >= start.Value && date <= finish.Value)
            {
                result.Add(task);
            }
        }
        return result;
    }

    /// <summary>
    /// Determines the temporal 4D construction state of an element at the specified date.
    /// </summary>
    public IfcElementSimulationState GetElementStateAt(IfcElement element, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Find all tasks assigned to this element
        var assignedTasks = AllTasks.Where(t => t.AssignedElements.Contains(element)).ToList();
        if (assignedTasks.Count == 0)
        {
            return IfcElementSimulationState.Constructed;
        }

        bool anyActive = false;
        bool allFinished = true;
        bool anyDemolished = false;

        foreach (var task in assignedTasks)
        {
            var start = task.Time?.ActualStartDate ?? task.Time?.PlannedStartDate;
            var finish = task.Time?.ActualFinishDate ?? task.Time?.PlannedFinishDate;

            if (!start.HasValue || !finish.HasValue)
            {
                continue;
            }

            if (date < start.Value)
            {
                allFinished = false;
            }
            else if (date >= start.Value && date <= finish.Value)
            {
                anyActive = true;
                allFinished = false;
                if (task.TaskType == IfcTaskType.Demolition)
                {
                    anyDemolished = true;
                }
            }
            else if (date > finish.Value)
            {
                if (task.TaskType == IfcTaskType.Demolition)
                {
                    anyDemolished = true;
                }
            }
        }

        if (anyDemolished) return IfcElementSimulationState.Demolished;
        if (anyActive) return IfcElementSimulationState.InProgress;
        if (allFinished) return IfcElementSimulationState.Constructed;

        return IfcElementSimulationState.Unstarted;
    }

    /// <summary>
    /// Calculates the overall schedule progress percentage (0.0 to 100.0) at the specified date.
    /// </summary>
    public double GetProgressAt(DateTime date)
    {
        var tasksWithDates = AllTasks.Where(t =>
            (t.Time?.ActualStartDate ?? t.Time?.PlannedStartDate).HasValue &&
            (t.Time?.ActualFinishDate ?? t.Time?.PlannedFinishDate).HasValue).ToList();

        if (tasksWithDates.Count == 0) return 100.0;

        double completedWeight = 0;
        foreach (var task in tasksWithDates)
        {
            var start = (task.Time?.ActualStartDate ?? task.Time?.PlannedStartDate)!.Value;
            var finish = (task.Time?.ActualFinishDate ?? task.Time?.PlannedFinishDate)!.Value;

            if (date >= finish)
            {
                completedWeight += 1.0;
            }
            else if (date > start && finish > start)
            {
                double fraction = (date - start).TotalSeconds / (finish - start).TotalSeconds;
                completedWeight += Math.Clamp(fraction, 0.0, 1.0);
            }
        }

        return Math.Round((completedWeight / tasksWithDates.Count) * 100.0, 2);
    }
}
