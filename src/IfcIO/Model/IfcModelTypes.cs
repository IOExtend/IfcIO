using IfcIO.Geometry;

namespace IfcIO.Model;

/// <summary>
/// A surface material and color definition.
/// </summary>
public sealed class IfcMaterial
{
    public required string Name { get; set; }
    public double R { get; set; } = 0.8;
    public double G { get; set; } = 0.8;
    public double B { get; set; } = 0.8;
    public double Alpha { get; set; } = 1.0;

    public static IfcMaterial Default { get; } = new() { Name = "Default Material" };
}

/// <summary>
/// A single typed property value.
/// </summary>
public sealed class IfcPropertySingleValue
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Type { get; set; } = "IfcText";

    public static IfcPropertySingleValue FromText(string name, string value) =>
        new() { Name = name, Value = value, Type = "IfcText" };

    public static IfcPropertySingleValue FromInt(string name, int value) =>
        new() { Name = name, Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Type = "IfcInteger" };

    public static IfcPropertySingleValue FromReal(string name, double value) =>
        new() { Name = name, Value = value.ToString("G15", System.Globalization.CultureInfo.InvariantCulture), Type = "IfcReal" };

    public static IfcPropertySingleValue FromBool(string name, bool value) =>
        new() { Name = name, Value = value ? ".T." : ".F.", Type = "IfcBoolean" };
}

/// <summary>
/// A collection of properties grouped into a named property set.
/// </summary>
public sealed class IfcPropertySet
{
    public string Name { get; set; } = string.Empty;
    public List<IfcPropertySingleValue> Properties { get; } = [];

    public IfcPropertySet() { }

    public IfcPropertySet(string name)
    {
        Name = name;
    }

    public void Add(string name, string value) => Properties.Add(IfcPropertySingleValue.FromText(name, value));
    public void Add(string name, int value) => Properties.Add(IfcPropertySingleValue.FromInt(name, value));
    public void Add(string name, double value) => Properties.Add(IfcPropertySingleValue.FromReal(name, value));
    public void Add(string name, bool value) => Properties.Add(IfcPropertySingleValue.FromBool(name, value));

    public string? GetValue(string propName) =>
        Properties.Find(p => p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase))?.Value;
}

/// <summary>
/// Abstract base class for physical BIM simple quantities (Length, Area, Volume, Count, Weight).
/// Conforms to buildingSMART IFC standard IFCPHYSICALSIMPLEQUANTITY.
/// </summary>
public abstract class IfcPhysicalQuantity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Gets the numeric magnitude of this physical simple quantity.</summary>
    public double Value => this switch
    {
        IfcQuantityLength l => l.LengthValue,
        IfcQuantityArea a => a.AreaValue,
        IfcQuantityVolume v => v.VolumeValue,
        IfcQuantityCount c => c.CountValue,
        IfcQuantityWeight w => w.WeightValue,
        _ => 0.0,
    };
}

/// <summary>
/// A physical length quantity (e.g. Height, Width, Length, Perimeter).
/// Conforms to buildingSMART standard IFCQUANTITYLENGTH.
/// </summary>
public sealed class IfcQuantityLength : IfcPhysicalQuantity
{
    public double LengthValue { get; set; }

    public IfcQuantityLength() { }

    public IfcQuantityLength(string name, double value, string? description = null)
    {
        Name = name;
        LengthValue = value;
        Description = description;
    }
}

/// <summary>
/// A physical area quantity (e.g. GrossArea, NetArea, GrossFootprintArea, NetSideArea).
/// Conforms to buildingSMART standard IFCQUANTITYAREA.
/// </summary>
public sealed class IfcQuantityArea : IfcPhysicalQuantity
{
    public double AreaValue { get; set; }

    public IfcQuantityArea() { }

    public IfcQuantityArea(string name, double value, string? description = null)
    {
        Name = name;
        AreaValue = value;
        Description = description;
    }
}

/// <summary>
/// A physical volume quantity (e.g. GrossVolume, NetVolume).
/// Conforms to buildingSMART standard IFCQUANTITYVOLUME.
/// </summary>
public sealed class IfcQuantityVolume : IfcPhysicalQuantity
{
    public double VolumeValue { get; set; }

    public IfcQuantityVolume() { }

    public IfcQuantityVolume(string name, double value, string? description = null)
    {
        Name = name;
        VolumeValue = value;
        Description = description;
    }
}

/// <summary>
/// A physical count quantity (e.g. number of items).
/// Conforms to buildingSMART standard IFCQUANTITYCOUNT.
/// </summary>
public sealed class IfcQuantityCount : IfcPhysicalQuantity
{
    public double CountValue { get; set; }

    public IfcQuantityCount() { }

