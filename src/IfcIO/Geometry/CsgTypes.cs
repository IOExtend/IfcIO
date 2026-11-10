namespace IfcIO.Geometry;

/// <summary>
/// Boolean operation type conforming to buildingSMART IFCBOOLEANOPERATOR.
/// </summary>
public enum IfcBooleanOperator
{
    Difference,
    Union,
    Intersection,
}

/// <summary>
/// A flat planar surface defined by a reference position and surface normal vector,
/// conforming to buildingSMART standard IFCPLANE.
/// </summary>
public sealed class IfcPlane
{
    public IfcVector3 Location { get; set; } = IfcVector3.Zero;
    public IfcVector3 Normal { get; set; } = IfcVector3.UnitZ;

    public IfcPlane() { }

    public IfcPlane(IfcVector3 location, IfcVector3 normal)
    {
        Location = location;
        Normal = normal.Normalized();
    }

    /// <summary>
    /// Computes the signed perpendicular distance from the point to this plane.
    /// Positive if on the side of the normal, negative otherwise.
    /// </summary>
    public double SignedDistanceTo(in IfcVector3 point)
    {
        var n = Normal.Normalized();
        return IfcVector3.Dot(n, point - Location);
    }
}

/// <summary>
/// A half-space solid defined by an infinite plane dividing 3D space,
/// conforming to buildingSMART standard IFCHALFSPACESOLID.
/// </summary>
public sealed class IfcHalfSpaceSolid
{
    public IfcPlane BaseSurface { get; set; } = new();

    /// <summary>
    /// If true, the half-space is the region in the direction of the surface normal;
    /// if false, the region is in the opposite direction.
    /// </summary>
    public bool AgreementFlag { get; set; } = true;

    public IfcHalfSpaceSolid() { }

    public IfcHalfSpaceSolid(IfcPlane baseSurface, bool agreementFlag = true)
    {
        BaseSurface = baseSurface;
        AgreementFlag = agreementFlag;
    }

    /// <summary>
    /// Tests whether the point lies inside the half-space volume.
    /// </summary>
    public bool ContainsPoint(in IfcVector3 point)
    {
        double dist = BaseSurface.SignedDistanceTo(point);
        return AgreementFlag ? dist >= -1e-7 : dist <= 1e-7;
    }
}

/// <summary>
/// Represents a Constructive Solid Geometry (CSG) boolean result or clipping result
/// between solid operands, conforming to buildingSMART IFCBOOLEANRESULT and IFCBOOLEANCLIPPINGRESULT.
/// </summary>
public sealed class IfcBooleanResult
{
    public IfcBooleanOperator Operator { get; set; } = IfcBooleanOperator.Difference;

    /// <summary>First operand (base solid or mesh to be clipped/joined).</summary>
    public IfcMesh? FirstMesh { get; set; }
    public IfcExtrudedAreaSolid? FirstSolid { get; set; }
    public IfcRevolvedAreaSolid? FirstRevolved { get; set; }

    /// <summary>Second operand (clipping half-space, cutting solid, or joining solid).</summary>
    public IfcHalfSpaceSolid? SecondHalfSpace { get; set; }
    public IfcExtrudedAreaSolid? SecondSolid { get; set; }
    public IfcMesh? SecondMesh { get; set; }

    /// <summary>
    /// True if this represents an IFCBOOLEANCLIPPINGRESULT (difference with a half-space solid).
    /// </summary>
    public bool IsClippingResult => SecondHalfSpace is not null || Operator == IfcBooleanOperator.Difference;

    public IfcBooleanResult() { }

    public IfcBooleanResult(
        IfcExtrudedAreaSolid solid,
        IfcHalfSpaceSolid halfSpace,
        IfcBooleanOperator op = IfcBooleanOperator.Difference)
    {
        FirstSolid = solid;
        SecondHalfSpace = halfSpace;
        Operator = op;
    }

    public IfcBooleanResult(
        IfcMesh mesh,
        IfcHalfSpaceSolid halfSpace,
        IfcBooleanOperator op = IfcBooleanOperator.Difference)
    {
        FirstMesh = mesh;
        SecondHalfSpace = halfSpace;
        Operator = op;
    }

    /// <summary>
    /// Computes the resulting triangulated 3D mesh by applying the CSG clipping operation.
    /// </summary>
    public IfcMesh ToMesh(string meshName = "BooleanResult")
    {
        var baseMesh = FirstMesh ?? FirstSolid?.ToMesh() ?? FirstRevolved?.ToMesh();
        if (baseMesh is null)
        {
            return IfcMesh.Create(0, meshName, Array.Empty<IfcVector3>(), Array.Empty<int>());
        }

        if (SecondHalfSpace is null)
        {
            return baseMesh;
        }

        return ClipMeshByHalfSpace(baseMesh, SecondHalfSpace, meshName);
    }

