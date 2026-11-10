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
/// Represents a building storey / level.
/// </summary>
public sealed class IfcBuildingStorey
{
    public required string Name { get; set; }
    public double Elevation { get; set; }
    public string? GlobalId { get; set; }
    public List<IfcElement> Elements { get; } = [];
}

/// <summary>
/// Represents a type definition object (e.g. IFCWALLTYPE, IFCBEAMTYPE, IFCCOLUMNTYPE, IFCSLABTYPE).
/// </summary>
public sealed class IfcTypeProduct
{
    public required string Name { get; set; }
    public string IfcType { get; set; } = "IFCBUILDINGELEMENTPROXYTYPE";
    public string? GlobalId { get; set; }
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
    public IfcMaterial? Material { get; set; }
    public List<IfcPropertySet> PropertySets { get; } = [];
    public List<IfcOpeningElement> Openings { get; } = [];

    /// <summary>
    /// Gets the polygon mesh representation of the element, either explicitly assigned or generated on demand from its parametric solid.
    /// </summary>
    public IfcMesh? GetEffectiveMesh() => Mesh ?? Solid?.ToMesh();

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
