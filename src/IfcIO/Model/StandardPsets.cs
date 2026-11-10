using System.Diagnostics.CodeAnalysis;

namespace IfcIO.Model;

/// <summary>
/// Strongly-typed factories and extensions for standard buildingSMART Property Sets (Psets).
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Conforms to buildingSMART IFC standard Pset naming conventions")]
public static class StandardPsets
{
    /// <summary>
    /// Creates a buildingSMART standard Pset_WallCommon property set.
    /// </summary>
    public static IfcPropertySet WallCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? thermalTransmittance = null,
        string? acousticRating = null,
        bool? combustible = null,
        string? surfaceSpreadOfFlame = null)
    {
        var pset = new IfcPropertySet("Pset_WallCommon");
        if (reference is not null) pset.Add("Reference", reference);
        if (loadBearing.HasValue) pset.Add("LoadBearing", loadBearing.Value);
        if (isExternal.HasValue) pset.Add("IsExternal", isExternal.Value);
        if (fireRating is not null) pset.Add("FireRating", fireRating);
        if (thermalTransmittance.HasValue) pset.Add("ThermalTransmittance", thermalTransmittance.Value);
        if (acousticRating is not null) pset.Add("AcousticRating", acousticRating);
        if (combustible.HasValue) pset.Add("Combustible", combustible.Value);
        if (surfaceSpreadOfFlame is not null) pset.Add("SurfaceSpreadOfFlame", surfaceSpreadOfFlame);
        return pset;
    }

    /// <summary>
    /// Alias for <see cref="WallCommon"/> matching exact buildingSMART schema identifier.
    /// </summary>
    public static IfcPropertySet Pset_WallCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? thermalTransmittance = null,
        string? acousticRating = null,
        bool? combustible = null,
        string? surfaceSpreadOfFlame = null) =>
        WallCommon(reference, loadBearing, isExternal, fireRating, thermalTransmittance, acousticRating, combustible, surfaceSpreadOfFlame);

    /// <summary>
    /// Creates a buildingSMART standard Pset_SlabCommon property set.
    /// </summary>
    public static IfcPropertySet SlabCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? thermalTransmittance = null,
        string? acousticRating = null,
        double? pitchAngle = null)
    {
        var pset = new IfcPropertySet("Pset_SlabCommon");
        if (reference is not null) pset.Add("Reference", reference);
        if (loadBearing.HasValue) pset.Add("LoadBearing", loadBearing.Value);
        if (isExternal.HasValue) pset.Add("IsExternal", isExternal.Value);
        if (fireRating is not null) pset.Add("FireRating", fireRating);
        if (thermalTransmittance.HasValue) pset.Add("ThermalTransmittance", thermalTransmittance.Value);
        if (acousticRating is not null) pset.Add("AcousticRating", acousticRating);
        if (pitchAngle.HasValue) pset.Add("PitchAngle", pitchAngle.Value);
        return pset;
    }

    /// <summary>
    /// Alias for <see cref="SlabCommon"/> matching exact buildingSMART schema identifier.
    /// </summary>
    public static IfcPropertySet Pset_SlabCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? thermalTransmittance = null,
        string? acousticRating = null,
        double? pitchAngle = null) =>
        SlabCommon(reference, loadBearing, isExternal, fireRating, thermalTransmittance, acousticRating, pitchAngle);

    /// <summary>
    /// Creates a buildingSMART standard Pset_ColumnCommon property set.
    /// </summary>
    public static IfcPropertySet ColumnCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? slope = null)
    {
        var pset = new IfcPropertySet("Pset_ColumnCommon");
        if (reference is not null) pset.Add("Reference", reference);
        if (loadBearing.HasValue) pset.Add("LoadBearing", loadBearing.Value);
        if (isExternal.HasValue) pset.Add("IsExternal", isExternal.Value);
        if (fireRating is not null) pset.Add("FireRating", fireRating);
        if (slope.HasValue) pset.Add("Slope", slope.Value);
        return pset;
    }

    /// <summary>
    /// Alias for <see cref="ColumnCommon"/> matching exact buildingSMART schema identifier.
    /// </summary>
    public static IfcPropertySet Pset_ColumnCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? slope = null) =>
        ColumnCommon(reference, loadBearing, isExternal, fireRating, slope);

    /// <summary>
    /// Creates a buildingSMART standard Pset_BeamCommon property set.
    /// </summary>
    public static IfcPropertySet BeamCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? span = null,
        double? roll = null)
    {
        var pset = new IfcPropertySet("Pset_BeamCommon");
        if (reference is not null) pset.Add("Reference", reference);
        if (loadBearing.HasValue) pset.Add("LoadBearing", loadBearing.Value);
        if (isExternal.HasValue) pset.Add("IsExternal", isExternal.Value);
        if (fireRating is not null) pset.Add("FireRating", fireRating);
        if (span.HasValue) pset.Add("Span", span.Value);
        if (roll.HasValue) pset.Add("Roll", roll.Value);
        return pset;
    }

    /// <summary>
    /// Alias for <see cref="BeamCommon"/> matching exact buildingSMART schema identifier.
    /// </summary>
    public static IfcPropertySet Pset_BeamCommon(
        string? reference = null,
        bool? loadBearing = null,
        bool? isExternal = null,
        string? fireRating = null,
        double? span = null,
        double? roll = null) =>
        BeamCommon(reference, loadBearing, isExternal, fireRating, span, roll);

    /// <summary>
    /// Creates a buildingSMART standard Pset_WindowCommon property set.
    /// </summary>
    public static IfcPropertySet WindowCommon(
        string? reference = null,
        bool? isExternal = null,
        string? fireRating = null,
        string? acousticRating = null,
        string? securityRating = null,
        double? overallHeight = null,
        double? overallWidth = null,
        double? thermalTransmittance = null)
    {
        var pset = new IfcPropertySet("Pset_WindowCommon");
        if (reference is not null) pset.Add("Reference", reference);
        if (isExternal.HasValue) pset.Add("IsExternal", isExternal.Value);
        if (fireRating is not null) pset.Add("FireRating", fireRating);
        if (acousticRating is not null) pset.Add("AcousticRating", acousticRating);
        if (securityRating is not null) pset.Add("SecurityRating", securityRating);
        if (overallHeight.HasValue) pset.Add("OverallHeight", overallHeight.Value);
        if (overallWidth.HasValue) pset.Add("OverallWidth", overallWidth.Value);
        if (thermalTransmittance.HasValue) pset.Add("ThermalTransmittance", thermalTransmittance.Value);
        return pset;
    }

    /// <summary>
    /// Alias for <see cref="WindowCommon"/> matching exact buildingSMART schema identifier.
    /// </summary>
    public static IfcPropertySet Pset_WindowCommon(
        string? reference = null,
        bool? isExternal = null,
        string? fireRating = null,
        string? acousticRating = null,
        string? securityRating = null,
        double? overallHeight = null,
        double? overallWidth = null,
        double? thermalTransmittance = null) =>
        WindowCommon(reference, isExternal, fireRating, acousticRating, securityRating, overallHeight, overallWidth, thermalTransmittance);

    /// <summary>
    /// Creates a buildingSMART standard Pset_DoorCommon property set.
    /// </summary>
    public static IfcPropertySet DoorCommon(
        string? reference = null,
        bool? isExternal = null,
        string? fireRating = null,
        string? acousticRating = null,
        string? securityRating = null,
        double? overallHeight = null,
        double? overallWidth = null,
        bool? handicapAccessible = null,
        bool? selfClosing = null)
    {
        var pset = new IfcPropertySet("Pset_DoorCommon");
        if (reference is not null) pset.Add("Reference", reference);
        if (isExternal.HasValue) pset.Add("IsExternal", isExternal.Value);
        if (fireRating is not null) pset.Add("FireRating", fireRating);
        if (acousticRating is not null) pset.Add("AcousticRating", acousticRating);
        if (securityRating is not null) pset.Add("SecurityRating", securityRating);
        if (overallHeight.HasValue) pset.Add("OverallHeight", overallHeight.Value);
        if (overallWidth.HasValue) pset.Add("OverallWidth", overallWidth.Value);
        if (handicapAccessible.HasValue) pset.Add("HandicapAccessible", handicapAccessible.Value);
        if (selfClosing.HasValue) pset.Add("SelfClosing", selfClosing.Value);
        return pset;
    }

    /// <summary>
    /// Alias for <see cref="DoorCommon"/> matching exact buildingSMART schema identifier.
    /// </summary>
    public static IfcPropertySet Pset_DoorCommon(
        string? reference = null,
        bool? isExternal = null,
        string? fireRating = null,
        string? acousticRating = null,
        string? securityRating = null,
        double? overallHeight = null,
        double? overallWidth = null,
        bool? handicapAccessible = null,
        bool? selfClosing = null) =>
        DoorCommon(reference, isExternal, fireRating, acousticRating, securityRating, overallHeight, overallWidth, handicapAccessible, selfClosing);
}
