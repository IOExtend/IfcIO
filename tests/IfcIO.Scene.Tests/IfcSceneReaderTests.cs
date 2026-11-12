using System.Text.RegularExpressions;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using IOScene;
using Xunit;

namespace IfcIO.Scene.Tests;

public class IfcSceneReaderTests
{
    private const string Millimetre = "#11=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);";
    private const string Foot = """
        #11=IFCCONVERSIONBASEDUNIT(#12,.LENGTHUNIT.,'FOOT',#13);
        #12=IFCDIMENSIONALEXPONENTS(1,0,0,0,0,0,0);
        #13=IFCMEASUREWITHUNIT(IFCLENGTHMEASURE(0.3048),#14);
        #14=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
        """;
    private const string Hostile = "\"></script><script>window.__xss=1</script>";

    // Site far from the origin; storey 3000 up; Panel placed at (10,20,0) in the storey and styled red.
    // Fitting A and B draw one representation map (origin (0,0,100)); B's mapping moves it by (0,250,0).
    private static string PlacedIfc(string lengthUnit, string extra = "") => $"""
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION((''),'2;1');
        FILE_NAME('adapter.ifc','2026-01-01T00:00:00',(''),(''),'','','');
        FILE_SCHEMA(('IFC2X3'));
        ENDSEC;
        DATA;
        #1=IFCPROJECT('0AdapterProject0000001',$,'Adapter',$,$,$,$,(#20),#10);
        #10=IFCUNITASSIGNMENT((#11));
        {lengthUnit}
        #20=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#21,$);
        #21=IFCAXIS2PLACEMENT3D(#22,$,$);
        #22=IFCCARTESIANPOINT((0.,0.,0.));
        #30=IFCSITE('0AdapterSite0000000001',$,'Site',$,$,#31,$,$,.ELEMENT.,$,$,$,$,$);
        #31=IFCLOCALPLACEMENT($,#32);
        #32=IFCAXIS2PLACEMENT3D(#33,$,$);
        #33=IFCCARTESIANPOINT((412345678.,2000.,0.));
        #40=IFCBUILDINGSTOREY('0AdapterStorey00000001',$,'Level 1',$,$,#41,$,$,.ELEMENT.,3000.);
        #41=IFCLOCALPLACEMENT(#31,#42);
        #42=IFCAXIS2PLACEMENT3D(#43,$,$);
        #43=IFCCARTESIANPOINT((0.,0.,3000.));
        #100=IFCBUILDINGELEMENTPROXY('1AdapterPanel000000001',$,'Panel',$,$,#101,#103,$,$);
        #101=IFCLOCALPLACEMENT(#41,#102);
        #102=IFCAXIS2PLACEMENT3D(#104,$,$);
        #104=IFCCARTESIANPOINT((10.,20.,0.));
        #103=IFCPRODUCTDEFINITIONSHAPE($,$,(#105));
        #105=IFCSHAPEREPRESENTATION(#20,'Body','Brep',(#106));
        #106=IFCFACETEDBREP(#107);
        #107=IFCCLOSEDSHELL((#108));
        #108=IFCFACE((#109));
        #109=IFCFACEOUTERBOUND(#110,.T.);
        #110=IFCPOLYLOOP((#22,#111,#112));
        #111=IFCCARTESIANPOINT((100.,0.,0.));
        #112=IFCCARTESIANPOINT((0.,100.,0.));
        #120=IFCCOLOURRGB($,1.,0.,0.);
        #121=IFCSURFACESTYLESHADING(#120);
        #122=IFCSURFACESTYLE('Red',.BOTH.,(#121));
        #123=IFCSTYLEDITEM(#106,(#122),$);
        #200=IFCREPRESENTATIONMAP(#201,#203);
        #201=IFCAXIS2PLACEMENT3D(#202,$,$);
        #202=IFCCARTESIANPOINT((0.,0.,100.));
        #203=IFCSHAPEREPRESENTATION(#20,'Body','Brep',(#204));
        #204=IFCFACETEDBREP(#107);
        #300=IFCFLOWFITTING('1AdapterFittingA000001',$,'Fitting A',$,$,#301,#303,$);
        #301=IFCLOCALPLACEMENT($,#302);
        #302=IFCAXIS2PLACEMENT3D(#304,$,$);
        #304=IFCCARTESIANPOINT((500.,0.,0.));
        #303=IFCPRODUCTDEFINITIONSHAPE($,$,(#305));
        #305=IFCSHAPEREPRESENTATION(#20,'Body','MappedRepresentation',(#306));
        #306=IFCMAPPEDITEM(#200,#307);
        #307=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#22,$,$);
        #310=IFCFLOWFITTING('1AdapterFittingB000001',$,'Fitting B',$,$,#301,#313,$);
        #313=IFCPRODUCTDEFINITIONSHAPE($,$,(#315));
        #315=IFCSHAPEREPRESENTATION(#20,'Body','MappedRepresentation',(#316));
        #316=IFCMAPPEDITEM(#200,#317);
        #317=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#318,$,$);
        #318=IFCCARTESIANPOINT((0.,250.,0.));
        #400=IFCRELCONTAINEDINSPATIALSTRUCTURE('2AdapterContainment001',$,$,$,(#100,#300,#310),#40);
        {extra}
        ENDSEC;
        END-ISO-10303-21;
        """;

