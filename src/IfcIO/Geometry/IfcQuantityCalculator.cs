using System.Globalization;
using IfcIO.Model;

namespace IfcIO.Geometry;

/// <summary>
/// Computational geometry engine for calculating buildingSMART Base Quantities (QTO)
/// directly from 3D polygon meshes and parametric solids.
/// </summary>
public static class IfcQuantityCalculator
{
    /// <summary>
    /// Computes the signed/absolute enclosed volume of a 3D polygon triangle mesh using the divergence theorem.
    /// </summary>
    public static double ComputeMeshVolume(IfcMesh mesh)
    {
        if (mesh is null || mesh.Vertices.Count < 3 || mesh.Indices.Count < 3) return 0.0;

        double totalVolume = 0.0;
        var verts = mesh.Vertices;
        var inds = mesh.Indices;

        for (int i = 0; i + 2 < inds.Count; i += 3)
        {
            int i0 = inds[i];
            int i1 = inds[i + 1];
            int i2 = inds[i + 2];

            if (i0 < 0 || i0 >= verts.Count ||
                i1 < 0 || i1 >= verts.Count ||
                i2 < 0 || i2 >= verts.Count)
                continue;

            var v0 = verts[i0];
            var v1 = verts[i1];
            var v2 = verts[i2];

            // Signed volume of tetrahedron from origin: 1/6 * (v0 . (v1 x v2))
            var cross = IfcVector3.Cross(v1, v2);
            totalVolume += IfcVector3.Dot(v0, cross);
        }

        return Math.Abs(totalVolume) / 6.0;
    }

    /// <summary>
    /// Computes the total surface area of all polygon faces in a 3D mesh.
    /// </summary>
    public static double ComputeMeshSurfaceArea(IfcMesh mesh)
    {
        if (mesh is null || mesh.Vertices.Count < 3 || mesh.Indices.Count < 3) return 0.0;

        double totalArea = 0.0;
        var verts = mesh.Vertices;
        var inds = mesh.Indices;

        for (int i = 0; i + 2 < inds.Count; i += 3)
        {
            int i0 = inds[i];
            int i1 = inds[i + 1];
            int i2 = inds[i + 2];

            if (i0 < 0 || i0 >= verts.Count ||
                i1 < 0 || i1 >= verts.Count ||
                i2 < 0 || i2 >= verts.Count)
                continue;

            var v0 = verts[i0];
            var v1 = verts[i1];
            var v2 = verts[i2];

            var cross = IfcVector3.Cross(v1 - v0, v2 - v0);
            totalArea += cross.Length * 0.5;
        }

        return totalArea;
    }

    /// <summary>
    /// Computes the 2D cross-sectional area of a profile definition.
    /// </summary>
    public static double ComputeProfileArea(IfcProfileDef profile)
    {
        if (profile is null) return 0.0;

        if (profile is IfcRectangleProfileDef rect)
        {
            return Math.Abs(rect.XDim * rect.YDim);
        }

        if (profile is IfcCircleProfileDef circle)
        {
            return Math.PI * circle.Radius * circle.Radius;
        }

        var pts = profile.GetBoundaryPoints(32);
        if (pts.Count < 3) return 0.0;

        // Shoelace formula in 2D
        double area = 0.0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p1 = pts[i];
            var p2 = pts[(i + 1) % pts.Count];
            area += (p1.X * p2.Y) - (p2.X * p1.Y);
        }

