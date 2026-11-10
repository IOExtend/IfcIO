using IfcIO.Geometry;
using IfcIO.Measure;
using IfcIO.Model;
using Xunit;

namespace IfcIO.Tests;

public sealed class MeasureEngineTests
{
    private static IfcMesh CreateUnitCubeMesh()
    {
        var verts = new List<IfcVector3>
        {
            new(0, 0, 0), // 0
            new(1, 0, 0), // 1
            new(1, 1, 0), // 2
            new(0, 1, 0), // 3
            new(0, 0, 1), // 4
            new(1, 0, 1), // 5
            new(1, 1, 1), // 6
            new(0, 1, 1), // 7
        };

        var indices = new List<int>
        {
            // Bottom (z = 0)
            0, 2, 1,  0, 3, 2,
            // Top (z = 1)
            4, 5, 6,  4, 6, 7,
            // Front (y = 0)
            0, 1, 5,  0, 5, 4,
            // Back (y = 1)
            3, 6, 2,  3, 7, 6,
            // Left (x = 0)
            0, 4, 7,  0, 7, 3,
            // Right (x = 1)
            1, 2, 6,  1, 6, 5,
        };

        return IfcMesh.Create(1, "UnitCube", verts, indices);
    }

    [Fact]
    public void MeasureDistance_ComputesCorrectEuclideanAndProjections()
    {
        var p1 = new IfcVector3(0, 0, 0);
        var p2 = new IfcVector3(3, 4, 12); // 3-4-12 triangle -> sqrt(9 + 16 + 144) = sqrt(169) = 13

        var m = IfcMeasureEngine.MeasureDistance(p1, p2);

        Assert.Equal(13.0, m.Distance, 3);
        Assert.Equal(3.0, m.DeltaX, 3);
        Assert.Equal(4.0, m.DeltaY, 3);
        Assert.Equal(12.0, m.DeltaZ, 3);
        Assert.Equal(5.0, m.HorizontalDistance, 3);
        Assert.Equal(12.0, m.VerticalDistance, 3);
        Assert.True(m.PitchDegrees > 60.0);
    }

    [Fact]
    public void MeasureAngle_ComputesCorrectDegrees()
    {
        var dirX = new IfcVector3(1, 0, 0);
        var dirY = new IfcVector3(0, 1, 0);

        double angle = IfcMeasureEngine.MeasureAngle(dirX, dirY);
        Assert.Equal(90.0, angle, 3);

        var a = new IfcVector3(1, 0, 0);
        var vertex = new IfcVector3(0, 0, 0);
        var b = new IfcVector3(0, 1, 0);
        double angle3Pt = IfcMeasureEngine.MeasureAngle(a, vertex, b);
        Assert.Equal(90.0, angle3Pt, 3);
    }

    [Fact]
    public void MeasurePath_ComputesCumulativeLength()
    {
        var pts = new[]
        {
            new IfcVector3(0, 0, 0),
            new IfcVector3(10, 0, 0),
            new IfcVector3(10, 10, 0),
            new IfcVector3(10, 10, 5),
        };

        var poly = IfcMeasureEngine.MeasurePath(pts);
        Assert.Equal(25.0, poly.TotalLength, 3);
        Assert.Equal(3, poly.SegmentLengths.Count);
        Assert.Equal(10.0, poly.SegmentLengths[0], 3);
        Assert.Equal(10.0, poly.SegmentLengths[1], 3);
        Assert.Equal(5.0, poly.SegmentLengths[2], 3);
    }

    [Fact]
    public void TriangleCalculations_ComputeAreaNormalAndClosestPoint()
    {
        var a = new IfcVector3(0, 0, 0);
        var b = new IfcVector3(2, 0, 0);
        var c = new IfcVector3(0, 2, 0);

        double area = IfcMeasureEngine.ComputeTriangleArea(a, b, c);
        Assert.Equal(2.0, area, 3);

        var norm = IfcMeasureEngine.ComputeTriangleNormal(a, b, c);
        Assert.Equal(0.0, norm.X, 3);
        Assert.Equal(0.0, norm.Y, 3);
        Assert.Equal(1.0, norm.Z, 3);

        // Point directly above interior of triangle: (0.5, 0.5, 5.0)
        var closest = IfcMeasureEngine.ClosestPointOnTriangle(new IfcVector3(0.5, 0.5, 5.0), a, b, c);
        Assert.Equal(0.5, closest.X, 3);
        Assert.Equal(0.5, closest.Y, 3);
        Assert.Equal(0.0, closest.Z, 3);

        // Point outside vertex A: (-1, -1, 0)
        var closestA = IfcMeasureEngine.ClosestPointOnTriangle(new IfcVector3(-1, -1, 0), a, b, c);
        Assert.Equal(0.0, closestA.X, 3);
        Assert.Equal(0.0, closestA.Y, 3);
    }

    [Fact]
    public void PointToMesh_MeasuresCorrectShortestDistance()
    {
        var cube = CreateUnitCubeMesh();
        var query = new IfcVector3(0.5, 0.5, 3.0); // 2 meters above top face (z = 1)

        var meas = IfcMeasureEngine.MeasurePointToMesh(query, cube);

        Assert.Equal(2.0, meas.Distance, 3);
        Assert.Equal(0.5, meas.ClosestPoint.X, 3);
        Assert.Equal(0.5, meas.ClosestPoint.Y, 3);
        Assert.Equal(1.0, meas.ClosestPoint.Z, 3);
    }

    [Fact]
    public void MeshCalculations_SurfaceAreaVolumeAndCentroid()
    {
        var cube = CreateUnitCubeMesh();

        double area = IfcMeasureEngine.ComputeMeshSurfaceArea(cube);
        Assert.Equal(6.0, area, 3); // 6 faces of 1x1

        double volume = IfcMeasureEngine.ComputeMeshVolume(cube);
        Assert.Equal(1.0, volume, 3); // 1x1x1 unit cube

        var centroid = IfcMeasureEngine.ComputeMeshCentroid(cube);
        Assert.Equal(0.5, centroid.X, 3);
        Assert.Equal(0.5, centroid.Y, 3);
        Assert.Equal(0.5, centroid.Z, 3);
    }

    [Fact]
    public void MeshToMesh_MeasuresClearance()
    {
        var cube1 = CreateUnitCubeMesh();

        // Second cube offset by 2m in X: from [3, 0, 0] to [4, 1, 1]
        var cube2Verts = cube1.Vertices.Select(v => v + new IfcVector3(3, 0, 0)).ToList();
        var cube2 = IfcMesh.Create(2, "Cube2", cube2Verts, cube1.Indices);

        var m = IfcMeasureEngine.MeasureMeshToMesh(cube1, cube2);

        // Gap between [0..1] and [3..4] in X is 2.0 meters
        Assert.Equal(2.0, m.Distance, 3);
        Assert.Equal(1.0, m.PointA.X, 3);
        Assert.Equal(3.0, m.PointB.X, 3);
    }
}