    private static readonly Vector3d[] Triangle = [new(0, 0, 0), new(100, 0, 0), new(0, 100, 0)];

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Read_PlacementFixture_MatchesGoldenIdentity()
    {
        var expected = File.ReadAllLines(FixturePath("placement.identity.txt")).Where(l => !l.StartsWith('#')).ToList();

        var result = Valid(IfcSceneReader.Read(FixturePath("placement.ifc")));

        Assert.Equal(expected[0], "key " + Assert.Single(result.Document.Models).Key);
        Assert.Equal(expected.Skip(1), result.Document.Elements.Select(e => e.SourceId));
        Assert.All(result.Document.Elements, e => Assert.Equal(e.SourceId, e.IfcGuid));
    }

    [Fact]
    public void Read_File_DescribesTheModel()
    {
        string path = FixturePath("placement.ifc");

        var model = Assert.Single(Valid(IfcSceneReader.Read(path)).Document.Models);
        var keyed = Assert.Single(IfcSceneReader.Read(path, new IfcSceneOptions { ModelKey = "Tower A" }).Document.Models);

        Assert.Equal("placement.ifc", model.Key);
        Assert.Equal("Tower A", keyed.Key);
        Assert.Equal(path, model.Path);
        Assert.Equal("Placement", model.Name);
        Assert.Equal("ifc", model.SourceFormat);
        Assert.Equal("mm", model.SourceLengthUnit);
        Assert.Matches(@"^IfcIO\.Scene \d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", model.Producer);
    }

    [Fact]
    public void Read_ModelWithoutKey_Throws()
    {
        var model = IfcModel.Create("No key");

        var ex = Assert.Throws<ArgumentException>(() => IfcSceneReader.Read(model, new IfcSceneOptions()));
        Assert.Contains("ModelKey", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Millimetre, IfcLengthUnit.Millimetre, 0.001)]
    [InlineData(Foot, IfcLengthUnit.Foot, 0.3048)]
    public void Read_PlacedAndMappedElements_LandInWorldMetres(string lengthUnit, IfcLengthUnit expectedUnit, double metres)
    {
        var result = Valid(Read(OpenText(PlacedIfc(lengthUnit))));
        var doc = result.Document;

        Assert.Equal(expectedUnit, result.LengthUnit);
        Assert.Equal(metres, result.MetresPerUnit);
        AssertPoints(Offset(new(412345678 + 10, 2000 + 20, 3000), metres), GeometryOf(doc, 100).Vertices);
        AssertPoints(Offset(new(500, 0, 100), metres), GeometryOf(doc, 300).Vertices);
        AssertPoints(Offset(new(500, 250, 100), metres), GeometryOf(doc, 310).Vertices);

        // IfcIO bakes mapped items into world meshes: the two fittings are two geometries, each placed once.
        Assert.Equal(3, result.GeometryCount);
        Assert.Equal(3, doc.Instances.Count);
        Assert.All(doc.Instances, i => Assert.Null(i.Transform));

        var panel = Assert.Single(doc.Elements, e => e.Id == 100);
        Assert.Equal("1AdapterPanel000000001", panel.SourceId);
        Assert.Equal("1AdapterPanel000000001", panel.IfcGuid);
        Assert.Equal("IFCBUILDINGELEMENTPROXY", panel.Category);
        Assert.Equal(SceneNodeKind.Element, panel.Kind);
        var red = Assert.Single(doc.Materials);
        Assert.Equal(new RgbaColor(255, 0, 0), red.Color);
        Assert.Equal(red.Id, InstanceOf(doc, 100).MaterialId);
    }

