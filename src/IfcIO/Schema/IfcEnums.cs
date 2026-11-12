namespace IfcIO.Schema;

/// <summary>
/// Supported IFC schema versions.
/// </summary>
public enum IfcSchema
{
    /// <summary>IFC2X3 Coordination View V2.0 (broadest industry support).</summary>
    Ifc2x3,

    /// <summary>IFC4 Reference View / Design Transfer View.</summary>
    Ifc4,
}

/// <summary>
/// Length units supported in IFC project unit assignment.
/// </summary>
public enum IfcLengthUnit
{
    /// <summary>Millimetres (IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)).</summary>
    Millimetre,

    /// <summary>Metres (IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)).</summary>
    Metre,

    /// <summary>Feet (IFCCONVERSIONBASEDUNIT of METRE with factor 0.3048).</summary>
    Foot,

    /// <summary>Inches (IFCCONVERSIONBASEDUNIT of METRE with factor 0.0254).</summary>
    Inch,
}

internal static class IfcLengthUnitExtensions
{
    /// <summary>Length of one model unit in metres.</summary>
    internal static double MetresPerUnit(this IfcLengthUnit unit) => unit switch
    {
        IfcLengthUnit.Millimetre => 0.001,
        IfcLengthUnit.Metre => 1.0,
        IfcLengthUnit.Foot => 0.3048,
        IfcLengthUnit.Inch => 0.0254,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown length unit."),
    };
}

/// <summary>
/// Plane angle units supported in IFC project unit assignment.
/// </summary>
public enum IfcPlaneAngleUnit
{
    /// <summary>Degrees (conversion based unit with factor 0.017453292519943278 rad).</summary>
    Degree,

    /// <summary>Radians (SI base unit).</summary>
    Radian,
}
