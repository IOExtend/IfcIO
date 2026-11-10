using IfcIO.Core;
using IfcIO.Geometry;

namespace IfcIO.Model;

/// <summary>
/// Flow direction of a distribution port conforming to buildingSMART IFCFLOWDIRECTIONENUM.
/// </summary>
public enum IfcFlowDirection
{
    Source,
    Sink,
    SourceAndSink,
    NotDefined,
}

/// <summary>
/// A port or terminal on an MEP distribution element (duct, pipe, pump, fan, fitting, terminal)
/// through which fluids, air, or energy flow.
/// Conforms to buildingSMART standard IFCDISTRIBUTIONPORT.
/// </summary>
public sealed class IfcDistributionPort
{
    public int Id { get; set; }
    public string Name { get; set; } = "Port";
    public string? Description { get; set; }
    public string GlobalId { get; set; } = IfcGuid.NewGuid();
    public IfcFlowDirection FlowDirection { get; set; } = IfcFlowDirection.SourceAndSink;
    public string? SystemType { get; set; }
    public IfcVector3 Position { get; set; } = IfcVector3.Zero;
    public IfcVector3 Direction { get; set; } = IfcVector3.UnitZ;

    /// <summary>Host MEP element to which this port belongs (IFCRELCONNECTSPORTTOELEMENT).</summary>
    public IfcElement? HostElement { get; set; }

    /// <summary>Opposing connected port linking another MEP element (IFCRELCONNECTSPORTS).</summary>
    public IfcDistributionPort? ConnectedPort { get; set; }

    public IfcDistributionPort() { }

    public IfcDistributionPort(
        string name,
        IfcFlowDirection flowDirection = IfcFlowDirection.SourceAndSink,
        IfcVector3? position = null,
        IfcVector3? direction = null,
        string? systemType = null)
    {
        Name = name;
        FlowDirection = flowDirection;
        Position = position ?? IfcVector3.Zero;
        Direction = direction ?? IfcVector3.UnitZ;
        SystemType = systemType;
    }
}