    [Fact]
    public void Read_Storeys_BecomeLevelsAndTreeNodes()
    {
        var doc = Valid(Read(OpenText(PlacedIfc(Millimetre)))).Document;

        var level = Assert.Single(doc.Levels);
        Assert.Equal("Level 1", level.Name);
        Assert.Equal(3.0, level.Elevation);
        var storey = Assert.Single(doc.Elements, e => e.Kind == SceneNodeKind.Level);
        Assert.Equal("0AdapterStorey00000001", storey.SourceId);
        Assert.Equal("IFCBUILDINGSTOREY", storey.Category);
        Assert.Null(storey.ParentId);
        Assert.Equal(level.Id, storey.LevelId);
        Assert.All(doc.Elements.Where(e => e != storey), e =>
        {
            Assert.Equal(storey.Id, e.ParentId);
            Assert.Equal(level.Id, e.LevelId);
        });
    }

    [Fact]
    public void Read_TypeObjectsAndLiningProperties_AreNotElements()
    {
        const string NonProducts = """
            #500=IFCWALLTYPE('1AdapterWallType000001',$,'WT',$,$,$,$,$,$,.STANDARD.);
            #501=IFCDOORSTYLE('1AdapterDoorStyle00001',$,'DS',$,$,$,$,$,.SINGLE_SWING_LEFT.,.WOOD.,.F.,.F.);
            #502=IFCDOORLININGPROPERTIES('1AdapterDoorLining0001',$,'DL',$,50.,$,$,$,$,$,$,$,$,$,$);
            """;
        var model = OpenText(PlacedIfc(Millimetre, NonProducts));
        long[] nonProducts = [500, 501, 502];
        // IfcIO lists them as elements, which is what the adapter has to undo.
        Assert.All(nonProducts, id => Assert.Contains(model.AllElements, e => e.Id == id));

        var result = Valid(Read(model));

        Assert.Equal(new long[] { 100, 300, 310 }, ElementsOfKind(result.Document).Select(e => e.Id).Order());
        Assert.Equal(3, result.ElementCount);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(IfcLengthUnit.Millimetre, 0.001)]
    [InlineData(IfcLengthUnit.Metre, 1.0)]
    public void Read_SavedModel_ScalesFromItsLengthUnit(IfcLengthUnit unit, double metres)
    {
        var model = IfcModel.Create("Units");
        model.LengthUnit = unit;
        model.AddMesh("Slab", [new(1000, 0, 0), new(2000, 0, 0), new(1000, 1500, 250)], [0, 1, 2], ifcType: "IFCSLAB");
        var opened = SaveAndOpen(model);

        var result = Valid(Read(opened));

        Assert.Equal(unit, result.LengthUnit);
        var source = Assert.Single(opened.AllElements).Mesh!.Vertices;
        var vertices = Assert.Single(result.Document.Geometries).Vertices;
        Assert.Equal(source.Select(v => new Vector3d(v.X * metres, v.Y * metres, v.Z * metres)), vertices);
        Assert.Equal(new Vector3d(2000 * metres, 1500 * metres, 250 * metres), result.Document.ComputeBounds().Max);
    }

