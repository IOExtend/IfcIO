using System.Globalization;
using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;

namespace IfcIO;

/// <summary>
/// Top-level in-memory IFC BIM model.
/// Manages spatial hierarchy (Project, Site, Building, Storeys), geometry meshes,
/// materials, property sets, and serialization to ISO 10303-21 STEP physical files.
/// </summary>
public sealed class IfcModel
{
    private const string DegreeInRadians = "0.017453292519943278";

    public IfcSchema Schema { get; set; } = IfcSchema.Ifc2x3;
    public IfcLengthUnit LengthUnit { get; set; } = IfcLengthUnit.Millimetre;
    public IfcPlaneAngleUnit AngleUnit { get; set; } = IfcPlaneAngleUnit.Degree;

    public string ProjectName { get; set; } = "IfcIO Project";
    public string BuildingName { get; set; } = "Default Building";
    public string Author { get; set; } = "mahach666";
    public string Organization { get; set; } = "IOExtend";
    public string Application { get; set; } = "IfcIO";

    public List<IfcBuildingStorey> Storeys { get; } = [];
    public List<IfcMaterial> Materials { get; } = [];

    public IEnumerable<IfcElement> AllElements => Storeys.SelectMany(s => s.Elements);

    public IfcModel()
    {
        // Default storey
        Storeys.Add(new IfcBuildingStorey { Name = "Level 1", Elevation = 0.0 });
    }

    /// <summary>Creates a new empty IFC model with the specified project name and schema.</summary>
    public static IfcModel Create(string projectName, IfcSchema schema = IfcSchema.Ifc2x3)
    {
        return new IfcModel
        {
            ProjectName = projectName,
            Schema = schema,
        };
    }

    /// <summary>Adds a storey to the building spatial hierarchy.</summary>
    public IfcBuildingStorey AddStorey(string name, double elevation = 0.0)
    {
        var storey = new IfcBuildingStorey { Name = name, Elevation = elevation };
        Storeys.Add(storey);
        return storey;
    }

    /// <summary>Adds an element to the specified storey (or the first storey by default).</summary>
    public void AddElement(IfcElement element, IfcBuildingStorey? storey = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        storey ??= Storeys[0];
        storey.Elements.Add(element);
    }

    /// <summary>Creates and adds an element with a 3D mesh geometry to the model.</summary>
    public IfcElement AddMesh(
        string name,
        IReadOnlyList<IfcVector3> vertices,
        IReadOnlyList<int> indices,
        IfcMaterial? material = null,
        IfcBuildingStorey? storey = null,
        string? ifcType = null)
    {
        storey ??= Storeys[0];

        int nextId = storey.Elements.Count + 1;
        var mesh = IfcMesh.Create(nextId, name, vertices, indices);

        var element = new IfcElement
        {
            Id = nextId,
            Name = name,
            IfcType = ifcType ?? ClassifyElement(name, material?.Name),
            Mesh = mesh,
            Material = material,
        };

        if (material is not null && !Materials.Contains(material))
        {
            Materials.Add(material);
        }

        storey.Elements.Add(element);
        return element;
    }

