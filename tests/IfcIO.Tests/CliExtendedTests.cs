using System.Text;
using IfcIO.Bcf;
using IfcIO.Cli;
using IfcIO.Model;
using IfcIO.Model.Cost;
using IfcIO.Model.Sequence;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class CliExtendedTests
{
    private static string CreateModelWithScheduleAndCost()
    {
        var model = IfcModel.Create("Scheduled Tower", IfcSchema.Ifc4);
        var storey = model.AddStorey("L1", 0.0);
        var wall = storey.AddWall("Exterior Wall");

        var schedule = model.AddWorkSchedule("Tower Schedule");
        schedule.StartTime = new DateTime(2026, 1, 1);
        schedule.FinishTime = new DateTime(2026, 6, 30);
        var task = schedule.AddTask("Construct Walls", "WBS-1.1", new DateTime(2026, 2, 1), new DateTime(2026, 3, 1));
        task.AssignElement(wall);

        var costSchedule = model.AddCostSchedule("Master Budget", "USD", IfcCostScheduleType.Budget);
        var costItem = costSchedule.AddCostItem("Wall Masonry", "04.20.00", unitRate: 150.0, quantity: 200.0, unitBasis: "m2");
        costItem.AssignElement(wall);

        string path = Path.Combine(Path.GetTempPath(), $"scheduled_{Guid.NewGuid():N}.ifc");
        model.Save(path);
        return path;
    }

    [Fact]
    public void Cli_Schedule_ExecutesSuccessfullyAndExportsCsv()
    {
        string ifcPath = CreateModelWithScheduleAndCost();
        string csvPath = Path.Combine(Path.GetTempPath(), $"schedule_{Guid.NewGuid():N}.csv");
        try
        {
            int code = Program.Main(["schedule", ifcPath, "--sim-date", "2026-02-15", "--export-csv", csvPath]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(csvPath));
            string csvContent = File.ReadAllText(csvPath);
            Assert.Contains("Tower Schedule", csvContent);
            Assert.Contains("Construct Walls", csvContent);
        }
        finally
        {
            if (File.Exists(ifcPath)) File.Delete(ifcPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void Cli_Cost_ExecutesSuccessfullyAndExportsCsv()
    {
        string ifcPath = CreateModelWithScheduleAndCost();
        string csvPath = Path.Combine(Path.GetTempPath(), $"cost_{Guid.NewGuid():N}.csv");
        try
        {
            int code = Program.Main(["cost", ifcPath, "--export-csv", csvPath]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(csvPath));
            string csvContent = File.ReadAllText(csvPath);
            Assert.Contains("Master Budget", csvContent);
            Assert.Contains("Wall Masonry", csvContent);
            Assert.Contains("30000", csvContent); // 150 * 200
        }
        finally
        {
            if (File.Exists(ifcPath)) File.Delete(ifcPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void Cli_Bcf_Read_ExecutesSuccessfully()
    {
        string bcfPath = Path.Combine(Path.GetTempPath(), $"issues_{Guid.NewGuid():N}.bcfzip");
        try
        {
            var archive = new BcfArchive("Coordination Check");
            var topic = archive.AddTopic("Clash at Column C3", "MEP duct interferes with structural column", "Clash", "High");
            topic.AddComment("Reviewing reroute options.", "Coordinator");
            archive.Save(bcfPath);

            int code = Program.Main(["bcf", "read", bcfPath]);
            Assert.Equal(0, code);
        }
        finally
        {
            if (File.Exists(bcfPath)) File.Delete(bcfPath);
        }
    }
}
