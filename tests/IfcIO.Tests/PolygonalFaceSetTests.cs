using IfcIO.Geometry;
using Xunit;

namespace IfcIO.Tests;

public class PolygonalFaceSetTests
{
    private static IfcMesh? OpenMesh(string schema, string faces, string coordinateList, string faceRefs, string pointNumbers = "$")
    {
        string ifc = $$"""
            ISO-10303-21;
            HEADER;
            FILE_SCHEMA(('{{schema}}'));
            ENDSEC;
            DATA;
            #1=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',$,'Indexed faces',$,$,$,$,(#7),#2);
            #2=IFCUNITASSIGNMENT((#3));
            #3=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
            #4=IFCCARTESIANPOINT((100.,200.,300.));
            #5=IFCAXIS2PLACEMENT3D(#4,$,$);
            #6=IFCLOCALPLACEMENT($,#5);
            #7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#15,$);
            #8=IFCCARTESIANPOINTLIST3D({{coordinateList}}{{(schema == "IFC4" ? "" : ",$")}});
            #9=IFCPOLYGONALFACESET(#8,.F.,{{faceRefs}},{{pointNumbers}});
            #10=IFCSHAPEREPRESENTATION(#7,'Body','Tessellation',(#9));
            #11=IFCPRODUCTDEFINITIONSHAPE($,$,(#10));
            #12=IFCWALL('1YvctVUKr0kugbFTf53O9L',$,'Wall',$,$,#6,#11,$,$);
            #14=IFCCARTESIANPOINT((0.,0.,0.));
            #15=IFCAXIS2PLACEMENT3D(#14,$,$);
            {{faces}}
            ENDSEC;
            END-ISO-10303-21;
            """;
        return Assert.Single(IfcModel.Open(new StringReader(ifc)).AllElements).Mesh;
    }

    [Theory]
    [InlineData("IFC4")]
    [InlineData("IFC4X3_ADD2")]
    public void Open_IndexedFaceWithHole_UsesPointNumbersAndPreservesPlacement(string schema)
    {
        var mesh = OpenMesh(schema,
            "#20=IFCINDEXEDPOLYGONALFACEWITHVOIDS((1,2,3,4),((5,6,7,8)));",
            "((1.,1.,0.),(3.,1.,0.),(3.,3.,0.),(1.,3.,0.),(0.,0.,0.),(4.,0.,0.),(4.,4.,0.),(0.,4.,0.),(1000.,0.,0.))",
            "(#20)", "(5,6,7,8,1,4,3,2)");

        Assert.NotNull(mesh);
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(8, mesh.TriangleCount);
        Assert.Equal(12, SignedArea(mesh), 9);
        Assert.Equal(new IfcBoundingBox(new(100, 200, 300), new(104, 204, 300)), mesh.BoundingBox);
    }

    [Fact]
    public void Open_OmitsInvalidFacesAndKeepsConcavityAndOrientation()
    {
        var mesh = OpenMesh("IFC4", """
            #20=IFCINDEXEDPOLYGONALFACE((6,5,4,3,2,1));
            #21=IFCINDEXEDPOLYGONALFACE((1,2,99));
            #22=IFCINDEXEDPOLYGONALFACEWITHVOIDS((1,2,3),((1,0,2)));
            """,
            "((0.,0.,0.),(2.,0.,0.),(2.,1.,0.),(1.,1.,0.),(1.,2.,0.),(0.,2.,0.))", "(#20,#21,#22,#20)");

        Assert.NotNull(mesh);
        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal(-3, SignedArea(mesh), 9);
    }

    [Fact]
    public void Open_IndexedFaceWithAnExteriorHole_DoesNotFillItAsAnIsland()
    {
        var mesh = OpenMesh("IFC4", "#20=IFCINDEXEDPOLYGONALFACEWITHVOIDS((1,2,3,4),((5,6,7)));",
            "((0.,0.,0.),(4.,0.,0.),(4.,4.,0.),(0.,4.,0.),(5.,0.,0.),(6.,0.,0.),(6.,1.,0.))", "(#20)");
        Assert.Null(mesh);
    }

    [Theory]
    [InlineData("#20=IFCINDEXEDPOLYGONALFACE((1,2,3,4));", "((0.,0.,0.),(4.,0.,0.),(4.,4.,1.),(0.,4.,0.))")]
    [InlineData("#20=IFCINDEXEDPOLYGONALFACE((1,2,3,));", "((0.,0.,0.),(4.,0.,0.),(4.,4.,0.))")]
    [InlineData("#20=IFCINDEXEDPOLYGONALFACEWITHVOIDS((1,2,3,4),((5,6,7)));",
        "((0.,0.,0.),(4.,0.,0.),(4.,4.,0.),(0.,4.,0.),(1.,1.,1.),(2.,1.,1.),(2.,2.,1.))")]
    public void Open_MalformedFace_IsOmitted(string face, string coordinates)
    {
        Assert.Null(OpenMesh("IFC4", face, coordinates, "(#20)"));
    }

    [Fact]
    public void Open_GoodFacesShareVerticesEvenWithInvalidNeighbours()
    {
        var mesh = OpenMesh("IFC4", """
            #20=IFCINDEXEDPOLYGONALFACE((1,2,3));
            #21=IFCINDEXEDPOLYGONALFACE((1,3,4));
            #22=IFCINDEXEDPOLYGONALFACE((1,3,5));
            """, "((0.,0.,0.),(4.,0.,0.),(4.,4.,0.),(0.,4.,0.),($,0.,0.))", "(#20,$,#21,#22)");
        Assert.NotNull(mesh);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal([0, 1, 2, 0, 2, 3], mesh.Indices);
        Assert.Equal(16, SignedArea(mesh), 9);
    }

    private static double SignedArea(IfcMesh mesh)
    {
        double sum = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]];
            var b = mesh.Vertices[mesh.Indices[i + 1]];
            var c = mesh.Vertices[mesh.Indices[i + 2]];
            sum += IfcVector3.Cross(b - a, c - a).Z * 0.5;
        }
        return sum;
    }
}