    /// <summary>Serializes the IFC model to a physical STEP file on disk.</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8, bufferSize: 65536);
        Save(writer, Path.GetFileName(path));
    }

    /// <summary>Serializes the IFC model to a <see cref="Stream"/> in ISO 10303-21 format.</summary>
    public void Save(Stream stream, string fileName = "model.ifc")
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, bufferSize: 65536, leaveOpen: true);
        Save(writer, fileName);
    }

    /// <summary>Serializes the IFC model to a <see cref="TextWriter"/> in ISO 10303-21 format.</summary>
    public void Save(TextWriter writer, string fileName = "model.ifc")
    {
        ArgumentNullException.ThrowIfNull(writer);

        var w = new StepWriter();
        string schemaStr = Schema == IfcSchema.Ifc4 ? "IFC4" : "IFC2X3";
        string viewDef = Schema == IfcSchema.Ifc4
            ? "ViewDefinition [DesignTransferView_V1.0]"
            : "ViewDefinition [CoordinationView_V2.0]";

        // 1. Project infrastructure & Owner History
        int personId = w.Add("IFCPERSON", $"$,{StepWriter.Str(Author)},$,$,$,$,$,$");
        int orgId = w.Add("IFCORGANIZATION", $"$,{StepWriter.Str(Organization)},$,$,$");
        int personOrgId = w.Add("IFCPERSONANDORGANIZATION", $"{StepWriter.Ref(personId)},{StepWriter.Ref(orgId)},$");
        int appId = w.Add("IFCAPPLICATION", $"{StepWriter.Ref(orgId)},'0.1.0',{StepWriter.Str(Application)},'IfcIO'");

        int epochSeconds = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        int ownerHistoryId = w.Add("IFCOWNERHISTORY",
            $"{StepWriter.Ref(personOrgId)},{StepWriter.Ref(appId)},.READWRITE.,.NOCHANGE.,$,$,$,{epochSeconds}");

        // 2. Units & Geometric Context
        int lengthUnitId = LengthUnit == IfcLengthUnit.Millimetre
            ? w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,.MILLI.,.METRE.")
            : w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,$,.METRE.");

        int radianId = w.Add("IFCSIUNIT", "*,.PLANEANGLEUNIT.,$,.RADIAN.");
        int dimExpId = w.Add("IFCDIMENSIONALEXPONENTS", "0,0,0,0,0,0,0");
        int angleMeasureId = w.Add("IFCMEASUREWITHUNIT", $"IFCPLANEANGLEMEASURE({DegreeInRadians}),{StepWriter.Ref(radianId)}");
        int degreeUnitId = w.Add("IFCCONVERSIONBASEDUNIT", $"{StepWriter.Ref(dimExpId)},.PLANEANGLEUNIT.,'DEGREE',{StepWriter.Ref(angleMeasureId)}");

        int areaUnitId = w.Add("IFCSIUNIT", "*,.AREAUNIT.,$,.SQUARE_METRE.");
        int volumeUnitId = w.Add("IFCSIUNIT", "*,.VOLUMEUNIT.,$,.CUBIC_METRE.");

        int unitAssignmentId = w.Add("IFCUNITASSIGNMENT",
            StepWriter.List(StepWriter.Ref(lengthUnitId), StepWriter.Ref(degreeUnitId), StepWriter.Ref(areaUnitId), StepWriter.Ref(volumeUnitId)));

        int originPointId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(0), StepWriter.Real(0), StepWriter.Real(0)));
        int axisZId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(0), StepWriter.Real(0), StepWriter.Real(1)));
        int axisXId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(1), StepWriter.Real(0), StepWriter.Real(0)));
        int worldPlacement3dId = w.Add("IFCAXIS2PLACEMENT3D",
            $"{StepWriter.Ref(originPointId)},{StepWriter.Ref(axisZId)},{StepWriter.Ref(axisXId)}");

        int mainContextId = w.Add("IFCGEOMETRICREPRESENTATIONCONTEXT",
            $"$,'Model',3,1.E-05,{StepWriter.Ref(worldPlacement3dId)},$");
        int bodySubContextId = w.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
            $"'Body','Model',*,*,*,*,{StepWriter.Ref(mainContextId)},$,.MODEL_VIEW.,$");

        // 3. Spatial Hierarchy: Project -> Site -> Building -> Storeys
        int projectId = w.Add("IFCPROJECT",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(ProjectName)},$,$,$,$,({StepWriter.Ref(mainContextId)}),{StepWriter.Ref(unitAssignmentId)}");

        int sitePlacementId = w.Add("IFCLOCALPLACEMENT", $"$,{StepWriter.Ref(worldPlacement3dId)}");
        int siteId = w.Add("IFCSITE",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},'Site',$,$,{StepWriter.Ref(sitePlacementId)},$,$,.ELEMENT.,$,$,$,$,$");

        int buildingPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(sitePlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
        int buildingId = w.Add("IFCBUILDING",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(BuildingName)},$,$,{StepWriter.Ref(buildingPlacementId)},$,$,.ELEMENT.,$,$,$,$");

        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(projectId)},({StepWriter.Ref(siteId)})");
        w.Add("IFCRELAGGREGATES",
            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(siteId)},({StepWriter.Ref(buildingId)})");

        // 4. Materials Cache
        var styleCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mat in Materials)
        {
            GetOrCreateSurfaceStyle(w, mat, styleCache);
        }

        // 5. Storeys & Elements
        var storeyIds = new List<int>();

        for (int sIdx = 0; sIdx < Storeys.Count; sIdx++)
        {
            var storey = Storeys[sIdx];
            int storeyPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(buildingPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
            int storeyId = w.Add("IFCBUILDINGSTOREY",
                $"{StepWriter.Str(storey.GlobalId ?? IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(storey.Name)},$,$,{StepWriter.Ref(storeyPlacementId)},$,$,.ELEMENT.,{StepWriter.Real(storey.Elevation)}");
            storeyIds.Add(storeyId);

            var storeyElementIds = new List<int>(storey.Elements.Count);

            for (int eIdx = 0; eIdx < storey.Elements.Count; eIdx++)
            {
                var element = storey.Elements[eIdx];

                int prodPlacementId = w.Add("IFCLOCALPLACEMENT",
                    $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

                int prodShapeId = StepWriter.Null == "$" ? w.Add("IFCPRODUCTDEFINITIONSHAPE", "$,$,()") : 0;

                if (element.Mesh is not null && element.Mesh.Vertices.Count >= 3 && element.Mesh.Indices.Count >= 3)
                {
                    int? styleId = null;
                    if (element.Material is not null)
                    {
                        styleId = GetOrCreateSurfaceStyle(w, element.Material, styleCache);
                    }

                    int brepId = WriteMeshFacetedBrep(w, element.Mesh.Vertices, element.Mesh.Indices);

                    if (styleId.HasValue)
                    {
                        w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(brepId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(element.Material?.Name)}");
                    }

                    int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                        $"{StepWriter.Ref(bodySubContextId)},'Body','Brep',({StepWriter.Ref(brepId)})");
                    prodShapeId = w.Add("IFCPRODUCTDEFINITIONSHAPE",
                        $"$,$,({StepWriter.Ref(shapeRepId)})");
                }

                string elemGuid = element.GlobalId ?? IfcGuid.FromElementId(element.Id, "IfcIO");
                string ifcType = !string.IsNullOrEmpty(element.IfcType)
                    ? element.IfcType
                    : ClassifyElement(element.Name, element.Material?.Name);

                int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, element.Name, prodPlacementId, prodShapeId, element.Tag ?? element.Id.ToString(CultureInfo.InvariantCulture));
                storeyElementIds.Add(elemId);

                // Property sets
                if (element.PropertySets.Count > 0)
                {
                    foreach (var pset in element.PropertySets)
                    {
                        var propIds = new List<int>();
                        foreach (var prop in pset.Properties)
                        {
                            string typeWrapper = prop.Type switch
                            {
                                "IfcInteger" => $"IFCINTEGER({prop.Value})",
                                "IfcReal" => $"IFCREAL({prop.Value})",
                                "IfcBoolean" => $"IFCBOOLEAN({prop.Value})",
                                _ => $"IFCTEXT({StepWriter.Str(prop.Value)})",
                            };

                            int pValId = w.Add("IFCPROPERTYSINGLEVALUE",
                                $"{StepWriter.Str(prop.Name)},$,{typeWrapper},$");
                            propIds.Add(pValId);
                        }

                        int psetId = w.Add("IFCPROPERTYSET",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(pset.Name)},$,{StepWriter.List(propIds.Select(StepWriter.Ref))}");

                        w.Add("IFCRELDEFINESBYPROPERTIES",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(elemId)}),{StepWriter.Ref(psetId)}");
                    }
                }
            }

            if (storeyElementIds.Count > 0)
            {
                w.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(storeyElementIds.Select(StepWriter.Ref))},{StepWriter.Ref(storeyId)}");
            }
        }

        if (storeyIds.Count > 0)
        {
            w.Add("IFCRELAGGREGATES",
                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(buildingId)},{StepWriter.List(storeyIds.Select(StepWriter.Ref))}");
        }

        w.WriteTo(writer, fileName, schemaStr, viewDef, Application, Organization);
    }

    /// <summary>Parses an existing IFC STEP file from disk into a structured model.</summary>
    public static IfcModel Open(string path)
    {
        var step = StepReader.Read(path);
        var model = new IfcModel
        {
            ProjectName = Path.GetFileNameWithoutExtension(path),
            Schema = (step.Schema?.Contains("IFC4", StringComparison.OrdinalIgnoreCase) == true) ? IfcSchema.Ifc4 : IfcSchema.Ifc2x3,
        };

        return model;
    }

    private static int WriteMeshFacetedBrep(StepWriter w, IReadOnlyList<IfcVector3> vertices, IReadOnlyList<int> indices)
    {
        var pointIds = new int[vertices.Count];
        for (int v = 0; v < vertices.Count; v++)
        {
            var pt = vertices[v];
            pointIds[v] = w.Add("IFCCARTESIANPOINT",
                StepWriter.List(StepWriter.Real(pt.X), StepWriter.Real(pt.Y), StepWriter.Real(pt.Z)));
        }

        var faceIds = new List<int>(indices.Count / 3);
        for (int i = 0; i <= indices.Count - 3; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if ((uint)i0 >= (uint)pointIds.Length || (uint)i1 >= (uint)pointIds.Length || (uint)i2 >= (uint)pointIds.Length)
            {
                continue;
            }

            int loopId = w.Add("IFCPOLYLOOP",
                StepWriter.List(StepWriter.Ref(pointIds[i0]), StepWriter.Ref(pointIds[i1]), StepWriter.Ref(pointIds[i2])));
            int boundId = w.Add("IFCFACEOUTERBOUND", $"{StepWriter.Ref(loopId)},.T.");
            faceIds.Add(w.Add("IFCFACE", StepWriter.List(StepWriter.Ref(boundId))));
        }

        int shellId = w.Add("IFCCLOSEDSHELL", StepWriter.List(faceIds.Select(StepWriter.Ref)));
        return w.Add("IFCFACETEDBREP", StepWriter.Ref(shellId));
    }

    private static int GetOrCreateSurfaceStyle(StepWriter w, IfcMaterial mat, Dictionary<string, int> cache)
    {
        string key = $"{mat.Name}_{mat.R:F2}_{mat.G:F2}_{mat.B:F2}";
        if (cache.TryGetValue(key, out int existingId))
        {
            return existingId;
        }

        int colorId = w.Add("IFCCOLOURRGB",
            $"$,{StepWriter.Real(mat.R)},{StepWriter.Real(mat.G)},{StepWriter.Real(mat.B)}");
        int shadingId = w.Add("IFCSURFACESTYLESHADING", StepWriter.Ref(colorId));
        int styleId = w.Add("IFCSURFACESTYLE",
            $"{StepWriter.Str(mat.Name)},.BOTH.,({StepWriter.Ref(shadingId)})");

        cache[key] = styleId;
        return styleId;
    }

    private static int AddIfcProduct(
        StepWriter w,
        string ifcType,
        string guid,
        int ownerHistoryId,
        string name,
        int placementId,
        int shapeId,
        string tag)
    {
        string commonArgs = $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,{StepWriter.Ref(placementId)},{StepWriter.Ref(shapeId)},{StepWriter.Str(tag)}";

        return ifcType switch
        {
            "IFCWALL" => w.Add("IFCWALL", commonArgs),
            "IFCSLAB" => w.Add("IFCSLAB", $"{commonArgs},.FLOOR."),
            "IFCCOLUMN" => w.Add("IFCCOLUMN", commonArgs),
            "IFCBEAM" => w.Add("IFCBEAM", commonArgs),
            "IFCREINFORCINGBAR" => w.Add("IFCREINFORCINGBAR", $"{commonArgs},$,$,$,$,$"),
            "IFCWINDOW" => w.Add("IFCWINDOW", $"{commonArgs},$,$"),
            "IFCDOOR" => w.Add("IFCDOOR", $"{commonArgs},$,$,$"),
            "IFCFOOTING" => w.Add("IFCFOOTING", $"{commonArgs},.FOOTING_BEAM."),
            "IFCRAILING" => w.Add("IFCRAILING", $"{commonArgs},.GUARDRAIL."),
            "IFCSTAIR" => w.Add("IFCSTAIR", $"{commonArgs},.STRAIGHT_RUN_STAIR."),
            "IFCROOF" => w.Add("IFCROOF", $"{commonArgs},.FLAT_ROOF."),
            "IFCPIPESEGMENT" => w.Add("IFCPIPESEGMENT", commonArgs),
            "IFCDUCTSEGMENT" => w.Add("IFCDUCTSEGMENT", commonArgs),
            _ => w.Add("IFCBUILDINGELEMENTPROXY", $"{commonArgs},.ELEMENT."),
        };
    }

    public static string ClassifyElement(string name, string? materialName = null)
    {
        string target = $"{name} {materialName}";

        if (ContainsAny(target, "стена", "стены", "wall", "walls"))
            return "IFCWALL";
        if (ContainsAny(target, "перекрытие", "перекрытия", "плита", "плиты", "floor", "floors", "slab", "slabs"))
            return "IFCSLAB";
        if (ContainsAny(target, "колонна", "колонны", "column", "columns"))
            return "IFCCOLUMN";
        if (ContainsAny(target, "балка", "балки", "beam", "beams", "ригель"))
            return "IFCBEAM";
        if (ContainsAny(target, "арматур", "rebar", "стержень", "каркас"))
            return "IFCREINFORCINGBAR";
        if (ContainsAny(target, "окно", "окна", "window", "windows"))
            return "IFCWINDOW";
        if (ContainsAny(target, "дверь", "двери", "door", "doors"))
            return "IFCDOOR";
        if (ContainsAny(target, "фундамент", "ростверк", "foundation", "footing"))
            return "IFCFOOTING";
        if (ContainsAny(target, "лестница", "лестницы", "stair", "stairs"))
            return "IFCSTAIR";
        if (ContainsAny(target, "ограждение", "ограждения", "railing", "railings", "перила"))
            return "IFCRAILING";
        if (ContainsAny(target, "кровля", "крыша", "roof", "roofs"))
            return "IFCROOF";
        if (ContainsAny(target, "труба", "трубы", "pipe", "pipes"))
            return "IFCPIPESEGMENT";
        if (ContainsAny(target, "воздуховод", "воздуховоды", "duct", "ducts"))
            return "IFCDUCTSEGMENT";

        return "IFCBUILDINGELEMENTPROXY";
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (text.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
