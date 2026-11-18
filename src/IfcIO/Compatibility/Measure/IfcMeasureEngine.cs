using System.Globalization;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Measure;

/// <summary>
/// Result of a 3D point-to-point distance measurement.
/// </summary>
public sealed record IfcDistanceMeasurement(
    IfcVector3 PointA,
    IfcVector3 PointB,
    double Distance,
    double DeltaX,
    double DeltaY,
    double DeltaZ,
    double HorizontalDistance,
    double VerticalDistance,
    double PitchDegrees,
    double AzimuthDegrees
)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Distance: {Distance:F3} m (dX: {DeltaX:F3}, dY: {DeltaY:F3}, dZ: {DeltaZ:F3}, Horiz: {HorizontalDistance:F3}, Pitch: {PitchDegrees:F1}°)");
}

/// <summary>
/// Result of a point-to-mesh shortest distance measurement.
/// </summary>
public sealed record IfcPointToMeshMeasurement(
    IfcVector3 QueryPoint,
    IfcVector3 ClosestPoint,
    double Distance,
    int ClosestTriangleIndex,
    IfcVector3 Normal
)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Point-to-Mesh Distance: {Distance:F3} m at {ClosestPoint}");
}

/// <summary>
/// Result of a mesh-to-mesh shortest distance measurement.
/// </summary>
public sealed record IfcMeshToMeshMeasurement(
    IfcMesh MeshA,
    IfcMesh MeshB,
    double Distance,
    IfcVector3 PointA,
    IfcVector3 PointB
)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Mesh-to-Mesh Clearance: {Distance:F3} m between {PointA} and {PointB}");
}

/// <summary>
/// Result of a cumulative polyline path distance measurement.
/// </summary>
public sealed record IfcPolylineMeasurement(
    IReadOnlyList<IfcVector3> Points,
    IReadOnlyList<double> SegmentLengths,
    double TotalLength
);

/// <summary>
/// High-precision 3D measurement engine providing point-to-point, point-to-mesh,
/// mesh-to-mesh clearance, triangle area, normal vector, and polyline distance calculations for IFC models.
/// </summary>
public static class IfcMeasureEngine
{
    /// <summary>Measures the 3D Euclidean distance and directional projections between two points.</summary>
    public static IfcDistanceMeasurement MeasureDistance(IfcVector3 a, IfcVector3 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double dz = b.Z - a.Z;

        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double horiz = Math.Sqrt(dx * dx + dy * dy);
        double vert = Math.Abs(dz);

        double pitch = dist > 1e-12 ? Math.Asin(Math.Clamp(dz / dist, -1.0, 1.0)) * (180.0 / Math.PI) : 0.0;
        double azimuth = horiz > 1e-12 ? Math.Atan2(dy, dx) * (180.0 / Math.PI) : 0.0;
        if (azimuth < 0.0) azimuth += 360.0;

        return new IfcDistanceMeasurement(a, b, dist, dx, dy, dz, horiz, vert, pitch, azimuth);
    }

    /// <summary>Computes the angle in degrees between two 3D direction vectors.</summary>
    public static double MeasureAngle(IfcVector3 dirA, IfcVector3 dirB)
    {
        double lenA = dirA.Length;
        double lenB = dirB.Length;
        if (lenA < 1e-12 || lenB < 1e-12) return 0.0;

        double cosTheta = IfcVector3.Dot(dirA, dirB) / (lenA * lenB);
        cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
        return Math.Acos(cosTheta) * (180.0 / Math.PI);
    }

    /// <summary>Computes the angle in degrees between two line segments meeting at a vertex: angle(A - Vertex - B).</summary>
    public static double MeasureAngle(IfcVector3 a, IfcVector3 vertex, IfcVector3 b) =>
        MeasureAngle(a - vertex, b - vertex);

    /// <summary>Measures cumulative polyline distance across a sequence of 3D points.</summary>
    public static IfcPolylineMeasurement MeasurePath(IEnumerable<IfcVector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var ptList = points.ToList();
        var segments = new List<double>();
        double total = 0.0;

        for (int i = 0; i < ptList.Count - 1; i++)
        {
            double len = (ptList[i] - ptList[i + 1]).Length;
            segments.Add(len);
            total += len;
        }

        return new IfcPolylineMeasurement(ptList, segments, total);
    }

    /// <summary>Computes the surface area of a 3D triangle defined by three vertices.</summary>
    public static double ComputeTriangleArea(IfcVector3 a, IfcVector3 b, IfcVector3 c)
    {
        var cross = IfcVector3.Cross(b - a, c - a);
        return cross.Length * 0.5;
    }

    /// <summary>Computes the unit normal vector of a 3D triangle defined by three vertices in CCW order.</summary>
    public static IfcVector3 ComputeTriangleNormal(IfcVector3 a, IfcVector3 b, IfcVector3 c) =>
        IfcVector3.Cross(b - a, c - a).Normalized();

