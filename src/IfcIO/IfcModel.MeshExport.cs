using IfcIO.Core;
using IfcIO.Geometry;

namespace IfcIO;

public sealed partial class IfcModel
{
    private static (string Type, List<int> Ids) WriteBodyMesh(StepWriter w, IfcMesh mesh, bool tessellate)
    {
        var parts = GetMeshParts(mesh);
        if (parts.Count == 0) throw new InvalidDataException("Cannot write a mesh without valid triangles.");

        bool closed = parts.All(part => part.Closed);
        if (tessellate)
            return ("Tessellation", [WriteMeshTriangulatedFaceSet(w, mesh.Vertices, parts.SelectMany(p => p.Indices).ToArray(), closed)]);

        var shells = parts.Select(p => WriteMeshShell(w, mesh.Vertices, p.Indices, p.Closed)).ToList();
        return closed
            ? ("Brep", shells.Select(id => w.Add("IFCFACETEDBREP", StepWriter.Ref(id))).ToList())
            : ("SurfaceModel", [w.Add("IFCSHELLBASEDSURFACEMODEL", StepWriter.List(shells.Select(StepWriter.Ref)))]);
    }

    private static int WriteBooleanMesh(StepWriter w, IfcMesh mesh, bool tessellate)
    {
        var parts = GetMeshParts(mesh);
        if (parts.Count == 0 || parts.Any(part => !part.Closed))
            throw new InvalidDataException("A Boolean mesh operand must be closed.");
        if (tessellate)
            return WriteMeshTriangulatedFaceSet(w, mesh.Vertices, parts.SelectMany(p => p.Indices).ToArray(), true);
        if (parts.Count != 1)
            throw new InvalidDataException("A faceted Boolean mesh operand must have one connected shell.");
        return w.Add("IFCFACETEDBREP", StepWriter.Ref(WriteMeshShell(w, mesh.Vertices, parts[0].Indices, true)));
    }

    private sealed class MeshPart
    {
        public List<int> Indices { get; } = [];
        public bool Closed { get; set; } = true;
    }

    /// <summary>Groups triangles by shared edges. A closed shell requires paired, oppositely directed edges.</summary>
    private static List<MeshPart> GetMeshParts(IfcMesh mesh)
    {
        var parents = new int[mesh.TriangleCount];
        Array.Fill(parents, -1);
        var edges = new Dictionary<(int, int), (int Face, int Count, int Balance)>();
        for (int i = 0; i < parents.Length; i++)
        {
            int a = mesh.Indices[i * 3], b = mesh.Indices[i * 3 + 1], c = mesh.Indices[i * 3 + 2];
            if ((uint)a >= (uint)mesh.Vertices.Count || (uint)b >= (uint)mesh.Vertices.Count
                || (uint)c >= (uint)mesh.Vertices.Count || a == b || b == c || a == c) continue;
            parents[i] = i;
            JoinEdge(a, b, i);
            JoinEdge(b, c, i);
            JoinEdge(c, a, i);
        }

        var byRoot = new Dictionary<int, MeshPart>();
        for (int i = 0; i < parents.Length; i++)
        {
            if (parents[i] < 0) continue;
            int root = Find(i);
            if (!byRoot.TryGetValue(root, out var part))
                byRoot.Add(root, part = new MeshPart());
            part.Indices.Add(mesh.Indices[i * 3]);
            part.Indices.Add(mesh.Indices[i * 3 + 1]);
            part.Indices.Add(mesh.Indices[i * 3 + 2]);
        }
        foreach (var edge in edges.Values)
            if (edge.Count != 2 || edge.Balance != 0)
                byRoot[Find(edge.Face)].Closed = false;
        return byRoot.Values.ToList();

        int Find(int face)
        {
            while (parents[face] != face)
            {
                parents[face] = parents[parents[face]];
                face = parents[face];
            }
            return face;
        }

        void JoinEdge(int first, int second, int face)
        {
            bool forward = first < second;
            var key = forward ? (first, second) : (second, first);
            int sign = forward ? 1 : -1;
            if (edges.TryGetValue(key, out var existing))
            {
                parents[Find(face)] = Find(existing.Face);
                edges[key] = (existing.Face, existing.Count + 1, existing.Balance + sign);
            }
            else
            {
                edges.Add(key, (face, 1, sign));
            }
        }
    }
}