    private static IfcMesh ClipMeshByHalfSpace(IfcMesh input, IfcHalfSpaceSolid halfSpace, string meshName)
    {
        var plane = halfSpace.BaseSurface;
        var normal = plane.Normal.Normalized();
        if (!halfSpace.AgreementFlag) normal = -normal;

        // Plane equation: N . X + d = 0
        double planeD = -IfcVector3.Dot(normal, plane.Location);

        var outVertices = new List<IfcVector3>();
        var outIndices = new List<int>();

        var inVerts = input.Vertices;
        var inIndices = input.Indices;

        for (int i = 0; i + 2 < inIndices.Count; i += 3)
        {
            int idx0 = inIndices[i];
            int idx1 = inIndices[i + 1];
            int idx2 = inIndices[i + 2];
            if (idx0 >= inVerts.Count || idx1 >= inVerts.Count || idx2 >= inVerts.Count) continue;

            var p0 = inVerts[idx0];
            var p1 = inVerts[idx1];
            var p2 = inVerts[idx2];

            double d0 = IfcVector3.Dot(normal, p0) + planeD;
            double d1 = IfcVector3.Dot(normal, p1) + planeD;
            double d2 = IfcVector3.Dot(normal, p2) + planeD;

            bool in0 = d0 >= -1e-7;
            bool in1 = d1 >= -1e-7;
            bool in2 = d2 >= -1e-7;

            int insideCount = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0);

            if (insideCount == 3)
            {
                // Entire triangle inside
                AddTriangle(outVertices, outIndices, p0, p1, p2);
            }
            else if (insideCount == 0)
            {
                // Entire triangle clipped out
                continue;
            }
            else if (insideCount == 1)
            {
                // 1 vertex inside: single smaller triangle
                IfcVector3 vIn, vOut1, vOut2;
                double dIn, dOut1, dOut2;

                if (in0)
                {
                    vIn = p0; dIn = d0;
                    vOut1 = p1; dOut1 = d1;
                    vOut2 = p2; dOut2 = d2;
                }
                else if (in1)
                {
                    vIn = p1; dIn = d1;
                    vOut1 = p2; dOut1 = d2;
                    vOut2 = p0; dOut2 = d0;
                }
                else
                {
                    vIn = p2; dIn = d2;
                    vOut1 = p0; dOut1 = d0;
                    vOut2 = p1; dOut2 = d1;
                }

                var i1 = IntersectSegment(vIn, vOut1, dIn, dOut1);
                var i2 = IntersectSegment(vIn, vOut2, dIn, dOut2);

                AddTriangle(outVertices, outIndices, vIn, i1, i2);
            }
            else // insideCount == 2
            {
                // 2 vertices inside: quad split into 2 triangles
                IfcVector3 vOut, vIn1, vIn2;
                double dOut, dIn1, dIn2;

                if (!in0)
                {
                    vOut = p0; dOut = d0;
                    vIn1 = p1; dIn1 = d1;
                    vIn2 = p2; dIn2 = d2;
                }
                else if (!in1)
                {
                    vOut = p1; dOut = d1;
                    vIn1 = p2; dIn1 = d2;
                    vIn2 = p0; dIn2 = d0;
                }
                else
                {
                    vOut = p2; dOut = d2;
                    vIn1 = p0; dIn1 = d0;
                    vIn2 = p1; dIn2 = d1;
                }

                var i1 = IntersectSegment(vIn1, vOut, dIn1, dOut);
                var i2 = IntersectSegment(vIn2, vOut, dIn2, dOut);

                AddTriangle(outVertices, outIndices, vIn1, i1, i2);
                AddTriangle(outVertices, outIndices, vIn1, i2, vIn2);
            }
        }

        return IfcMesh.Create(0, meshName, outVertices, outIndices);
    }

    private static IfcVector3 IntersectSegment(in IfcVector3 a, in IfcVector3 b, double da, double db)
    {
        double diff = da - db;
        if (Math.Abs(diff) < 1e-12) return (a + b) * 0.5;
        double t = da / diff;
        return a + (b - a) * t;
    }

    private static void AddTriangle(
        List<IfcVector3> vertices,
        List<int> indices,
        in IfcVector3 p0, in IfcVector3 p1, in IfcVector3 p2)
    {
        int baseIdx = vertices.Count;
        vertices.Add(p0);
        vertices.Add(p1);
        vertices.Add(p2);
        indices.Add(baseIdx);
        indices.Add(baseIdx + 1);
        indices.Add(baseIdx + 2);
    }
}