        return Math.Abs(area) * 0.5;
    }

    /// <summary>
    /// Computes the 2D perimeter of a profile definition.
    /// </summary>
    public static double ComputeProfilePerimeter(IfcProfileDef profile)
    {
        if (profile is null) return 0.0;

        if (profile is IfcRectangleProfileDef rect)
        {
            return 2.0 * (Math.Abs(rect.XDim) + Math.Abs(rect.YDim));
        }

        if (profile is IfcCircleProfileDef circle)
        {
            return 2.0 * Math.PI * Math.Abs(circle.Radius);
        }

        var pts = profile.GetBoundaryPoints(32);
        if (pts.Count < 2) return 0.0;

        double perimeter = 0.0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p1 = pts[i];
            var p2 = pts[(i + 1) % pts.Count];
            perimeter += (p2 - p1).Length;
        }

        return perimeter;
    }

    /// <summary>
    /// Automatically calculates buildingSMART Base Quantities for an IFC element based on its 3D geometry.
    /// </summary>
    public static IfcQuantitySet CalculateBaseQuantities(IfcElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        double grossVolume = 0.0;
        double totalSurfaceArea = 0.0;
        double length = 0.0;
        double width = 0.0;
        double height = 0.0;
        double footprintArea = 0.0;
        double perimeter = 0.0;

        if (element.Solid is not null)
        {
            var solid = element.Solid;
            double profArea = ComputeProfileArea(solid.Profile);
            perimeter = ComputeProfilePerimeter(solid.Profile);
            height = solid.Depth;
            grossVolume = profArea * solid.Depth;
            footprintArea = profArea;
            double sideArea = perimeter * solid.Depth;
            totalSurfaceArea = (2.0 * profArea) + sideArea;

            if (solid.Profile is IfcRectangleProfileDef rect)
            {
                length = Math.Max(rect.XDim, rect.YDim);
                width = Math.Min(rect.XDim, rect.YDim);
            }
            else if (solid.Profile is IfcCircleProfileDef circle)
            {
                length = circle.Radius * 2.0;
                width = circle.Radius * 2.0;
            }
            else
            {
                length = Math.Sqrt(profArea);
                width = length;
            }
        }
        else if (element.RevolvedSolid is not null)
        {
            var mesh = element.RevolvedSolid.ToMesh();
            grossVolume = ComputeMeshVolume(mesh);
            totalSurfaceArea = ComputeMeshSurfaceArea(mesh);
            var aabb = mesh.BoundingBox;
            length = aabb.Max.X - aabb.Min.X;
            width = aabb.Max.Y - aabb.Min.Y;
            height = aabb.Max.Z - aabb.Min.Z;
            footprintArea = length * width;
            perimeter = 2.0 * (length + width);
        }
        else if (element.Mesh is not null)
        {
            var mesh = element.Mesh;
            grossVolume = ComputeMeshVolume(mesh);
            totalSurfaceArea = ComputeMeshSurfaceArea(mesh);
            var aabb = mesh.BoundingBox;
            length = aabb.Max.X - aabb.Min.X;
            width = aabb.Max.Y - aabb.Min.Y;
            height = aabb.Max.Z - aabb.Min.Z;
            footprintArea = length * width;
            perimeter = 2.0 * (length + width);
        }

        // Calculate opening deductions
        double openingsVolume = 0.0;
        double openingsSideArea = 0.0;
        foreach (var op in element.Openings)
        {
            if (op.Solid is not null)
            {
                double opProfArea = ComputeProfileArea(op.Solid.Profile);
                double opPerim = ComputeProfilePerimeter(op.Solid.Profile);
                openingsVolume += opProfArea * op.Solid.Depth;
                openingsSideArea += opPerim * op.Solid.Depth;
            }
            else if (op.Mesh is not null)
            {
                openingsVolume += ComputeMeshVolume(op.Mesh);
                openingsSideArea += ComputeMeshSurfaceArea(op.Mesh) * 0.5;
            }
        }

        double netVolume = Math.Max(0.0, grossVolume - openingsVolume);
        double netSideArea = Math.Max(0.0, totalSurfaceArea - (2.0 * footprintArea) - openingsSideArea);
        double grossSideArea = Math.Max(0.0, totalSurfaceArea - (2.0 * footprintArea));

        string typeUpper = element.IfcType.ToUpperInvariant();

        if (typeUpper.Contains("WALL", StringComparison.Ordinal))
        {
            return StandardQtoSets.WallBaseQuantities(
                length: Math.Round(length, 4),
                width: Math.Round(width, 4),
                height: Math.Round(height, 4),
                grossFootprintArea: Math.Round(footprintArea, 4),
                netFootprintArea: Math.Round(footprintArea, 4),
                grossSideArea: Math.Round(grossSideArea, 4),
                netSideArea: Math.Round(netSideArea, 4),
                grossVolume: Math.Round(grossVolume, 4),
                netVolume: Math.Round(netVolume, 4));
        }

        if (typeUpper.Contains("SLAB", StringComparison.Ordinal))
        {
            return StandardQtoSets.SlabBaseQuantities(
                width: Math.Round(width, 4),
                length: Math.Round(length, 4),
                depth: Math.Round(height, 4),
                perimeter: Math.Round(perimeter, 4),
                grossArea: Math.Round(footprintArea, 4),
                netArea: Math.Round(footprintArea, 4),
                grossVolume: Math.Round(grossVolume, 4),
                netVolume: Math.Round(netVolume, 4));
        }

        if (typeUpper.Contains("COLUMN", StringComparison.Ordinal))
        {
            return StandardQtoSets.ColumnBaseQuantities(
                length: Math.Round(height, 4),
                crossSectionArea: Math.Round(footprintArea, 4),
                outerSurfaceArea: Math.Round(grossSideArea, 4),
                totalSurfaceArea: Math.Round(totalSurfaceArea, 4),
                grossVolume: Math.Round(grossVolume, 4),
                netVolume: Math.Round(netVolume, 4));
        }

        if (typeUpper.Contains("BEAM", StringComparison.Ordinal))
        {
            return StandardQtoSets.BeamBaseQuantities(
                length: Math.Round(length, 4),
                crossSectionArea: Math.Round(width * height, 4),
                outerSurfaceArea: Math.Round(totalSurfaceArea, 4),
                grossVolume: Math.Round(grossVolume, 4),
                netVolume: Math.Round(netVolume, 4));
        }

        if (typeUpper.Contains("WINDOW", StringComparison.Ordinal))
        {
            return StandardQtoSets.WindowBaseQuantities(
                height: Math.Round(height, 4),
                width: Math.Round(width, 4),
                area: Math.Round(footprintArea > 0 ? footprintArea : length * height, 4),
                perimeter: Math.Round(perimeter, 4));
        }

        if (typeUpper.Contains("DOOR", StringComparison.Ordinal))
        {
            return StandardQtoSets.DoorBaseQuantities(
                height: Math.Round(height, 4),
                width: Math.Round(width, 4),
                area: Math.Round(footprintArea > 0 ? footprintArea : length * height, 4),
                perimeter: Math.Round(perimeter, 4));
        }

        // Generic fallback Qto set
        var genericQto = new IfcQuantitySet("Qto_BaseQuantities");
        genericQto.AddVolume("GrossVolume", Math.Round(grossVolume, 4));
        genericQto.AddVolume("NetVolume", Math.Round(netVolume, 4));
        genericQto.AddArea("TotalSurfaceArea", Math.Round(totalSurfaceArea, 4));
        genericQto.AddLength("Height", Math.Round(height, 4));
        genericQto.AddLength("Length", Math.Round(length, 4));
        genericQto.AddLength("Width", Math.Round(width, 4));
        return genericQto;
    }
}
