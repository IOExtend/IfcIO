using IfcIO.Model;
using IfcIO.Model.Cost;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class CostTests
{
    [Fact]
    public void CostSchedule_CanCalculateRollupCosts()
    {
        var schedule = new IfcCostSchedule("Tower Cost Plan", "USD", IfcCostScheduleType.Budget);

        var concreteWork = schedule.AddCostItem("Concrete Superstructure", "03.00.00");
        var slabItem = concreteWork.AddSubItem("Cast-in-Place Floor Slabs", "03.31.00", unitRate: 250.0, quantity: 120.0, unitBasis: "m3");
        var columnItem = concreteWork.AddSubItem("Structural Columns", "03.32.00", unitRate: 400.0, quantity: 35.0, unitBasis: "m3");

        Assert.Equal(30000.0, slabItem.TotalCost); // 250 * 120
        Assert.Equal(14000.0, columnItem.TotalCost); // 400 * 35
        Assert.Equal(44000.0, concreteWork.TotalCost);
        Assert.Equal(44000.0, schedule.GetTotalCost());
    }

    [Fact]
    public void CostItem_CanAssignElementsAndCostValues()
    {
        var item = new IfcCostItem("Site Earthwork", "02.30.00");
        item.AddCostValue(15000.0, "USD", "Labor");
        item.AddCostValue(25000.0, "USD", "Equipment");
        item.AddCostValue(5000.0, "USD", "Material");

        var slab = new IfcElement { Id = 1, Name = "Foundation Slab" };
        item.AssignElement(slab);

        Assert.Equal(45000.0, item.TotalCost);
        Assert.Single(item.AssignedElements);
        Assert.Equal("Foundation Slab", item.AssignedElements[0].Name);
    }

    [Theory]
    [InlineData(IfcSchema.Ifc4)]
    [InlineData(IfcSchema.Ifc2x3)]
    public void CostSchedule_StepRoundtrip_PreservesCostItems(IfcSchema schema)
    {
        var model = IfcModel.Create("Budget Project", schema);
        var storey = model.AddStorey("Storey 1", 0.0);
        var slab = storey.AddSlab("Ground Slab");

        var costSchedule = model.AddCostSchedule("Master Budget", "USD", IfcCostScheduleType.Budget);
        var costItem = costSchedule.AddCostItem("Slab Package", "03.30.00", unitRate: 300.0, quantity: 50.0, unitBasis: "m3");
        costItem.AssignElement(slab);

        using var ms = new MemoryStream();
        model.Save(ms);
        ms.Position = 0;

        var loaded = IfcModel.Open(ms);
        Assert.Single(loaded.CostSchedules);
        var loadedCs = loaded.CostSchedules[0];
        Assert.Equal("Master Budget", loadedCs.Name);
        Assert.Single(loadedCs.RootCostItems);

        var loadedItem = loadedCs.RootCostItems[0];
        Assert.Equal("Slab Package", loadedItem.Name);
        Assert.Equal("03.30.00", loadedItem.CostCode);
        Assert.Single(loadedItem.AssignedElements);
        Assert.Equal(slab.Name, loadedItem.AssignedElements[0].Name);
    }
}
