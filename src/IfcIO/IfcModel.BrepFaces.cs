using System.Globalization;
using IfcIO.Core;
using IfcIO.Geometry;

namespace IfcIO;

public sealed partial class IfcModel
{
    private static IfcMesh? ParseFacetedFaces(StepFile step, StepEntity item, IEnumerable<int> faces, string meshName)
    {
        var vertices = new List<IfcVector3>();
        var indices = new List<int>();
        var globalIndices = new Dictionary<int, int>();
        var faceVertices = new List<IfcVector3>();
        var facePointIds = new List<int>();
        var bounds = new List<IfcFaceTriangulator.Bound>();

        foreach (int faceId in faces)
        {
            var face = step.Find(faceId);
            if (face is null || ReadReferenceList(face, 0) is not { Count: > 0 } boundIds) continue;

            // Stage a whole face so bad points do not alter the indices or bounding box of good faces.
            faceVertices.Clear();
            facePointIds.Clear();
            bounds.Clear();
            bool valid = true;
            foreach (int boundId in boundIds)
            {
                var bound = step.Find(boundId);
                var loop = step.Find(bound?.GetRef(0));
                if (bound is null || bound.GetBool(1) is not bool orientation
                    || loop is null || !loop.Type.Equals("IFCPOLYLOOP", StringComparison.OrdinalIgnoreCase)
                    || ReadReferenceList(loop, 0) is not { Count: >= 3 } pointIds)
                {
                    valid = false;
                    break;
                }

                var points = new List<int>(pointIds.Count);
                foreach (int pointId in pointIds)
                {
                    IfcVector3 point;
                    if (globalIndices.TryGetValue(pointId, out int existing))
                        point = vertices[existing];
                    else if (!TryReadFacetedPoint(step.Find(pointId), out point))
                    {
                        valid = false;
                        break;
                    }

                    points.Add(faceVertices.Count);
                    faceVertices.Add(point);
                    facePointIds.Add(pointId);
                }
                if (!valid) break;
                if (!orientation) points.Reverse();
                bounds.Add(new IfcFaceTriangulator.Bound(points, bound.Type.Equals("IFCFACEOUTERBOUND", StringComparison.OrdinalIgnoreCase)));
            }
            if (!valid || IfcFaceTriangulator.Triangulate(faceVertices, bounds) is not { } triangles) continue;

            var faceToGlobal = new int[facePointIds.Count];
            for (int i = 0; i < facePointIds.Count; i++)
            {
                int pointId = facePointIds[i];
                if (!globalIndices.TryGetValue(pointId, out int index))
                {
                    index = vertices.Count;
                    globalIndices.Add(pointId, index);
                    vertices.Add(faceVertices[i]);
                }
                faceToGlobal[i] = index;
            }
            foreach (int i in triangles) indices.Add(faceToGlobal[i]);
        }

        return indices.Count == 0 ? null : IfcMesh.Create(item.Id, meshName, vertices, indices);
    }

    private static List<int>? ReadReferenceList(StepEntity entity, int argument)
    {
        string? value = entity.GetRaw(argument);
        if (value is not { Length: >= 2 } || value[0] != '(' || value[^1] != ')'
            || value.AsSpan(1, value.Length - 2).TrimEnd().EndsWith(",")) return null;
        var items = StepEntity.ParseArguments(value[1..^1]);
        var ids = new List<int>(items.Count);
        foreach (var reference in items)
        {
            if (!reference.StartsWith('#') || !int.TryParse(reference.AsSpan(1), NumberStyles.None,
                CultureInfo.InvariantCulture, out int id) || id <= 0) return null;
            ids.Add(id);
        }
        return ids;
    }

    private static bool TryReadFacetedPoint(StepEntity? point, out IfcVector3 value)
    {
        value = default;
        if (point is null || !point.Type.Equals("IFCCARTESIANPOINT", StringComparison.OrdinalIgnoreCase)
            || point.GetRaw(0) is not { Length: >= 2 } raw || raw[0] != '(' || raw[^1] != ')'
            || raw.AsSpan(1, raw.Length - 2).TrimEnd().EndsWith(",")) return false;
        var xyz = StepEntity.ParseArguments(raw[1..^1]);
        if (xyz.Count != 3 || !Finite(xyz[0], out double x) || !Finite(xyz[1], out double y) || !Finite(xyz[2], out double z))
            return false;
        value = new IfcVector3(x, y, z);
        return true;

        static bool Finite(string token, out double number) =>
            double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
    }
}
