using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
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
    public void IfcModel_Ifc4Schema_EmitsIfc4HeaderAndTessellation()
    {
        var model = IfcModel.Create("IFC4 Model", IfcSchema.Ifc4);
        model.UseTessellationInIfc4 = true;
        model.AddMesh("Плита перекрытия", [new(0, 0, 0), new(2000, 0, 0), new(0, 2000, 0)], [0, 1, 2]);

        using var sw = new StringWriter();
        model.Save(sw, "ifc4.ifc");
        string ifc = sw.ToString();

        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.Contains("DesignTransferView", ifc);
        Assert.Contains("IFCTRIANGULATEDFACESET(", ifc);
        Assert.Contains("IFCCARTESIANPOINTLIST3D(", ifc);
        Assert.Contains("'Tessellation'", ifc);
    }

    [Fact]
    public void IfcModel_TypeProducts_EmitsTypeDefinitionsAndRelation()
    {
        var model = IfcModel.Create("Typed Project", IfcSchema.Ifc4);
        var wallType = model.GetOrCreateType("W200_Concrete", "IFCWALLTYPE");

        var elem = model.AddMesh("Стена 1", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2]);
        elem.TypeProduct = wallType;

        using var sw = new StringWriter();
        model.Save(sw, "typed.ifc");
        string ifc = sw.ToString();

        Assert.Contains("IFCWALLTYPE(", ifc);
        Assert.Contains("IFCRELDEFINESBYTYPE(", ifc);
        Assert.Contains("W200_Concrete", ifc);
    }

    [Fact]
    public void IfcModel_SaveZip_CreatesValidIfcZipArchive()
    {
        var model = IfcModel.Create("Zipped Building", IfcSchema.Ifc2x3);
        model.AddMesh("Фундамент ФМ-1", [new(0, 0, 0), new(5000, 0, 0), new(0, 5000, 0)], [0, 1, 2]);

        string tempZip = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.ifczip");
        try
        {
            model.Save(tempZip);
            Assert.True(File.Exists(tempZip));

            using var fs = File.OpenRead(tempZip);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            Assert.Single(zip.Entries);
            Assert.EndsWith(".ifc", zip.Entries[0].Name, StringComparison.OrdinalIgnoreCase);

            using var entryStream = zip.Entries[0].Open();
            using var reader = new StreamReader(entryStream);
            string ifcContent = reader.ReadToEnd();
            Assert.Contains("ISO-10303-21;", ifcContent);
            Assert.Contains("IFCFOOTING(", ifcContent);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }

    [Fact]
    public void IfcModel_Open_Roundtrip_ReconstructsModelElementsAndMeshes()
    {
        var original = IfcModel.Create("Roundtrip Project", IfcSchema.Ifc2x3);
        var storey = original.AddStorey("2nd Floor", 3600.0);
        var mat = new IfcMaterial { Name = "Steel S355", R = 0.2, G = 0.4, B = 0.8 };

        var wall = original.AddMesh("Балка Б-1", [new(0, 0, 0), new(100, 0, 0), new(0, 50, 0)], [0, 1, 2], mat, storey);
        var pset = wall.GetOrCreatePropertySet("Pset_BeamCommon");
        pset.Add("Span", 6000.0);
        pset.Add("Profile", "HEA 300");

        string tempIfc = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}.ifc");
        try
        {
            original.Save(tempIfc);

            // Reopen
            var restored = IfcModel.Open(tempIfc);

            Assert.Equal("Roundtrip Project", restored.ProjectName);
            Assert.NotEmpty(restored.Storeys);
            var restoredStorey = restored.Storeys.Find(s => s.Name == "2nd Floor");
            Assert.NotNull(restoredStorey);
            Assert.Equal(3600.0, restoredStorey.Elevation, precision: 2);

            Assert.NotEmpty(restored.AllElements);
            var restoredElem = restored.AllElements.First();
            Assert.Contains("Балка", restoredElem.Name);
            Assert.NotNull(restoredElem.Mesh);
            Assert.Equal(3, restoredElem.Mesh.Vertices.Count);
            Assert.Equal(3, restoredElem.Mesh.Indices.Count);

            // Verify property roundtrip
            var restoredPset = restoredElem.PropertySets.Find(p => p.Name == "Pset_BeamCommon");
            Assert.NotNull(restoredPset);
            Assert.Equal("HEA 300", restoredPset.GetValue("Profile"));

            // Verify material roundtrip
            Assert.NotNull(restoredElem.Material);
            Assert.Equal("Steel S355", restoredElem.Material.Name);
            Assert.Equal(0.2, restoredElem.Material.R, precision: 2);
        }
        finally
        {
            if (File.Exists(tempIfc)) File.Delete(tempIfc);
        }
    }

    [Fact]
    public void IfcModel_SaveGlb_ProducesValidGlbContainer()
    {
        var model = IfcModel.Create("GLB Export Test", IfcSchema.Ifc4);
        var mat = new IfcMaterial { Name = "Glass", R = 0.1, G = 0.8, B = 0.9, Alpha = 0.5 };
        model.AddMesh("Витраж", [new(0, 0, 0), new(100, 0, 0), new(0, 100, 0)], [0, 1, 2], mat);

        string tempGlb = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.glb");
        try
        {
            model.SaveGlb(tempGlb);
            Assert.True(File.Exists(tempGlb));

            byte[] bytes = File.ReadAllBytes(tempGlb);
            Assert.True(bytes.Length > 20);

            // Check glTF magic
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
            Assert.Equal(0x46546C67u, magic); // "glTF"
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
            Assert.Equal(2u, version);

            // Verify JSON chunk
            uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
            string jsonText = System.Text.Encoding.UTF8.GetString(bytes, 20, (int)jsonLen);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("asset", out _));
            Assert.True(root.TryGetProperty("meshes", out _));
            Assert.True(root.TryGetProperty("materials", out _));
        }
        finally
        {
            if (File.Exists(tempGlb)) File.Delete(tempGlb);
        }
    }

    [Fact]
    public void IfcModel_SaveObj_ProducesValidObjAndMtl()
    {
        var model = IfcModel.Create("OBJ Export Test", IfcSchema.Ifc2x3);
        var mat = new IfcMaterial { Name = "Wood", R = 0.6, G = 0.4, B = 0.2 };
        model.AddMesh("Ограждение", [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [0, 1, 2], mat);

        string tempObj = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.obj");
        string tempMtl = Path.ChangeExtension(tempObj, ".mtl");
        try
        {
            model.SaveObj(tempObj);
            Assert.True(File.Exists(tempObj));
            Assert.True(File.Exists(tempMtl));

            string objContent = File.ReadAllText(tempObj);
            Assert.Contains("v ", objContent);
            Assert.Contains("f 1 2 3", objContent);
            Assert.Contains("mtllib", objContent);
            Assert.Contains("usemtl Wood", objContent);

            string mtlContent = File.ReadAllText(tempMtl);
            Assert.Contains("newmtl Wood", mtlContent);
            Assert.Contains("Kd ", mtlContent);
        }
        finally
        {
            if (File.Exists(tempObj)) File.Delete(tempObj);
            if (File.Exists(tempMtl)) File.Delete(tempMtl);
        }
    }
}
