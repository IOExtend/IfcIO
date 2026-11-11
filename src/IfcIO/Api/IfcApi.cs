using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Model.Cost;
using IfcIO.Model.Sequence;
using IfcIO.Schema;

namespace IfcIO.Api;

/// <summary>
/// High-level API facade inspired by IfcOpenShell API (ifcopenshell.api), providing granular,
/// domain-driven operations for construction scheduling (4D), cost planning (5D), spatial hierarchy, and BIM modeling.
/// </summary>
public static class IfcApi
{
    /// <summary>4D Construction sequencing and work schedule operations.</summary>
    public static class Sequence
    {
        /// <summary>Creates and adds a new work schedule to the model.</summary>
        public static IfcWorkSchedule AddWorkSchedule(IfcModel model, string name, string? description = null)
        {
            ArgumentNullException.ThrowIfNull(model);
            return model.AddWorkSchedule(name, description);
        }

        /// <summary>Creates and adds a task to a work schedule.</summary>
        public static IfcTask AddTask(
            IfcWorkSchedule schedule,
            string name,
            string? identification = null,
            DateTime? start = null,
            DateTime? finish = null,
            IfcTaskType taskType = IfcTaskType.Construction)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            return schedule.AddTask(name, identification, start, finish, taskType);
        }

        /// <summary>Assigns one or more building elements to a construction or demolition task.</summary>
        public static void AssignProcess(IfcTask task, params IfcElement[] elements)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(elements);
            task.AssignElements(elements);
        }

        /// <summary>Creates a sequence dependency link between a predecessor and successor task.</summary>
        public static IfcTaskRelationship AddSequence(
            IfcTask predecessor,
            IfcTask successor,
            IfcSequenceType sequenceType = IfcSequenceType.FinishToStart,
            TimeSpan? lag = null)
        {
            ArgumentNullException.ThrowIfNull(predecessor);
            ArgumentNullException.ThrowIfNull(successor);
            return predecessor.AddSuccessor(successor, sequenceType, lag);
        }
    }

    /// <summary>5D Cost planning and budget estimation operations.</summary>
    public static class Cost
    {
        /// <summary>Creates and adds a cost schedule to the model.</summary>
        public static IfcCostSchedule AddCostSchedule(
            IfcModel model,
            string name,
            string currency = "USD",
            IfcCostScheduleType type = IfcCostScheduleType.CostPlan)
        {
            ArgumentNullException.ThrowIfNull(model);
            return model.AddCostSchedule(name, currency, type);
        }

        /// <summary>Creates and adds a cost item to a cost schedule.</summary>
        public static IfcCostItem AddCostItem(
            IfcCostSchedule schedule,
            string name,
            string? costCode = null,
            double? unitRate = null,
            double? quantity = null,
            string? unitBasis = null)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            return schedule.AddCostItem(name, costCode, unitRate, quantity, unitBasis);
        }

        /// <summary>Assigns one or more building elements to a cost item.</summary>
        public static void AssignCostControl(IfcCostItem costItem, params IfcElement[] elements)
        {
            ArgumentNullException.ThrowIfNull(costItem);
            ArgumentNullException.ThrowIfNull(elements);
            foreach (var el in elements) costItem.AssignElement(el);
        }

        /// <summary>Assigns a physical quantity to a cost item for automatic rate calculations.</summary>
        public static void AssignQuantity(IfcCostItem costItem, IfcPhysicalQuantity quantity)
        {
            ArgumentNullException.ThrowIfNull(costItem);
            ArgumentNullException.ThrowIfNull(quantity);
            costItem.AssignQuantity(quantity);
        }
    }

    /// <summary>Spatial organization, storeys, and zone operations.</summary>
    public static class Spatial
    {
        /// <summary>Creates and adds a spatial zone grouping one or more spaces.</summary>
        public static IfcZone AddZone(IfcModel model, string name, string? zoneType = null, params IfcSpace[] spaces)
        {
            ArgumentNullException.ThrowIfNull(model);
            return model.AddZone(name, zoneType, spaces);
        }

        /// <summary>Assigns building elements to a building storey.</summary>
        public static void AssignContainer(IfcBuildingStorey storey, params IfcElement[] elements)
        {
            ArgumentNullException.ThrowIfNull(storey);
            ArgumentNullException.ThrowIfNull(elements);
            foreach (var el in elements)
            {
                if (!storey.Elements.Contains(el))
                {
                    storey.Elements.Add(el);
                }
            }
        }
    }

    /// <summary>Root model and building element factory operations.</summary>
    public static class Root
    {
        /// <summary>Initializes a new IFC project model.</summary>
        public static IfcModel CreateProject(string projectName, IfcSchema schema = IfcSchema.Ifc4) =>
            IfcModel.Create(projectName, schema);

        /// <summary>Creates and registers a building storey.</summary>
        public static IfcBuildingStorey CreateStorey(IfcModel model, string name, double elevation = 0.0)
        {
            ArgumentNullException.ThrowIfNull(model);
            return model.AddStorey(name, elevation);
        }

        /// <summary>Creates and adds an IFCWALL element to a storey.</summary>
        public static IfcElement CreateWall(IfcBuildingStorey storey, string name, IfcMesh? mesh = null, IfcMaterial? material = null)
        {
            ArgumentNullException.ThrowIfNull(storey);
            var wall = new IfcElement
            {
                Id = storey.Elements.Count + 1,
                Name = name,
                IfcType = "IFCWALL",
                Mesh = mesh,
                Material = material
            };
            storey.Elements.Add(wall);
            return wall;
        }

        /// <summary>Creates and adds an IFCSLAB element to a storey.</summary>
        public static IfcElement CreateSlab(IfcBuildingStorey storey, string name, IfcMesh? mesh = null, IfcMaterial? material = null)
        {
            ArgumentNullException.ThrowIfNull(storey);
            var slab = new IfcElement
            {
                Id = storey.Elements.Count + 1,
                Name = name,
                IfcType = "IFCSLAB",
                Mesh = mesh,
                Material = material
            };
            storey.Elements.Add(slab);
            return slab;
        }

        /// <summary>Creates and adds an IFCCOLUMN element to a storey.</summary>
        public static IfcElement CreateColumn(IfcBuildingStorey storey, string name, IfcMesh? mesh = null, IfcMaterial? material = null)
        {
            ArgumentNullException.ThrowIfNull(storey);
            var column = new IfcElement
            {
                Id = storey.Elements.Count + 1,
                Name = name,
                IfcType = "IFCCOLUMN",
                Mesh = mesh,
                Material = material
            };
            storey.Elements.Add(column);
            return column;
        }

        /// <summary>Creates and adds an IFCBEAM element to a storey.</summary>
        public static IfcElement CreateBeam(IfcBuildingStorey storey, string name, IfcMesh? mesh = null, IfcMaterial? material = null)
        {
            ArgumentNullException.ThrowIfNull(storey);
            var beam = new IfcElement
            {
                Id = storey.Elements.Count + 1,
                Name = name,
                IfcType = "IFCBEAM",
                Mesh = mesh,
                Material = material
            };
            storey.Elements.Add(beam);
            return beam;
        }
    }
}
