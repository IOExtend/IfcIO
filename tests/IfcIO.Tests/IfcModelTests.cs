using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public class IfcModelTests
{
    [Fact]
    public void IfcGuid_RoundTrip_ParsesCorrectly()
    {
        var original = Guid.NewGuid();
        string ifcGuid = IfcGuid.ToIfcGuid(original);

        Assert.Equal(22, ifcGuid.Length);

        var parsed = IfcGuid.Parse(ifcGuid);
        Assert.NotNull(parsed);
        Assert.Equal(original, parsed.Value);
    }

    [Fact]
    public void IfcGuid_FromElementId_IsDeterministic()
    {
        string id1 = IfcGuid.FromElementId(12345, "TestNamespace");
        string id2 = IfcGuid.FromElementId(12345, "TestNamespace");
        string id3 = IfcGuid.FromElementId(54321, "TestNamespace");

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void StepWriter_EscapesUnicodeCyrillicCorrectly()
    {
        string cyrillic = "Стена монолитная";
        string escaped = StepWriter.Escape(cyrillic);

        Assert.Contains(@"\X2\", escaped);
        Assert.Contains(@"\X0\", escaped);

        string? unescaped = StepWriter.Unescape(escaped);
        Assert.Equal(cyrillic, unescaped);
    }

    [Fact]
    public void StepWriter_RealFormatting_AlwaysIncludesDecimalPoint()
    {
        Assert.Contains(".", StepWriter.Real(0));
        Assert.Contains(".", StepWriter.Real(100));
        Assert.Contains(".", StepWriter.Real(123.456));

        Assert.Contains(".", StepWriter.RealExact(0));
        Assert.Contains(".", StepWriter.RealExact(100));
    }

    [Fact]
    public void IfcModel_BuildsValidCoordinationViewStepFile()
    {
        var model = IfcModel.Create("Residential Complex Building A", IfcSchema.Ifc2x3);
        model.Author = "Test Author";
        model.Organization = "IOExtend";

        var storey1 = model.AddStorey("Floor 1", 0.0);
        var storey2 = model.AddStorey("Floor 2", 3300.0);

        var concrete = new IfcMaterial { Name = "Concrete B25", R = 0.7, G = 0.72, B = 0.75 };

        // Tetrahedron mesh
        var vertices = new[]
        {
            new IfcVector3(0, 0, 0),
            new IfcVector3(2000, 0, 0),
            new IfcVector3(1000, 2000, 0),
            new IfcVector3(1000, 1000, 2000),
        };
        var indices = new[]
        {
            0, 2, 1,
            0, 1, 3,
            1, 2, 3,
            2, 0, 3,
        };

        var wall = model.AddMesh("Стена монолитная 200", vertices, indices, concrete, storey1);
        var pset = wall.GetOrCreatePropertySet("Pset_WallCommon");
        pset.Add("LoadBearing", true);
        pset.Add("Thickness", 200.0);
        pset.Add("FireRating", "REI 120");

        using var sw = new StringWriter();
        model.Save(sw, "BuildingA.ifc");

        string ifc = sw.ToString();

        // 1. Structure
        Assert.StartsWith("ISO-10303-21;", ifc.TrimStart());
        Assert.Contains("HEADER;", ifc);
        Assert.Contains("FILE_SCHEMA(('IFC2X3'));", ifc);
        Assert.Contains("DATA;", ifc);
        Assert.Contains("END-ISO-10303-21;", ifc);

        // 2. Spatial Entities
        Assert.Contains("IFCPROJECT(", ifc);
        Assert.Contains("IFCSITE(", ifc);
        Assert.Contains("IFCBUILDING(", ifc);
        Assert.Contains("IFCBUILDINGSTOREY(", ifc);
        Assert.Contains("IFCRELAGGREGATES(", ifc);
        Assert.Contains("IFCRELCONTAINEDINSPATIALSTRUCTURE(", ifc);

        // 3. Wall classification
        Assert.Contains("IFCWALL(", ifc);

        // 4. Geometry
        Assert.Contains("IFCFACETEDBREP(", ifc);
        Assert.Contains("IFCCLOSEDSHELL(", ifc);
        Assert.Contains("IFCFACE(", ifc);
        Assert.Contains("IFCPOLYLOOP(", ifc);
        Assert.Contains("IFCCARTESIANPOINT(", ifc);

        // 5. Materials
        Assert.Contains("IFCSURFACESTYLE(", ifc);
        Assert.Contains("IFCCOLOURRGB(", ifc);
        Assert.Contains("IFCSTYLEDITEM(", ifc);

        // 6. Properties
        Assert.Contains("IFCPROPERTYSET(", ifc);
        Assert.Contains("Pset_WallCommon", ifc);
        Assert.Contains("IFCPROPERTYSINGLEVALUE(", ifc);
        Assert.Contains("IFCRELDEFINESBYPROPERTIES(", ifc);
    }

    [Fact]
    public void StepReader_ParsesGeneratedStepFile()
    {
        var model = IfcModel.Create("Reader Test", IfcSchema.Ifc2x3);
        model.AddMesh("Колонна К-1", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2]);

        using var sw = new StringWriter();
        model.Save(sw, "test.ifc");

        using var sr = new StringReader(sw.ToString());
        var step = StepReader.Read(sr);

        Assert.Equal("IFC2X3", step.Schema);
        Assert.NotEmpty(step.Entities);
        Assert.Single(step.OfType("IFCPROJECT"));
        Assert.Single(step.OfType("IFCCOLUMN"));
        Assert.Single(step.OfType("IFCFACETEDBREP"));
    }

    [Fact]
    public void IfcModel_Ifc4Schema_EmitsIfc4Header()
    {
        var model = IfcModel.Create("IFC4 Model", IfcSchema.Ifc4);
        using var sw = new StringWriter();
        model.Save(sw, "ifc4.ifc");
        string ifc = sw.ToString();

        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.Contains("DesignTransferView", ifc);
    }
}