    public IfcQuantityCount(string name, double value, string? description = null)
    {
        Name = name;
        CountValue = value;
        Description = description;
    }
}

/// <summary>
/// A physical weight quantity (e.g. GrossWeight, NetWeight).
/// Conforms to buildingSMART standard IFCQUANTITYWEIGHT.
/// </summary>
public sealed class IfcQuantityWeight : IfcPhysicalQuantity
{
    public double WeightValue { get; set; }

    public IfcQuantityWeight() { }

    public IfcQuantityWeight(string name, double value, string? description = null)
    {
        Name = name;
        WeightValue = value;
        Description = description;
    }
}

/// <summary>
/// A collection of physical simple quantities associated with an element or element type.
/// Conforms to buildingSMART IFC standard IFCELEMENTQUANTITY.
/// </summary>
public sealed class IfcQuantitySet
{
    public string Name { get; set; } = "BaseQuantities";
    public string? Description { get; set; }
    public string? MethodOfMeasurement { get; set; }
    public List<IfcPhysicalQuantity> Quantities { get; } = [];

    public IfcQuantitySet() { }

    public IfcQuantitySet(string name, string? description = null, string? methodOfMeasurement = null)
    {
        Name = name;
        Description = description;
        MethodOfMeasurement = methodOfMeasurement;
    }

    public void AddLength(string name, double value, string? desc = null) => Quantities.Add(new IfcQuantityLength(name, value, desc));
    public void AddArea(string name, double value, string? desc = null) => Quantities.Add(new IfcQuantityArea(name, value, desc));
    public void AddVolume(string name, double value, string? desc = null) => Quantities.Add(new IfcQuantityVolume(name, value, desc));
    public void AddCount(string name, double value, string? desc = null) => Quantities.Add(new IfcQuantityCount(name, value, desc));
    public void AddWeight(string name, double value, string? desc = null) => Quantities.Add(new IfcQuantityWeight(name, value, desc));

    public double? GetLength(string name) =>
        Quantities.OfType<IfcQuantityLength>().FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.LengthValue;

    public double? GetArea(string name) =>
        Quantities.OfType<IfcQuantityArea>().FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.AreaValue;

    public double? GetVolume(string name) =>
        Quantities.OfType<IfcQuantityVolume>().FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.VolumeValue;

    public double? GetCount(string name) =>
        Quantities.OfType<IfcQuantityCount>().FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.CountValue;

    public double? GetWeight(string name) =>
        Quantities.OfType<IfcQuantityWeight>().FirstOrDefault(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.WeightValue;
}

/// <summary>
/// Represents a building storey / level.
/// </summary>
public sealed class IfcBuildingStorey
{
    public required string Name { get; set; }
    public double Elevation { get; set; }
    public string? GlobalId { get; set; }
    public List<IfcElement> Elements { get; } = [];
    public List<IfcSpace> Spaces { get; } = [];
}

/// <summary>
/// Represents a type definition object (e.g. IFCWALLTYPE, IFCBEAMTYPE, IFCCOLUMNTYPE, IFCSLABTYPE).
/// </summary>
public sealed class IfcTypeProduct
{
    public required string Name { get; set; }
    public string IfcType { get; set; } = "IFCBUILDINGELEMENTPROXYTYPE";
    public string? GlobalId { get; set; }
    public string? Description { get; set; }
    public string? Tag { get; set; }
    public List<IfcPropertySet> PropertySets { get; } = [];

    public IfcPropertySet GetOrCreatePropertySet(string name)
    {
        var existing = PropertySets.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var pset = new IfcPropertySet(name);
        PropertySets.Add(pset);
        return pset;
    }
}

/// <summary>
/// Base class for physical BIM elements (Wall, Slab, Column, Beam, Proxy, etc.).
/// </summary>
public class IfcElement
{
    public required int Id { get; init; }
    public required string Name { get; set; }
    public string IfcType { get; set; } = "IFCBUILDINGELEMENTPROXY";
    public string? GlobalId { get; set; }
    public string? Tag { get; set; }

    public IfcTypeProduct? TypeProduct { get; set; }
    public IfcMesh? Mesh { get; set; }
    public IfcExtrudedAreaSolid? Solid { get; set; }
    public IfcRevolvedAreaSolid? RevolvedSolid { get; set; }
    public IfcBooleanResult? BooleanResult { get; set; }
    public IfcMaterial? Material { get; set; }
    public IfcMaterialLayerSetUsage? MaterialLayerSet { get; set; }
    public IfcMaterialProfileSetUsage? MaterialProfileSet { get; set; }
    public List<IfcDistributionPort> Ports { get; } = [];
    public List<IfcPropertySet> PropertySets { get; } = [];
    public List<IfcQuantitySet> QuantitySets { get; } = [];
    public List<IfcOpeningElement> Openings { get; } = [];

