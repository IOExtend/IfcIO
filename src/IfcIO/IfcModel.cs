using System.Globalization;
using System.IO.Compression;
using System.Text;
using IfcIO.Core;
using IfcIO.Exporters;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;

namespace IfcIO;

/// <summary>
/// Top-level in-memory IFC BIM model.
/// Manages spatial hierarchy (Project, Site, Building, Storeys), geometry meshes,
/// materials, property sets, and serialization to ISO 10303-21 STEP physical files (.ifc, .ifczip),
/// binary glTF 2.0 (.glb), and Wavefront OBJ (.obj).
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

    /// <summary>Whether to use modern IfcTriangulatedFaceSet for IFC4 exports (60% smaller files, faster).</summary>
    public bool UseTessellationInIfc4 { get; set; } = true;

    public List<IfcBuildingStorey> Storeys { get; } = [];
    public List<IfcMaterial> Materials { get; } = [];
    public List<IfcTypeProduct> TypeProducts { get; } = [];

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

    /// <summary>Gets or creates a reusable IFC type definition object.</summary>
    public IfcTypeProduct GetOrCreateType(string typeName, string? ifcType = null)
    {
        var existing = TypeProducts.Find(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var type = new IfcTypeProduct
        {
            Name = typeName,
            IfcType = ifcType ?? DeriveTypeProduct(typeName),
        };
        TypeProducts.Add(type);
        return type;
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

    /// <summary>Validates the model against buildingSMART MVD and geometric integrity rules.</summary>
    public Validation.IfcValidationResult Validate() => Validation.IfcValidator.Validate(this);

    /// <summary>Serializes the IFC model to a file on disk (.ifc or .ifczip).</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.EndsWith(".ifczip", StringComparison.OrdinalIgnoreCase))
        {
            SaveZip(path);
            return;
        }

        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, Encoding.UTF8, bufferSize: 65536);
        Save(writer, Path.GetFileName(path));
    }

    /// <summary>Serializes the IFC model to a compressed PKZip (.ifczip) archive file on disk.</summary>
    public void SaveZip(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string innerName = Path.GetFileNameWithoutExtension(path) + ".ifc";

        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(innerName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8, bufferSize: 65536);
        Save(writer, innerName);
    }

    /// <summary>Serializes the IFC model to a <see cref="Stream"/> in ISO 10303-21 format.</summary>
    public void Save(Stream stream, string fileName = "model.ifc")
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 65536, leaveOpen: true);
        Save(writer, fileName);
    }

    /// <summary>Exports the model to binary glTF 2.0 (.glb) format.</summary>
    public void SaveGlb(string path) => IfcGlbExporter.Save(this, path);

    /// <summary>Exports the model to binary glTF 2.0 (.glb) stream.</summary>
    public void SaveGlb(Stream stream) => IfcGlbExporter.Write(this, stream);

    /// <summary>Exports the model to Wavefront OBJ format (.obj + .mtl).</summary>
    public void SaveObj(string path) => IfcObjExporter.Save(this, path);

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

        // 5. Type Products
        var typeIdMap = new Dictionary<IfcTypeProduct, int>();
        foreach (var type in TypeProducts)
        {
            string typeGuid = type.GlobalId ?? IfcGuid.NewGuid();
            int typeEntityId = w.Add(type.IfcType,
                $"{StepWriter.Str(typeGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(type.Name)},$,$,$,$,{StepWriter.Str(type.Tag ?? type.Name)}");
            typeIdMap[type] = typeEntityId;
        }

        // 6. Storeys & Elements
        var storeyIds = new List<int>();
        var typeToElementsMap = new Dictionary<IfcTypeProduct, List<int>>();

        bool useTessellation = (Schema == IfcSchema.Ifc4 && UseTessellationInIfc4);

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

                    int geomId;
                    string repType;

                    if (useTessellation)
                    {
                        geomId = WriteMeshTriangulatedFaceSet(w, element.Mesh.Vertices, element.Mesh.Indices);
                        repType = "Tessellation";
                    }
                    else
                    {
                        geomId = WriteMeshFacetedBrep(w, element.Mesh.Vertices, element.Mesh.Indices);
                        repType = "Brep";
                    }

                    if (styleId.HasValue)
                    {
                        w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(geomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(element.Material?.Name)}");
                    }

                    int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                        $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},({StepWriter.Ref(geomId)})");
                    prodShapeId = w.Add("IFCPRODUCTDEFINITIONSHAPE",
                        $"$,$,({StepWriter.Ref(shapeRepId)})");
                }

                string elemGuid = element.GlobalId ?? IfcGuid.FromElementId(element.Id, "IfcIO");
                string ifcType = !string.IsNullOrEmpty(element.IfcType)
                    ? element.IfcType
                    : ClassifyElement(element.Name, element.Material?.Name);

                int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, element.Name, prodPlacementId, prodShapeId, element.Tag ?? element.Id.ToString(CultureInfo.InvariantCulture));
                storeyElementIds.Add(elemId);

                // Type relation tracking
                if (element.TypeProduct is not null)
                {
                    if (!typeToElementsMap.TryGetValue(element.TypeProduct, out var list))
                    {
                        list = [];
                        typeToElementsMap[element.TypeProduct] = list;
                    }
                    list.Add(elemId);
                }

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

        // 7. RelDefinesByType
        foreach (var (typeProduct, elemIds) in typeToElementsMap)
        {
            if (typeIdMap.TryGetValue(typeProduct, out int typeEntityId) && elemIds.Count > 0)
            {
                w.Add("IFCRELDEFINESBYTYPE",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(elemIds.Select(StepWriter.Ref))},{StepWriter.Ref(typeEntityId)}");
            }
        }

        if (storeyIds.Count > 0)
        {
            w.Add("IFCRELAGGREGATES",
                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(buildingId)},{StepWriter.List(storeyIds.Select(StepWriter.Ref))}");
        }

        w.WriteTo(writer, fileName, schemaStr, viewDef, Application, Organization);
    }

    /// <summary>Parses an existing IFC STEP file (.ifc or .ifczip) from disk into a structured model.</summary>
    public static IfcModel Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.EndsWith(".ifczip", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The .ifczip archive does not contain an .ifc file.");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Open(reader, Path.GetFileNameWithoutExtension(path));
        }

        using var fileStream = File.OpenRead(path);
        using var fileReader = new StreamReader(fileStream, Encoding.UTF8);
        return Open(fileReader, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Parses an existing IFC STEP stream into a structured model with storeys, elements, properties, and meshes.</summary>
    public static IfcModel Open(TextReader reader, string? modelName = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var step = StepReader.Read(reader);
        var schema = (step.Schema?.Contains("IFC4", StringComparison.OrdinalIgnoreCase) == true)
            ? IfcSchema.Ifc4
            : IfcSchema.Ifc2x3;

        var model = new IfcModel
        {
            ProjectName = modelName ?? step.FileName ?? "Imported IFC Project",
            Schema = schema,
        };
        model.Storeys.Clear(); // Clear default storey

        // 1. Parse Project, Building
        var projectEntity = step.OfType("IFCPROJECT").FirstOrDefault();
        if (projectEntity is not null)
        {
            model.ProjectName = projectEntity.GetString(2) ?? model.ProjectName;
        }
        var buildingEntity = step.OfType("IFCBUILDING").FirstOrDefault();
        if (buildingEntity is not null)
        {
            model.BuildingName = buildingEntity.GetString(2) ?? model.BuildingName;
        }

        // 2. Parse Storeys
        var storeyMap = new Dictionary<int, IfcBuildingStorey>();
        foreach (var sEnt in step.OfType("IFCBUILDINGSTOREY"))
        {
            var storey = new IfcBuildingStorey
            {
                Name = sEnt.GetString(2) ?? $"Level {sEnt.Id}",
                Elevation = sEnt.GetReal(9) ?? 0.0,
                GlobalId = sEnt.GetString(0),
            };
            model.Storeys.Add(storey);
            storeyMap[sEnt.Id] = storey;
        }

        if (model.Storeys.Count == 0)
        {
            var defStorey = new IfcBuildingStorey { Name = "Level 1", Elevation = 0.0 };
            model.Storeys.Add(defStorey);
        }

        // 3. Parse Materials & Styles
        var styleToMaterialMap = new Dictionary<int, IfcMaterial>();
        foreach (var styleEnt in step.OfType("IFCSURFACESTYLE"))
        {
            string matName = styleEnt.GetString(0) ?? "Material";
            var styles = styleEnt.GetRefList(2);
            foreach (int shadingId in styles)
            {
                var shadingEnt = step.Find(shadingId);
                if (shadingEnt is not null && shadingEnt.Type.Equals("IFCSURFACESTYLESHADING", StringComparison.OrdinalIgnoreCase))
                {
                    int? colId = shadingEnt.GetRef(0);
                    var colEnt = step.Find(colId);
                    if (colEnt is not null && colEnt.Type.Equals("IFCCOLOURRGB", StringComparison.OrdinalIgnoreCase))
                    {
                        double r = colEnt.GetReal(1) ?? 0.8;
                        double g = colEnt.GetReal(2) ?? 0.8;
                        double b = colEnt.GetReal(3) ?? 0.8;

                        var mat = new IfcMaterial { Name = matName, R = r, G = g, B = b };
                        styleToMaterialMap[styleEnt.Id] = mat;
                        if (!model.Materials.Contains(mat)) model.Materials.Add(mat);
                        break;
                    }
                }
            }
        }

        // Map styled items to materials
        var geomToMaterialMap = new Dictionary<int, IfcMaterial>();
        foreach (var styledItem in step.OfType("IFCSTYLEDITEM"))
        {
            int? geomId = styledItem.GetRef(0);
            var styles = styledItem.GetRefList(1);
            if (geomId.HasValue)
            {
                foreach (int sId in styles)
                {
                    if (styleToMaterialMap.TryGetValue(sId, out var mat))
                    {
                        geomToMaterialMap[geomId.Value] = mat;
                        break;
                    }
                }
            }
        }

        // 4. Map Elements to Storeys
        var elementToStoreyMap = new Dictionary<int, IfcBuildingStorey>();
        foreach (var relSpatial in step.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
        {
            int? structId = relSpatial.GetRef(5);
            if (structId.HasValue && storeyMap.TryGetValue(structId.Value, out var targetStorey))
            {
                var elemIds = relSpatial.GetRefList(4);
                foreach (int eId in elemIds)
                {
                    elementToStoreyMap[eId] = targetStorey;
                }
            }
        }

        // 5. Parse Property Sets
        var psetMap = new Dictionary<int, IfcPropertySet>();
        foreach (var psetEnt in step.OfType("IFCPROPERTYSET"))
        {
            string psetName = psetEnt.GetString(2) ?? "PropertySet";
            var pset = new IfcPropertySet(psetName);
            var propIds = psetEnt.GetRefList(4);

            foreach (int pId in propIds)
            {
                var pValEnt = step.Find(pId);
                if (pValEnt is not null && pValEnt.Type.Equals("IFCPROPERTYSINGLEVALUE", StringComparison.OrdinalIgnoreCase))
                {
                    string propName = pValEnt.GetString(0) ?? "Property";
                    string? rawVal = pValEnt.GetRaw(2);
                    string cleanVal = ExtractNominalValue(rawVal);
                    pset.Add(propName, cleanVal);
                }
            }
            psetMap[psetEnt.Id] = pset;
        }

        var elementPropertySets = new Dictionary<int, List<IfcPropertySet>>();
        foreach (var relProps in step.OfType("IFCRELDEFINESBYPROPERTIES"))
        {
            int? psetId = relProps.GetRef(5);
            if (psetId.HasValue && psetMap.TryGetValue(psetId.Value, out var pset))
            {
                var elemIds = relProps.GetRefList(4);
                foreach (int eId in elemIds)
                {
                    if (!elementPropertySets.TryGetValue(eId, out var list))
                    {
                        list = [];
                        elementPropertySets[eId] = list;
                    }
                    list.Add(pset);
                }
            }
        }

        // 6. Parse Elements & Meshes
        var defaultStorey = model.Storeys[0];

        foreach (var entity in step.Entities.Values)
        {
            if (!IsBimProductType(entity.Type)) continue;

            string globalId = entity.GetString(0) ?? IfcGuid.NewGuid();
            string name = entity.GetString(2) ?? entity.Type;
            string? tag = entity.GetString(7);

            var targetStorey = elementToStoreyMap.TryGetValue(entity.Id, out var s) ? s : defaultStorey;

            var element = new IfcElement
            {
                Id = entity.Id,
                Name = name,
                IfcType = entity.Type,
                GlobalId = globalId,
                Tag = tag,
            };

            // Link property sets
            if (elementPropertySets.TryGetValue(entity.Id, out var psets))
            {
                element.PropertySets.AddRange(psets);
            }

            // Parse Mesh Geometry
            int? shapeDefId = entity.GetRef(6);
            if (shapeDefId.HasValue)
            {
                var mesh = ParseMeshGeometry(step, shapeDefId.Value, name, geomToMaterialMap, out var mat);
                if (mesh is not null)
                {
                    element.Mesh = mesh;
                }
                if (mat is not null)
                {
                    element.Material = mat;
                }
            }

            targetStorey.Elements.Add(element);
        }

        return model;
    }

    private static IfcMesh? ParseMeshGeometry(
        StepFile step,
        int shapeDefId,
        string meshName,
        Dictionary<int, IfcMaterial> geomToMaterialMap,
        out IfcMaterial? material)
    {
        material = null;
        var shapeDef = step.Find(shapeDefId);
        if (shapeDef is null) return null;

        var repIds = shapeDef.GetRefList(2);
        foreach (int repId in repIds)
        {
            var repEnt = step.Find(repId);
            if (repEnt is null || !repEnt.Type.Equals("IFCSHAPEREPRESENTATION", StringComparison.OrdinalIgnoreCase))
                continue;

            var itemIds = repEnt.GetRefList(3);
            foreach (int itemId in itemIds)
            {
                var itemEnt = step.Find(itemId);
                if (itemEnt is null) continue;

                if (geomToMaterialMap.TryGetValue(itemId, out var mat))
                {
                    material = mat;
                }

                // A. IFCFACETEDBREP
                if (itemEnt.Type.Equals("IFCFACETEDBREP", StringComparison.OrdinalIgnoreCase))
                {
                    int? shellId = itemEnt.GetRef(0);
                    var shellEnt = step.Find(shellId);
                    if (shellEnt is null) continue;

                    var vertices = new List<IfcVector3>();
                    var indices = new List<int>();
                    var pointIndexMap = new Dictionary<int, int>();

                    var faceIds = shellEnt.GetRefList(0);
                    foreach (int faceId in faceIds)
                    {
                        var faceEnt = step.Find(faceId);
                        if (faceEnt is null) continue;

                        var boundIds = faceEnt.GetRefList(0);
                        foreach (int boundId in boundIds)
                        {
                            var boundEnt = step.Find(boundId);
                            if (boundEnt is null) continue;

                            int? loopId = boundEnt.GetRef(0);
                            var loopEnt = step.Find(loopId);
                            if (loopEnt is null || !loopEnt.Type.Equals("IFCPOLYLOOP", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var ptIds = loopEnt.GetRefList(0);
                            var faceIndices = new List<int>(ptIds.Count);

                            foreach (int ptId in ptIds)
                            {
                                if (!pointIndexMap.TryGetValue(ptId, out int idx))
                                {
                                    var ptEnt = step.Find(ptId);
                                    if (ptEnt is not null)
                                    {
                                        string coordStr = ptEnt.GetRaw(0) ?? string.Empty;
                                        var coords = StepEntity.ParseArguments(coordStr.Trim('(', ')'));
                                        double x = coords.Count > 0 && double.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) ? cx : 0.0;
                                        double y = coords.Count > 1 && double.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy) ? cy : 0.0;
                                        double z = coords.Count > 2 && double.TryParse(coords[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cz) ? cz : 0.0;

                                        idx = vertices.Count;
                                        vertices.Add(new IfcVector3(x, y, z));
                                        pointIndexMap[ptId] = idx;
                                    }
                                }
                                faceIndices.Add(idx);
                            }

                            // Fan triangulation for convex polygons
                            for (int i = 1; i + 1 < faceIndices.Count; i++)
                            {
                                indices.Add(faceIndices[0]);
                                indices.Add(faceIndices[i]);
                                indices.Add(faceIndices[i + 1]);
                            }
                        }
                    }

                    if (vertices.Count >= 3 && indices.Count >= 3)
                    {
                        return IfcMesh.Create(itemId, meshName, vertices, indices);
                    }
                }

                // B. IFCTRIANGULATEDFACESET (IFC4)
                if (itemEnt.Type.Equals("IFCTRIANGULATEDFACESET", StringComparison.OrdinalIgnoreCase))
                {
                    int? ptListId = itemEnt.GetRef(0);
                    var ptListEnt = step.Find(ptListId);
                    if (ptListEnt is null) continue;

                    var vertices = new List<IfcVector3>();
                    string rawCoords = ptListEnt.GetRaw(0) ?? string.Empty;

                    // Parse point list: ((x1, y1, z1), (x2, y2, z2), ...)
                    foreach (var tuple in StepEntity.ParseArguments(rawCoords.Trim('(', ')')))
                    {
                        var parts = StepEntity.ParseArguments(tuple.Trim('(', ')'));
                        double x = parts.Count > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double px) ? px : 0.0;
                        double y = parts.Count > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double py) ? py : 0.0;
                        double z = parts.Count > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double pz) ? pz : 0.0;
                        vertices.Add(new IfcVector3(x, y, z));
                    }

                    var indices = new List<int>();
                    string rawTriangles = itemEnt.GetRaw(3) ?? string.Empty;
                    foreach (var triTuple in StepEntity.ParseArguments(rawTriangles.Trim('(', ')')))
                    {
                        var parts = StepEntity.ParseArguments(triTuple.Trim('(', ')'));
                        if (parts.Count >= 3)
                        {
                            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int i0) &&
                                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int i1) &&
                                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int i2))
                            {
                                // STEP 1-based to 0-based
                                indices.Add(i0 - 1);
                                indices.Add(i1 - 1);
                                indices.Add(i2 - 1);
                            }
                        }
                    }

                    if (vertices.Count >= 3 && indices.Count >= 3)
                    {
                        return IfcMesh.Create(itemId, meshName, vertices, indices);
                    }
                }
            }
        }

        return null;
    }

    private static string ExtractNominalValue(string? rawVal)
    {
        if (string.IsNullOrWhiteSpace(rawVal) || rawVal == "$" || rawVal == "*") return string.Empty;

        int p1 = rawVal.IndexOf('(');
        int p2 = rawVal.LastIndexOf(')');
        if (p1 >= 0 && p2 > p1)
        {
            string inner = rawVal[(p1 + 1)..p2].Trim();
            if (inner.StartsWith('\'') && inner.EndsWith('\'') && inner.Length >= 2)
            {
                return StepWriter.Unescape(inner[1..^1]) ?? inner[1..^1];
            }
            return StepWriter.Unescape(inner) ?? inner;
        }

        return StepWriter.Unescape(rawVal) ?? rawVal;
    }

    private static bool IsBimProductType(string type)
    {
        return type.StartsWith("IFCWALL", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCSLAB", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCCOLUMN", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCBEAM", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCWINDOW", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCDOOR", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCFOOTING", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCREINFORCINGBAR", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCSTAIR", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCRAILING", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCROOF", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCPIPESEGMENT", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCDUCTSEGMENT", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCMEMBER", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCPLATE", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCCOVERING", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("IFCFURNISHINGELEMENT", StringComparison.OrdinalIgnoreCase)
            || type.Equals("IFCBUILDINGELEMENTPROXY", StringComparison.OrdinalIgnoreCase);
    }

    private static int WriteMeshTriangulatedFaceSet(StepWriter w, IReadOnlyList<IfcVector3> vertices, IReadOnlyList<int> indices)
    {
        var coords = new StringBuilder();
        for (int v = 0; v < vertices.Count; v++)
        {
            if (v > 0) coords.Append(',');
            var pt = vertices[v];
            coords.Append('(')
                  .Append(StepWriter.Real(pt.X)).Append(',')
                  .Append(StepWriter.Real(pt.Y)).Append(',')
                  .Append(StepWriter.Real(pt.Z)).Append(')');
        }

        int pointListId = w.Add("IFCCARTESIANPOINTLIST3D", $"({coords})");

        var coordIndex = new StringBuilder();
        int triCount = indices.Count / 3;
        for (int i = 0; i < triCount; i++)
        {
            if (i > 0) coordIndex.Append(',');
            coordIndex.Append('(')
                      .Append(indices[i * 3] + 1).Append(',')
                      .Append(indices[i * 3 + 1] + 1).Append(',')
                      .Append(indices[i * 3 + 2] + 1).Append(')');
        }

        return w.Add("IFCTRIANGULATEDFACESET", $"{StepWriter.Ref(pointListId)},$,.T.,({coordIndex}),$");
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

    private static string DeriveTypeProduct(string name)
    {
        string elemType = ClassifyElement(name);
        return elemType switch
        {
            "IFCWALL" => "IFCWALLTYPE",
            "IFCSLAB" => "IFCSLABTYPE",
            "IFCCOLUMN" => "IFCCOLUMNTYPE",
            "IFCBEAM" => "IFCBEAMTYPE",
            "IFCREINFORCINGBAR" => "IFCREINFORCINGBARTYPE",
            "IFCWINDOW" => "IFCWINDOWSTYLE",
            "IFCDOOR" => "IFCDOORSTYLE",
            "IFCFOOTING" => "IFCFOOTINGTYPE",
            "IFCRAILING" => "IFCRAILINGTYPE",
            "IFCSTAIR" => "IFCSTAIRFLIGHTTYPE",
            "IFCROOF" => "IFCROOFTYPE",
            "IFCPIPESEGMENT" => "IFCPIPESEGMENTTYPE",
            "IFCDUCTSEGMENT" => "IFCDUCTSEGMENTTYPE",
            _ => "IFCBUILDINGELEMENTPROXYTYPE",
        };
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
