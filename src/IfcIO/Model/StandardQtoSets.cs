using System.Diagnostics.CodeAnalysis;

namespace IfcIO.Model;

/// <summary>
/// Strongly-typed factories and builders for standard buildingSMART Quantity Takeoff Sets (Qto).
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Conforms to buildingSMART IFC standard Qto naming conventions")]
public static class StandardQtoSets
{
    /// <summary>
    /// Creates a buildingSMART standard Qto_WallBaseQuantities quantity set.
    /// </summary>
    public static IfcQuantitySet WallBaseQuantities(
        double? length = null,
        double? width = null,
        double? height = null,
        double? grossFootprintArea = null,
        double? netFootprintArea = null,
        double? grossSideArea = null,
        double? netSideArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null)
    {
        var qset = new IfcQuantitySet("Qto_WallBaseQuantities");
        if (length.HasValue) qset.AddLength("Length", length.Value);
        if (width.HasValue) qset.AddLength("Width", width.Value);
        if (height.HasValue) qset.AddLength("Height", height.Value);
        if (grossFootprintArea.HasValue) qset.AddArea("GrossFootprintArea", grossFootprintArea.Value);
        if (netFootprintArea.HasValue) qset.AddArea("NetFootprintArea", netFootprintArea.Value);
        if (grossSideArea.HasValue) qset.AddArea("GrossSideArea", grossSideArea.Value);
        if (netSideArea.HasValue) qset.AddArea("NetSideArea", netSideArea.Value);
        if (grossVolume.HasValue) qset.AddVolume("GrossVolume", grossVolume.Value);
        if (netVolume.HasValue) qset.AddVolume("NetVolume", netVolume.Value);
        if (grossWeight.HasValue) qset.AddWeight("GrossWeight", grossWeight.Value);
        if (netWeight.HasValue) qset.AddWeight("NetWeight", netWeight.Value);
        return qset;
    }

    /// <summary>
    /// Alias for <see cref="WallBaseQuantities"/> matching exact buildingSMART identifier.
    /// </summary>
    public static IfcQuantitySet Qto_WallBaseQuantities(
        double? length = null,
        double? width = null,
        double? height = null,
        double? grossFootprintArea = null,
        double? netFootprintArea = null,
        double? grossSideArea = null,
        double? netSideArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null) =>
        WallBaseQuantities(length, width, height, grossFootprintArea, netFootprintArea, grossSideArea, netSideArea, grossVolume, netVolume, grossWeight, netWeight);

    /// <summary>
    /// Creates a buildingSMART standard Qto_SlabBaseQuantities quantity set.
    /// </summary>
    public static IfcQuantitySet SlabBaseQuantities(
        double? width = null,
        double? length = null,
        double? depth = null,
        double? perimeter = null,
        double? grossArea = null,
        double? netArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null)
    {
        var qset = new IfcQuantitySet("Qto_SlabBaseQuantities");
        if (width.HasValue) qset.AddLength("Width", width.Value);
        if (length.HasValue) qset.AddLength("Length", length.Value);
        if (depth.HasValue) qset.AddLength("Depth", depth.Value);
        if (perimeter.HasValue) qset.AddLength("Perimeter", perimeter.Value);
        if (grossArea.HasValue) qset.AddArea("GrossArea", grossArea.Value);
        if (netArea.HasValue) qset.AddArea("NetArea", netArea.Value);
        if (grossVolume.HasValue) qset.AddVolume("GrossVolume", grossVolume.Value);
        if (netVolume.HasValue) qset.AddVolume("NetVolume", netVolume.Value);
        if (grossWeight.HasValue) qset.AddWeight("GrossWeight", grossWeight.Value);
        if (netWeight.HasValue) qset.AddWeight("NetWeight", netWeight.Value);
        return qset;
    }

    /// <summary>
    /// Alias for <see cref="SlabBaseQuantities"/> matching exact buildingSMART identifier.
    /// </summary>
    public static IfcQuantitySet Qto_SlabBaseQuantities(
        double? width = null,
        double? length = null,
        double? depth = null,
        double? perimeter = null,
        double? grossArea = null,
        double? netArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null) =>
        SlabBaseQuantities(width, length, depth, perimeter, grossArea, netArea, grossVolume, netVolume, grossWeight, netWeight);

