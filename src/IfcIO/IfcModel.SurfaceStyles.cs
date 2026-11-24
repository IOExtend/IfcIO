using IfcIO.Core;
using IfcIO.Model;

namespace IfcIO;

public sealed partial class IfcModel
{
    private static (Dictionary<int, IfcMaterial> Geometry, Dictionary<int, IfcMaterial> Materials)
        ReadSurfaceStyles(StepFile step)
    {
        var styles = new Dictionary<int, IfcMaterial>();
        foreach (var style in step.IndexedOfType("IFCSURFACESTYLE"))
        {
            foreach (int appearanceId in style.GetRefList(2))
            {
                var appearance = step.Find(appearanceId);
                if (appearance is null || !(appearance.Type.Equals("IFCSURFACESTYLESHADING", StringComparison.OrdinalIgnoreCase)
                    || appearance.Type.Equals("IFCSURFACESTYLERENDERING", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var rgb = step.Find(appearance.GetRef(0));
                if (rgb is null || !rgb.Type.Equals("IFCCOLOURRGB", StringComparison.OrdinalIgnoreCase)) continue;

                var material = new IfcMaterial
                {
                    Name = style.GetString(0) ?? "Material",
                    R = RestrictToUnitInterval(rgb.GetReal(1), 0.8),
                    G = RestrictToUnitInterval(rgb.GetReal(2), 0.8),
                    B = RestrictToUnitInterval(rgb.GetReal(3), 0.8),
                    Alpha = 1.0 - RestrictToUnitInterval(appearance.GetReal(1), 0.0),
                };
                styles.Add(style.Id, material);
                break;
            }
        }

        // A styled item can reference a surface style directly, or through the IFC2x3 assignment wrapper.
        var assignments = new Dictionary<int, IfcMaterial>();
        foreach (var assignment in step.IndexedOfType("IFCPRESENTATIONSTYLEASSIGNMENT"))
        {
            foreach (int styleId in assignment.GetRefList(0))
            {
                if (!styles.TryGetValue(styleId, out var material)) continue;
                assignments.Add(assignment.Id, material);
                break;
            }
        }

        var byStyledItem = new Dictionary<int, IfcMaterial>();
        var byGeometry = new Dictionary<int, IfcMaterial>();
        foreach (var item in step.IndexedOfType("IFCSTYLEDITEM"))
        {
            foreach (int styleId in item.GetRefList(1))
            {
                if (!styles.TryGetValue(styleId, out var material) && !assignments.TryGetValue(styleId, out material))
                    continue;
                byStyledItem.Add(item.Id, material);
                if (item.GetRef(0) is int geometryId) byGeometry[geometryId] = material;
                break;
            }
        }

        // In a material representation the styled item itself is the appearance; its Item may be unset.
        var byMaterial = new Dictionary<int, IfcMaterial>();
        foreach (var definition in step.IndexedOfType("IFCMATERIALDEFINITIONREPRESENTATION"))
        {
            if (definition.GetRef(3) is not int materialId || byMaterial.ContainsKey(materialId)) continue;
            foreach (int representationId in definition.GetRefList(2))
            {
                var representation = step.Find(representationId);
                if (representation is null || !representation.Type.Equals("IFCSTYLEDREPRESENTATION", StringComparison.OrdinalIgnoreCase))
                    continue;
                var appearance = representation.GetRefList(3).Select(id => byStyledItem.GetValueOrDefault(id)).FirstOrDefault(m => m is not null);
                if (appearance is null) continue;
                byMaterial.Add(materialId, appearance);
                break;
            }
        }

        return (byGeometry, byMaterial);
    }

    private static double RestrictToUnitInterval(double? value, double fallback) =>
        value is double channel && double.IsFinite(channel) ? Math.Clamp(channel, 0, 1) : fallback;
}
