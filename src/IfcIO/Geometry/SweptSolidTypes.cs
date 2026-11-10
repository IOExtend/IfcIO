using System.Globalization;

namespace IfcIO.Geometry;

/// <summary>
/// Abstract base class for 2D profile definitions used in swept solids and extrusions.
/// </summary>
public abstract class IfcProfileDef
{
    public string? ProfileName { get; set; }
    public string ProfileType { get; set; } = "AREA";

    /// <summary>
    /// Computes the 2D boundary points of the profile in its local coordinate system.
    /// </summary>
    public abstract IReadOnlyList<IfcVector3> GetBoundaryPoints(int segments = 16);
}

/// <summary>
/// A parameterized rectangular 2D profile centered at an optional 2D position offset.
/// </summary>
public sealed class IfcRectangleProfileDef : IfcProfileDef
{
    public double XDim { get; set; }
    public double YDim { get; set; }
    public IfcVector3 Position { get; set; } = IfcVector3.Zero;

    public IfcRectangleProfileDef() { }

    public IfcRectangleProfileDef(double xDim, double yDim, IfcVector3? position = null, string? name = null)
    {
        XDim = xDim;
        YDim = yDim;
        Position = position ?? IfcVector3.Zero;
        ProfileName = name;
    }

    public override IReadOnlyList<IfcVector3> GetBoundaryPoints(int segments = 16)
    {
        double hx = XDim * 0.5;
        double hy = YDim * 0.5;

        return
        [
            new(Position.X - hx, Position.Y - hy, 0),
            new(Position.X + hx, Position.Y - hy, 0),
            new(Position.X + hx, Position.Y + hy, 0),
            new(Position.X - hx, Position.Y + hy, 0),
        ];
    }
}

/// <summary>
/// A parameterized circular 2D profile defined by radius and an optional center offset.
/// </summary>
public sealed class IfcCircleProfileDef : IfcProfileDef
{
    public double Radius { get; set; }
    public IfcVector3 Position { get; set; } = IfcVector3.Zero;

    public IfcCircleProfileDef() { }

    public IfcCircleProfileDef(double radius, IfcVector3? position = null, string? name = null)
    {
        Radius = radius;
        Position = position ?? IfcVector3.Zero;
        ProfileName = name;
    }

    public override IReadOnlyList<IfcVector3> GetBoundaryPoints(int segments = 16)
    {
        int count = Math.Max(3, segments);
        var points = new List<IfcVector3>(count);
        double step = 2.0 * Math.PI / count;

        for (int i = 0; i < count; i++)
        {
            double angle = i * step;
            points.Add(new IfcVector3(
                Position.X + Radius * Math.Cos(angle),
                Position.Y + Radius * Math.Sin(angle),
                0));
        }

        return points;
    }
}

/// <summary>
/// An arbitrary closed 2D profile defined by an ordered list of boundary vertices.
/// </summary>
public sealed class IfcArbitraryClosedProfileDef : IfcProfileDef
{
    public List<IfcVector3> CurvePoints { get; } = [];

    public IfcArbitraryClosedProfileDef() { }

    public IfcArbitraryClosedProfileDef(IEnumerable<IfcVector3> points, string? name = null)
    {
        CurvePoints.AddRange(points);
        ProfileName = name;
    }

    public override IReadOnlyList<IfcVector3> GetBoundaryPoints(int segments = 16) => CurvePoints;
}

/// <summary>
/// A 3D solid geometry created by sweeping a 2D profile along an extrusion direction for a given depth.
/// Conforms to buildingSMART IFC standard IFCEXTRUDEDAREASOLID.
/// </summary>
public sealed class IfcExtrudedAreaSolid
{
    public IfcProfileDef Profile { get; set; }
    public IfcVector3 Position { get; set; } = IfcVector3.Zero;
    public IfcVector3 Direction { get; set; } = IfcVector3.UnitZ;
    public double Depth { get; set; }

    public IfcExtrudedAreaSolid(IfcProfileDef profile, double depth, IfcVector3? position = null, IfcVector3? direction = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Depth = depth;
        Position = position ?? IfcVector3.Zero;
        Direction = direction ?? IfcVector3.UnitZ;
    }

    /// <summary>
    /// Converts the extruded swept solid into a triangulated 3D polygon mesh.
    /// </summary>
    public IfcMesh ToMesh(int segments = 16, string meshName = "ExtrudedSolid")
    {
        var boundary2D = Profile.GetBoundaryPoints(segments);
        int n = boundary2D.Count;
        if (n < 3)
        {
            return IfcMesh.Create(0, meshName, Array.Empty<IfcVector3>(), Array.Empty<int>());
        }

        // Construct orthonormal coordinate frame
        var dirNorm = Direction.Normalized();
        if (dirNorm.Length < 1e-6) dirNorm = IfcVector3.UnitZ;

        IfcVector3 xAxis, yAxis;
        if (Math.Abs(dirNorm.Z) > 0.999)
        {
            xAxis = IfcVector3.UnitX;
            yAxis = IfcVector3.Cross(dirNorm, xAxis).Normalized();
            xAxis = IfcVector3.Cross(yAxis, dirNorm).Normalized();
        }
        else
        {
            xAxis = IfcVector3.Cross(IfcVector3.UnitZ, dirNorm).Normalized();
            yAxis = IfcVector3.Cross(dirNorm, xAxis).Normalized();
        }

        var vertices = new List<IfcVector3>(n * 2);
        var indices = new List<int>(n * 6 + (n - 2) * 6);

        // Transform 2D profile points to 3D base and top vertices
        for (int i = 0; i < n; i++)
        {
            var p2 = boundary2D[i];
            var basePt = Position + (p2.X * xAxis) + (p2.Y * yAxis);
            vertices.Add(basePt);
        }

        var extrudeOffset = dirNorm * Depth;
        for (int i = 0; i < n; i++)
        {
            vertices.Add(vertices[i] + extrudeOffset);
        }

        // Side walls (connecting bottom loop [0..n-1] to top loop [n..2n-1])
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int b0 = i;
            int b1 = next;
            int t0 = i + n;
            int t1 = next + n;

            // Quad formed by b0, b1, t1, t0
            indices.Add(b0);
            indices.Add(b1);
            indices.Add(t1);

            indices.Add(b0);
            indices.Add(t1);
            indices.Add(t0);
        }

