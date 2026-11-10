using IfcIO.Geometry;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class MepPortTests
{
    [Fact]
    public void Ports_AddAndConnect_SetsBidirectionalReference()
    {
        var model = IfcModel.Create("MEP Ports Test");
        var duct = model.AddExtrudedRectangle("Duct_1", 200, 200, 1000);
        var fan = model.AddExtrudedRectangle("Fan_1", 300, 300, 500);

        var portDuctOut = IfcModel.AddDistributionPort(duct, "Port_Duct_Out", IfcFlowDirection.Source, new IfcVector3(0, 0, 1000));
        var portFanIn = IfcModel.AddDistributionPort(fan, "Port_Fan_In", IfcFlowDirection.Sink, new IfcVector3(0, 0, 0));

        IfcModel.ConnectPorts(portDuctOut, portFanIn);

        Assert.Equal(portFanIn, portDuctOut.ConnectedPort);
        Assert.Equal(portDuctOut, portFanIn.ConnectedPort);
        Assert.Equal(duct, portDuctOut.HostElement);
        Assert.Equal(fan, portFanIn.HostElement);
    }

    [Fact]
    public void TraceConnectedElements_TraversesNetworkChain()
    {
        var model = IfcModel.Create("MEP Network Tracing");
        var pump = model.AddExtrudedRectangle("Pump", 50, 50, 100);
        var pipe1 = model.AddExtrudedCircle("Pipe_1", 25, 200);
        var valve = model.AddExtrudedCircle("Valve", 30, 50);
        var pipe2 = model.AddExtrudedCircle("Pipe_2", 25, 200);

        var pPumpOut = IfcModel.AddDistributionPort(pump, "Pump_Out", IfcFlowDirection.Source);
        var pPipe1In = IfcModel.AddDistributionPort(pipe1, "Pipe1_In", IfcFlowDirection.Sink);
        var pPipe1Out = IfcModel.AddDistributionPort(pipe1, "Pipe1_Out", IfcFlowDirection.Source);
        var pValveIn = IfcModel.AddDistributionPort(valve, "Valve_In", IfcFlowDirection.Sink);
        var pValveOut = IfcModel.AddDistributionPort(valve, "Valve_Out", IfcFlowDirection.Source);
        var pPipe2In = IfcModel.AddDistributionPort(pipe2, "Pipe2_In", IfcFlowDirection.Sink);

        IfcModel.ConnectPorts(pPumpOut, pPipe1In);
        IfcModel.ConnectPorts(pPipe1Out, pValveIn);
        IfcModel.ConnectPorts(pValveOut, pPipe2In);

        var traced = IfcModel.TraceConnectedElements(pump);
        Assert.Equal(4, traced.Count);
        Assert.Contains(pump, traced);
        Assert.Contains(pipe1, traced);
        Assert.Contains(valve, traced);
        Assert.Contains(pipe2, traced);
    }

    [Fact]
    public void Ports_SaveAndLoad_RoundtripsSuccessfully()
    {
        var model = IfcModel.Create("MEP Port Roundtrip");
        var ductA = model.AddExtrudedRectangle("SupplyDuct", 250, 150, 2000);
        var ductB = model.AddExtrudedRectangle("BranchDuct", 250, 150, 1000);

        var pA = IfcModel.AddDistributionPort(ductA, "Port_A", IfcFlowDirection.Source, new IfcVector3(0, 0, 2000), IfcVector3.UnitZ, "HVAC");
        var pB = IfcModel.AddDistributionPort(ductB, "Port_B", IfcFlowDirection.Sink, new IfcVector3(0, 0, 0), -IfcVector3.UnitZ, "HVAC");
        IfcModel.ConnectPorts(pA, pB);

        string stepStr = model.ToStepString();
        Assert.Contains("IFCDISTRIBUTIONPORT", stepStr);
        Assert.Contains("IFCRELCONNECTSPORTTOELEMENT", stepStr);
        Assert.Contains("IFCRELCONNECTSPORTS", stepStr);

        using var reader = new StringReader(stepStr);
        var loaded = IfcModel.Open(reader);

        var lA = loaded.AllElements.FirstOrDefault(e => e.Name == "SupplyDuct");
        var lB = loaded.AllElements.FirstOrDefault(e => e.Name == "BranchDuct");

        Assert.NotNull(lA);
        Assert.NotNull(lB);
        Assert.Single(lA.Ports);
        Assert.Single(lB.Ports);

        var portA = lA.Ports[0];
        var portB = lB.Ports[0];

        Assert.Equal("Port_A", portA.Name);
        Assert.Equal(IfcFlowDirection.Source, portA.FlowDirection);
        Assert.Equal("Port_B", portB.Name);
        Assert.Equal(IfcFlowDirection.Sink, portB.FlowDirection);

        Assert.Equal(portB, portA.ConnectedPort);
        Assert.Equal(portA, portB.ConnectedPort);
    }
}