    [Fact]
    public void Read_SavedModel_KeepsPropertiesQuantitiesGlobalIdsAndReportsElementsWithoutGeometry()
    {
        var model = IfcModel.Create("Properties");
        var storey = model.AddStorey("Level 2", 3000);
        var wall = model.AddMesh("Wall", Mm(Triangle), [0, 1, 2], new IfcMaterial { Name = "Brick", R = 1, G = 0.5, B = 0 }, storey, "IFCWALL");
        wall.GetOrCreatePropertySet("Pset_WallCommon").Add("FireRating", "EI 60");
        wall.GetOrCreateQuantitySet("Qto_WallBaseQuantities").AddLength("Length", 3000.5);
        model.AddElement(new IfcElement { Id = 2, Name = "Annotation" }, storey);
        var opened = SaveAndOpen(model);
        var openedWall = Assert.Single(opened.AllElements, e => e.Name == "Wall");
        var openedEmpty = Assert.Single(opened.AllElements, e => e.Name == "Annotation");

        var result = Valid(Read(opened));
        var doc = result.Document;

        var element = Assert.Single(doc.Elements, e => e.Id == openedWall.Id);
        Assert.False(string.IsNullOrEmpty(openedWall.GlobalId));
        Assert.Equal(openedWall.GlobalId, element.SourceId);
        Assert.Equal("IFCWALL", element.Category);
        Assert.Equal("Level 2", Assert.Single(doc.Levels, l => l.Id == element.LevelId).Name);

        var sets = element.PropertySetIds.Select(id => Assert.Single(doc.PropertySets, s => s.Id == id)).ToList();
        Assert.Equal(["ifc:Pset_WallCommon", "ifc:Qto_WallBaseQuantities"], sets.Select(s => s.Key));
        Assert.Equal(["Pset_WallCommon", "Qto_WallBaseQuantities"], sets.Select(s => s.Name));
        var fireRating = Assert.Single(sets[0].Properties);
        Assert.Equal("FireRating", fireRating.Name);
        Assert.Equal("ifc:Pset_WallCommon.FireRating", fireRating.Key);
        Assert.Equal(SceneValue.FromText("EI 60"), fireRating.Value);
        var length = Assert.Single(sets[1].Properties);
        Assert.Equal("ifc:Qto_WallBaseQuantities.Length", length.Key);
        Assert.Equal(SceneValue.FromNumber(3000.5, null, "3000.5"), length.Value);

        var brick = Assert.Single(doc.Materials);
        Assert.Equal("Brick", brick.Name);
        Assert.Equal(new RgbaColor(255, 128, 0, 255), brick.Color);

        Assert.Equal(2, result.ElementCount);
        Assert.Equal(1, result.ElementsWithGeometry);
        var issue = Assert.Single(result.Diagnostics);
        Assert.Equal((SceneSeverity.Info, "no-geometry", openedEmpty.GlobalId), (issue.Severity, issue.Code, issue.SourceId));
        Assert.DoesNotContain(doc.Instances, i => i.ElementId == openedEmpty.Id);
        Assert.Contains(doc.Elements, e => e.Id == openedEmpty.Id);
    }

    [Fact]
    public void Read_SharedPropertySet_IsOneSceneSet()
    {
        const string Shared = """
            #600=IFCPROPERTYSINGLEVALUE('Reference',$,IFCIDENTIFIER('P-1'),$);
            #601=IFCPROPERTYSET('1AdapterSharedPset0001',$,'Pset_Shared',$,(#600));
            #602=IFCRELDEFINESBYPROPERTIES('1AdapterSharedRel00001',$,$,$,(#300,#310),#601);
            """;

        var doc = Valid(Read(OpenText(PlacedIfc(Millimetre, Shared)))).Document;

        var set = Assert.Single(doc.PropertySets);
        Assert.Equal("ifc:Pset_Shared", set.Key);
        Assert.Equal([set.Id], Assert.Single(doc.Elements, e => e.Id == 300).PropertySetIds);
        Assert.Equal([set.Id], Assert.Single(doc.Elements, e => e.Id == 310).PropertySetIds);
        Assert.Equal(SceneValue.FromText("P-1"), Assert.Single(set.Properties).Value);
    }

    [Fact]
    public void Read_HostileStrings_SurviveSceneJson()
    {
        var model = IfcModel.Create("Hostile");
        var wall = model.AddMesh(Hostile, Mm(Triangle), [0, 1, 2], ifcType: "IFCWALL");
        wall.GetOrCreatePropertySet(Hostile).Add(Hostile, Hostile);
        var opened = SaveAndOpen(model);

        var doc = Valid(Read(opened)).Document;
        var read = SceneJson.Read(SceneJson.Write(doc));

        var element = Assert.Single(ElementsOfKind(read));
        Assert.Equal(Hostile, element.Name);
        var property = Assert.Single(Assert.Single(read.PropertySets).Properties);
        Assert.Equal((Hostile, $"ifc:{Hostile}.{Hostile}", Hostile), (property.Name, property.Key, property.Value.Text));
    }