    /// <summary>
    /// Computes the point on a 3D triangle closest to the specified query point.
    /// Implements Christer Ericson's Voronoi region algorithm.
    /// </summary>
    public static IfcVector3 ClosestPointOnTriangle(IfcVector3 p, IfcVector3 a, IfcVector3 b, IfcVector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        double d1 = IfcVector3.Dot(ab, ap);
        double d2 = IfcVector3.Dot(ac, ap);
        if (d1 <= 0.0 && d2 <= 0.0) return a;

        var bp = p - b;
        double d3 = IfcVector3.Dot(ab, bp);
        double d4 = IfcVector3.Dot(ac, bp);
        if (d3 >= 0.0 && d4 <= d3) return b;

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
        {
            double v = d1 / (d1 - d3);
            return a + ab * v;
        }

        var cp = p - c;
        double d5 = IfcVector3.Dot(ab, cp);
        double d6 = IfcVector3.Dot(ac, cp);
        if (d6 >= 0.0 && d5 <= d6) return c;

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
        {
            double w = d2 / (d2 - d6);
            return a + ac * w;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
        {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + (c - b) * w;
        }

        double denom = 1.0 / (va + vb + vc);
        double vCoord = vb * denom;
        double wCoord = vc * denom;
        return a + ab * vCoord + ac * wCoord;
    }

    /// <summary>Measures the shortest distance from a 3D point to a mesh surface, locating the closest point.</summary>
    public static IfcPointToMeshMeasurement MeasurePointToMesh(IfcVector3 point, IfcMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = mesh.Vertices;
        var indices = mesh.Indices;

        if (verts.Count == 0 || indices.Count < 3)
        {
            return new IfcPointToMeshMeasurement(point, point, 0.0, -1, IfcVector3.Zero);
        }

        double minDistance = double.PositiveInfinity;
        var closestPt = IfcVector3.Zero;
        int closestTri = -1;
        var closestNormal = IfcVector3.Zero;

        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];

            if (idx0 >= verts.Count || idx1 >= verts.Count || idx2 >= verts.Count) continue;

            var v0 = verts[idx0];
            var v1 = verts[idx1];
            var v2 = verts[idx2];

            var ptOnTri = ClosestPointOnTriangle(point, v0, v1, v2);
            double dist = (point - ptOnTri).Length;

            if (dist < minDistance)
            {
                minDistance = dist;
                closestPt = ptOnTri;
                closestTri = i;
                closestNormal = ComputeTriangleNormal(v0, v1, v2);
            }
        }

        return new IfcPointToMeshMeasurement(point, closestPt, minDistance, closestTri, closestNormal);
    }

    /// <summary>Measures the clearance (minimum distance) between two 3D polygon meshes.</summary>
    public static IfcMeshToMeshMeasurement MeasureMeshToMesh(IfcMesh meshA, IfcMesh meshB)
    {
        ArgumentNullException.ThrowIfNull(meshA);
        ArgumentNullException.ThrowIfNull(meshB);

        var vertsA = meshA.Vertices;
        var vertsB = meshB.Vertices;

        if (vertsA.Count == 0 || vertsB.Count == 0 || meshA.Indices.Count < 3 || meshB.Indices.Count < 3)
        {
            return new IfcMeshToMeshMeasurement(meshA, meshB, 0.0, IfcVector3.Zero, IfcVector3.Zero);
        }

        double minDistance = double.PositiveInfinity;
        var bestPtA = IfcVector3.Zero;
        var bestPtB = IfcVector3.Zero;

        // Sample vertices of A against mesh B
        for (int i = 0; i < vertsA.Count; i++)
        {
            var pA = vertsA[i];
            var m = MeasurePointToMesh(pA, meshB);
            if (m.Distance < minDistance)
            {
                minDistance = m.Distance;
                bestPtA = pA;
                bestPtB = m.ClosestPoint;
                if (minDistance < 1e-6) break;
            }
        }

        // Sample vertices of B against mesh A
        if (minDistance > 1e-6)
        {
            for (int i = 0; i < vertsB.Count; i++)
            {
                var pB = vertsB[i];
                var m = MeasurePointToMesh(pB, meshA);
                if (m.Distance < minDistance)
                {
                    minDistance = m.Distance;
                    bestPtA = m.ClosestPoint;
                    bestPtB = pB;
                    if (minDistance < 1e-6) break;
                }
            }
        }

        return new IfcMeshToMeshMeasurement(meshA, meshB, minDistance, bestPtA, bestPtB);
    }

    /// <summary>Computes the total surface area of all triangles in a mesh.</summary>
    public static double ComputeMeshSurfaceArea(IfcMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = mesh.Vertices;
        var indices = mesh.Indices;
        double totalArea = 0.0;

        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];

            if (idx0 < verts.Count && idx1 < verts.Count && idx2 < verts.Count)
            {
                totalArea += ComputeTriangleArea(verts[idx0], verts[idx1], verts[idx2]);
            }
        }

        return totalArea;
    }

    /// <summary>Computes the signed volume of a closed 3D manifold solid mesh.</summary>
    public static double ComputeMeshVolume(IfcMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = mesh.Vertices;
        var indices = mesh.Indices;
        double volume = 0.0;

        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];

            if (idx0 < verts.Count && idx1 < verts.Count && idx2 < verts.Count)
            {
                var v0 = verts[idx0];
                var v1 = verts[idx1];
                var v2 = verts[idx2];

                volume += IfcVector3.Dot(v0, IfcVector3.Cross(v1, v2)) / 6.0;
            }
        }

        return Math.Abs(volume);
    }

    /// <summary>Computes the average vertex centroid of a mesh.</summary>
    public static IfcVector3 ComputeMeshCentroid(IfcMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var verts = mesh.Vertices;
        if (verts.Count == 0) return IfcVector3.Zero;

        double sumX = 0, sumY = 0, sumZ = 0;
        for (int i = 0; i < verts.Count; i++)
        {
            sumX += verts[i].X;
            sumY += verts[i].Y;
            sumZ += verts[i].Z;
        }

        return new IfcVector3(sumX / verts.Count, sumY / verts.Count, sumZ / verts.Count);
    }
}
