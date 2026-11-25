using System.Globalization;
using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Schema;

namespace IfcIO;

public sealed partial class IfcModel
{
    private static IfcMesh? ParseSweptDisk(GeometryReadContext ctx, StepEntity item, string meshName)
    {
        var step = ctx.Step;
        bool polygonal = item.Type.Equals("IFCSWEPTDISKSOLIDPOLYGONAL", StringComparison.OrdinalIgnoreCase);
        bool ifc2x3 = ctx.Schema == IfcSchema.Ifc2x3;
        if (polygonal && (ifc2x3 || !item.IsNullParameter(5))) return null; // Filleted joints need a curved sweep.

        if (item.GetReal(1) is not double radius || !double.IsFinite(radius) || radius <= 0) return null;
        double innerRadius = 0;
        if (!item.IsNullParameter(2))
        {
            if (item.GetReal(2) is not double inner || !double.IsFinite(inner) || inner <= 0 || inner >= radius) return null;
            innerRadius = inner;
        }

        if (step.Find(item.GetRef(0)) is not { } curve) return null;
        if (ifc2x3 && !curve.Type.Equals("IFCPOLYLINE", StringComparison.OrdinalIgnoreCase)) return null;
        var points = ReadLinearDirectrix(ctx, curve, polygonal);
        if (points is not { Count: >= 2 }) return null;

        double start = !ifc2x3 && item.IsNullParameter(3) ? 0 : item.GetReal(3) ?? double.NaN;
        double end = !ifc2x3 && item.IsNullParameter(4) ? points.Count - 1 : item.GetReal(4) ?? double.NaN;
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end > points.Count - 1 || end <= start) return null;

        // Each linear edge occupies one unit in the IFC curve's parameter domain.
        int first = (int)Math.Floor(start);
        int limit = (int)Math.Ceiling(end);
        long vertexCount = ((long)limit - first + 1) * IfcSweptDiskMesher.Sides * (innerRadius > 0 ? 2 : 1)
            + (innerRadius > 0 ? 0 : 2);
        ctx.CheckVertexBudget(vertexCount);
        var path = new List<IfcVector3>(limit - first + 1) { Interpolate(first, start) };
        for (int i = first + 1; i < end; i++) AddPathPoint(points[i]);
        AddPathPoint(Interpolate(limit - 1, end));
        if (path[0] == path[^1]) return null; // Closed directrices need a seam-aware frame.

        var mesh = IfcSweptDiskMesher.Build(item.Id, meshName, path, radius, innerRadius);
        // The caller charges success. Bound repeated failed attempts that could allocate all the rings.
        if (mesh is null) ctx.ChargeVertices((int)vertexCount);
        return mesh;

        IfcVector3 Interpolate(int segment, double parameter)
        {
            double fraction = parameter - segment;
            return points[segment] * (1 - fraction) + points[segment + 1] * fraction;
        }

        void AddPathPoint(IfcVector3 point)
        {
            // Trim first: a redundant straight-line vertex still occupies a parameter interval.
            if (path.Count >= 2)
            {
                var incoming = (path[^1] - path[^2]).Normalized();
                var outgoing = (point - path[^1]).Normalized();
                if (IfcVector3.Dot(incoming, outgoing) > 0
                    && IfcVector3.Cross(incoming, outgoing).Length <= 1e-12)
                {
                    path[^1] = point;
                    return;
                }
            }
            path.Add(point);
        }
    }

    private static List<IfcVector3>? ReadLinearDirectrix(GeometryReadContext ctx, StepEntity curve, bool polygonal)
    {
        var points = new List<IfcVector3>();
        if (curve.Type.Equals("IFCPOLYLINE", StringComparison.OrdinalIgnoreCase))
        {
            if (ReadReferenceList(curve, 0) is not { Count: >= 2 } ids) return null;
            foreach (int id in ids)
            {
                if (!TryReadFacetedPoint(ctx.Step.Find(id), out var point)) return null;
                points.Add(point);
            }
            return points;
        }

        if (!curve.Type.Equals("IFCINDEXEDPOLYCURVE", StringComparison.OrdinalIgnoreCase)
            || ReadTessellatedPoints(ctx, curve.GetRef(0)) is not { Length: >= 2 } coordinates
            || (!curve.IsNullParameter(2) && curve.GetBool(2) != false)) return null;

        if (curve.IsNullParameter(1))
        {
            foreach (var point in coordinates)
            {
                if (point is not IfcVector3 coordinate) return null;
                points.Add(coordinate);
            }
            return points;
        }
        if (polygonal) return null; // The polygonal subtype requires Segments to be omitted.

        int lastIndex = -1;
        var segments = ParseStepList(curve.GetRaw(1));
        foreach (string segment in segments)
        {
            const string lineIndex = "IFCLINEINDEX";
            if (!segment.StartsWith(lineIndex, StringComparison.OrdinalIgnoreCase)) return null;
            var wrapper = ParseStepList(segment[lineIndex.Length..].TrimStart());
            if (wrapper.Count != 1) return null;
            var indices = ParseStepList(wrapper[0]);
            if (indices.Count < 2) return null;
            for (int i = 0; i < indices.Count; i++)
            {
                if (!int.TryParse(indices[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    || index <= 0 || index > coordinates.Length || coordinates[index - 1] is not IfcVector3 point)
                    return null;
                if (i == 0 && lastIndex != -1)
                {
                    if (index != lastIndex) return null;
                }
                else
                {
                    points.Add(point);
                }
                lastIndex = index;
            }
        }
        return points;
    }
}
