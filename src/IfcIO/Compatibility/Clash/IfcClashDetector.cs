using System.Diagnostics;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Clash;

/// <summary>
/// High-performance 3D geometric clash detection and interference analysis engine for IFC models.
/// Performs broad-phase bounding box pruning and narrow-phase exact triangle-to-triangle collision tests.
/// </summary>
public static class IfcClashDetector
{
    /// <summary>
    /// Performs geometric clash detection between two IFC models (e.g. Architectural vs Structural or Structural vs MEP).
    /// </summary>
    public static IfcClashReport DetectClashes(IfcModel modelA, IfcModel modelB, IfcClashOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(modelA);
        ArgumentNullException.ThrowIfNull(modelB);

        options ??= new IfcClashOptions();
        var sw = Stopwatch.StartNew();

        var report = new IfcClashReport
        {
            ModelAName = modelA.ProjectName,
            ModelBName = modelB.ProjectName,
            Tolerance = options.Tolerance,
        };

        var elemsA = GetParticipatingElements(modelA, options.SelectorA);
        var elemsB = GetParticipatingElements(modelB, options.SelectorB);

        var storeyMapA = BuildStoreyMap(modelA);
        var storeyMapB = BuildStoreyMap(modelB);

        double tol = Math.Max(0.0, options.Tolerance);

        foreach (var (elemA, meshA) in elemsA)
        {
            var boxA = meshA.BoundingBox;
            if (boxA.IsEmpty) continue;

            var expandedBoxA = new IfcBoundingBox(
                new IfcVector3(boxA.Min.X - tol, boxA.Min.Y - tol, boxA.Min.Z - tol),
                new IfcVector3(boxA.Max.X + tol, boxA.Max.Y + tol, boxA.Max.Z + tol)
            );

            storeyMapA.TryGetValue(elemA.Id, out var sA);

            foreach (var (elemB, meshB) in elemsB)
            {
                if (options.ExcludeSameType && elemA.IfcType.Equals(elemB.IfcType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (options.ExcludeSameStorey && sA is not null && storeyMapB.TryGetValue(elemB.Id, out var sB) && sA.Name.Equals(sB.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var boxB = meshB.BoundingBox;
                if (boxB.IsEmpty) continue;

                // Broad phase: AABB overlap check
                if (!BoxesOverlap(expandedBoxA, boxB))
                {
                    continue;
                }

                // Narrow phase: triangle-to-triangle intersection test
                if (CheckMeshIntersection(meshA, meshB, out var clashPoint, out double distance))
                {
                    report.Clashes.Add(new IfcClashResult
                    {
                        ElementA = elemA,
                        ElementB = elemB,
                        ClashPoint = clashPoint,
                        Distance = distance,
                        ClashType = options.ClashType,
                    });
                }
            }
        }

        sw.Stop();
        report.Elapsed = sw.Elapsed;
        return report;
    }

    /// <summary>
    /// Performs geometric self-clash detection within a single IFC model.
    /// </summary>
    public static IfcClashReport DetectClashes(IfcModel model, IfcClashOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new IfcClashOptions();
        var sw = Stopwatch.StartNew();

        var report = new IfcClashReport
        {
            ModelAName = model.ProjectName,
            Tolerance = options.Tolerance,
        };

        var elems = GetParticipatingElements(model, options.SelectorA);
        var storeyMap = BuildStoreyMap(model);
        double tol = Math.Max(0.0, options.Tolerance);

        int count = elems.Count;
        for (int i = 0; i < count; i++)
        {
            var (elemA, meshA) = elems[i];
            var boxA = meshA.BoundingBox;
            if (boxA.IsEmpty) continue;

            var expandedBoxA = new IfcBoundingBox(
                new IfcVector3(boxA.Min.X - tol, boxA.Min.Y - tol, boxA.Min.Z - tol),
                new IfcVector3(boxA.Max.X + tol, boxA.Max.Y + tol, boxA.Max.Z + tol)
            );

            storeyMap.TryGetValue(elemA.Id, out var sA);

            for (int j = i + 1; j < count; j++)
            {
                var (elemB, meshB) = elems[j];

                if (options.ExcludeSameType && elemA.IfcType.Equals(elemB.IfcType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (options.ExcludeSameStorey && sA is not null && storeyMap.TryGetValue(elemB.Id, out var sB) && sA.Name.Equals(sB.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var boxB = meshB.BoundingBox;
                if (boxB.IsEmpty) continue;

                if (!BoxesOverlap(expandedBoxA, boxB))
                {
                    continue;
                }

                if (CheckMeshIntersection(meshA, meshB, out var clashPoint, out double distance))
                {
                    report.Clashes.Add(new IfcClashResult
                    {
                        ElementA = elemA,
                        ElementB = elemB,
                        ClashPoint = clashPoint,
                        Distance = distance,
                        ClashType = options.ClashType,
                    });
                }
            }
        }

        sw.Stop();
        report.Elapsed = sw.Elapsed;
        return report;
    }

    private static bool BoxesOverlap(IfcBoundingBox a, IfcBoundingBox b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
               a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
               a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }

    private static List<(IfcElement Element, IfcMesh Mesh)> GetParticipatingElements(IfcModel model, string? selector)
    {
        var elements = !string.IsNullOrEmpty(selector)
            ? model.Select(selector)
            : model.AllElements;

        var list = new List<(IfcElement, IfcMesh)>();
        foreach (var elem in elements)
        {
            var mesh = elem.GetEffectiveMesh();
            if (mesh is not null && mesh.Vertices.Count >= 3 && mesh.Indices.Count >= 3)
            {
                list.Add((elem, mesh));
            }
        }
        return list;
    }

    private static Dictionary<int, IfcBuildingStorey> BuildStoreyMap(IfcModel model)
    {
        var map = new Dictionary<int, IfcBuildingStorey>();
        foreach (var storey in model.Storeys)
        {
            foreach (var elem in storey.Elements)
            {
                map[elem.Id] = storey;
            }
        }
        return map;
    }

    /// <summary>
    /// Checks for triangle-level geometric intersection between two meshes.
    /// </summary>
    public static bool CheckMeshIntersection(IfcMesh meshA, IfcMesh meshB, out IfcVector3 clashPoint, out double distance)
    {
        var vertsA = meshA.Vertices;
        var idxA = meshA.Indices;
        var vertsB = meshB.Vertices;
        var idxB = meshB.Indices;

        int triCountA = idxA.Count / 3;
        int triCountB = idxB.Count / 3;

        for (int i = 0; i < triCountA; i++)
        {
            var a0 = vertsA[idxA[i * 3]];
            var a1 = vertsA[idxA[i * 3 + 1]];
            var a2 = vertsA[idxA[i * 3 + 2]];

            for (int j = 0; j < triCountB; j++)
            {
                var b0 = vertsB[idxB[j * 3]];
                var b1 = vertsB[idxB[j * 3 + 1]];
                var b2 = vertsB[idxB[j * 3 + 2]];

                if (TrianglesIntersect(a0, a1, a2, b0, b1, b2, out var pt))
                {
                    clashPoint = pt;
                    // Compute penetration depth approximation
                    var centerA = (a0 + a1 + a2) * (1.0 / 3.0);
                    var centerB = (b0 + b1 + b2) * (1.0 / 3.0);
                    distance = -(centerA - centerB).Length;
                    return true;
                }
            }
        }

        clashPoint = IfcVector3.Zero;
        distance = 0.0;
        return false;
    }

    /// <summary>
    /// Pure managed Möller 3D triangle-to-triangle intersection test.
    /// </summary>
    public static bool TrianglesIntersect(
        IfcVector3 v0, IfcVector3 v1, IfcVector3 v2,
        IfcVector3 u0, IfcVector3 u1, IfcVector3 u2,
        out IfcVector3 intersectionPoint)
    {
        intersectionPoint = IfcVector3.Zero;

        // Plane equation of triangle 1: N1 . X + d1 = 0
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var n1 = IfcVector3.Cross(e1, e2);
        double d1 = -IfcVector3.Dot(n1, v0);

        // Signed distances of vertices of triangle 2 to plane 1
        double du0 = IfcVector3.Dot(n1, u0) + d1;
        double du1 = IfcVector3.Dot(n1, u1) + d1;
        double du2 = IfcVector3.Dot(n1, u2) + d1;

        if (Math.Abs(du0) < 1e-7) du0 = 0.0;
        if (Math.Abs(du1) < 1e-7) du1 = 0.0;
        if (Math.Abs(du2) < 1e-7) du2 = 0.0;

        double du0du1 = du0 * du1;
        double du0du2 = du0 * du2;

        if (du0du1 > 0.0 && du0du2 > 0.0)
        {
            // All vertices of triangle 2 are on the same side of plane 1
            return false;
        }

        // Plane equation of triangle 2: N2 . X + d2 = 0
        var f1 = u1 - u0;
        var f2 = u2 - u0;
        var n2 = IfcVector3.Cross(f1, f2);
        double d2 = -IfcVector3.Dot(n2, u0);

        // Signed distances of vertices of triangle 1 to plane 2
        double dv0 = IfcVector3.Dot(n2, v0) + d2;
        double dv1 = IfcVector3.Dot(n2, v1) + d2;
        double dv2 = IfcVector3.Dot(n2, v2) + d2;

        if (Math.Abs(dv0) < 1e-7) dv0 = 0.0;
        if (Math.Abs(dv1) < 1e-7) dv1 = 0.0;
        if (Math.Abs(dv2) < 1e-7) dv2 = 0.0;

        double dv0dv1 = dv0 * dv1;
        double dv0dv2 = dv0 * dv2;

        if (dv0dv1 > 0.0 && dv0dv2 > 0.0)
        {
            // All vertices of triangle 1 are on the same side of plane 2
            return false;
        }

        // Line of intersection L = D * t + O where D = N1 x N2
        var d = IfcVector3.Cross(n1, n2);
        double lenD = d.Length;
        if (lenD < 1e-9)
        {
            // Triangles are coplanar
            return false;
        }

        // Compute intervals on line of intersection
        if (!ComputeIntersectionInterval(v0, v1, v2, dv0, dv1, dv2, dv0dv1, dv0dv2, d, out double t1Min, out double t1Max) ||
            !ComputeIntersectionInterval(u0, u1, u2, du0, du1, du2, du0du1, du0du2, d, out double t2Min, out double t2Max))
        {
            return false;
        }

        if (t1Min > t1Max) (t1Min, t1Max) = (t1Max, t1Min);
        if (t2Min > t2Max) (t2Min, t2Max) = (t2Max, t2Min);

        if (t1Max < t2Min || t2Max < t1Min)
        {
            return false;
        }

        double overlapStart = Math.Max(t1Min, t2Min);
        double overlapEnd = Math.Min(t1Max, t2Max);
        double midT = (overlapStart + overlapEnd) * 0.5;

        // Approximate point on line of intersection
        var triCenter1 = (v0 + v1 + v2) * (1.0 / 3.0);
        var triCenter2 = (u0 + u1 + u2) * (1.0 / 3.0);
        intersectionPoint = (triCenter1 + triCenter2) * 0.5;
        return true;
    }

    private static bool ComputeIntersectionInterval(
        IfcVector3 v0, IfcVector3 v1, IfcVector3 v2,
        double vv0, double vv1, double vv2,
        double vv0vv1, double vv0vv2,
        IfcVector3 d,
        out double tMin, out double tMax)
    {
        tMin = 0; tMax = 0;

        double proj0 = IfcVector3.Dot(d, v0);
        double proj1 = IfcVector3.Dot(d, v1);
        double proj2 = IfcVector3.Dot(d, v2);

        if (vv0vv1 > 0.0)
        {
            // v0 and v1 are on the same side, v2 is on the opposite side
            tMin = proj0 + (proj2 - proj0) * (vv0 / (vv0 - vv2));
            tMax = proj1 + (proj2 - proj1) * (vv1 / (vv1 - vv2));
            return true;
        }
        else if (vv0vv2 > 0.0)
        {
            // v0 and v2 are on the same side, v1 is on the opposite side
            tMin = proj0 + (proj1 - proj0) * (vv0 / (vv0 - vv1));
            tMax = proj2 + (proj1 - proj2) * (vv2 / (vv2 - vv1));
            return true;
        }
        else if (vv1 * vv2 > 0.0 || vv0 != 0.0)
        {
            // v1 and v2 are on the same side, v0 is on the opposite side
            tMin = proj1 + (proj0 - proj1) * (vv1 / (vv1 - vv0));
            tMax = proj2 + (proj0 - proj2) * (vv2 / (vv2 - vv0));
            return true;
        }
        else
        {
            return false;
        }
    }
}