    [Fact]
    public void Read_MaterialAlpha_BecomesColourAlphaAndElementsWithoutMaterialGetClassColour()
    {
        // IfcIO does not read transparency from files yet, so the material is set in memory.
        var model = IfcModel.Create("Glass");
        model.AddMesh("Pane", Mm(Triangle), [0, 1, 2], new IfcMaterial { Name = "Glass", R = 0.2, G = 0.4, B = 0.6, Alpha = 0.35 }, ifcType: "IFCPLATE");
        var window = model.AddMesh("Window", Mm(Triangle), [0, 1, 2], ifcType: "IFCWINDOW");

        var doc = Valid(Read(model)).Document;

        var glass = Assert.Single(doc.Materials);
        Assert.Equal((51, 102, 153), (glass.Color.R, glass.Color.G, glass.Color.B));
        Assert.Equal(0.35, glass.Color.Af, 0.5 / 255);
        var instance = InstanceOf(doc, window.Id);
        Assert.Null(instance.MaterialId);
        Assert.Equal(0.5, instance.Color!.Value.Af, 0.5 / 255);
    }

    [Fact]
    public void Read_SpacesOnlyWhenIncluded()
    {
        var model = IfcModel.Create("Spaces");
        model.AddMesh("Wall", Mm(Triangle), [0, 1, 2], ifcType: "IFCWALL");
        var space = model.AddSpace("Room 1", mesh: IfcMesh.Create(1, "Room 1", Mm(Triangle), [0, 1, 2]));

        Assert.DoesNotContain(Valid(Read(model)).Document.Elements, e => e.Id == space.Id);

        var doc = Valid(Read(model, new IfcSceneOptions { ModelKey = "spaces", IncludeSpaces = true })).Document;
        Assert.Equal("IFCSPACE", Assert.Single(doc.Elements, e => e.Id == space.Id).Category);
        Assert.Equal(0.3, InstanceOf(doc, space.Id).Color!.Value.Af, 0.5 / 255);
    }