    /// <summary>
    /// Creates a buildingSMART standard Qto_ColumnBaseQuantities quantity set.
    /// </summary>
    public static IfcQuantitySet ColumnBaseQuantities(
        double? length = null,
        double? crossSectionArea = null,
        double? outerSurfaceArea = null,
        double? totalSurfaceArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null)
    {
        var qset = new IfcQuantitySet("Qto_ColumnBaseQuantities");
        if (length.HasValue) qset.AddLength("Length", length.Value);
        if (crossSectionArea.HasValue) qset.AddArea("CrossSectionArea", crossSectionArea.Value);
        if (outerSurfaceArea.HasValue) qset.AddArea("OuterSurfaceArea", outerSurfaceArea.Value);
        if (totalSurfaceArea.HasValue) qset.AddArea("TotalSurfaceArea", totalSurfaceArea.Value);
        if (grossVolume.HasValue) qset.AddVolume("GrossVolume", grossVolume.Value);
        if (netVolume.HasValue) qset.AddVolume("NetVolume", netVolume.Value);
        if (grossWeight.HasValue) qset.AddWeight("GrossWeight", grossWeight.Value);
        if (netWeight.HasValue) qset.AddWeight("NetWeight", netWeight.Value);
        return qset;
    }

    /// <summary>
    /// Alias for <see cref="ColumnBaseQuantities"/> matching exact buildingSMART identifier.
    /// </summary>
    public static IfcQuantitySet Qto_ColumnBaseQuantities(
        double? length = null,
        double? crossSectionArea = null,
        double? outerSurfaceArea = null,
        double? totalSurfaceArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null) =>
        ColumnBaseQuantities(length, crossSectionArea, outerSurfaceArea, totalSurfaceArea, grossVolume, netVolume, grossWeight, netWeight);

    /// <summary>
    /// Creates a buildingSMART standard Qto_BeamBaseQuantities quantity set.
    /// </summary>
    public static IfcQuantitySet BeamBaseQuantities(
        double? length = null,
        double? crossSectionArea = null,
        double? outerSurfaceArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null)
    {
        var qset = new IfcQuantitySet("Qto_BeamBaseQuantities");
        if (length.HasValue) qset.AddLength("Length", length.Value);
        if (crossSectionArea.HasValue) qset.AddArea("CrossSectionArea", crossSectionArea.Value);
        if (outerSurfaceArea.HasValue) qset.AddArea("OuterSurfaceArea", outerSurfaceArea.Value);
        if (grossVolume.HasValue) qset.AddVolume("GrossVolume", grossVolume.Value);
        if (netVolume.HasValue) qset.AddVolume("NetVolume", netVolume.Value);
        if (grossWeight.HasValue) qset.AddWeight("GrossWeight", grossWeight.Value);
        if (netWeight.HasValue) qset.AddWeight("NetWeight", netWeight.Value);
        return qset;
    }

    /// <summary>
    /// Alias for <see cref="BeamBaseQuantities"/> matching exact buildingSMART identifier.
    /// </summary>
    public static IfcQuantitySet Qto_BeamBaseQuantities(
        double? length = null,
        double? crossSectionArea = null,
        double? outerSurfaceArea = null,
        double? grossVolume = null,
        double? netVolume = null,
        double? grossWeight = null,
        double? netWeight = null) =>
        BeamBaseQuantities(length, crossSectionArea, outerSurfaceArea, grossVolume, netVolume, grossWeight, netWeight);

    /// <summary>
    /// Creates a buildingSMART standard Qto_WindowBaseQuantities quantity set.
    /// </summary>
    public static IfcQuantitySet WindowBaseQuantities(
        double? height = null,
        double? width = null,
        double? area = null,
        double? perimeter = null)
    {
        var qset = new IfcQuantitySet("Qto_WindowBaseQuantities");
        if (height.HasValue) qset.AddLength("Height", height.Value);
        if (width.HasValue) qset.AddLength("Width", width.Value);
        if (area.HasValue) qset.AddArea("Area", area.Value);
        if (perimeter.HasValue) qset.AddLength("Perimeter", perimeter.Value);
        return qset;
    }

    /// <summary>
    /// Alias for <see cref="WindowBaseQuantities"/> matching exact buildingSMART identifier.
    /// </summary>
    public static IfcQuantitySet Qto_WindowBaseQuantities(
        double? height = null,
        double? width = null,
        double? area = null,
        double? perimeter = null) =>
        WindowBaseQuantities(height, width, area, perimeter);

    /// <summary>
    /// Creates a buildingSMART standard Qto_DoorBaseQuantities quantity set.
    /// </summary>
    public static IfcQuantitySet DoorBaseQuantities(
        double? height = null,
        double? width = null,
        double? area = null,
        double? perimeter = null)
    {
        var qset = new IfcQuantitySet("Qto_DoorBaseQuantities");
        if (height.HasValue) qset.AddLength("Height", height.Value);
        if (width.HasValue) qset.AddLength("Width", width.Value);
        if (area.HasValue) qset.AddArea("Area", area.Value);
        if (perimeter.HasValue) qset.AddLength("Perimeter", perimeter.Value);
        return qset;
    }

    /// <summary>
    /// Alias for <see cref="DoorBaseQuantities"/> matching exact buildingSMART identifier.
    /// </summary>
    public static IfcQuantitySet Qto_DoorBaseQuantities(
        double? height = null,
        double? width = null,
        double? area = null,
        double? perimeter = null) =>
        DoorBaseQuantities(height, width, area, perimeter);
}
