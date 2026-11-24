using System.Globalization;
using System.Text;
using IfcIO.Geometry;
using Xunit;

namespace IfcIO.Tests;

public sealed class BrepFaceTests
{
    [Fact]
    public void Open_TriangulatesAConcaveFaceWithoutSpillingPastTheBoundary()
    {
        var mesh = ReadFace(
            [[(2, 1), (1, 1), (1, 2), (0, 2), (0, 0), (2, 0)]]);

        Assert.NotNull(mesh);
        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal(3.0, Area(mesh), 9);
        Assert.All(Triangles(mesh), triangle => Assert.True(triangle.Z > 0));
    }

    [Fact]
    public void Open_SubtractsContainedBoundsButKeepsADisjointRegion()
    {
        var mesh = ReadFace(
            [[(0, 0), (4, 0), (4, 4), (0, 4)],
             [(1, 1), (3, 1), (3, 3), (1, 3)],
             [(5, 0), (6, 0), (6, 1), (5, 1)]]);

        Assert.NotNull(mesh);
        Assert.Equal(13.0, Area(mesh), 9);
        Assert.All(Triangles(mesh), triangle => Assert.True(triangle.Z > 0));
        Assert.DoesNotContain(TriangleCentroids(mesh), p => p.X > 1 && p.X < 3 && p.Y > 1 && p.Y < 3);
        Assert.Contains(TriangleCentroids(mesh), p => p.X > 5);
    }

    [Fact]
    public void Open_ContainsANestedIslandAndSupportsMultipleHoles()
    {
        var mesh = ReadFace(
            [[(0, 0), (10, 0), (10, 10), (0, 10)],
             [(1, 1), (4, 1), (4, 4), (1, 4)],
             [(2, 2), (3, 2), (3, 3), (2, 3)],
             [(6, 6), (9, 6), (9, 9), (6, 9)]]);

        Assert.NotNull(mesh);
        Assert.Equal(83.0, Area(mesh), 9);
        Assert.Contains(TriangleCentroids(mesh), p => p.X > 2 && p.X < 3 && p.Y > 2 && p.Y < 3);
    }

    [Fact]
    public void Open_RespectsOuterBoundOrientation()
    {
        var mesh = ReadFace([[(0, 0), (3, 0), (3, 3), (0, 3)]], reverseOuter: true);

        Assert.NotNull(mesh);
        Assert.Equal(9.0, Area(mesh), 9);
        Assert.All(Triangles(mesh), triangle => Assert.True(triangle.Z < 0));
    }

    [Fact]
    public void Open_OmitsTheFaceWhenAHoleHasAnInvalidCoordinate()
    {
        var mesh = ReadFace(
            [[(0, 0), (4, 0), (4, 4), (0, 4)], [(1, 1), (3, 1), (3, 3), (1, 3)]],
            invalidCoordinate: true);

        Assert.Null(mesh);
    }

    [Fact]
    public void Open_PreservesAreaAndWindingOfAnOffsetVerticalFaceWithAHole()
    {
        var mesh = ReadFace(
            [[(0, 0), (4, 0), (4, 4), (0, 4)], [(1, 1), (3, 1), (3, 3), (1, 3)]],
            vertical: true);

        Assert.NotNull(mesh);
        Assert.Equal(12.0, Area(mesh), 9);
        Assert.All(Triangles(mesh), triangle => Assert.True(triangle.Y < 0));
        Assert.All(mesh.Vertices, vertex => Assert.Equal(2_000_000.0, vertex.Y));
    }

    [Fact]
    public void Open_RejectsCrossingBoundsRatherThanFillingAnAmbiguousHole()
    {
        var mesh = ReadFace(
            [[(0, 0), (4, 0), (4, 4), (0, 4)], [(3, 1), (5, 1), (5, 3), (3, 3)]]);

        Assert.Null(mesh);
    }

    [Fact]
    public void Open_KeepsSmallContoursIndependentlyOfTheFaceExtent()
    {
        var separate = ReadFace([[(0, 0), (4, 0), (4, 4), (0, 4)],
            [(2_000_000, 0), (2_000_001, 0), (2_000_001, 1), (2_000_000, 1)]]);
        Assert.NotNull(separate);
        Assert.Equal(17.0, Area(separate), 9);

        var hole = ReadFace([[(0, 0), (100, 0), (100, 100), (0, 100)],
            [(50, 50), (50.00005, 50), (50.00005, 50.00005), (50, 50.00005)]]);
        Assert.NotNull(hole);
        Assert.Equal(8, hole.TriangleCount);
        Assert.Equal(10_000.0 - 50e-6 * 50e-6, Area(hole), 10);
    }