    /// <summary>
    /// Gets the polygon mesh representation of the element, either explicitly assigned or generated on demand from its parametric solid or CSG boolean operation.
    /// </summary>
    public IfcMesh? GetEffectiveMesh() => Mesh ?? BooleanResult?.ToMesh() ?? Solid?.ToMesh() ?? RevolvedSolid?.ToMesh();

    public IfcPropertySet GetOrCreatePropertySet(string name)
    {
        var existing = PropertySets.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var pset = new IfcPropertySet(name);
        PropertySets.Add(pset);
        return pset;
    }

    public IfcQuantitySet GetOrCreateQuantitySet(string name)
    {
        var existing = QuantitySets.Find(q => q.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var qset = new IfcQuantitySet(name);
        QuantitySets.Add(qset);
        return qset;
    }

    /// <summary>
    /// Evaluates whether this element matches an IfcOpenShell-style selector query string.
    /// </summary>
    public bool Matches(string selectorQuery, IfcModel? model = null) =>
        Query.IfcSelector.Parse(selectorQuery).Matches(this, model);
}

/// <summary>
/// Represents a void or opening in a building element (such as a wall or slab cutout),
/// conforming to buildingSMART standard IFCOPENINGELEMENT.
/// </summary>
public sealed class IfcOpeningElement : IfcElement
{
    /// <summary>
    /// An optional filling element (e.g. Window or Door) installed inside this opening.
    /// Maps to buildingSMART IFCRELFILLSELEMENT.
    /// </summary>
    public IfcElement? Filling { get; set; }

    public IfcOpeningElement()
    {
        IfcType = "IFCOPENINGELEMENT";
    }
}

/// <summary>
/// Physical or virtual space boundary definition.
/// </summary>
public enum IfcPhysicalOrVirtual
{
    Physical,
    Virtual,
    NotDefined
}

/// <summary>
/// Internal or external space boundary classification.
/// </summary>
public enum IfcInternalOrExternal
{
    Internal,
    External,
    NotDefined
}

/// <summary>
/// Connects a space to an enclosing physical element (wall, slab, column) or virtual boundary.
/// Conforms to buildingSMART standard IFCRELSPACEBOUNDARY.
/// </summary>
public sealed class IfcSpaceBoundary
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IfcPhysicalOrVirtual PhysicalOrVirtual { get; set; } = IfcPhysicalOrVirtual.Physical;
    public IfcInternalOrExternal InternalOrExternal { get; set; } = IfcInternalOrExternal.Internal;
    public IfcElement? RelatedBuildingElement { get; set; }
    public IfcMesh? ConnectionGeometry { get; set; }
}

/// <summary>
/// Represents an enclosed space, room, or zone within a building,
/// conforming to buildingSMART standard IFCSPACE.
/// </summary>
public sealed class IfcSpace : IfcElement
{
    public string? LongName { get; set; }
    public string? SpaceNumber { get; set; }
    public IfcInternalOrExternal InteriorOrExteriorSpace { get; set; } = IfcInternalOrExternal.Internal;
    public double GrossFloorArea { get; set; }
    public double NetFloorArea { get; set; }
    public double GrossPerimeter { get; set; }
    public double Height { get; set; } = 3.0;
    public double GrossVolume { get; set; }
    public List<IfcSpaceBoundary> Boundaries { get; } = [];

    public IfcSpace()
    {
        IfcType = "IFCSPACE";
    }
}

/// <summary>
/// An arbitrary logical grouping of building elements,
/// conforming to buildingSMART standard IFCGROUP.
/// </summary>
public class IfcGroup
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GlobalId { get; set; }
    public List<IfcElement> Elements { get; } = [];
    public List<IfcPropertySet> PropertySets { get; } = [];

    public IfcGroup() { }

    public IfcGroup(string name, string? description = null)
    {
        Name = name;
        Description = description;
    }

    public IfcPropertySet GetOrCreatePropertySet(string name)
    {
        var existing = PropertySets.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var pset = new IfcPropertySet(name);
        PropertySets.Add(pset);
        return pset;
    }
}

/// <summary>
/// A functional building system grouping elements (e.g. HVAC, Piping, Electrical, FireProtection),
/// conforming to buildingSMART standard IFCSYSTEM (or IFCDISTRIBUTIONSYSTEM in IFC4).
/// </summary>
public sealed class IfcSystem : IfcGroup
{
    public string SystemType { get; set; } = "HVAC";
    public string? PredefinedType { get; set; }

    public IfcSystem() { }

    public IfcSystem(string name, string systemType = "HVAC") : base(name)
    {
        SystemType = systemType;
    }
}