        // Bottom cap (reversed winding for outward facing normal)
        var bottom2D = boundary2D.Select(p => new Vector2D(p.X, p.Y)).ToList();
        var bottomTris = TriangulatePolygon(bottom2D);
        for (int i = 0; i < bottomTris.Count; i += 3)
        {
            // Reverse order for bottom facing outward
            indices.Add(bottomTris[i]);
            indices.Add(bottomTris[i + 2]);
            indices.Add(bottomTris[i + 1]);
        }

        // Top cap (normal facing outward)
        for (int i = 0; i < bottomTris.Count; i += 3)
        {
            indices.Add(bottomTris[i] + n);
            indices.Add(bottomTris[i + 1] + n);
            indices.Add(bottomTris[i + 2] + n);
        }

        return IfcMesh.Create(0, meshName, vertices, indices);
    }

    private readonly record struct Vector2D(double X, double Y);

    private static List<int> TriangulatePolygon(IReadOnlyList<Vector2D> poly)
    {
        int n = poly.Count;
        var result = new List<int>((n - 2) * 3);
        if (n < 3) return result;

        if (n == 3)
        {
            result.Add(0);
            result.Add(1);
            result.Add(2);
            return result;
        }

        if (n == 4)
        {
            // Simple quad fan
            result.Add(0); result.Add(1); result.Add(2);
            result.Add(0); result.Add(2); result.Add(3);
            return result;
        }

        // Standard ear-clipping algorithm for simple polygons
        var indices = new List<int>(n);
        for (int i = 0; i < n; i++) indices.Add(i);

        // Ensure CCW orientation
        double signedArea = 0;
        for (int i = 0; i < n; i++)
        {
            var p1 = poly[i];
            var p2 = poly[(i + 1) % n];
            signedArea += (p1.X * p2.Y - p2.X * p1.Y);
        }
        if (signedArea < 0)
        {
            indices.Reverse();
        }

        int count = indices.Count;
        int maxIter = count * 3;
        int iter = 0;
        int idx = 0;

        while (indices.Count > 2 && iter++ < maxIter)
        {
            int prev = (idx - 1 + indices.Count) % indices.Count;
            int curr = idx % indices.Count;
            int next = (idx + 1) % indices.Count;

            int iPrev = indices[prev];
            int iCurr = indices[curr];
            int iNext = indices[next];

            var a = poly[iPrev];
            var b = poly[iCurr];
            var c = poly[iNext];

            if (IsConvex(a, b, c) && !ContainsAnyPoint(poly, indices, prev, curr, next, a, b, c))
            {
                result.Add(iPrev);
                result.Add(iCurr);
                result.Add(iNext);
                indices.RemoveAt(curr);
                iter = 0;
            }
            else
            {
                idx++;
            }
        }

        // Fallback fan if ear-clipping had degeneracies
        if (indices.Count > 2)
        {
            for (int i = 1; i + 1 < indices.Count; i++)
            {
                result.Add(indices[0]);
                result.Add(indices[i]);
                result.Add(indices[i + 1]);
            }
        }

        return result;
    }

    private static bool IsConvex(Vector2D a, Vector2D b, Vector2D c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 1e-9;
    }

    private static bool ContainsAnyPoint(
        IReadOnlyList<Vector2D> poly,
        List<int> indices,
        int prev, int curr, int next,
        Vector2D a, Vector2D b, Vector2D c)
    {
        for (int i = 0; i < indices.Count; i++)
        {
            if (i == prev || i == curr || i == next) continue;
            var pt = poly[indices[i]];
            if (PointInTriangle(pt, a, b, c)) return true;
        }
        return false;
    }

    private static bool PointInTriangle(Vector2D p, Vector2D a, Vector2D b, Vector2D c)
    {
        double d1 = Sign(p, a, b);
        double d2 = Sign(p, b, c);
        double d3 = Sign(p, c, a);

        bool hasNeg = (d1 < -1e-9) || (d2 < -1e-9) || (d3 < -1e-9);
        bool hasPos = (d1 > 1e-9) || (d2 > 1e-9) || (d3 > 1e-9);

        return !(hasNeg && hasPos);
    }

    private static double Sign(Vector2D p1, Vector2D p2, Vector2D p3)
    {
        return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
    }
}