    [Fact]
    public void Open_LargeConvexFaceDoesNotRequireQuadraticValidation()
    {
        const int size = 7_000;
        var circle = Enumerable.Range(0, size).Select(index =>
        {
            double angle = index * (2.0 * Math.PI / size);
            return (Math.Cos(angle), Math.Sin(angle));
        }).ToArray();

        var mesh = ReadFace([circle]);

        Assert.NotNull(mesh);
        Assert.Equal(size - 2, mesh.TriangleCount);
        Assert.Equal(size / 2.0 * Math.Sin(2.0 * Math.PI / size), Area(mesh), 8);
    }

    [Fact]
    public void Open_TriangulatesARegularGridOfHolesWithinTheWorkLimit()
    {
        List<(double X, double Y)[]> rings = [[(0, 0), (49, 0), (49, 49), (0, 49)]];
        for (int y = 1; y < 48; y += 3)
            for (int x = 1; x < 48; x += 3)
                rings.Add([(x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1)]);

        var mesh = ReadFace(rings);

        Assert.NotNull(mesh);
        Assert.Equal(2_145.0, Area(mesh), 8);
    }

    private static IfcMesh? ReadFace(List<(double X, double Y)[]> rings,
        bool reverseOuter = false, bool invalidCoordinate = false, bool vertical = false)
    {
        var text = new StringBuilder("""
            ISO-10303-21;
            HEADER; FILE_SCHEMA(('IFC2X3')); ENDSEC;
            DATA;
            #1=IFCWALL('01vctVUKr0kugbFTf53O9L',$,'Face test',$,$,$,#2,$);
            #2=IFCPRODUCTDEFINITIONSHAPE($,$,(#3));
            #3=IFCSHAPEREPRESENTATION($,'Body','Brep',(#4));
            #4=IFCFACETEDBREP(#5);
            #5=IFCCLOSEDSHELL((#6));
            """);
        text.AppendLine();
        int next = 20;
        var bounds = new List<string>();
        for (int ring = 0; ring < rings.Count; ring++)
        {
            var pointIds = new List<string>();
            for (int index = 0; index < rings[ring].Length; index++)
            {
                var (x, y) = rings[ring][index];
                int pointId = next++;
                string coordinate = invalidCoordinate && ring == 1 && index == 0 ? "$"
                    : (vertical ? 1_000_000 + x : x).ToString(CultureInfo.InvariantCulture);
                double worldY = vertical ? 2_000_000 : y;
                double worldZ = vertical ? 3_000_000 + y : 0;
                pointIds.Add($"#{pointId}");
                text.AppendLine(CultureInfo.InvariantCulture, $"#{pointId}=IFCCARTESIANPOINT(({coordinate},{worldY},{worldZ}));");
            }
            int poly = next++;
            text.AppendLine(CultureInfo.InvariantCulture, $"#{poly}=IFCPOLYLOOP(({string.Join(",", pointIds)}));");
            int bound = next++;
            text.AppendLine(CultureInfo.InvariantCulture, $"#{bound}=IFCFACE{(ring == 0 ? "OUTER" : "")}BOUND(#{poly},.{(ring == 0 && reverseOuter ? "F" : "T")}.);");
            bounds.Add($"#{bound}");
        }
        text.AppendLine(CultureInfo.InvariantCulture, $"#6=IFCFACE(({string.Join(",", bounds)}));");
        text.AppendLine("ENDSEC; END-ISO-10303-21;");
        var model = IfcModel.Open(new StringReader(text.ToString()));
        return Assert.Single(model.AllElements).Mesh;
    }

    private static IEnumerable<IfcVector3> Triangles(IfcMesh mesh)
    {
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]];
            var b = mesh.Vertices[mesh.Indices[i + 1]];
            var c = mesh.Vertices[mesh.Indices[i + 2]];
            yield return IfcVector3.Cross(b - a, c - a);
        }
    }

    private static IEnumerable<IfcVector3> TriangleCentroids(IfcMesh mesh)
    {
        for (int i = 0; i < mesh.Indices.Count; i += 3)
            yield return (mesh.Vertices[mesh.Indices[i]] + mesh.Vertices[mesh.Indices[i + 1]] + mesh.Vertices[mesh.Indices[i + 2]]) * (1.0 / 3.0);
    }

    private static double Area(IfcMesh mesh) => Triangles(mesh).Sum(n => n.Length) / 2.0;
}
