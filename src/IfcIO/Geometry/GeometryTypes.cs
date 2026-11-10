namespace IfcIO.Geometry;

/// <summary>
/// A 3D coordinate vector with double precision.
/// </summary>
public readonly record struct IfcVector3(double X, double Y, double Z)
{
    public static readonly IfcVector3 Zero = new(0, 0, 0);
    public static readonly IfcVector3 UnitX = new(1, 0, 0);
    public static readonly IfcVector3 UnitY = new(0, 1, 0);
    public static readonly IfcVector3 UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public IfcVector3 Normalized()
    {
        double len = Length;
        return len > 1e-12 ? new(X / len, Y / len, Z / len) : Zero;
    }

    public static IfcVector3 operator +(IfcVector3 a, IfcVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static IfcVector3 operator -(IfcVector3 a, IfcVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static IfcVector3 operator *(IfcVector3 a, double scalar) => new(a.X * scalar, a.Y * scalar, a.Z * scalar);
    public static IfcVector3 operator *(double scalar, IfcVector3 a) => new(a.X * scalar, a.Y * scalar, a.Z * scalar);

    public static double Dot(IfcVector3 a, IfcVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static IfcVector3 Cross(IfcVector3 a, IfcVector3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X
    );

    public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}

/// <summary>
/// Axis-aligned 3D bounding box.
/// </summary>
public readonly record struct IfcBoundingBox(IfcVector3 Min, IfcVector3 Max)
{
    public static readonly IfcBoundingBox Empty = new(
        new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity)
    );

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public IfcVector3 Size => IsEmpty ? IfcVector3.Zero : new(Max.X - Min.X, Max.Y - Min.Y, Max.Z - Min.Z);

    public IfcVector3 Center => IsEmpty ? IfcVector3.Zero : new(
        (Min.X + Max.X) * 0.5,
        (Min.Y + Max.Y) * 0.5,
        (Min.Z + Max.Z) * 0.5
    );

    public static IfcBoundingBox FromPoints(IReadOnlyList<IfcVector3> points)
    {
        if (points.Count == 0) return Empty;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;
        }

        return new IfcBoundingBox(new(minX, minY, minZ), new(maxX, maxY, maxZ));
    }
}

/// <summary>
/// A 3D polygon mesh representation composed of vertices and triangle indices.
/// </summary>
public sealed class IfcMesh
{
    public int Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public required IReadOnlyList<IfcVector3> Vertices { get; init; }
    public required IReadOnlyList<int> Indices { get; init; }
    public IfcBoundingBox BoundingBox { get; init; }

    public int TriangleCount => Indices.Count / 3;

    public IfcMesh() { }

    public static IfcMesh Create(int id, string name, IReadOnlyList<IfcVector3> vertices, IReadOnlyList<int> indices)
    {
        return new IfcMesh
        {
            Id = id,
            Name = name,
            Vertices = vertices,
            Indices = indices,
            BoundingBox = IfcBoundingBox.FromPoints(vertices),
        };
    }
}
