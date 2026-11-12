namespace IfcIO.Geometry;

/// <summary>
/// Affine 3D transform: <c>p' = X * p.X + Y * p.Y + Z * p.Z + Origin</c>.
/// The axis columns may be scaled, sheared or mirrored (IFC placements, mapping operators, extrusion shear).
/// </summary>
internal readonly record struct IfcTransform(IfcVector3 X, IfcVector3 Y, IfcVector3 Z, IfcVector3 Origin)
{
    private const double Tolerance = 1e-9;

    public static readonly IfcTransform Identity = new(IfcVector3.UnitX, IfcVector3.UnitY, IfcVector3.UnitZ, IfcVector3.Zero);

    public static IfcTransform Scale(double factor) =>
        new(IfcVector3.UnitX * factor, IfcVector3.UnitY * factor, IfcVector3.UnitZ * factor, IfcVector3.Zero);

    public static IfcTransform Translation(IfcVector3 offset) =>
        new(IfcVector3.UnitX, IfcVector3.UnitY, IfcVector3.UnitZ, offset);

    public double Determinant => IfcVector3.Dot(X, IfcVector3.Cross(Y, Z));

    /// <summary>True when the linear part is the identity (the transform is at most a translation).</summary>
    public bool IsTranslationOnly => Near(X, IfcVector3.UnitX) && Near(Y, IfcVector3.UnitY) && Near(Z, IfcVector3.UnitZ);

    public bool IsIdentity => IsTranslationOnly && Near(Origin, IfcVector3.Zero);

    public IfcVector3 Apply(IfcVector3 p) => (X * p.X) + (Y * p.Y) + (Z * p.Z) + Origin;

    public IfcVector3 ApplyVector(IfcVector3 v) => (X * v.X) + (Y * v.Y) + (Z * v.Z);

    /// <summary>Returns the transform that applies <paramref name="inner"/> first and then this transform.</summary>
    public IfcTransform Multiply(IfcTransform inner) =>
        new(ApplyVector(inner.X), ApplyVector(inner.Y), ApplyVector(inner.Z), Apply(inner.Origin));

    /// <summary>
    /// Transforms the mesh vertices. Triangle winding is reversed when the transform mirrors,
    /// so faces keep pointing outwards.
    /// </summary>
    public IfcMesh ApplyTo(IfcMesh mesh)
    {
        if (IsIdentity) return mesh;

        var vertices = new IfcVector3[mesh.Vertices.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Apply(mesh.Vertices[i]);
        }

        IReadOnlyList<int> indices = mesh.Indices;
        if (Determinant < 0)
        {
            var flipped = indices.ToArray();
            for (int i = 0; i + 2 < flipped.Length; i += 3)
            {
                (flipped[i + 1], flipped[i + 2]) = (flipped[i + 2], flipped[i + 1]);
            }
            indices = flipped;
        }

        return IfcMesh.Create(mesh.Id, mesh.Name, vertices, indices);
    }

    private static bool Near(IfcVector3 a, IfcVector3 b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance && Math.Abs(a.Z - b.Z) <= Tolerance;
}
