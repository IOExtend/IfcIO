using System.Globalization;
using IfcIO.Core;
using IfcIO.Geometry;

namespace IfcIO;

public sealed partial class IfcModel
{
    private static IfcMesh? ParseTriangulatedFaceSet(GeometryReadContext ctx, StepEntity item, string meshName)
    {
        var vertices = ReadTessellatedPoints(ctx, item.GetRef(0));
        if (vertices is null) return null;

        string? pointNumberList = item.GetRaw(4);
        var pointNumbers = ParseStepList(pointNumberList);
        if (pointNumberList is not null && pointNumbers.Count == 0) return null;
        // Before IFC4 Addendum 2 this slot held NormalIndex, a list of triplets.
        if (pointNumbers.Count > 0 && pointNumbers[0].StartsWith('(')) pointNumbers.Clear();

        var indices = new List<int>();
        foreach (var tuple in ParseStepList(item.GetRaw(3)))
        {
            var parts = ParseStepList(tuple);
            if (parts.Count != 3 || !TryResolveVertex(parts[0], pointNumbers, vertices, out int a)
                || !TryResolveVertex(parts[1], pointNumbers, vertices, out int b)
                || !TryResolveVertex(parts[2], pointNumbers, vertices, out int c)) continue;
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        return CreateIndexedMesh(item.Id, meshName, vertices, indices);
    }

    private static IfcMesh? ParsePolygonalFaceSet(GeometryReadContext ctx, StepEntity item, string meshName)
    {
        var vertices = ReadTessellatedPoints(ctx, item.GetRef(0));
        if (vertices is null) return null;
        string? pointNumberList = item.GetRaw(3);
        var pointNumbers = ParseStepList(pointNumberList);
        if (pointNumberList is not null && pointNumbers.Count == 0) return null;

        var indices = new List<int>();
        var faceVertices = new List<IfcVector3>();
        var faceToGlobal = new List<int>();
        var bounds = new List<IfcFaceTriangulator.Bound>();
        var seenFaces = new HashSet<int>();

        foreach (string reference in ParseStepList(item.GetRaw(2)))
        {
            if (!reference.StartsWith('#') || !int.TryParse(reference.AsSpan(1), NumberStyles.None,
                CultureInfo.InvariantCulture, out int faceId) || faceId <= 0) continue;
            // A repeated face is the same surface; do not triangulate it repeatedly.
            if (!seenFaces.Add(faceId)) continue;
            var face = ctx.Step.Find(faceId);
            if (face is null) continue;
            bool hasHoles = face.Type.Equals("IFCINDEXEDPOLYGONALFACEWITHVOIDS", StringComparison.OrdinalIgnoreCase);
            if (!hasHoles && !face.Type.Equals("IFCINDEXEDPOLYGONALFACE", StringComparison.OrdinalIgnoreCase)) continue;

            faceVertices.Clear();
            faceToGlobal.Clear();
            bounds.Clear();
            if (!ReadRing(face.GetRaw(0), isOuter: true)) continue;
            if (hasHoles)
            {
                var holes = ParseStepList(face.GetRaw(1));
                if (holes.Count == 0 || !holes.All(hole => ReadRing(hole, isOuter: false))) continue;
            }

            if (IfcFaceTriangulator.Triangulate(faceVertices, bounds, strictPolygon: true) is not { } triangles) continue;
            foreach (int local in triangles) indices.Add(faceToGlobal[local]);
        }

        return CreateIndexedMesh(item.Id, meshName, vertices, indices);

        bool ReadRing(string? raw, bool isOuter)
        {
            var numbers = ParseStepList(raw);
            if (numbers.Count < 3 || numbers.Count > IfcFaceTriangulator.MaxFaceVertices - faceVertices.Count) return false;
            var ring = new List<int>(numbers.Count);
            foreach (string number in numbers)
            {
                if (!TryResolveVertex(number, pointNumbers, vertices, out int index)) return false;
                ring.Add(faceVertices.Count);
                faceVertices.Add(vertices[index].GetValueOrDefault());
                faceToGlobal.Add(index);
            }
            bounds.Add(new IfcFaceTriangulator.Bound(ring, isOuter));
            return true;
        }
    }

    private static IfcVector3?[]? ReadTessellatedPoints(GeometryReadContext ctx, int? id)
    {
        if (id is not int pointListId) return null;
        if (ctx.TessellatedPointLists.TryGetValue(pointListId, out var cached)) return cached;
        var pointList = ctx.Step.Find(pointListId);
        if (pointList is null || !pointList.Type.Equals("IFCCARTESIANPOINTLIST3D", StringComparison.OrdinalIgnoreCase)) return null;
        var tuples = ParseStepList(pointList.GetRaw(0));
        ctx.ChargeVertices(tuples.Count);
        var points = new IfcVector3?[tuples.Count];
        for (int i = 0; i < tuples.Count; i++)
        {
            var xyz = ParseStepList(tuples[i]);
            if (xyz.Count == 3 && TryCoordinate(xyz[0], out double x)
                && TryCoordinate(xyz[1], out double y) && TryCoordinate(xyz[2], out double z))
                points[i] = new IfcVector3(x, y, z);
        }
        ctx.TessellatedPointLists.Add(pointListId, points);
        return points;

        static bool TryCoordinate(string value, out double coordinate) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate) && double.IsFinite(coordinate);
    }

    private static bool TryResolveVertex(string token, List<string> pointNumbers, IfcVector3?[] points, out int index)
    {
        index = 0;
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number <= 0) return false;
        if (pointNumbers.Count > 0 && (number > pointNumbers.Count
            || !int.TryParse(pointNumbers[number - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))) return false;
        if (number <= 0 || number > points.Length || !points[number - 1].HasValue) return false;
        index = number - 1;
        return true;
    }

    private static IfcMesh? CreateIndexedMesh(int id, string name, IfcVector3?[] points, List<int> indices)
    {
        if (indices.Count == 0) return null;
        var vertices = new List<IfcVector3>(Math.Min(points.Length, indices.Count));
        if (indices.Count < points.Length / 4)
        {
            // Sparse sets must not scan or retain a large shared coordinate table.
            var used = new HashSet<int>(indices);
            var sparseRemap = new Dictionary<int, int>(used.Count);
            foreach (int source in used.Order())
            {
                sparseRemap.Add(source, vertices.Count);
                vertices.Add(points[source].GetValueOrDefault());
            }
            for (int i = 0; i < indices.Count; i++) indices[i] = sparseRemap[indices[i]];
            return IfcMesh.Create(id, name, vertices, indices);
        }

        var remap = new int[points.Length];
        foreach (int index in indices) remap[index] = 1;
        for (int i = 0; i < points.Length; i++)
        {
            if (remap[i] == 0) continue;
            remap[i] = vertices.Count;
            vertices.Add(points[i].GetValueOrDefault());
        }
        if (vertices.Count != points.Length)
            for (int i = 0; i < indices.Count; i++) indices[i] = remap[indices[i]];
        return IfcMesh.Create(id, name, vertices, indices);
    }
}
