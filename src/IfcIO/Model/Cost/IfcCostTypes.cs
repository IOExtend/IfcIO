using System.Globalization;

namespace IfcIO.Model.Cost;

/// <summary>
/// Predefined classification for cost schedules conforming to buildingSMART standard IFCCOSTSCHEDULETYPEENUM.
/// </summary>
public enum IfcCostScheduleType
{
    Budget,
    CostPlan,
    Estimate,
    Tender,
    Actual,
    Valuation,
    Pricing,
    UserDefined,
    NotDefined
}

/// <summary>
/// A monetary or rate value associated with a cost item.
/// Conforms to buildingSMART standard IFCAPPLIEDVALUE.
/// </summary>
public sealed class IfcCostValue
{
    public string? Name { get; set; }

    /// <summary>Cost category (e.g. Material, Labor, Equipment, Subcontractor, Overhead, Profit).</summary>
    public string? Category { get; set; }

    /// <summary>The numeric cost amount.</summary>
    public double Amount { get; set; }

    /// <summary>Currency code (e.g. USD, EUR, GBP, JPY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Measurement unit basis (e.g. "m", "m2", "m3", "kg", "hour", "unit").</summary>
    public string? UnitBasis { get; set; }

    public IfcCostValue() { }

    public IfcCostValue(double amount, string currency = "USD", string? category = null, string? name = null)
    {
        Amount = amount;
        Currency = currency;
        Category = category;
        Name = name;
    }
}

/// <summary>
/// An individual cost line item or cost breakdown structure node within a cost schedule.
/// Conforms to buildingSMART standard IFCCOSTITEM.
/// </summary>
public sealed class IfcCostItem
{
    public string GlobalId { get; set; } = Core.IfcGuid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Cost Breakdown Structure (CBS) code or MasterFormat / UniFormat code (e.g. "03.30.00").</summary>
    public string? CostCode { get; set; }

    /// <summary>Unit rate per quantity unit (e.g. $150 per m3 of concrete).</summary>
    public double? UnitRate { get; set; }

    /// <summary>Total quantified volume, area, length, or count.</summary>
    public double? Quantity { get; set; }

    /// <summary>Measurement unit string (e.g. "m3", "m2", "m", "pcs", "kg").</summary>
    public string? UnitBasis { get; set; }

    /// <summary>Discrete applied values and cost components (Labor, Material, etc.).</summary>
    public List<IfcCostValue> CostValues { get; } = [];

    /// <summary>Hierarchical sub-cost items subordinate to this cost node.</summary>
    public List<IfcCostItem> SubCostItems { get; } = [];

    /// <summary>Building elements assigned to this cost item via IFCRELASSIGNSTOCONTROL.</summary>
    public List<IfcElement> AssignedElements { get; } = [];

    /// <summary>Physical quantities linked to this cost item for automated rate-based takeoffs.</summary>
    public List<IfcPhysicalQuantity> AssignedQuantities { get; } = [];

    public IfcCostItem() { }

    public IfcCostItem(string name, string? costCode = null, double? unitRate = null, double? quantity = null, string? unitBasis = null)
    {
        Name = name;
        CostCode = costCode;
        UnitRate = unitRate;
        Quantity = quantity;
        UnitBasis = unitBasis;
    }

    /// <summary>
    /// Gets the calculated total cost for this item, either explicitly via cost values, computed from UnitRate * Quantity, or aggregated from sub-items.
    /// </summary>
    public double TotalCost
    {
        get
        {
            if (SubCostItems.Count > 0)
            {
                return SubCostItems.Sum(item => item.TotalCost);
            }

            if (CostValues.Count > 0)
            {
                return CostValues.Sum(v => v.Amount);
            }

            if (UnitRate.HasValue && Quantity.HasValue)
            {
                return UnitRate.Value * Quantity.Value;
            }

            return 0.0;
        }
    }

    /// <summary>Adds a nested sub-cost item to this node.</summary>
    public IfcCostItem AddSubItem(string name, string? costCode = null, double? unitRate = null, double? quantity = null, string? unitBasis = null)
    {
        var subItem = new IfcCostItem(name, costCode, unitRate, quantity, unitBasis);
        SubCostItems.Add(subItem);
        return subItem;
    }

    /// <summary>Adds a discrete cost value breakdown component.</summary>
    public void AddCostValue(double amount, string currency = "USD", string? category = null, string? name = null)
    {
        CostValues.Add(new IfcCostValue(amount, currency, category, name));
    }

    /// <summary>Assigns an element to this cost item.</summary>
    public void AssignElement(IfcElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!AssignedElements.Contains(element))
        {
            AssignedElements.Add(element);
        }
    }

    /// <summary>Assigns a physical quantity to this cost item.</summary>
    public void AssignQuantity(IfcPhysicalQuantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        if (!AssignedQuantities.Contains(quantity))
        {
            AssignedQuantities.Add(quantity);
        }
    }
}

/// <summary>
/// A collection of cost items representing a budget, estimate, or cost plan.
/// Conforms to buildingSMART standard IFCCOSTSCHEDULE.
/// </summary>
public sealed class IfcCostSchedule
{
    public string GlobalId { get; set; } = Core.IfcGuid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IfcCostScheduleType ScheduleType { get; set; } = IfcCostScheduleType.CostPlan;
    public string? Status { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime UpdateDate { get; set; } = DateTime.UtcNow;

    /// <summary>Top-level root cost items in the Cost Breakdown Structure (CBS).</summary>
    public List<IfcCostItem> RootCostItems { get; } = [];

    public IfcCostSchedule() { }

    public IfcCostSchedule(string name, string currency = "USD", IfcCostScheduleType type = IfcCostScheduleType.CostPlan)
    {
        Name = name;
        Currency = currency;
        ScheduleType = type;
    }

    /// <summary>Enumerates all cost items across the hierarchical CBS tree.</summary>
    public IEnumerable<IfcCostItem> AllCostItems
    {
        get
        {
            var stack = new Stack<IfcCostItem>(RootCostItems);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                for (int i = current.SubCostItems.Count - 1; i >= 0; i--)
                {
                    stack.Push(current.SubCostItems[i]);
                }
            }
        }
    }

    /// <summary>Adds a top-level cost item to the schedule.</summary>
    public IfcCostItem AddCostItem(string name, string? costCode = null, double? unitRate = null, double? quantity = null, string? unitBasis = null)
    {
        var item = new IfcCostItem(name, costCode, unitRate, quantity, unitBasis);
        RootCostItems.Add(item);
        return item;
    }

    /// <summary>Calculates the grand total cost of all root cost items in the schedule.</summary>
    public double GetTotalCost() => RootCostItems.Sum(item => item.TotalCost);

    /// <summary>Finds a cost item by its CBS cost code or name.</summary>
    public IfcCostItem? FindItem(string codeOrName)
    {
        ArgumentNullException.ThrowIfNull(codeOrName);
        return AllCostItems.FirstOrDefault(item =>
            string.Equals(item.CostCode, codeOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, codeOrName, StringComparison.OrdinalIgnoreCase));
    }
}