    [Fact]
    public void Read_DuplicateElementIds_Throws()
    {
        // A model built in memory numbers elements per storey, so two storeys both start at 1.
        var model = IfcModel.Create("Duplicates");
        model.AddMesh("A", Mm(Triangle), [0, 1, 2]);
        model.AddMesh("B", Mm(Triangle), [0, 1, 2], storey: model.AddStorey("Level 2", 3000));

        var ex = Assert.Throws<ArgumentException>(() => Read(model));
        Assert.Contains("used twice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_InvalidMesh_IsReportedNotDrawn()
    {
        var model = IfcModel.Create("Invalid");
        model.AddMesh("Bad", Mm(Triangle), [0, 1, 7]);
        model.AddMesh("Empty", Mm(Triangle), []);

        var result = Valid(Read(model));

        Assert.Empty(result.Document.Geometries);
        Assert.Empty(result.Document.Instances);
        Assert.Equal(
            [(SceneSeverity.Warning, "invalid-mesh", (string?)null), (SceneSeverity.Warning, "empty-mesh", null)],
            result.Diagnostics.Select(d => (d.Severity, d.Code, d.SourceId)));
    }

    [Fact]
    public void Read_WorkSchedule_BecomesScheduleWithElements()
    {
        var model = IfcModel.Create("Schedule", IfcSchema.Ifc4);
        var wall = model.AddMesh("Wall", Mm(Triangle), [0, 1, 2], ifcType: "IFCWALL");
        var schedule = model.AddWorkSchedule("Build");
        var start = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        schedule.AddTask("Erect walls", "T1", start, start.AddDays(9)).AssignElement(wall);
        schedule.AddTask("Undated", "T2");
        var opened = SaveAndOpen(model);

        var result = Valid(Read(opened));

        var sceneSchedule = Assert.Single(result.Document.Schedules);
        Assert.Equal("Build", sceneSchedule.Name);
        Assert.Equal(1, sceneSchedule.ModelId);
        var task = Assert.Single(sceneSchedule.Tasks);
        Assert.Equal("Erect walls", task.Name);
        Assert.Equal(new DateTimeOffset(start), task.PlannedStart);
        Assert.Equal(new DateTimeOffset(start.AddDays(9)), task.PlannedEnd);
        Assert.Equal([Assert.Single(opened.AllElements).Id], task.ElementIds);
        Assert.Equal(1, result.SkippedTasks);
    }

    [Fact]
    public void Read_Ifc4TriangulatedFaceSet_IsReportedWithoutGeometry()
    {
        // Documented limit: IfcIO's reader returns no mesh for IfcTriangulatedFaceSet, even the one it writes itself.
        // When this test fails, IfcIO reads it, and the limit in docs/scene-adapter.md goes.
        var model = IfcModel.Create("Tessellated", IfcSchema.Ifc4);
        model.AddMesh("Slab", Mm(Triangle), [0, 1, 2], ifcType: "IFCSLAB");
        using var writer = new StringWriter();
        model.Save(writer);
        Assert.Contains("IFCTRIANGULATEDFACESET(", writer.ToString(), StringComparison.Ordinal);

        var result = Valid(Read(OpenText(writer.ToString())));

        Assert.Empty(result.Document.Geometries);
        Assert.Equal("no-geometry", Assert.Single(result.Diagnostics).Code);
    }

    [IfcFileFact]
    public void Read_ConfiguredIfcFile_ConvertsEveryElement()
    {
        string path = Environment.GetEnvironmentVariable(IfcFileFactAttribute.Variable)!;
        var model = IfcModel.Open(path);

        var result = IfcSceneReader.Read(path);

        // A real file may draw warnings (for example a malformed GlobalId), but never errors.
        Assert.DoesNotContain(result.Document.Validate(), d => d.Severity == SceneSeverity.Error);
        Assert.Equal(
            model.AllElements.Count(e => !Regex.IsMatch(e.IfcType, "(TYPE|STYLE|PROPERTIES)$", RegexOptions.IgnoreCase)),
            result.ElementCount);
        Assert.Equal(result.ElementsWithGeometry, result.GeometryCount);
        Assert.All(ElementsOfKind(result.Document), e => Assert.False(string.IsNullOrEmpty(e.SourceId)));
        Assert.NotEmpty(SceneJson.Write(result.Document));
    }

    // Every converted fixture must pass Validate() without errors or warnings.
    private static IfcSceneResult Valid(IfcSceneResult result)
    {
        Assert.Empty(result.Document.Validate());
        return result;
    }

    private static IfcSceneResult Read(IfcModel model, IfcSceneOptions? options = null) =>
        IfcSceneReader.Read(model, options ?? new IfcSceneOptions { ModelKey = "test.ifc" });

    private static IfcModel OpenText(string ifc)
    {
        using var reader = new StringReader(ifc);
        return IfcModel.Open(reader);
    }

    // Models are saved as IFC2x3 (faceted breps) unless a test needs IFC4: IfcIO does not read back the
    // IfcTriangulatedFaceSet it writes for IFC4.
    private static IfcModel SaveAndOpen(IfcModel model)
    {
        using var writer = new StringWriter();
        model.Save(writer);
        return OpenText(writer.ToString());
    }

    private static IEnumerable<SceneElement> ElementsOfKind(SceneDocument doc) => doc.Elements.Where(e => e.Kind == SceneNodeKind.Element);

    private static IfcVector3[] Mm(Vector3d[] points) => [.. points.Select(p => new IfcVector3(p.X, p.Y, p.Z))];

    private static SceneInstance InstanceOf(SceneDocument doc, long elementId) => Assert.Single(doc.Instances, i => i.ElementId == elementId);

    private static SceneGeometry GeometryOf(SceneDocument doc, long elementId) =>
        Assert.Single(doc.Geometries, g => g.Id == InstanceOf(doc, elementId).GeometryId);

    private static Vector3d[] Offset(Vector3d origin, double metres) =>
        [.. Triangle.Select(p => (p + origin) * metres)];

    private static void AssertPoints(Vector3d[] expected, IReadOnlyList<Vector3d> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            // 1e-6 m: far tighter than float precision at 400 km from the origin, so doubles must be kept.
            Assert.True(Vector3d.Distance(expected[i], actual[i]) <= 1e-6, $"Vertex {i}: expected {expected[i]}, actual {actual[i]}");
        }
    }
}

/// <summary>Runs only when <see cref="Variable"/> names an IFC file; the repository ships none of real size.</summary>
public sealed class IfcFileFactAttribute : FactAttribute
{
    public const string Variable = "IFCIO_SCENE_IFC_FILE";

    public IfcFileFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to an IFC file to run this test.";
        }
    }
}
