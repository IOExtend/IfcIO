using System.Diagnostics.CodeAnalysis;
using IfcIO.Geometry;

namespace IfcIO.Model;

/// <summary>
/// A single layer of material within a multi-layer composite construction (e.g. plaster, insulation, concrete).
/// Conforms to buildingSMART standard IFCMATERIALLAYER.
/// </summary>
public sealed class IfcMaterialLayer
{
    public required IfcMaterial Material { get; set; }
    public double LayerThickness { get; set; }
    public bool IsVentilated { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    public IfcMaterialLayer() { }

    [SetsRequiredMembers]
    public IfcMaterialLayer(IfcMaterial material, double thickness, bool isVentilated = false, string? name = null)
    {
        Material = material ?? throw new ArgumentNullException(nameof(material));
        LayerThickness = thickness;
        IsVentilated = isVentilated;
        Name = name;
    }
}

/// <summary>
/// An ordered sequence of material layers forming a composite wall, slab, or roof.
/// Conforms to buildingSMART standard IFCMATERIALLAYERSET.
/// </summary>
public sealed class IfcMaterialLayerSet
{
    public string LayerSetName { get; set; } = "Material Layer Set";
    public string? Description { get; set; }
    public List<IfcMaterialLayer> Layers { get; } = [];

    public double TotalThickness => Layers.Sum(l => l.LayerThickness);

    public IfcMaterialLayerSet() { }

    public IfcMaterialLayerSet(string layerSetName, IEnumerable<IfcMaterialLayer>? layers = null)
    {
        LayerSetName = layerSetName;
        if (layers is not null)
        {
            Layers.AddRange(layers);
        }
    }

    public IfcMaterialLayer AddLayer(IfcMaterial material, double thickness, bool isVentilated = false, string? name = null)
    {
        var layer = new IfcMaterialLayer(material, thickness, isVentilated, name);
        Layers.Add(layer);
        return layer;
    }
}

/// <summary>
/// Specifies how a material layer set is positioned and oriented relative to a host element reference axis.
/// Conforms to buildingSMART standard IFCMATERIALLAYERSETUSAGE.
/// </summary>
public sealed class IfcMaterialLayerSetUsage
{
    public required IfcMaterialLayerSet ForLayerSet { get; set; }
    public string LayerSetDirection { get; set; } = ".AXIS2.";
    public string DirectionSense { get; set; } = ".POSITIVE.";
    public double OffsetFromReferenceLine { get; set; }

    public IfcMaterialLayerSetUsage() { }

    [SetsRequiredMembers]
    public IfcMaterialLayerSetUsage(
        IfcMaterialLayerSet layerSet,
        double offsetFromReferenceLine = 0.0,
        string layerSetDirection = ".AXIS2.",
        string directionSense = ".POSITIVE.")
    {
        ForLayerSet = layerSet ?? throw new ArgumentNullException(nameof(layerSet));
        OffsetFromReferenceLine = offsetFromReferenceLine;
        LayerSetDirection = layerSetDirection;
        DirectionSense = directionSense;
    }
}

/// <summary>
/// Associates a material with a parameterized 2D profile (e.g. steel grade S355 assigned to HEA 200 profile).
/// Conforms to buildingSMART standard IFCMATERIALPROFILE.
/// </summary>
public sealed class IfcMaterialProfile
{
    public string? Name { get; set; }
    public IfcMaterial? Material { get; set; }
    public required IfcProfileDef Profile { get; set; }

    public IfcMaterialProfile() { }

    [SetsRequiredMembers]
    public IfcMaterialProfile(IfcProfileDef profile, IfcMaterial? material = null, string? name = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Material = material;
        Name = name;
    }
}

/// <summary>
/// A collection of material profiles forming a structural member cross-section.
/// Conforms to buildingSMART standard IFCMATERIALPROFILESET.
/// </summary>
public sealed class IfcMaterialProfileSet
{
    public string Name { get; set; } = "Material Profile Set";
    public string? Description { get; set; }
    public List<IfcMaterialProfile> MaterialProfiles { get; } = [];

    public IfcMaterialProfileSet() { }

    public IfcMaterialProfileSet(string name, IEnumerable<IfcMaterialProfile>? profiles = null)
    {
        Name = name;
        if (profiles is not null)
        {
            MaterialProfiles.AddRange(profiles);
        }
    }

    public IfcMaterialProfile AddProfile(IfcProfileDef profile, IfcMaterial? material = null, string? name = null)
    {
        var mp = new IfcMaterialProfile(profile, material, name);
        MaterialProfiles.Add(mp);
        return mp;
    }
}

/// <summary>
/// Specifies the positioning alignment (cardinal point) of a material profile set along an element axis.
/// Conforms to buildingSMART standard IFCMATERIALPROFILESETUSAGE.
/// </summary>
public sealed class IfcMaterialProfileSetUsage
{
    public required IfcMaterialProfileSet ForProfileSet { get; set; }

    /// <summary>
    /// Cardinal point alignment (1 = bottom-left, 5 = centroid/center, 8 = top-center, etc., 1..9).
    /// </summary>
    public int CardinalPoint { get; set; } = 5;

    public IfcMaterialProfileSetUsage() { }

    [SetsRequiredMembers]
    public IfcMaterialProfileSetUsage(IfcMaterialProfileSet profileSet, int cardinalPoint = 5)
    {
        ForProfileSet = profileSet ?? throw new ArgumentNullException(nameof(profileSet));
        CardinalPoint = cardinalPoint;
    }
}
