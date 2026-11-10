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
