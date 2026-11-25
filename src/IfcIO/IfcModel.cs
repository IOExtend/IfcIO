using System.Globalization;
using System.IO.Compression;
using System.Text;
using IfcIO.Core;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;

namespace IfcIO;

/// <summary>
/// Top-level in-memory IFC BIM model.
/// Manages spatial hierarchy (Project, Site, Building, Storeys), geometry meshes,
/// materials, property sets, and serialization to ISO 10303-21 STEP physical files (.ifc, .ifczip).
/// </summary>
public sealed partial class IfcModel
{
    private const string DegreeInRadians = "0.017453292519943278";

    public IfcSchema Schema { get; set; } = IfcSchema.Ifc2x3;
    /// <summary>The exact FILE_SCHEMA identifier of the source file, or null for a new model.</summary>
    public string? SourceSchemaIdentifier { get; internal set; }

    internal bool IsIfc4x3Subset => Schema == IfcSchema.Ifc4x3 || IsIfc4x3Identifier(SourceSchemaIdentifier);
    /// <summary>Number of IFC4X3 product representations or ports omitted because their placement is unsupported.</summary>
    public int UnsupportedPlacementCount { get; internal set; }

    /// <summary>Boolean import warnings for skipped operands or operations not evaluated in the mesh.</summary>
    public IReadOnlyList<string> BooleanImportWarnings { get; internal set; } = [];
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
    public List<IfcMaterialLayerSet> MaterialLayerSets { get; } = [];
    public List<IfcMaterialProfileSet> MaterialProfileSets { get; } = [];
    public List<IfcTypeProduct> TypeProducts { get; } = [];
    public List<IfcGroup> Groups { get; } = [];
    public List<IfcSystem> Systems { get; } = [];
    public List<IfcZone> Zones { get; } = [];
    public List<Model.Sequence.IfcWorkSchedule> WorkSchedules { get; } = [];
    public List<Model.Cost.IfcCostSchedule> CostSchedules { get; } = [];

    public IEnumerable<IfcElement> AllElements => Storeys.SelectMany(s => s.Elements);
    public IEnumerable<IfcSpace> AllSpaces => Storeys.SelectMany(s => s.Spaces);
    public IEnumerable<IfcOpeningElement> AllOpenings => AllElements.SelectMany(e => e.Openings);
    public IEnumerable<IfcDistributionPort> AllPorts => AllElements.SelectMany(e => e.Ports);

    private bool _hasCustomStorey;
    private readonly HashSet<int> _reservedElementIds = [];
    private int _nextElementId = 1;

    public IfcModel()
    {
        // Default storey
        RegisterStorey(new IfcBuildingStorey { Name = "Level 1", Elevation = 0.0 });
    }

    internal int AllocateElementId(IfcBuildingStorey? targetStorey = null)
    {
        foreach (var storey in Storeys)
            AttachStorey(storey);
        if (targetStorey is not null && targetStorey.Model != this)
            AttachStorey(targetStorey);
        while (!_reservedElementIds.Add(_nextElementId))
        {
            if (_nextElementId == int.MaxValue)
                throw new InvalidOperationException("The model has exhausted its positive element IDs.");
            _nextElementId++;
        }
        return _nextElementId;
    }

    internal void ReserveElementIds(IfcElement element)
    {
        _reservedElementIds.Add(element.Id);
        foreach (var opening in element.Openings)
        {
            _reservedElementIds.Add(opening.Id);
            if (opening.Filling is { } filling)
                _reservedElementIds.Add(filling.Id);
        }
    }

    private void AttachStorey(IfcBuildingStorey storey)
    {
        if (storey.Model is null) storey.Model = this;
        storey.SynchronizeIds(this);
    }

    internal void RegisterStorey(IfcBuildingStorey storey)
    {
        AttachStorey(storey);
        Storeys.Add(storey);
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
        var existing = Storeys.Find(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            AttachStorey(existing);
            _hasCustomStorey = true;
            existing.Elevation = elevation;
            return existing;
        }

        // If no custom storeys have been configured and the only storey is the default empty "Level 1", reuse/rename it
        if (!_hasCustomStorey && Storeys.Count == 1 && Storeys[0].Elements.Count == 0 && Storeys[0].Spaces.Count == 0 && Storeys[0].Name == "Level 1" && Math.Abs(Storeys[0].Elevation) < 1e-9)
        {
            _hasCustomStorey = true;
            Storeys[0].Name = name;
            Storeys[0].Elevation = elevation;
            return Storeys[0];
        }

        _hasCustomStorey = true;
        var storey = new IfcBuildingStorey { Name = name, Elevation = elevation };
        RegisterStorey(storey);
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
        AttachStorey(storey);
        ReserveElementIds(element);
        storey.AddExistingElement(element);
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

        int nextId = AllocateElementId(storey);
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

        storey.AddExistingElement(element);
        return element;
    }

    /// <summary>Creates and adds an element with a parametric rectangular extruded swept solid.</summary>
    public IfcElement AddExtrudedRectangle(
        string name,
        double xDim,
        double yDim,
        double depth,
        IfcVector3? position = null,
        IfcVector3? direction = null,
        IfcMaterial? material = null,
        IfcBuildingStorey? storey = null,
        string? ifcType = null)
    {
        storey ??= Storeys[0];
        int nextId = AllocateElementId(storey);
        var profile = new IfcRectangleProfileDef(xDim, yDim, name: $"{name}_Profile");
        var solid = new IfcExtrudedAreaSolid(profile, depth, position, direction);

        var element = new IfcElement
        {
            Id = nextId,
            Name = name,
            IfcType = ifcType ?? ClassifyElement(name, material?.Name),
            Solid = solid,
            Material = material,
        };

        if (material is not null && !Materials.Contains(material))
        {
            Materials.Add(material);
        }

        storey.AddExistingElement(element);
        return element;
    }

    /// <summary>Creates and adds an element with a parametric circular extruded swept solid.</summary>
    public IfcElement AddExtrudedCircle(
        string name,
        double radius,
        double depth,
        IfcVector3? position = null,
        IfcVector3? direction = null,
        IfcMaterial? material = null,
        IfcBuildingStorey? storey = null,
        string? ifcType = null)
    {
        storey ??= Storeys[0];
        int nextId = AllocateElementId(storey);
        var profile = new IfcCircleProfileDef(radius, name: $"{name}_Profile");
        var solid = new IfcExtrudedAreaSolid(profile, depth, position, direction);

        var element = new IfcElement
        {
            Id = nextId,
            Name = name,
            IfcType = ifcType ?? ClassifyElement(name, material?.Name),
            Solid = solid,
            Material = material,
        };

        if (material is not null && !Materials.Contains(material))
        {
            Materials.Add(material);
        }

        storey.AddExistingElement(element);
        return element;
    }

    /// <summary>Creates and adds an element with a parametric revolved swept solid.</summary>
    public IfcElement AddRevolvedSolid(
        string name,
        IfcProfileDef profile,
        double angle = 2.0 * Math.PI,
        IfcVector3? position = null,
        IfcAxis1Placement? axis = null,
        IfcMaterial? material = null,
        IfcBuildingStorey? storey = null,
        string? ifcType = null)
    {
        storey ??= Storeys[0];
        int nextId = AllocateElementId(storey);
        var solid = new IfcRevolvedAreaSolid(profile, angle, position, axis);

        var element = new IfcElement
        {
            Id = nextId,
            Name = name,
            IfcType = ifcType ?? ClassifyElement(name, material?.Name),
            RevolvedSolid = solid,
            Material = material,
        };

        if (material is not null && !Materials.Contains(material))
        {
            Materials.Add(material);
        }

        storey.AddExistingElement(element);
        return element;
    }

    /// <summary>
    /// Computes buildingSMART standard Base Quantities (QTO) for all elements in the model based on their 3D geometry.
    /// </summary>
    /// <param name="overwriteExisting">Whether to replace existing Qto_* sets if already present.</param>
    public void CalculateAllBaseQuantities(bool overwriteExisting = false)
    {
        foreach (var element in AllElements)
        {
            if (!overwriteExisting && element.QuantitySets.Any(q => q.Name.StartsWith("Qto_", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var baseQto = IfcQuantityCalculator.CalculateBaseQuantities(element);
            if (overwriteExisting)
            {
                element.QuantitySets.RemoveAll(q => q.Name.Equals(baseQto.Name, StringComparison.OrdinalIgnoreCase));
            }
            element.QuantitySets.Add(baseQto);
        }
    }

    /// <summary>
    /// Adds an opening / void element to a host building element (e.g. wall cutout for window/door).
    /// </summary>
    public IfcOpeningElement AddOpening(
        IfcElement hostElement,
        string name,
        IfcExtrudedAreaSolid? solid = null,
        IfcMesh? mesh = null,
        IfcElement? filling = null)
    {
        ArgumentNullException.ThrowIfNull(hostElement);

        ReserveElementIds(hostElement);
        if (filling is not null) ReserveElementIds(filling);
        int nextId = AllocateElementId();
        var opening = new IfcOpeningElement
        {
            Id = nextId,
            Name = name,
            Solid = solid,
            Mesh = mesh,
            Filling = filling,
        };

        hostElement.Openings.Add(opening);
        return opening;
    }

    /// <summary>
    /// Adds an architectural space, room, or zone to the specified storey.
    /// </summary>
    public IfcSpace AddSpace(
        string name,
        string? spaceNumber = null,
        string? longName = null,
        double grossFloorArea = 0.0,
        double height = 3.0,
        IfcBuildingStorey? storey = null,
        IfcMesh? mesh = null)
    {
        storey ??= Storeys[0];
        int nextId = AllocateElementId(storey);
        var space = new IfcSpace
        {
            Id = nextId,
            Name = name,
            SpaceNumber = spaceNumber,
            LongName = longName ?? name,
            GrossFloorArea = grossFloorArea,
            NetFloorArea = grossFloorArea > 0 ? grossFloorArea * 0.95 : 0.0,
            Height = height,
            GrossVolume = grossFloorArea * height,
            Mesh = mesh,
        };
        storey.AddExistingSpace(space);
        return space;
    }

    /// <summary>
    /// Creates and registers a functional MEP building system (e.g. HVAC, Hydronic, Plumbing, Electrical).
    /// </summary>
    public IfcSystem AddSystem(string name, string systemType = "HVAC", IEnumerable<IfcElement>? elements = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sys = new IfcSystem(name, systemType);
        if (elements is not null)
        {
            sys.Elements.AddRange(elements);
        }
        Systems.Add(sys);
        return sys;
    }

    /// <summary>
    /// Creates and registers a logical grouping of building elements.
    /// </summary>
    public IfcGroup AddGroup(string name, string? description = null, IEnumerable<IfcElement>? elements = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var group = new IfcGroup { Name = name, Description = description };
        if (elements is not null)
        {
            group.Elements.AddRange(elements);
        }
        Groups.Add(group);
        return group;
    }

    /// <summary>
    /// Creates and registers a spatial zone grouping spaces sharing thermal, fire, or acoustic classifications.
    /// Conforms to buildingSMART standard IFCZONE.
    /// </summary>
    public IfcZone AddZone(string name, string? zoneType = null, IEnumerable<IfcSpace>? spaces = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var zone = new IfcZone(name, zoneType);
        if (spaces is not null)
        {
            foreach (var s in spaces) zone.AddSpace(s);
        }
        Zones.Add(zone);
        return zone;
    }

    /// <summary>
    /// Creates and registers a 4D construction work schedule.
    /// Conforms to buildingSMART standard IFCWORKSCHEDULE.
    /// </summary>
    public Model.Sequence.IfcWorkSchedule AddWorkSchedule(string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var schedule = new Model.Sequence.IfcWorkSchedule(name, description);
        WorkSchedules.Add(schedule);
        return schedule;
    }

    /// <summary>
    /// Creates and registers a 5D cost schedule / budget plan.
    /// Conforms to buildingSMART standard IFCCOSTSCHEDULE.
    /// </summary>
    public Model.Cost.IfcCostSchedule AddCostSchedule(
        string name,
        string currency = "USD",
        Model.Cost.IfcCostScheduleType type = Model.Cost.IfcCostScheduleType.CostPlan)
    {
        ArgumentNullException.ThrowIfNull(name);
        var schedule = new Model.Cost.IfcCostSchedule(name, currency, type);
        CostSchedules.Add(schedule);
        return schedule;
    }

    /// <summary>Calculates the grand total cost across all cost schedules in the model.</summary>
    public double CalculateTotalCost() => CostSchedules.Sum(s => s.GetTotalCost());

    /// <summary>Gets the 4D construction simulation state of an element at the specified date using the active work schedule.</summary>
    public Model.Sequence.IfcElementSimulationState GetElementSimulationStateAt(IfcElement element, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (WorkSchedules.Count == 0) return Model.Sequence.IfcElementSimulationState.Constructed;
        return WorkSchedules[0].GetElementStateAt(element, date);
    }

    /// <summary>Gets the overall 4D construction schedule progress (0.0 to 100.0) at the specified date.</summary>
    public double GetSimulationProgressAt(DateTime date)
    {
        if (WorkSchedules.Count == 0) return 100.0;
        return WorkSchedules[0].GetProgressAt(date);
    }

    /// <summary>
    /// Adds an element defined by a 3D solid clipped by an infinite plane (Constructive Solid Geometry / Boolean clipping).
    /// Conforms to buildingSMART IFCBOOLEANCLIPPINGRESULT.
    /// </summary>
    public IfcElement AddClippedSolid(
        string name,
        IfcExtrudedAreaSolid solid,
        IfcPlane plane,
        bool agreementFlag = true,
        IfcMaterial? material = null,
        IfcBuildingStorey? storey = null,
        string ifcType = "IfcBuildingElementProxy")
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(solid);
        ArgumentNullException.ThrowIfNull(plane);

        storey ??= Storeys[0];
        int nextId = AllocateElementId(storey);

        var halfSpace = new IfcHalfSpaceSolid(plane, agreementFlag);
        var booleanResult = new IfcBooleanResult(solid, halfSpace, IfcBooleanOperator.Difference);
        var element = new IfcElement
        {
            Id = nextId,
            Name = name,
            IfcType = ifcType,
            BooleanResult = booleanResult,
            Material = material,
        };
        AddElement(element, storey);
        return element;
    }

    /// <summary>
    /// Adds an MEP distribution port (terminal) to an element for flow network connectivity.
    /// Conforms to buildingSMART IFCDISTRIBUTIONPORT and IFCRELCONNECTSPORTTOELEMENT.
    /// </summary>
    public static IfcDistributionPort AddDistributionPort(
        IfcElement hostElement,
        string name,
        IfcFlowDirection flowDirection = IfcFlowDirection.SourceAndSink,
        IfcVector3? position = null,
        IfcVector3? direction = null,
        string? systemType = null)
    {
        ArgumentNullException.ThrowIfNull(hostElement);
        ArgumentNullException.ThrowIfNull(name);

        var port = new IfcDistributionPort(name, flowDirection, position, direction, systemType)
        {
            HostElement = hostElement,
        };
        hostElement.Ports.Add(port);
        return port;
    }

    /// <summary>
    /// Connects two distribution ports to model fluid, airflow, or electrical continuity between MEP elements.
    /// Conforms to buildingSMART IFCRELCONNECTSPORTS.
    /// </summary>
    public static void ConnectPorts(IfcDistributionPort portA, IfcDistributionPort portB)
    {
        ArgumentNullException.ThrowIfNull(portA);
        ArgumentNullException.ThrowIfNull(portB);

        portA.ConnectedPort = portB;
        portB.ConnectedPort = portA;
    }

    /// <summary>
    /// Traverses the connected MEP distribution element network using Breadth-First Search (BFS) starting from the specified element.
    /// </summary>
    public static List<IfcElement> TraceConnectedElements(IfcElement startElement)
    {
        ArgumentNullException.ThrowIfNull(startElement);

        var visited = new HashSet<IfcElement>();
        var queue = new Queue<IfcElement>();
        queue.Enqueue(startElement);
        visited.Add(startElement);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var port in current.Ports)
            {
                var targetPort = port.ConnectedPort;
                if (targetPort?.HostElement is not null && visited.Add(targetPort.HostElement))
                {
                    queue.Enqueue(targetPort.HostElement);
                }
            }
        }

        return visited.ToList();
    }

    /// <summary>
    /// Creates and registers a multi-layer material set (composite construction) in the model.
    /// Conforms to buildingSMART IFCMATERIALLAYERSET.
    /// </summary>
    public IfcMaterialLayerSet AddMaterialLayerSet(string name, IEnumerable<IfcMaterialLayer>? layers = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var layerSet = new IfcMaterialLayerSet(name, layers);
        MaterialLayerSets.Add(layerSet);
        return layerSet;
    }

    /// <summary>
    /// Associates a multi-layer material set with an element with an optional reference line offset.
    /// Conforms to buildingSMART IFCMATERIALLAYERSETUSAGE and IFCRELASSOCIATESMATERIAL.
    /// </summary>
    public void AssignMaterialLayerSet(IfcElement element, IfcMaterialLayerSet layerSet, double offset = 0.0)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(layerSet);

        element.MaterialLayerSet = new IfcMaterialLayerSetUsage(layerSet, offset);
        if (!MaterialLayerSets.Contains(layerSet))
        {
            MaterialLayerSets.Add(layerSet);
        }
    }

    /// <summary>
    /// Creates and registers a structural material profile set in the model.
    /// Conforms to buildingSMART IFCMATERIALPROFILESET.
    /// </summary>
    public IfcMaterialProfileSet AddMaterialProfileSet(string name, IEnumerable<IfcMaterialProfile>? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var profileSet = new IfcMaterialProfileSet(name, profiles);
        MaterialProfileSets.Add(profileSet);
        return profileSet;
    }

    /// <summary>
    /// Associates a material profile set with a structural element with a cardinal point alignment.
    /// Conforms to buildingSMART IFCMATERIALPROFILESETUSAGE and IFCRELASSOCIATESMATERIAL.
    /// </summary>
    public void AssignMaterialProfileSet(IfcElement element, IfcMaterialProfileSet profileSet, int cardinalPoint = 5)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(profileSet);

        element.MaterialProfileSet = new IfcMaterialProfileSetUsage(profileSet, cardinalPoint);
        if (!MaterialProfileSets.Contains(profileSet))
        {
            MaterialProfileSets.Add(profileSet);
        }
    }

    /// <summary>Validates the model against buildingSMART MVD and geometric integrity rules.</summary>
    public Validation.IfcValidationResult Validate() => Validation.IfcValidator.Validate(this);

    /// <summary>Selects all elements in the model matching an IfcOpenShell-style selector query string.</summary>
    public IEnumerable<IfcElement> Select(string selectorQuery) =>
        Query.IfcSelector.Parse(selectorQuery).Evaluate(this);

    /// <summary>Purges unreferenced, empty, and duplicate property sets, quantity sets, and empty building storeys.</summary>
    public Patch.IfcPurgeStats Purge() => Patch.IfcPatch.Purge(this);

    /// <summary>Offsets all 3D mesh vertices in the model by a translation vector.</summary>
    public void OffsetCoordinates(Geometry.IfcVector3 offset) => Patch.IfcPatch.OffsetCoordinates(this, offset);

    /// <summary>
    /// Detects large absolute coordinate offsets and centers geometry around the local origin (0,0,0) to prevent precision jitter.
    /// </summary>
    public bool ResetCoordinates(out Geometry.IfcVector3 offset, double thresholdDistance = 10000.0) =>
        Patch.IfcPatch.ResetCoordinates(this, out offset, thresholdDistance);

    /// <summary>Extracts elements matching a selector query string into a new self-contained IFC model.</summary>
    public IfcModel Extract(string selectorQuery, string? newProjectName = null) =>
        Patch.IfcPatch.Extract(this, selectorQuery, newProjectName);

    /// <summary>Extracts elements matching a predicate into a new self-contained IFC model.</summary>
    public IfcModel Extract(Func<IfcElement, bool> predicate, string? newProjectName = null) =>
        Patch.IfcPatch.Extract(this, predicate, newProjectName);

    /// <summary>Merges multiple IFC models into a single coordinated model, reconciling storeys and re-indexing IDs.</summary>
    public static IfcModel Merge(IEnumerable<IfcModel> models, string mergedProjectName = "Merged Project") =>
        Patch.IfcPatch.Merge(models, mergedProjectName);

    /// <summary>Validates the model against a buildingSMART Information Delivery Specification (IDS 1.0) XML file.</summary>
    public Validation.Ids.IfcIdsValidationReport ValidateIds(string idsPath) =>
        Validation.Ids.IfcIdsValidator.ValidateFile(this, idsPath);

    /// <summary>Validates the model against a buildingSMART Information Delivery Specification (IDS 1.0) XML stream.</summary>
    public Validation.Ids.IfcIdsValidationReport ValidateIds(Stream idsStream) =>
        Validation.Ids.IfcIdsValidator.Validate(this, idsStream);

    /// <summary>Validates the model against a loaded buildingSMART IDS <see cref="System.Xml.Linq.XDocument"/>.</summary>
    public Validation.Ids.IfcIdsValidationReport ValidateIds(System.Xml.Linq.XDocument idsDoc) =>
        Validation.Ids.IfcIdsValidator.Validate(this, idsDoc);

    /// <summary>Serializes the IFC model to a file on disk (.ifc or .ifczip).</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        EnsureWritableSchema();
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
        EnsureWritableSchema();
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
        EnsureWritableSchema();
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 65536, leaveOpen: true);
        Save(writer, fileName);
    }

    /// <summary>Serializes the IFC model to an ISO 10303-21 STEP physical string.</summary>
    public string ToStepString(string fileName = "model.ifc")
    {
        using var sw = new StringWriter();
        Save(sw, fileName);
        return sw.ToString();
    }

    /// <summary>Serializes the IFC model to a <see cref="TextWriter"/> in ISO 10303-21 format.</summary>
    public void Save(TextWriter writer, string fileName = "model.ifc")
    {
        ArgumentNullException.ThrowIfNull(writer);
        EnsureWritableSchema();

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
        int lengthUnitId = WriteLengthUnit(w, LengthUnit);
        int angleUnitId = WritePlaneAngleUnit(w, AngleUnit);

        int areaUnitId = w.Add("IFCSIUNIT", "*,.AREAUNIT.,$,.SQUARE_METRE.");
        int volumeUnitId = w.Add("IFCSIUNIT", "*,.VOLUMEUNIT.,$,.CUBIC_METRE.");

        int unitAssignmentId = w.Add("IFCUNITASSIGNMENT",
            StepWriter.List(StepWriter.Ref(lengthUnitId), StepWriter.Ref(angleUnitId), StepWriter.Ref(areaUnitId), StepWriter.Ref(volumeUnitId)));

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
        var styleCache = new Dictionary<SurfaceStyleKey, int>();

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
        var elementEntityMap = new Dictionary<IfcElement, int>();
        var productGuids = new HashSet<string>(StringComparer.Ordinal);
        var duplicatedProductIds = ReserveProductIdentities(productGuids);
        int nextCollision = 2;

        string ProductGuid(IfcElement element, string domain)
        {
            if (element.GlobalId is { } assigned) return assigned;
            string generated = IfcGuid.FromElementId(element.Id, domain);
            if (duplicatedProductIds.Contains((element.Id, domain)) || productGuids.Contains(generated))
                generated = IfcGuid.FromKey(domain + ":" + element.DuplicateIdentity);
            while (!productGuids.Add(generated))
                generated = IfcGuid.FromElementId(element.Id, domain + ":" + (nextCollision++).ToString(CultureInfo.InvariantCulture));
            return generated;
        }

        bool useTessellation = (Schema == IfcSchema.Ifc4 && UseTessellationInIfc4);

        for (int sIdx = 0; sIdx < Storeys.Count; sIdx++)
        {
            var storey = Storeys[sIdx];
            int storeyPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(buildingPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
            int storeyId = w.Add("IFCBUILDINGSTOREY",
                $"{StepWriter.Str(storey.GlobalId ?? IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(storey.Name)},$,$,{StepWriter.Ref(storeyPlacementId)},$,$,.ELEMENT.,{StepWriter.Real(storey.Elevation)}");
            storeyIds.Add(storeyId);

            var storeyElementIds = new List<int>(storey.Elements.Count + storey.Spaces.Count);

            for (int eIdx = 0; eIdx < storey.Elements.Count; eIdx++)
            {
                var element = storey.Elements[eIdx];

                int prodPlacementId = w.Add("IFCLOCALPLACEMENT",
                    $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

                int prodShapeId = WriteElementShape(w, element, bodySubContextId, styleCache, useTessellation);

                string elemGuid = ProductGuid(element, "IfcIO");
                string ifcType = !string.IsNullOrEmpty(element.IfcType)
                    ? element.IfcType
                    : ClassifyElement(element.Name, element.Material?.Name);

                int elemId = AddIfcProduct(w, ifcType, elemGuid, ownerHistoryId, element.Name, prodPlacementId, prodShapeId, element.Tag ?? element.Id.ToString(CultureInfo.InvariantCulture));
                storeyElementIds.Add(elemId);
                elementEntityMap[element] = elemId;

                // Openings (voids) and fillings
                if (element.Openings.Count > 0)
                {
                    foreach (var opening in element.Openings)
                    {
                        int openPlacementId = w.Add("IFCLOCALPLACEMENT",
                            $"{StepWriter.Ref(prodPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
                        int openShapeId = WriteElementShape(w, opening, bodySubContextId, styleCache, useTessellation);

                        string openGuid = ProductGuid(opening, "IfcIO-Opening");
                        int openElemId = AddIfcProduct(w, "IFCOPENINGELEMENT", openGuid, ownerHistoryId, opening.Name,
                            openPlacementId, openShapeId, opening.Tag ?? opening.Id.ToString(CultureInfo.InvariantCulture));

                        w.Add("IFCRELVOIDSELEMENT",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(elemId)},{StepWriter.Ref(openElemId)}");

                        if (opening.Filling is not null)
                        {
                            var filling = opening.Filling;
                            int fillPlacementId = w.Add("IFCLOCALPLACEMENT",
                                $"{StepWriter.Ref(openPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");
                            int fillShapeId = WriteElementShape(w, filling, bodySubContextId, styleCache, useTessellation);

                            string fillGuid = ProductGuid(filling, "IfcIO-Filling");
                            string fillType = !string.IsNullOrEmpty(filling.IfcType)
                                ? filling.IfcType
                                : ClassifyElement(filling.Name, filling.Material?.Name);

                            int fillElemId = AddIfcProduct(w, fillType, fillGuid, ownerHistoryId, filling.Name, fillPlacementId, fillShapeId, filling.Tag ?? filling.Id.ToString(CultureInfo.InvariantCulture));
                            storeyElementIds.Add(fillElemId);
                            elementEntityMap[filling] = fillElemId;

                            w.Add("IFCRELFILLSELEMENT",
                                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(openElemId)},{StepWriter.Ref(fillElemId)}");
                        }
                    }
                }

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

                // Property sets & Quantity sets
                WritePropertyAndQuantitySets(w, element, elemId, ownerHistoryId);
            }

            // Spaces within this storey
            for (int spIdx = 0; spIdx < storey.Spaces.Count; spIdx++)
            {
                var space = storey.Spaces[spIdx];
                int spacePlacementId = w.Add("IFCLOCALPLACEMENT",
                    $"{StepWriter.Ref(storeyPlacementId)},{StepWriter.Ref(worldPlacement3dId)}");

                int spaceShapeId = 0;
                if (space.GetEffectiveMesh() is not null)
                {
                    spaceShapeId = WriteElementShape(w, space, bodySubContextId, styleCache, useTessellation);
                }
                string spaceShapeRef = spaceShapeId > 0 ? StepWriter.Ref(spaceShapeId) : "$";

                string spaceGuid = ProductGuid(space, "IfcIO-Space");
                string intExt = space.InteriorOrExteriorSpace == IfcInternalOrExternal.External ? ".EXTERNAL." : ".INTERNAL.";

                int spaceId = w.Add("IFCSPACE",
                    $"{StepWriter.Str(spaceGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(space.Name)},{StepWriter.Str(space.SpaceNumber)},$,{StepWriter.Ref(spacePlacementId)},{spaceShapeRef},{StepWriter.Str(space.LongName ?? space.Name)},{intExt},.ELEMENT.,{StepWriter.Real(storey.Elevation)}");

                storeyElementIds.Add(spaceId);
                elementEntityMap[space] = spaceId;

                WritePropertyAndQuantitySets(w, space, spaceId, ownerHistoryId);
            }

            // Space boundaries
            for (int spIdx = 0; spIdx < storey.Spaces.Count; spIdx++)
            {
                var space = storey.Spaces[spIdx];
                if (elementEntityMap.TryGetValue(space, out int spaceId))
                {
                    foreach (var boundary in space.Boundaries)
                    {
                        string boundName = string.IsNullOrEmpty(boundary.Name) ? "SpaceBoundary" : boundary.Name;
                        int? relatedElemId = null;
                        if (boundary.RelatedBuildingElement is not null && elementEntityMap.TryGetValue(boundary.RelatedBuildingElement, out int rId))
                        {
                            relatedElemId = rId;
                        }
                        string relatedRef = relatedElemId.HasValue ? StepWriter.Ref(relatedElemId.Value) : "$";
                        string physVirt = boundary.PhysicalOrVirtual == IfcPhysicalOrVirtual.Virtual ? ".VIRTUAL." : ".PHYSICAL.";
                        string intExtBound = boundary.InternalOrExternal == IfcInternalOrExternal.External ? ".EXTERNAL." : ".INTERNAL.";

                        w.Add("IFCRELSPACEBOUNDARY",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(boundName)},{StepWriter.Str(boundary.Description)},{StepWriter.Ref(spaceId)},{relatedRef},$,{physVirt},{intExtBound}");
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

        // 8. Systems and Groups
        foreach (var sys in Systems)
        {
            string sysGuid = sys.GlobalId ?? IfcGuid.NewGuid();
            int sysId = (Schema == IfcSchema.Ifc4)
                ? w.Add("IFCDISTRIBUTIONSYSTEM", $"{StepWriter.Str(sysGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(sys.Name)},{StepWriter.Str(sys.Description)},{StepWriter.Str(sys.SystemType)},$,$,.NOTDEFINED.")
                : w.Add("IFCSYSTEM", $"{StepWriter.Str(sysGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(sys.Name)},{StepWriter.Str(sys.Description)},{StepWriter.Str(sys.SystemType)}");

            var memberIds = new List<int>();
            foreach (var elem in sys.Elements)
            {
                if (elementEntityMap.TryGetValue(elem, out int memberId))
                {
                    memberIds.Add(memberId);
                }
            }

            if (memberIds.Count > 0)
            {
                w.Add("IFCRELASSIGNSTOGROUP",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(memberIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(sysId)}");
            }
        }

        foreach (var grp in Groups)
        {
            string grpGuid = grp.GlobalId ?? IfcGuid.NewGuid();
            int grpId = w.Add("IFCGROUP",
                $"{StepWriter.Str(grpGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(grp.Name)},{StepWriter.Str(grp.Description)}");

            var memberIds = new List<int>();
            foreach (var elem in grp.Elements)
            {
                if (elementEntityMap.TryGetValue(elem, out int memberId))
                {
                    memberIds.Add(memberId);
                }
            }

            if (memberIds.Count > 0)
            {
                w.Add("IFCRELASSIGNSTOGROUP",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(memberIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(grpId)}");
            }
        }

        // Zones (IFCZONE)
        foreach (var zone in Zones)
        {
            string zoneGuid = zone.GlobalId ?? IfcGuid.NewGuid();
            int zoneId = w.Add("IFCZONE",
                $"{StepWriter.Str(zoneGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(zone.Name)},{StepWriter.Str(zone.Description)},{StepWriter.Str(zone.ZoneType)}");

            var spaceMemberIds = new List<int>();
            foreach (var space in zone.Spaces)
            {
                if (elementEntityMap.TryGetValue(space, out int sId))
                {
                    spaceMemberIds.Add(sId);
                }
            }

            if (spaceMemberIds.Count > 0)
            {
                w.Add("IFCRELASSIGNSTOGROUP",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(spaceMemberIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(zoneId)}");
            }
        }

        // 4D Construction Work Schedules (IFCWORKSCHEDULE, IFCTASK, IFCTASKTIME, IFCRELASSIGNSTOPROCESS, IFCRELSEQUENCE)
        var taskEntityMap = new Dictionary<Model.Sequence.IfcTask, int>();
        foreach (var ws in WorkSchedules)
        {
            string wsGuid = ws.GlobalId ?? IfcGuid.NewGuid();
            string wsStartStr = ws.StartTime.HasValue ? StepWriter.Str(ws.StartTime.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : "$";
            string wsFinStr = ws.FinishTime.HasValue ? StepWriter.Str(ws.FinishTime.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : "$";
            int wsId = (Schema == IfcSchema.Ifc4)
                ? w.Add("IFCWORKSCHEDULE", $"{StepWriter.Str(wsGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(ws.Name)},{StepWriter.Str(ws.Description)},$,{StepWriter.Str(ws.CreationDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))},$,$,$,$,{wsStartStr},{wsFinStr}")
                : w.Add("IFCWORKSCHEDULE", $"{StepWriter.Str(wsGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(ws.Name)},{StepWriter.Str(ws.Description)},$,$,{StepWriter.Str(ws.CreationDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))},$,$,$,{wsStartStr},{wsFinStr}");

            var scheduleTaskIds = new List<int>();
            foreach (var task in ws.AllTasks)
            {
                string tGuid = task.GlobalId ?? IfcGuid.NewGuid();
                string tTypeStr = $".{task.TaskType.ToString().ToUpperInvariant()}.";
                bool isMilestone = task.Time?.IsMilestone == true;
                string isMileStr = isMilestone ? ".T." : ".F.";

                int? timeId = null;
                if (task.Time is not null)
                {
                    string pStart = task.Time.PlannedStartDate.HasValue ? StepWriter.Str(task.Time.PlannedStartDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : "$";
                    string pFin = task.Time.PlannedFinishDate.HasValue ? StepWriter.Str(task.Time.PlannedFinishDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : "$";
                    string aStart = task.Time.ActualStartDate.HasValue ? StepWriter.Str(task.Time.ActualStartDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : "$";
                    string aFin = task.Time.ActualFinishDate.HasValue ? StepWriter.Str(task.Time.ActualFinishDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : "$";
                    string compStr = task.Time.Completion.HasValue ? StepWriter.Real(task.Time.Completion.Value) : "$";

                    if (Schema == IfcSchema.Ifc4)
                    {
                        timeId = w.Add("IFCTASKTIME",
                            $"$,$,.NOTDEFINED.,$,$,$,$,$,$,$,{pStart},{pFin},{aStart},{aFin},{compStr},$");
                    }
                    else
                    {
                        timeId = w.Add("IFCSCHEDULETIMECONTROL",
                            $"$,$,$,$,$,$,$,$,$,$,{aStart},{aFin},{pStart},{pFin},{compStr},$");
                    }
                }

                string timeRef = timeId.HasValue ? StepWriter.Ref(timeId.Value) : "$";
                string prioStr = task.Priority.HasValue ? StepWriter.Number(task.Priority.Value) : "$";
                int taskId = (Schema == IfcSchema.Ifc4)
                    ? w.Add("IFCTASK", $"{StepWriter.Str(tGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(task.Name)},{StepWriter.Str(task.Description)},$,{StepWriter.Str(task.Identification)},{StepWriter.Str(task.Status)},$,{isMileStr},{prioStr},{timeRef},{tTypeStr}")
                    : w.Add("IFCTASK", $"{StepWriter.Str(tGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(task.Name)},{StepWriter.Str(task.Description)},$,{StepWriter.Str(task.Identification)},{StepWriter.Str(task.Status)},$,{isMileStr},{prioStr}");

                taskEntityMap[task] = taskId;
                scheduleTaskIds.Add(taskId);

                // Assign elements via IFCRELASSIGNSTOPROCESS
                var assignedElemIds = new List<int>();
                foreach (var el in task.AssignedElements)
                {
                    if (elementEntityMap.TryGetValue(el, out int elId))
                    {
                        assignedElemIds.Add(elId);
                    }
                }
                if (assignedElemIds.Count > 0)
                {
                    w.Add("IFCRELASSIGNSTOPROCESS",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(assignedElemIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(taskId)},$");
                }
            }

            if (scheduleTaskIds.Count > 0)
            {
                w.Add("IFCRELASSIGNSTOCONTROL",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(scheduleTaskIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(wsId)}");
            }
        }

        // Sequence relationships (IFCRELSEQUENCE)
        foreach (var ws in WorkSchedules)
        {
            foreach (var task in ws.AllTasks)
            {
                if (!taskEntityMap.TryGetValue(task, out int relProcId)) continue;

                foreach (var succ in task.Successors)
                {
                    if (taskEntityMap.TryGetValue(succ.RelatedProcess, out int succId))
                    {
                        string seqEnum = succ.SequenceType switch
                        {
                            Model.Sequence.IfcSequenceType.StartToStart => ".START_START.",
                            Model.Sequence.IfcSequenceType.StartToFinish => ".START_FINISH.",
                            Model.Sequence.IfcSequenceType.FinishToFinish => ".FINISH_FINISH.",
                            _ => ".FINISH_START."
                        };
                        w.Add("IFCRELSEQUENCE",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(succ.Description)},$,{StepWriter.Ref(relProcId)},{StepWriter.Ref(succId)},$,{seqEnum}");
                    }
                }
            }
        }

        // 5D Cost Schedules & Cost Items (IFCCOSTSCHEDULE, IFCCOSTITEM, IFCAPPLIEDVALUE, IFCRELASSIGNSTOCONTROL, IFCRELNESTING)
        int? costMetreId = LengthUnit == IfcLengthUnit.Metre ? lengthUnitId : null;
        foreach (var cs in CostSchedules)
        {
            string csGuid = cs.GlobalId ?? IfcGuid.NewGuid();
            string csTypeEnum = $".{cs.ScheduleType.ToString().ToUpperInvariant()}.";
            int csId = (Schema == IfcSchema.Ifc4)
                ? w.Add("IFCCOSTSCHEDULE", $"{StepWriter.Str(csGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(cs.Name)},{StepWriter.Str(cs.Description)},{StepWriter.Str(cs.Currency)},$,$,$,{StepWriter.Str(cs.Status)},$,{StepWriter.Str(cs.UpdateDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))},$,{csTypeEnum}")
                : w.Add("IFCCOSTSCHEDULE", $"{StepWriter.Str(csGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(cs.Name)},{StepWriter.Str(cs.Description)},{StepWriter.Str(cs.Currency)},$,$,$,{StepWriter.Str(cs.Status)},$,{StepWriter.Str(cs.UpdateDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))},$,{csTypeEnum}");

            var csItemIds = new List<int>();
            var itemEntityMap = new Dictionary<Model.Cost.IfcCostItem, int>();

            foreach (var item in cs.AllCostItems)
            {
                string ciGuid = item.GlobalId ?? IfcGuid.NewGuid();

                // Cost values (IFCAPPLIEDVALUE)
                var appliedValIds = new List<int>();
                if (item.CostValues.Count > 0)
                {
                    foreach (var cv in item.CostValues)
                    {
                        int avId = w.Add("IFCAPPLIEDVALUE",
                            $"{StepWriter.Str(cv.Name)},$,IFCMONETARYMEASURE({StepWriter.Real(cv.Amount)}),$,$,$,{StepWriter.Str(cv.Category ?? cv.Currency)},$,$,$");
                        appliedValIds.Add(avId);
                    }
                }
                if (item.UnitRate.HasValue)
                {
                    int avId = w.Add("IFCAPPLIEDVALUE",
                        $"{StepWriter.Str("UnitRate")},{StepWriter.Str(item.UnitBasis)},IFCMONETARYMEASURE({StepWriter.Real(item.UnitRate.Value)}),$,$,$,'UnitRate',$,$,$");
                    appliedValIds.Add(avId);
                }

                // Quantities (IFCQUANTITY*)
                var qtyValIds = new List<int>();
                if (item.Quantity.HasValue)
                {
                    string qtyType = item.UnitBasis?.ToLowerInvariant() switch
                    {
                        "m2" or "sqm" => "IFCQUANTITYAREA",
                        "m3" or "cum" => "IFCQUANTITYVOLUME",
                        "m" or "lm" => "IFCQUANTITYLENGTH",
                        _ => "IFCQUANTITYCOUNT"
                    };
                    string unit = qtyType == "IFCQUANTITYLENGTH"
                        ? StepWriter.Ref(costMetreId ??= WriteLengthUnit(w, IfcLengthUnit.Metre)) : "$";
                    string formula = Schema == IfcSchema.Ifc4 ? ",$" : "";
                    int qId = w.Add(qtyType, $"{StepWriter.Str("Quantity")},{StepWriter.Str(item.UnitBasis)},{unit},{StepWriter.Real(item.Quantity.Value)}{formula}");
                    qtyValIds.Add(qId);
                }

                string costValuesArg = appliedValIds.Count > 0 ? StepWriter.List(appliedValIds.Select(StepWriter.Ref)) : "$";
                string quantitiesArg = qtyValIds.Count > 0 ? StepWriter.List(qtyValIds.Select(StepWriter.Ref)) : "$";

                int ciId = (Schema == IfcSchema.Ifc4)
                    ? w.Add("IFCCOSTITEM", $"{StepWriter.Str(ciGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(item.Name)},{StepWriter.Str(item.Description)},$,{StepWriter.Str(item.CostCode)},{costValuesArg},{quantitiesArg}")
                    : w.Add("IFCCOSTITEM", $"{StepWriter.Str(ciGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(item.Name)},{StepWriter.Str(item.Description)},$,{StepWriter.Str(item.CostCode)}");

                itemEntityMap[item] = ciId;

                if (cs.RootCostItems.Contains(item))
                {
                    csItemIds.Add(ciId);
                }

                if (Schema == IfcSchema.Ifc2x3 && (appliedValIds.Count > 0 || qtyValIds.Count > 0))
                {
                    var allValRefs = appliedValIds.Concat(qtyValIds).ToList();
                    w.Add("IFCRELASSIGNSTOCONTROL",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(allValRefs.Select(StepWriter.Ref))},$,{StepWriter.Ref(ciId)}");
                }

                // Assigned elements
                var assignedElemIds = new List<int>();
                foreach (var el in item.AssignedElements)
                {
                    if (elementEntityMap.TryGetValue(el, out int elId))
                    {
                        assignedElemIds.Add(elId);
                    }
                }
                if (assignedElemIds.Count > 0)
                {
                    w.Add("IFCRELASSIGNSTOCONTROL",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(assignedElemIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(ciId)}");
                }
            }

            // Sub-cost items via IFCRELNESTING
            foreach (var item in cs.AllCostItems)
            {
                if (item.SubCostItems.Count > 0 && itemEntityMap.TryGetValue(item, out int parentCiId))
                {
                    var subCiIds = item.SubCostItems.Where(s => itemEntityMap.ContainsKey(s)).Select(s => itemEntityMap[s]).ToList();
                    if (subCiIds.Count > 0)
                    {
                        w.Add("IFCRELNESTING",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(parentCiId)},{StepWriter.List(subCiIds.Select(StepWriter.Ref))}");
                    }
                }
            }

            if (csItemIds.Count > 0)
            {
                w.Add("IFCRELASSIGNSTOCONTROL",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.List(csItemIds.Select(StepWriter.Ref))},$,{StepWriter.Ref(csId)}");
            }
        }

        // 9. Material Layers & Profiles
        var materialEntityMap = new Dictionary<IfcMaterial, int>();
        int WriteMaterial(IfcMaterial material)
        {
            if (materialEntityMap.TryGetValue(material, out int existing)) return existing;
            string attributes = StepWriter.Str(material.Name) + (Schema == IfcSchema.Ifc4 ? ",$,$" : "");
            int id = w.Add("IFCMATERIAL", attributes);
            materialEntityMap.Add(material, id);
            if ((material.R, material.G, material.B, material.Alpha) != (0.8, 0.8, 0.8, 1.0))
            {
                int style = GetOrCreateSurfaceStyle(w, material, styleCache);
                int item = w.Add("IFCSTYLEDITEM", $"$,({StepWriter.Ref(style)}),$");
                int representation = w.Add("IFCSTYLEDREPRESENTATION",
                    $"{StepWriter.Ref(mainContextId)},'Style','Material',({StepWriter.Ref(item)})");
                w.Add("IFCMATERIALDEFINITIONREPRESENTATION", $"$,$,({StepWriter.Ref(representation)}),{StepWriter.Ref(id)}");
            }
            return id;
        }

        foreach (var material in Materials) WriteMaterial(material);
        foreach (var (element, id) in elementEntityMap)
        {
            if (element.AssignedMaterial is not { } material || element.MaterialLayerSet is not null || element.MaterialProfileSet is not null)
                continue;
            int matId = WriteMaterial(material);
            w.Add("IFCRELASSOCIATESMATERIAL",
                $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(id)}),{StepWriter.Ref(matId)}");
        }

        var layerSetMap = new Dictionary<IfcMaterialLayerSet, int>();
        foreach (var ls in MaterialLayerSets)
        {
            var layerIds = new List<int>();
            foreach (var layer in ls.Layers)
            {
                int matId = WriteMaterial(layer.Material);
                string isVent = layer.IsVentilated ? ".T." : ".F.";
                int layId = w.Add("IFCMATERIALLAYER",
                    $"{StepWriter.Ref(matId)},{StepWriter.Real(layer.LayerThickness)},{isVent},{StepWriter.Str(layer.Name)},{StepWriter.Str(layer.Description)}");
                layerIds.Add(layId);
            }
            int lsId = w.Add("IFCMATERIALLAYERSET",
                $"{StepWriter.List(layerIds.Select(StepWriter.Ref))},{StepWriter.Str(ls.LayerSetName)},{StepWriter.Str(ls.Description)}");
            layerSetMap[ls] = lsId;
        }

        foreach (var elem in AllElements)
        {
            if (elem.MaterialLayerSet is not null && elementEntityMap.TryGetValue(elem, out int elemId))
            {
                var usage = elem.MaterialLayerSet;
                if (!layerSetMap.TryGetValue(usage.ForLayerSet, out int lsId))
                {
                    var layerIds = new List<int>();
                    foreach (var layer in usage.ForLayerSet.Layers)
                    {
                        int matId = WriteMaterial(layer.Material);
                        string isVent = layer.IsVentilated ? ".T." : ".F.";
                        int layId = w.Add("IFCMATERIALLAYER",
                            $"{StepWriter.Ref(matId)},{StepWriter.Real(layer.LayerThickness)},{isVent},{StepWriter.Str(layer.Name)},{StepWriter.Str(layer.Description)}");
                        layerIds.Add(layId);
                    }
                    lsId = w.Add("IFCMATERIALLAYERSET",
                        $"{StepWriter.List(layerIds.Select(StepWriter.Ref))},{StepWriter.Str(usage.ForLayerSet.LayerSetName)},{StepWriter.Str(usage.ForLayerSet.Description)}");
                    layerSetMap[usage.ForLayerSet] = lsId;
                }

                int usageId = w.Add("IFCMATERIALLAYERSETUSAGE",
                    $"{StepWriter.Ref(lsId)},{usage.LayerSetDirection},{usage.DirectionSense},{StepWriter.Real(usage.OffsetFromReferenceLine)}");
                w.Add("IFCRELASSOCIATESMATERIAL",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(elemId)}),{StepWriter.Ref(usageId)}");
            }
        }

        var profileSetMap = new Dictionary<IfcMaterialProfileSet, int>();
        foreach (var ps in MaterialProfileSets)
        {
            var mpIds = new List<int>();
            foreach (var mp in ps.MaterialProfiles)
            {
                int matId = mp.Material is null ? 0 : WriteMaterial(mp.Material);
                int profDefId = WriteProfileDef(w, mp.Profile);
                string matRef = matId > 0 ? StepWriter.Ref(matId) : "$";
                int mpId = w.Add("IFCMATERIALPROFILE",
                    $"{StepWriter.Str(mp.Name)},$,{matRef},{StepWriter.Ref(profDefId)},$,$");
                mpIds.Add(mpId);
            }
            int psId = w.Add("IFCMATERIALPROFILESET",
                $"{StepWriter.Str(ps.Name)},{StepWriter.Str(ps.Description)},{StepWriter.List(mpIds.Select(StepWriter.Ref))},$");
            profileSetMap[ps] = psId;
        }

        foreach (var elem in AllElements)
        {
            if (elem.MaterialProfileSet is not null && elementEntityMap.TryGetValue(elem, out int elemId))
            {
                var usage = elem.MaterialProfileSet;
                if (!profileSetMap.TryGetValue(usage.ForProfileSet, out int psId))
                {
                    var mpIds = new List<int>();
                    foreach (var mp in usage.ForProfileSet.MaterialProfiles)
                    {
                        int matId = mp.Material is null ? 0 : WriteMaterial(mp.Material);
                        int profDefId = WriteProfileDef(w, mp.Profile);
                        string matRef = matId > 0 ? StepWriter.Ref(matId) : "$";
                        int mpId = w.Add("IFCMATERIALPROFILE",
                            $"{StepWriter.Str(mp.Name)},$,{matRef},{StepWriter.Ref(profDefId)},$,$");
                        mpIds.Add(mpId);
                    }
                    psId = w.Add("IFCMATERIALPROFILESET",
                        $"{StepWriter.Str(usage.ForProfileSet.Name)},{StepWriter.Str(usage.ForProfileSet.Description)},{StepWriter.List(mpIds.Select(StepWriter.Ref))},$");
                    profileSetMap[usage.ForProfileSet] = psId;
                }

                int usageId = w.Add("IFCMATERIALPROFILESETUSAGE",
                    $"{StepWriter.Ref(psId)},{usage.CardinalPoint},$");
                w.Add("IFCRELASSOCIATESMATERIAL",
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(elemId)}),{StepWriter.Ref(usageId)}");
            }
        }

        // 10. MEP Distribution Ports & Network Connectivity
        var portEntityMap = new Dictionary<IfcDistributionPort, int>();
        foreach (var elem in AllElements)
        {
            if (elem.Ports.Count > 0 && elementEntityMap.TryGetValue(elem, out int hostElemId))
            {
                foreach (var port in elem.Ports)
                {
                    int ptId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(port.Position.X), StepWriter.Real(port.Position.Y), StepWriter.Real(port.Position.Z)));
                    int dirId = w.Add("IFCDIRECTION", StepWriter.List(StepWriter.Real(port.Direction.X), StepWriter.Real(port.Direction.Y), StepWriter.Real(port.Direction.Z)));
                    int pl3dId = w.Add("IFCAXIS2PLACEMENT3D", $"{StepWriter.Ref(ptId)},{StepWriter.Ref(dirId)},$");
                    int portPlacementId = w.Add("IFCLOCALPLACEMENT", $"{StepWriter.Ref(worldPlacement3dId)},{StepWriter.Ref(pl3dId)}");

                    string flowDir = port.FlowDirection switch
                    {
                        IfcFlowDirection.Source => ".SOURCE.",
                        IfcFlowDirection.Sink => ".SINK.",
                        IfcFlowDirection.SourceAndSink => ".SOURCEANDSINK.",
                        _ => ".NOTDEFINED.",
                    };

                    string portGuid = port.GlobalId ?? IfcGuid.NewGuid();
                    int portId = w.Add("IFCDISTRIBUTIONPORT",
                        $"{StepWriter.Str(portGuid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(port.Name)},{StepWriter.Str(port.Description)},{StepWriter.Str(port.SystemType)},{StepWriter.Ref(portPlacementId)},$,{flowDir}");
                    portEntityMap[port] = portId;

                    w.Add("IFCRELCONNECTSPORTTOELEMENT",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(portId)},{StepWriter.Ref(hostElemId)}");
                }
            }
        }

        var connectedPairs = new HashSet<(int, int)>();
        foreach (var elem in AllElements)
        {
            foreach (var port in elem.Ports)
            {
                if (port.ConnectedPort is not null &&
                    portEntityMap.TryGetValue(port, out int portIdA) &&
                    portEntityMap.TryGetValue(port.ConnectedPort, out int portIdB))
                {
                    int min = Math.Min(portIdA, portIdB);
                    int max = Math.Max(portIdA, portIdB);
                    if (connectedPairs.Add((min, max)))
                    {
                        w.Add("IFCRELCONNECTSPORTS",
                            $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,{StepWriter.Ref(portIdA)},{StepWriter.Ref(portIdB)},$");
                    }
                }
            }
        }

        w.WriteTo(writer, fileName, schemaStr, viewDef, Application, Organization);
    }

    internal void EnsureWritableSchema()
    {
        if (IsIfc4x3Subset)
            throw new NotSupportedException("IFC4X3 is supported for subset import only. Rewriting it as IFC is not supported.");
        if (Schema is not (IfcSchema.Ifc2x3 or IfcSchema.Ifc4))
            throw new NotSupportedException($"Writing IFC schema '{Schema}' is not supported.");
    }

    private void WritePropertyAndQuantitySets(StepWriter w, IfcElement element, int entityId, int ownerHistoryId)
    {
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
                    $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(entityId)}),{StepWriter.Ref(psetId)}");
            }
        }

        // Quantity sets
        if (element.QuantitySets.Count > 0)
        {
            string formula = Schema == IfcSchema.Ifc4 ? ",$" : "";
            foreach (var qset in element.QuantitySets)
            {
                var qIds = new List<int>();
                foreach (var q in qset.Quantities)
                {
                    int qId = q switch
                    {
                         IfcQuantityLength ql => w.Add("IFCQUANTITYLENGTH",
                             $"{StepWriter.Str(ql.Name)},{StepWriter.Str(ql.Description)},$,{StepWriter.Real(ql.LengthValue)}{formula}"),
                        IfcQuantityArea qa => w.Add("IFCQUANTITYAREA",
                            $"{StepWriter.Str(qa.Name)},{StepWriter.Str(qa.Description)},$,{StepWriter.Real(qa.AreaValue)}"),
                        IfcQuantityVolume qv => w.Add("IFCQUANTITYVOLUME",
                            $"{StepWriter.Str(qv.Name)},{StepWriter.Str(qv.Description)},$,{StepWriter.Real(qv.VolumeValue)}"),
                        IfcQuantityCount qc => w.Add("IFCQUANTITYCOUNT",
                            $"{StepWriter.Str(qc.Name)},{StepWriter.Str(qc.Description)},$,{StepWriter.Real(qc.CountValue)}"),
                        IfcQuantityWeight qw => w.Add("IFCQUANTITYWEIGHT",
                            $"{StepWriter.Str(qw.Name)},{StepWriter.Str(qw.Description)},$,{StepWriter.Real(qw.WeightValue)}"),
                        _ => -1,
                    };
                    if (qId > 0) qIds.Add(qId);
                }

                if (qIds.Count > 0)
                {
                    int qsetId = w.Add("IFCELEMENTQUANTITY",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(qset.Name)},{StepWriter.Str(qset.Description)},{StepWriter.Str(qset.MethodOfMeasurement)},{StepWriter.List(qIds.Select(StepWriter.Ref))}");

                    w.Add("IFCRELDEFINESBYPROPERTIES",
                        $"{StepWriter.Str(IfcGuid.NewGuid())},{StepWriter.Ref(ownerHistoryId)},$,$,({StepWriter.Ref(entityId)}),{StepWriter.Ref(qsetId)}");
                }
            }
        }
    }

    private HashSet<(int Id, string Domain)> ReserveProductIdentities(HashSet<string> guids)
    {
        var generatedIds = new HashSet<(int Id, string Domain)>();
        var duplicates = new HashSet<(int Id, string Domain)>();
        void Reserve(string? guid)
        {
            if (guid is not null) guids.Add(guid);
        }

        void Product(IfcElement element, string domain)
        {
            if (element.GlobalId is { } guid) Reserve(guid);
            else if (!generatedIds.Add((element.Id, domain))) duplicates.Add((element.Id, domain));
        }

        foreach (var type in TypeProducts) Reserve(type.GlobalId);
        foreach (var group in Groups) Reserve(group.GlobalId);
        foreach (var system in Systems) Reserve(system.GlobalId);
        foreach (var zone in Zones) Reserve(zone.GlobalId);
        foreach (var schedule in WorkSchedules)
        {
            Reserve(schedule.GlobalId);
            foreach (var task in schedule.AllTasks) Reserve(task.GlobalId);
        }
        foreach (var schedule in CostSchedules)
        {
            Reserve(schedule.GlobalId);
            foreach (var item in schedule.AllCostItems) Reserve(item.GlobalId);
        }
        foreach (var storey in Storeys)
        {
            Reserve(storey.GlobalId);
            foreach (var element in storey.Elements)
            {
                Product(element, "IfcIO");
                foreach (var port in element.Ports) Reserve(port.GlobalId);
                foreach (var opening in element.Openings)
                {
                    Product(opening, "IfcIO-Opening");
                    if (opening.Filling is { } filling) Product(filling, "IfcIO-Filling");
                }
            }
            foreach (var space in storey.Spaces) Product(space, "IfcIO-Space");
        }
        return duplicates;
    }

    /// <summary>Parses an existing IFC STEP file (.ifc or .ifczip) from disk into a structured model.</summary>
    public static IfcModel Open(string path) => Open(path, options: null);

    /// <summary>Parses an IFC file from disk with the specified read options.</summary>
    public static IfcModel Open(string path, IfcReadOptions? options)
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
            return Open(reader, Path.GetFileNameWithoutExtension(path), options);
        }

        using var fileStream = File.OpenRead(path);
        using var fileReader = new StreamReader(fileStream, Encoding.UTF8);
        return Open(fileReader, Path.GetFileNameWithoutExtension(path), options);
    }

    /// <summary>Parses an existing IFC STEP physical file or stream into a structured model.</summary>
    public static IfcModel Open(Stream stream, string? modelName = null) => Open(stream, modelName, options: null);

    /// <summary>Parses an IFC byte stream with the specified read options.</summary>
    public static IfcModel Open(Stream stream, string? modelName, IfcReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true);
        return Open(reader, modelName, options);
    }

    /// <summary>Parses an existing IFC STEP stream into a structured model with storeys, elements, properties, and meshes.</summary>
    public static IfcModel Open(TextReader reader, string? modelName = null) => Open(reader, modelName, options: null);

    /// <summary>Parses an IFC text stream with the specified read options.</summary>
    public static IfcModel Open(TextReader reader, string? modelName, IfcReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var step = StepReader.Read(reader, indexTypes: true);
        var schema = ReadSchema(step);

        var model = new IfcModel
        {
            ProjectName = modelName ?? step.FileName ?? "Imported IFC Project",
            Schema = schema,
            SourceSchemaIdentifier = step.Schema,
        };
        model.Storeys.Clear(); // Clear default storey

        // 1. Parse Project, Building
        var projectEntity = step.IndexedOfType("IFCPROJECT").FirstOrDefault();
        if (projectEntity is not null)
        {
            model.ProjectName = projectEntity.GetString(2) ?? model.ProjectName;
        }
        var buildingEntity = step.IndexedOfType("IFCBUILDING").FirstOrDefault();
        if (buildingEntity is not null)
        {
            model.BuildingName = buildingEntity.GetString(2) ?? model.BuildingName;
        }

        // 1b. Units: geometry is kept in model length units. A unit without an IfcLengthUnit
        // counterpart (e.g. centimetres) is converted to metres while reading.
        var assignment = step.Find(projectEntity?.GetRef(8)) ?? step.IndexedOfType("IFCUNITASSIGNMENT").FirstOrDefault();
        double? fileMetresPerUnit = ParseProjectUnitFactor(step, assignment, ".LENGTHUNIT.");
        double geometryScale = 1.0;
        if (fileMetresPerUnit.HasValue)
        {
            var knownUnit = ToLengthUnit(fileMetresPerUnit.Value);
            model.LengthUnit = knownUnit ?? IfcLengthUnit.Metre;
            if (knownUnit is null) geometryScale = fileMetresPerUnit.Value;
        }
        double radiansPerAngleUnit = ParseProjectUnitFactor(step, assignment, ".PLANEANGLEUNIT.") ?? 1.0;
        const double radiansPerDegree = Math.PI / 180.0;
        model.AngleUnit = Math.Abs(radiansPerAngleUnit - radiansPerDegree) <= radiansPerDegree * 1e-6
            ? IfcPlaneAngleUnit.Degree : IfcPlaneAngleUnit.Radian;
        if (options?.LegacyIfcIoRevolvedAnglesInRadians == true)
            radiansPerAngleUnit = 1.0;

        // 2. Parse Storeys
        var storeyMap = new Dictionary<int, IfcBuildingStorey>();
        foreach (var sEnt in step.IndexedOfType("IFCBUILDINGSTOREY"))
        {
            var storey = new IfcBuildingStorey
            {
                Name = sEnt.GetString(2) ?? $"Level {sEnt.Id}",
                Elevation = (sEnt.GetReal(9) ?? 0.0) * geometryScale,
                GlobalId = sEnt.GetString(0),
            };
            model.RegisterStorey(storey);
            storeyMap[sEnt.Id] = storey;
        }

        if (model.Storeys.Count == 0)
        {
            var defStorey = new IfcBuildingStorey { Name = "Level 1", Elevation = 0.0 };
            model.RegisterStorey(defStorey);
        }

        // 3. Parse Materials & Styles
        var (geomToMaterialMap, materialAppearances) = ReadSurfaceStyles(step);

        // 4. Map Elements to Storeys
        var elementToStoreyMap = new Dictionary<int, IfcBuildingStorey>();
        foreach (var relSpatial in step.IndexedOfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
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
        InheritAggregateStoreys(step, storeyMap, elementToStoreyMap);

        // 5. Parse Property Sets and Quantity Sets
        var psetMap = new Dictionary<int, IfcPropertySet>();
        foreach (var psetEnt in step.IndexedOfType("IFCPROPERTYSET"))
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

        var qsetMap = new Dictionary<int, IfcQuantitySet>();
        foreach (var qsetEnt in step.IndexedOfType("IFCELEMENTQUANTITY"))
        {
            string qsetName = qsetEnt.GetString(2) ?? "BaseQuantities";
            string? desc = qsetEnt.GetString(3);
            string? method = qsetEnt.GetString(4);
            var qset = new IfcQuantitySet(qsetName, desc, method);
            var qIds = qsetEnt.GetRefList(5);

            foreach (int qId in qIds)
            {
                var qEnt = step.Find(qId);
                if (qEnt is null) continue;

                string qName = qEnt.GetString(0) ?? "Quantity";
                string? qDesc = qEnt.GetString(1);
                double val = qEnt.GetReal(3) ?? 0.0;

                if (qEnt.Type.Equals("IFCQUANTITYLENGTH", StringComparison.OrdinalIgnoreCase))
                {
                    double? metresPerQuantityUnit = qEnt.GetRef(2) is int unitId
                        ? PositiveUnitFactor(step, step.Find(unitId), ".LENGTHUNIT.")
                        : fileMetresPerUnit;
                    if (metresPerQuantityUnit is double metres)
                        val *= metres / model.LengthUnit.MetresPerUnit();
                    qset.AddLength(qName, val, qDesc);
                }
                else if (qEnt.Type.Equals("IFCQUANTITYAREA", StringComparison.OrdinalIgnoreCase))
                {
                    qset.AddArea(qName, val, qDesc);
                }
                else if (qEnt.Type.Equals("IFCQUANTITYVOLUME", StringComparison.OrdinalIgnoreCase))
                {
                    qset.AddVolume(qName, val, qDesc);
                }
                else if (qEnt.Type.Equals("IFCQUANTITYCOUNT", StringComparison.OrdinalIgnoreCase))
                {
                    qset.AddCount(qName, val, qDesc);
                }
                else if (qEnt.Type.Equals("IFCQUANTITYWEIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    qset.AddWeight(qName, val, qDesc);
                }
            }
            qsetMap[qsetEnt.Id] = qset;
        }

        var elementPropertySets = new Dictionary<int, List<IfcPropertySet>>();
        var elementQuantitySets = new Dictionary<int, List<IfcQuantitySet>>();
        foreach (var relProps in step.IndexedOfType("IFCRELDEFINESBYPROPERTIES"))
        {
            int? propDefId = relProps.GetRef(5);
            if (!propDefId.HasValue) continue;

            var elemIds = relProps.GetRefList(4);

            if (psetMap.TryGetValue(propDefId.Value, out var pset))
            {
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
            else if (qsetMap.TryGetValue(propDefId.Value, out var qset))
            {
                foreach (int eId in elemIds)
                {
                    if (!elementQuantitySets.TryGetValue(eId, out var qlist))
                    {
                        qlist = [];
                        elementQuantitySets[eId] = qlist;
                    }
                    qlist.Add(qset);
                }
            }
        }

        // 6. Parse Elements & Meshes (meshes are resolved into world coordinates)
        var geometry = new GeometryReadContext(
            step,
            geomToMaterialMap,
            IfcTransform.Scale(geometryScale).Multiply(ParseWorldCoordinateSystem(step)),
            radiansPerAngleUnit,
            schema,
            omitUnsupportedPlacements: schema == IfcSchema.Ifc4x3,
            keepParametric: geometryScale == 1.0);
        var defaultStorey = model.Storeys[0];
        var allElementsMap = new Dictionary<int, IfcElement>();
        var productTypes = GetProductImportTypes(step, schema);
        var voidHosts = FindVoidHosts(step, productTypes);

        foreach (var entity in step.EntityValues)
        {
            if (!productTypes.TryGetValue(entity.Type, out var occurrence)) continue;
            int? shapeDefId = entity.GetRef(6);
            if (occurrence.NeedsBody && !voidHosts.Contains(entity.Id) && !HasBodyRepresentation(step, shapeDefId)) continue;

            string globalId = entity.GetString(0) ?? IfcGuid.NewGuid();
            string name = entity.GetString(2) ?? entity.Type;
            string? tag = occurrence.TagIndex >= 0 ? entity.GetString(occurrence.TagIndex) : null;

            var targetStorey = elementToStoreyMap.TryGetValue(entity.Id, out var s) ? s : defaultStorey;

            bool isSpace = entity.Type.Equals("IFCSPACE", StringComparison.OrdinalIgnoreCase);
            bool isOpening = occurrence.IsOpening;

            IfcElement element;
            if (isOpening)
            {
                element = new IfcOpeningElement
                {
                    Id = entity.Id,
                    Name = name,
                    IfcType = entity.Type,
                    GlobalId = globalId,
                    Tag = tag,
                };
            }
            else if (isSpace)
            {
                string? spaceNum = entity.GetString(3);
                string? longName = entity.GetString(7);
                var space = new IfcSpace
                {
                    Id = entity.Id,
                    Name = name,
                    LongName = longName ?? name,
                    SpaceNumber = spaceNum,
                    IfcType = entity.Type,
                    GlobalId = globalId,
                    Tag = spaceNum,
                };
                element = space;
            }
            else
            {
                element = new IfcElement
                {
                    Id = entity.Id,
                    Name = name,
                    IfcType = entity.Type,
                    GlobalId = globalId,
                    Tag = tag,
                };
            }

            // Link property sets and quantity sets
            if (elementPropertySets.TryGetValue(entity.Id, out var psets))
            {
                element.PropertySets.AddRange(psets);
            }
            if (elementQuantitySets.TryGetValue(entity.Id, out var qsets))
            {
                element.QuantitySets.AddRange(qsets);

                if (element is IfcSpace spaceElem)
                {
                    foreach (var qs in qsets)
                    {
                        var area = qs.GetArea("GrossFloorArea") ?? qs.GetArea("NetFloorArea") ?? qs.GetArea("GrossArea");
                        if (area.HasValue && spaceElem.GrossFloorArea <= 0) spaceElem.GrossFloorArea = area.Value;

                        var netArea = qs.GetArea("NetFloorArea");
                        if (netArea.HasValue && spaceElem.NetFloorArea <= 0) spaceElem.NetFloorArea = netArea.Value;

                        var height = qs.GetLength("Height") ?? qs.GetLength("ClearHeight");
                        if (height.HasValue && spaceElem.Height <= 0) spaceElem.Height = height.Value;

                        var vol = qs.GetVolume("GrossVolume") ?? qs.GetVolume("NetVolume");
                        if (vol.HasValue && spaceElem.GrossVolume <= 0) spaceElem.GrossVolume = vol.Value;
                    }
                }
            }

            // Parse Mesh / Solid Geometry
            if (shapeDefId.HasValue && ResolveObjectPlacement(geometry, entity.GetRef(5)) is { } placement)
            {
                var mesh = ParseGeometry(geometry, shapeDefId.Value, name, placement, out var mat, out var solid, out var revSolid,
                    out var boolResult, out bool unwritableBooleanBody, bodyOnly: occurrence.NeedsBody);
                element.HasUnwritableBooleanBody = unwritableBooleanBody;
                if (mesh is not null)
                {
                    element.Mesh = mesh;
                }
                if (solid is not null)
                {
                    element.Solid = solid;
                }
                if (revSolid is not null)
                {
                    element.RevolvedSolid = revSolid;
                }
                if (boolResult is not null)
                {
                    element.BooleanResult = boolResult;
                }
                if (mat is not null)
                {
                    element.SurfaceMaterial = mat;
                }
            }
            else if (shapeDefId.HasValue)
            {
                model.UnsupportedPlacementCount++;
            }

            allElementsMap[entity.Id] = element;
            if (isSpace)
            {
                targetStorey.Spaces.Add((IfcSpace)element);
            }
            else if (!isOpening)
            {
                targetStorey.Elements.Add(element);
            }
        }

        // 7. Parse Voids (IFCRELVOIDSELEMENT) and Fillings (IFCRELFILLSELEMENT)
        foreach (var relVoids in step.IndexedOfType("IFCRELVOIDSELEMENT"))
        {
            int? hostId = relVoids.GetRef(4);
            int? openId = relVoids.GetRef(5);
            if (hostId.HasValue && openId.HasValue &&
                allElementsMap.TryGetValue(hostId.Value, out var hostElem) &&
                allElementsMap.TryGetValue(openId.Value, out var openElem) &&
                openElem is IfcOpeningElement opening)
            {
                if (!hostElem.Openings.Contains(opening))
                {
                    hostElem.Openings.Add(opening);
                }
            }
        }

        foreach (var relFills in step.IndexedOfType("IFCRELFILLSELEMENT"))
        {
            int? openId = relFills.GetRef(4);
            int? fillId = relFills.GetRef(5);
            if (openId.HasValue && fillId.HasValue &&
                allElementsMap.TryGetValue(openId.Value, out var openElem) &&
                openElem is IfcOpeningElement opening &&
                allElementsMap.TryGetValue(fillId.Value, out var fillElem))
            {
                opening.Filling = fillElem;
            }
        }

        // 8. Parse Space Boundaries (IFCRELSPACEBOUNDARY)
        foreach (var relBound in step.IndexedOfType("IFCRELSPACEBOUNDARY"))
        {
            int? spaceId = relBound.GetRef(4);
            int? relElemId = relBound.GetRef(5);
            if (spaceId.HasValue && allElementsMap.TryGetValue(spaceId.Value, out var spaceElem) && spaceElem is IfcSpace targetSpace)
            {
                IfcElement? relElem = null;
                if (relElemId.HasValue && allElementsMap.TryGetValue(relElemId.Value, out var boundElem))
                {
                    relElem = boundElem;
                }

                string bName = relBound.GetString(2) ?? "Boundary";
                string? bDesc = relBound.GetString(3);
                string? physVirtStr = relBound.GetString(7);
                string? intExtStr = relBound.GetString(8);

                var bound = new IfcSpaceBoundary
                {
                    Name = bName,
                    Description = bDesc,
                    PhysicalOrVirtual = physVirtStr?.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase) == true ? IfcPhysicalOrVirtual.Virtual : IfcPhysicalOrVirtual.Physical,
                    InternalOrExternal = intExtStr?.Contains("EXTERNAL", StringComparison.OrdinalIgnoreCase) == true ? IfcInternalOrExternal.External : IfcInternalOrExternal.Internal,
                    RelatedBuildingElement = relElem,
                };
                targetSpace.Boundaries.Add(bound);
            }
        }

        // 9. Parse Systems & Groups (IFCSYSTEM, IFCDISTRIBUTIONSYSTEM, IFCGROUP, IFCRELASSIGNSTOGROUP)
        var groupMap = new Dictionary<int, IfcGroup>();

        foreach (var sysEnt in step.IndexedOfType("IFCSYSTEM").Concat(step.IndexedOfType("IFCDISTRIBUTIONSYSTEM")))
        {
            string sysGuid = sysEnt.GetString(0) ?? IfcGuid.NewGuid();
            string sysName = sysEnt.GetString(2) ?? "System";
            string? sysDesc = sysEnt.GetString(3);
            string sysType = sysEnt.GetString(4) ?? "HVAC";

            var sys = new IfcSystem
            {
                Name = sysName,
                Description = sysDesc,
                SystemType = sysType,
                GlobalId = sysGuid,
            };
            model.Systems.Add(sys);
            groupMap[sysEnt.Id] = sys;
        }

        foreach (var grpEnt in step.IndexedOfType("IFCGROUP"))
        {
            if (groupMap.ContainsKey(grpEnt.Id)) continue;

            string grpGuid = grpEnt.GetString(0) ?? IfcGuid.NewGuid();
            string grpName = grpEnt.GetString(2) ?? "Group";
            string? grpDesc = grpEnt.GetString(3);

            var grp = new IfcGroup
            {
                Name = grpName,
                Description = grpDesc,
                GlobalId = grpGuid,
            };
            model.Groups.Add(grp);
            groupMap[grpEnt.Id] = grp;
        }

        foreach (var zoneEnt in step.IndexedOfType("IFCZONE"))
        {
            string zoneGuid = zoneEnt.GetString(0) ?? IfcGuid.NewGuid();
            string zName = zoneEnt.GetString(2) ?? "Zone";
            string? zDesc = zoneEnt.GetString(3);
            string? zType = zoneEnt.GetString(4);

            var zone = new IfcZone(zName, zType)
            {
                GlobalId = zoneGuid,
                Description = zDesc,
            };
            model.Zones.Add(zone);
            groupMap[zoneEnt.Id] = zone;
        }

        foreach (var relGroup in step.IndexedOfType("IFCRELASSIGNSTOGROUP"))
        {
            int? groupId = relGroup.GetRef(6);
            if (groupId.HasValue && groupMap.TryGetValue(groupId.Value, out var targetGroup))
            {
                var memberIds = relGroup.GetRefList(4);
                foreach (int mId in memberIds)
                {
                    if (allElementsMap.TryGetValue(mId, out var elem))
                    {
                        if (targetGroup is IfcZone z && elem is IfcSpace sp)
                        {
                            z.AddSpace(sp);
                        }
                        else if (!targetGroup.Elements.Contains(elem))
                        {
                            targetGroup.Elements.Add(elem);
                        }
                    }
                }
            }
        }

        // 9b. Parse 4D Construction Work Schedules (IFCWORKSCHEDULE, IFCTASK, IFCTASKTIME, IFCSCHEDULETIMECONTROL, IFCRELASSIGNSTOPROCESS, IFCRELSEQUENCE)
        var taskMap = new Dictionary<int, Model.Sequence.IfcTask>();
        foreach (var tEnt in step.IndexedOfType("IFCTASK"))
        {
            string tGuid = tEnt.GetString(0) ?? IfcGuid.NewGuid();
            string tName = tEnt.GetString(2) ?? "Task";
            string? tDesc = tEnt.GetString(3);
            string? tIdent = tEnt.GetString(5);
            string? tStatus = tEnt.GetString(6);
            int? priority = tEnt.GetInt(9);

            var task = new Model.Sequence.IfcTask(tName, tIdent)
            {
                GlobalId = tGuid,
                Description = tDesc,
                Status = tStatus,
                Priority = priority
            };

            var timeRef = tEnt.GetRef(10);
            if (timeRef.HasValue)
            {
                var timeEnt = step.Find(timeRef.Value);
                if (timeEnt is not null)
                {
                    task.Time = new Model.Sequence.IfcTaskTime();
                    if (DateTime.TryParse(timeEnt.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pStart))
                        task.Time.PlannedStartDate = pStart;
                    if (DateTime.TryParse(timeEnt.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pFin))
                        task.Time.PlannedFinishDate = pFin;
                    if (DateTime.TryParse(timeEnt.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var aStart))
                        task.Time.ActualStartDate = aStart;
                    if (DateTime.TryParse(timeEnt.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var aFin))
                        task.Time.ActualFinishDate = aFin;
                    task.Time.Completion = timeEnt.GetReal(14);
                }
            }

            taskMap[tEnt.Id] = task;
        }

        foreach (var wsEnt in step.IndexedOfType("IFCWORKSCHEDULE"))
        {
            string wsGuid = wsEnt.GetString(0) ?? IfcGuid.NewGuid();
            string wsName = wsEnt.GetString(2) ?? "Schedule";
            string? wsDesc = wsEnt.GetString(3);
            var ws = new Model.Sequence.IfcWorkSchedule(wsName, wsDesc)
            {
                GlobalId = wsGuid
            };
            if (DateTime.TryParse(wsEnt.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var wsStart))
                ws.StartTime = wsStart;
            if (DateTime.TryParse(wsEnt.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var wsFin))
                ws.FinishTime = wsFin;

            model.WorkSchedules.Add(ws);
        }

        // Connect tasks to elements via IFCRELASSIGNSTOPROCESS
        foreach (var relProc in step.IndexedOfType("IFCRELASSIGNSTOPROCESS"))
        {
            var procRef = relProc.GetRef(6);
            if (procRef.HasValue && taskMap.TryGetValue(procRef.Value, out var task))
            {
                foreach (var elId in relProc.GetRefList(4))
                {
                    if (allElementsMap.TryGetValue(elId, out var el))
                    {
                        task.AssignElement(el);
                    }
                }
            }
        }

        // Connect sequence dependencies via IFCRELSEQUENCE
        foreach (var relSeq in step.IndexedOfType("IFCRELSEQUENCE"))
        {
            var relProcRef = relSeq.GetRef(4);
            var relSuccRef = relSeq.GetRef(5);
            if (relProcRef.HasValue && relSuccRef.HasValue &&
                taskMap.TryGetValue(relProcRef.Value, out var pred) &&
                taskMap.TryGetValue(relSuccRef.Value, out var succ))
            {
                string? seqTypeStr = relSeq.GetString(7);
                var seqType = seqTypeStr?.Contains("START_START", StringComparison.OrdinalIgnoreCase) == true ? Model.Sequence.IfcSequenceType.StartToStart :
                              seqTypeStr?.Contains("START_FINISH", StringComparison.OrdinalIgnoreCase) == true ? Model.Sequence.IfcSequenceType.StartToFinish :
                              seqTypeStr?.Contains("FINISH_FINISH", StringComparison.OrdinalIgnoreCase) == true ? Model.Sequence.IfcSequenceType.FinishToFinish :
                              Model.Sequence.IfcSequenceType.FinishToStart;
                pred.AddSuccessor(succ, seqType);
            }
        }

        // 9c. Parse 5D Cost Schedules & Cost Items (IFCCOSTSCHEDULE, IFCCOSTITEM, IFCAPPLIEDVALUE, IFCRELASSIGNSTOCONTROL, IFCRELNESTING)
        var costItemMap = new Dictionary<int, Model.Cost.IfcCostItem>();
        foreach (var ciEnt in step.IndexedOfType("IFCCOSTITEM"))
        {
            string ciGuid = ciEnt.GetString(0) ?? IfcGuid.NewGuid();
            string ciName = ciEnt.GetString(2) ?? "Cost Item";
            string? ciDesc = ciEnt.GetString(3);
            string? ciCode = ciEnt.GetString(5);

            var ci = new Model.Cost.IfcCostItem(ciName, ciCode)
            {
                GlobalId = ciGuid,
                Description = ciDesc
            };

            // Direct references in IFC4 (argument 6: CostValues, argument 7: CostQuantities)
            foreach (int vRef in ciEnt.GetRefList(6))
            {
                var vEnt = step.Find(vRef);
                if (vEnt is not null && vEnt.Type.Equals("IFCAPPLIEDVALUE", StringComparison.OrdinalIgnoreCase))
                {
                    string? vName = vEnt.GetString(0);
                    string? vDesc = vEnt.GetString(1);
                    string? rawAmount = vEnt.GetRaw(2);
                    string cleanAmount = ExtractNominalValue(rawAmount);
                    string? vCat = vEnt.GetString(6);

                    if (double.TryParse(cleanAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out double amt))
                    {
                        if (string.Equals(vName, "UnitRate", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(vCat, "UnitRate", StringComparison.OrdinalIgnoreCase))
                        {
                            ci.UnitRate = amt;
                            if (!string.IsNullOrEmpty(vDesc)) ci.UnitBasis = vDesc;
                        }
                        else
                        {
                            ci.AddCostValue(amt, "USD", vCat, vName);
                        }
                    }
                }
            }

            foreach (int qRef in ciEnt.GetRefList(7))
            {
                var qEnt = step.Find(qRef);
                if (qEnt is not null && qEnt.Type.StartsWith("IFCQUANTITY", StringComparison.OrdinalIgnoreCase))
                {
                    double? qVal = qEnt.GetReal(3);
                    if (qVal.HasValue)
                    {
                        ci.Quantity = qVal.Value;
                        string? qDesc = qEnt.GetString(1);
                        if (!string.IsNullOrEmpty(qDesc)) ci.UnitBasis = qDesc;
                    }
                }
            }

            costItemMap[ciEnt.Id] = ci;
        }

        foreach (var csEnt in step.IndexedOfType("IFCCOSTSCHEDULE"))
        {
            string csGuid = csEnt.GetString(0) ?? IfcGuid.NewGuid();
            string csName = csEnt.GetString(2) ?? "Cost Schedule";
            string? csDesc = csEnt.GetString(3);
            string? csCurrency = csEnt.GetString(4);
            var cs = new Model.Cost.IfcCostSchedule(csName, string.IsNullOrWhiteSpace(csCurrency) ? "USD" : csCurrency)
            {
                GlobalId = csGuid,
                Description = csDesc
            };

            string? typeRaw = csEnt.GetRaw(12) ?? csEnt.GetRaw(6);
            if (!string.IsNullOrWhiteSpace(typeRaw))
            {
                string cleanType = typeRaw.Trim('.').Replace("_", "");
                if (Enum.TryParse<Model.Cost.IfcCostScheduleType>(cleanType, true, out var parsedType))
                {
                    cs.ScheduleType = parsedType;
                }
            }

            model.CostSchedules.Add(cs);
        }

        // Connect sub-items via IFCRELNESTING
        foreach (var relNest in step.IndexedOfType("IFCRELNESTING"))
        {
            int? parentRef = relNest.GetRef(4);
            if (parentRef.HasValue && costItemMap.TryGetValue(parentRef.Value, out var parentItem))
            {
                foreach (int childRef in relNest.GetRefList(5))
                {
                    if (costItemMap.TryGetValue(childRef, out var childItem) && !parentItem.SubCostItems.Contains(childItem))
                    {
                        parentItem.SubCostItems.Add(childItem);
                    }
                }
            }
        }

        // Connect control assignments via IFCRELASSIGNSTOCONTROL
        foreach (var relCtrl in step.IndexedOfType("IFCRELASSIGNSTOCONTROL"))
        {
            var ctrlRef = relCtrl.GetRef(6);
            if (!ctrlRef.HasValue) continue;

            string? ctrlGuid = step.Find(ctrlRef.Value)?.GetString(0);

            // Connect to work schedule
            var ws = model.WorkSchedules.Find(s => s.GlobalId == ctrlGuid);
            if (ws is not null)
            {
                foreach (var rId in relCtrl.GetRefList(4))
                {
                    if (taskMap.TryGetValue(rId, out var task) && !ws.RootTasks.Contains(task))
                    {
                        ws.RootTasks.Add(task);
                    }
                }
                continue;
            }

            // Connect to cost schedule
            var cs = model.CostSchedules.Find(s => s.GlobalId == ctrlGuid);
            if (cs is not null)
            {
                foreach (var rId in relCtrl.GetRefList(4))
                {
                    if (costItemMap.TryGetValue(rId, out var item) && !cs.RootCostItems.Contains(item))
                    {
                        cs.RootCostItems.Add(item);
                    }
                }
                continue;
            }

            // Connect elements or applied values/quantities to cost item
            if (costItemMap.TryGetValue(ctrlRef.Value, out var costItem))
            {
                foreach (var rId in relCtrl.GetRefList(4))
                {
                    if (allElementsMap.TryGetValue(rId, out var el))
                    {
                        costItem.AssignElement(el);
                    }
                    else
                    {
                        var relatedEnt = step.Find(rId);
                        if (relatedEnt is not null)
                        {
                            if (relatedEnt.Type.Equals("IFCAPPLIEDVALUE", StringComparison.OrdinalIgnoreCase))
                            {
                                string? vName = relatedEnt.GetString(0);
                                string? vDesc = relatedEnt.GetString(1);
                                string cleanAmount = ExtractNominalValue(relatedEnt.GetRaw(2));
                                string? vCat = relatedEnt.GetString(6);
                                if (double.TryParse(cleanAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out double amt))
                                {
                                    if (string.Equals(vName, "UnitRate", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(vCat, "UnitRate", StringComparison.OrdinalIgnoreCase))
                                    {
                                        costItem.UnitRate = amt;
                                        if (!string.IsNullOrEmpty(vDesc)) costItem.UnitBasis = vDesc;
                                    }
                                    else
                                    {
                                        costItem.AddCostValue(amt, "USD", vCat, vName);
                                    }
                                }
                            }
                            else if (relatedEnt.Type.StartsWith("IFCQUANTITY", StringComparison.OrdinalIgnoreCase))
                            {
                                double? qVal = relatedEnt.GetReal(3);
                                if (qVal.HasValue)
                                {
                                    costItem.Quantity = qVal.Value;
                                    string? qDesc = relatedEnt.GetString(1);
                                    if (!string.IsNullOrEmpty(qDesc)) costItem.UnitBasis = qDesc;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 10. Parse Materials & Composite Systems (IFCMATERIAL, IFCMATERIALLAYER, IFCMATERIALLAYERSET, IFCMATERIALLAYERSETUSAGE)
        var materialMap = new Dictionary<int, IfcMaterial>();
        foreach (var matEnt in step.IndexedOfType("IFCMATERIAL"))
        {
            string name = matEnt.GetString(0) ?? "Material";
            var material = materialAppearances.TryGetValue(matEnt.Id, out var appearance)
                ? new IfcMaterial { Name = name, R = appearance.R, G = appearance.G, B = appearance.B, Alpha = appearance.Alpha }
                : new IfcMaterial { Name = name };
            model.Materials.Add(material);
            materialMap.Add(matEnt.Id, material);
        }

        var layerMap = new Dictionary<int, IfcMaterialLayer>();
        foreach (var layEnt in step.IndexedOfType("IFCMATERIALLAYER"))
        {
            int? matId = layEnt.GetRef(0);
            IfcMaterial mat = (matId.HasValue && materialMap.TryGetValue(matId.Value, out var m))
                ? m
                : new IfcMaterial { Name = "DefaultMaterial" };
            double thick = layEnt.GetReal(1) ?? 0.0;
            string? isVentStr = layEnt.GetRaw(2);
            bool isVent = isVentStr?.Contains(".T.", StringComparison.OrdinalIgnoreCase) == true;
            string? lName = layEnt.GetString(3);
            string? lDesc = layEnt.GetString(4);
            layerMap[layEnt.Id] = new IfcMaterialLayer(mat, thick, isVent, lName) { Description = lDesc };
        }

        var layerSetMap = new Dictionary<int, IfcMaterialLayerSet>();
        foreach (var lsEnt in step.IndexedOfType("IFCMATERIALLAYERSET"))
        {
            var layIds = lsEnt.GetRefList(0);
            string lsName = lsEnt.GetString(1) ?? "Material Layer Set";
            string? lsDesc = lsEnt.GetString(2);
            var layers = layIds.Where(id => layerMap.ContainsKey(id)).Select(id => layerMap[id]);
            var ls = new IfcMaterialLayerSet(lsName, layers) { Description = lsDesc };
            layerSetMap[lsEnt.Id] = ls;
            model.MaterialLayerSets.Add(ls);
        }

        var layerSetUsageMap = new Dictionary<int, IfcMaterialLayerSetUsage>();
        foreach (var usageEnt in step.IndexedOfType("IFCMATERIALLAYERSETUSAGE"))
        {
            int? lsId = usageEnt.GetRef(0);
            if (lsId.HasValue && layerSetMap.TryGetValue(lsId.Value, out var ls))
            {
                string dir = usageEnt.GetRaw(1) ?? ".AXIS2.";
                string sense = usageEnt.GetRaw(2) ?? ".POSITIVE.";
                double offset = usageEnt.GetReal(3) ?? 0.0;
                layerSetUsageMap[usageEnt.Id] = new IfcMaterialLayerSetUsage(ls, offset, dir, sense);
            }
        }

        // 11. Parse Material Profile Sets (IFCMATERIALPROFILE, IFCMATERIALPROFILESET, IFCMATERIALPROFILESETUSAGE)
        var materialProfileMap = new Dictionary<int, IfcMaterialProfile>();
        foreach (var mpEnt in step.IndexedOfType("IFCMATERIALPROFILE"))
        {
            string? mpName = mpEnt.GetString(0);
            int? matId = mpEnt.GetRef(2);
            int? profId = mpEnt.GetRef(3);
            var prof = ParseProfile(step, profId, out _);
            if (prof is not null)
            {
                IfcMaterial? mat = (matId.HasValue && materialMap.TryGetValue(matId.Value, out var m)) ? m : null;
                materialProfileMap[mpEnt.Id] = new IfcMaterialProfile(prof, mat, mpName);
            }
        }

        var profileSetMap = new Dictionary<int, IfcMaterialProfileSet>();
        foreach (var psEnt in step.IndexedOfType("IFCMATERIALPROFILESET"))
        {
            string psName = psEnt.GetString(0) ?? "Material Profile Set";
            string? psDesc = psEnt.GetString(1);
            var mpIds = psEnt.GetRefList(2);
            var profiles = mpIds.Where(id => materialProfileMap.ContainsKey(id)).Select(id => materialProfileMap[id]);
            var ps = new IfcMaterialProfileSet(psName, profiles) { Description = psDesc };
            profileSetMap[psEnt.Id] = ps;
            model.MaterialProfileSets.Add(ps);
        }

        var profileSetUsageMap = new Dictionary<int, IfcMaterialProfileSetUsage>();
        foreach (var usageEnt in step.IndexedOfType("IFCMATERIALPROFILESETUSAGE"))
        {
            int? psId = usageEnt.GetRef(0);
            if (psId.HasValue && profileSetMap.TryGetValue(psId.Value, out var ps))
            {
                int card = usageEnt.GetInt(1) ?? 5;
                profileSetUsageMap[usageEnt.Id] = new IfcMaterialProfileSetUsage(ps, card);
            }
        }

        // 12. Parse Material Associations (IFCRELASSOCIATESMATERIAL)
        foreach (var relMat in step.IndexedOfType("IFCRELASSOCIATESMATERIAL"))
        {
            var elemIds = relMat.GetRefList(4);
            int? matRelId = relMat.GetRef(5);
            if (!matRelId.HasValue) continue;

            foreach (int eId in elemIds)
            {
                if (allElementsMap.TryGetValue(eId, out var elem))
                {
                    if (layerSetUsageMap.TryGetValue(matRelId.Value, out var lu))
                    {
                        elem.MaterialLayerSet = lu;
                    }
                    else if (layerSetMap.TryGetValue(matRelId.Value, out var lset))
                    {
                        elem.MaterialLayerSet = new IfcMaterialLayerSetUsage(lset);
                    }
                    else if (profileSetUsageMap.TryGetValue(matRelId.Value, out var pu))
                    {
                        elem.MaterialProfileSet = pu;
                    }
                    else if (profileSetMap.TryGetValue(matRelId.Value, out var pset))
                    {
                        elem.MaterialProfileSet = new IfcMaterialProfileSetUsage(pset);
                    }
                    else if (materialMap.TryGetValue(matRelId.Value, out var directMat))
                    {
                        elem.Material = directMat;
                    }
                }
            }
        }

        // 13. Parse MEP Distribution Ports & Network Connectivity
        var portMap = new Dictionary<int, IfcDistributionPort>();
        foreach (var portEnt in step.IndexedOfType("IFCDISTRIBUTIONPORT"))
        {
            string portGuid = portEnt.GetString(0) ?? IfcGuid.NewGuid();
            string portName = portEnt.GetString(2) ?? "Port";
            string? desc = portEnt.GetString(3);
            string? sysType = portEnt.GetString(4);
            if (ResolveObjectPlacement(geometry, portEnt.GetRef(5)) is not { } portPlacement)
            {
                model.UnsupportedPlacementCount++;
                continue;
            }
            var pos = portPlacement.Origin;
            var dir = portPlacement.Z.Normalized();
            if (dir == IfcVector3.Zero) dir = IfcVector3.UnitZ;

            string? rawFlow = portEnt.GetRaw(7);
            var flow = IfcFlowDirection.NotDefined;
            if (rawFlow?.Contains(".SOURCEANDSINK.", StringComparison.OrdinalIgnoreCase) == true)
                flow = IfcFlowDirection.SourceAndSink;
            else if (rawFlow?.Contains(".SOURCE.", StringComparison.OrdinalIgnoreCase) == true)
                flow = IfcFlowDirection.Source;
            else if (rawFlow?.Contains(".SINK.", StringComparison.OrdinalIgnoreCase) == true)
                flow = IfcFlowDirection.Sink;

            var port = new IfcDistributionPort(portName, flow, pos, dir, sysType)
            {
                Id = portEnt.Id,
                GlobalId = portGuid,
                Description = desc,
            };
            portMap[portEnt.Id] = port;
        }

        foreach (var relPortElem in step.IndexedOfType("IFCRELCONNECTSPORTTOELEMENT"))
        {
            int? portId = relPortElem.GetRef(4);
            int? elemId = relPortElem.GetRef(5);
            if (portId.HasValue && elemId.HasValue &&
                portMap.TryGetValue(portId.Value, out var port) &&
                allElementsMap.TryGetValue(elemId.Value, out var hostElem))
            {
                port.HostElement = hostElem;
                if (!hostElem.Ports.Contains(port))
                {
                    hostElem.Ports.Add(port);
                }
            }
        }

        foreach (var relPorts in step.IndexedOfType("IFCRELCONNECTSPORTS"))
        {
            int? portId1 = relPorts.GetRef(4);
            int? portId2 = relPorts.GetRef(5);
            if (portId1.HasValue && portId2.HasValue &&
                portMap.TryGetValue(portId1.Value, out var portA) &&
                portMap.TryGetValue(portId2.Value, out var portB))
            {
                portA.ConnectedPort = portB;
                portB.ConnectedPort = portA;
            }
        }

        model._reservedElementIds.UnionWith(allElementsMap.Keys);
        model.BooleanImportWarnings = geometry.BooleanWarnings.Order(StringComparer.Ordinal).ToArray();
        return model;
    }

    private static IfcSchema ReadSchema(StepFile step)
    {
        if (step.SchemaIdentifiers.Count > 1)
            throw new NotSupportedException($"Multiple IFC schema identifiers are not supported: {string.Join(", ", step.SchemaIdentifiers)}.");
        if (string.IsNullOrWhiteSpace(step.Schema))
            throw new InvalidDataException("The STEP file does not declare an IFC schema in FILE_SCHEMA.");
        string? name = SchemaName(step.Schema);
        if (string.Equals(name, "IFC2X3", StringComparison.OrdinalIgnoreCase)) return IfcSchema.Ifc2x3;
        if (string.Equals(name, "IFC4", StringComparison.OrdinalIgnoreCase)) return IfcSchema.Ifc4;
        if (IsIfc4x3Identifier(step.Schema)) return IfcSchema.Ifc4x3;
        throw new NotSupportedException($"IFC schema '{step.Schema}' is not supported.");
    }

    private static bool IsIfc4x3Identifier(string? identifier) =>
        identifier is not null && SchemaName(identifier)?.ToUpperInvariant() is "IFC4X3" or "IFC4X3_ADD1" or "IFC4X3_ADD2";

    private static string? SchemaName(string identifier)
    {
        // Part 21 allows a numeric object identifier after the exact EXPRESS schema name.
        int brace = identifier.IndexOf('{');
        if (brace < 0) return identifier;
        if (brace == 0 || !identifier.EndsWith('}')) return null;
        string[] arcs = identifier[(brace + 1)..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (arcs.Length < 2 || arcs[0] is not ("0" or "1" or "2") || arcs.Any(arc => !arc.All(char.IsAsciiDigit)))
            return null;
        if (arcs[0] != "2" && (!int.TryParse(arcs[1], NumberStyles.None, CultureInfo.InvariantCulture, out int second) || second > 39))
            return null;
        return identifier[..brace].TrimEnd(' ');
    }

    private int WriteElementShape(StepWriter w, IfcElement element, int bodySubContextId, Dictionary<SurfaceStyleKey, int> styleCache, bool useTessellation)
    {
        if (element.HasUnwritableBooleanBody && element.BooleanResult is null)
            throw new InvalidDataException($"Element '{element.Name}' has a Boolean body that cannot be written without losing operands.");

        int? styleId = null;
        var appearance = element.GetEffectiveMaterial();
        if (appearance is not null)
        {
            styleId = GetOrCreateSurfaceStyle(w, appearance, styleCache);
        }

        if (element.Solid is not null)
        {
            int solidGeomId = WriteExtrudedAreaSolid(w, element.Solid);
            if (styleId.HasValue)
            {
                w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(solidGeomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(appearance?.Name)}");
            }
            int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                $"{StepWriter.Ref(bodySubContextId)},'Body','SweptSolid',({StepWriter.Ref(solidGeomId)})");
            return w.Add("IFCPRODUCTDEFINITIONSHAPE",
                $"$,$,({StepWriter.Ref(shapeRepId)})");
        }

        if (element.RevolvedSolid is { } revolution && CanWriteRevolution(revolution))
        {
            int solidGeomId = WriteRevolvedAreaSolid(w, revolution);
            if (styleId.HasValue)
            {
                w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(solidGeomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(appearance?.Name)}");
            }
            int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                $"{StepWriter.Ref(bodySubContextId)},'Body','SweptSolid',({StepWriter.Ref(solidGeomId)})");
            return w.Add("IFCPRODUCTDEFINITIONSHAPE",
                $"$,$,({StepWriter.Ref(shapeRepId)})");
        }

        if (element.BooleanResult is not null)
        {
            int solidGeomId = WriteBooleanResult(w, element.BooleanResult, useTessellation);
            if (styleId.HasValue)
            {
                w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(solidGeomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(appearance?.Name)}");
            }
            string repType = CanWriteClipping(element.BooleanResult) ? "Clipping" : "CSG";
            int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},({StepWriter.Ref(solidGeomId)})");
            return w.Add("IFCPRODUCTDEFINITIONSHAPE",
                $"$,$,({StepWriter.Ref(shapeRepId)})");
        }

        var mesh = element.Mesh ?? element.RevolvedSolid?.ToMesh();
        if (mesh is not null && mesh.Vertices.Count >= 3 && mesh.Indices.Count >= 3)
        {
            var (repType, geomIds) = WriteBodyMesh(w, mesh, useTessellation);

            if (styleId.HasValue)
            {
                foreach (int geomId in geomIds)
                    w.Add("IFCSTYLEDITEM", $"{StepWriter.Ref(geomId)},({StepWriter.Ref(styleId.Value)}),{StepWriter.Str(appearance?.Name)}");
            }

            int shapeRepId = w.Add("IFCSHAPEREPRESENTATION",
                $"{StepWriter.Ref(bodySubContextId)},'Body',{StepWriter.Str(repType)},{StepWriter.List(geomIds.Select(StepWriter.Ref))}");
            return w.Add("IFCPRODUCTDEFINITIONSHAPE",
                $"$,$,({StepWriter.Ref(shapeRepId)})");
        }

        return 0;
    }

    private int WriteProfileDef(StepWriter w, IfcProfileDef profile)
    {
        bool ifc2x3 = Schema == IfcSchema.Ifc2x3;
        string Placement(IfcVector3 position)
        {
            // Position is mandatory on IfcParameterizedProfileDef in IFC2x3.
            bool hasOffset = Math.Abs(position.X) > 1e-9 || Math.Abs(position.Y) > 1e-9;
            if (!ifc2x3 && !hasOffset) return "$";
            int point = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(position.X), StepWriter.Real(position.Y)));
            return StepWriter.Ref(w.Add("IFCAXIS2PLACEMENT2D", $"{StepWriter.Ref(point)},$"));
        }

        // Omitting a zero radius represents a sharp corner where IFC2x3 requires a positive measure.
        string Fillet(double? radius)
        {
            if (radius is not double value || ifc2x3 && value == 0) return "$";
            string formatted = StepWriter.Real(value);
            return value > 0 && formatted == "0."
                ? value.ToString("0.0##############E+0", CultureInfo.InvariantCulture)
                : formatted;
        }

        if (profile is IfcRectangleProfileDef rect)
        {
            return w.Add("IFCRECTANGLEPROFILEDEF", $".AREA.,{StepWriter.Str(rect.ProfileName)},{Placement(rect.Position)},{StepWriter.Real(rect.XDim)},{StepWriter.Real(rect.YDim)}");
        }
        if (profile is IfcCircleProfileDef circ)
        {
            return w.Add("IFCCIRCLEPROFILEDEF", $".AREA.,{StepWriter.Str(circ.ProfileName)},{Placement(circ.Position)},{StepWriter.Real(circ.Radius)}");
        }
        if (profile is IfcArbitraryClosedProfileDef poly)
        {
            var ptIds = new List<int>(poly.CurvePoints.Count);
            foreach (var pt in poly.CurvePoints)
            {
                ptIds.Add(w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(pt.X), StepWriter.Real(pt.Y))));
            }
            int polyLineId = w.Add("IFCPOLYLINE", StepWriter.List(ptIds.Select(StepWriter.Ref)));
            return w.Add("IFCARBITRARYCLOSEDPROFILEDEF", $".AREA.,{StepWriter.Str(poly.ProfileName)},{StepWriter.Ref(polyLineId)}");
        }
        if (profile is IfcIShapeProfileDef ishape)
        {
            // FlangeEdgeRadius and FlangeSlope are present only in IFC4.
            string flange = ifc2x3 ? "" : ",$,$";
            return w.Add("IFCISHAPEPROFILEDEF",
                $".AREA.,{StepWriter.Str(ishape.ProfileName)},{Placement(ishape.Position)},{StepWriter.Real(ishape.OverallWidth)},{StepWriter.Real(ishape.OverallDepth)},{StepWriter.Real(ishape.WebThickness)},{StepWriter.Real(ishape.FlangeThickness)},{Fillet(ishape.FilletRadius)}{flange}");
        }
        if (profile is IfcLShapeProfileDef lshape)
        {
            // CentreOfGravityInX/Y are present only in IFC2x3.
            string centre = ifc2x3 ? ",$,$" : "";
            return w.Add("IFCLSHAPEPROFILEDEF",
                $".AREA.,{StepWriter.Str(lshape.ProfileName)},{Placement(lshape.Position)},{StepWriter.Real(lshape.Depth)},{StepWriter.Real(lshape.Width)},{StepWriter.Real(lshape.Thickness)},{Fillet(lshape.FilletRadius)},$,${centre}");
        }
        if (profile is IfcUShapeProfileDef ushape)
        {
            // EdgeRadius and FlangeSlope exist in both schemas, CentreOfGravityInX only in IFC2x3.
            string centre = ifc2x3 ? ",$" : "";
            return w.Add("IFCUSHAPEPROFILEDEF",
                $".AREA.,{StepWriter.Str(ushape.ProfileName)},{Placement(ushape.Position)},{StepWriter.Real(ushape.Depth)},{StepWriter.Real(ushape.FlangeWidth)},{StepWriter.Real(ushape.WebThickness)},{StepWriter.Real(ushape.FlangeThickness)},{Fillet(ushape.FilletRadius)},$,${centre}");
        }

        throw new NotSupportedException($"Profile type {profile.GetType().Name} is not supported.");
    }

    private int WriteBooleanResult(StepWriter w, IfcBooleanResult csg, bool useTessellation)
    {
        int firstId = 0;
        if (csg.FirstSolid is not null)
            firstId = WriteExtrudedAreaSolid(w, csg.FirstSolid);
        else if (csg.FirstRevolved is { } revolution && CanWriteRevolution(revolution))
            firstId = WriteRevolvedAreaSolid(w, revolution);
        else if ((csg.FirstMesh ?? csg.FirstRevolved?.ToMesh()) is { } firstMesh)
            firstId = WriteBooleanMesh(w, firstMesh, useTessellation);
        else if (csg.FirstBoolean is { } firstBoolean)
            firstId = WriteBooleanResult(w, firstBoolean, useTessellation);

        int secondId = 0;
        if (csg.SecondHalfSpace is not null)
        {
            var hs = csg.SecondHalfSpace;
            var plane = hs.BaseSurface;
            int locPtId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(plane.Location.X), StepWriter.Real(plane.Location.Y), StepWriter.Real(plane.Location.Z)));
            int axisDirId = WriteDirection(w, plane.Normal);
            int refDirId = WriteDirection(w, FirstProjAxis(plane.Normal.Normalized(), null));
            int pl3dId = w.Add("IFCAXIS2PLACEMENT3D", $"{StepWriter.Ref(locPtId)},{StepWriter.Ref(axisDirId)},{StepWriter.Ref(refDirId)}");
            int planeId = w.Add("IFCPLANE", StepWriter.Ref(pl3dId));
            string flag = hs.AgreementFlag ? ".T." : ".F.";
            secondId = w.Add("IFCHALFSPACESOLID", $"{StepWriter.Ref(planeId)},{flag}");
        }
        else if (csg.SecondSolid is not null)
        {
            secondId = WriteExtrudedAreaSolid(w, csg.SecondSolid);
        }
        else if (csg.SecondRevolved is { } secondRevolved && CanWriteRevolution(secondRevolved))
        {
            secondId = WriteRevolvedAreaSolid(w, secondRevolved);
        }
        else if ((csg.SecondMesh ?? csg.SecondRevolved?.ToMesh()) is { } secondMesh)
        {
            secondId = WriteBooleanMesh(w, secondMesh, useTessellation);
        }
        else if (csg.SecondBoolean is { } secondBoolean)
        {
            secondId = WriteBooleanResult(w, secondBoolean, useTessellation);
        }

        string op = csg.Operator switch
        {
            IfcBooleanOperator.Union => ".UNION.",
            IfcBooleanOperator.Intersection => ".INTERSECTION.",
            _ => ".DIFFERENCE.",
        };

        if (firstId == 0 || secondId == 0)
            throw new InvalidDataException("Cannot write a Boolean result without both operands.");

        string entityType = CanWriteClipping(csg) ? "IFCBOOLEANCLIPPINGRESULT" : "IFCBOOLEANRESULT";
        return w.Add(entityType, $"{op},{StepWriter.Ref(firstId)},{StepWriter.Ref(secondId)}");
    }

    private int WriteExtrudedAreaSolid(StepWriter w, IfcExtrudedAreaSolid solid)
    {
        int profileId = WriteProfileDef(w, solid.Profile);
        var frame = solid.GetProfileFrame();
        int solidPtId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(solid.Position.X), StepWriter.Real(solid.Position.Y), StepWriter.Real(solid.Position.Z)));
        string axis = "$", refDirection = "$";
        if (frame.Z != IfcVector3.UnitZ)
        {
            axis = StepWriter.Ref(WriteDirection(w, frame.Z));
            refDirection = StepWriter.Ref(WriteDirection(w, frame.X));
        }
        int solidPlacementId = w.Add("IFCAXIS2PLACEMENT3D", $"{StepWriter.Ref(solidPtId)},{axis},{refDirection}");
        int dirId = WriteDirection(w, IfcVector3.UnitZ);

        return w.Add("IFCEXTRUDEDAREASOLID", $"{StepWriter.Ref(profileId)},{StepWriter.Ref(solidPlacementId)},{StepWriter.Ref(dirId)},{StepWriter.Real(solid.Depth)}");
    }

    private static int WriteDirection(StepWriter w, IfcVector3 direction) =>
        w.Add("IFCDIRECTION", StepWriter.List(StepWriter.RealExact(direction.X), StepWriter.RealExact(direction.Y), StepWriter.RealExact(direction.Z)));

    private static bool CanWriteRevolution(IfcRevolvedAreaSolid solid) =>
        solid.Axis.Location.Z == solid.Position.Z && solid.Axis.Axis.Z == 0 && solid.Axis.Axis != IfcVector3.Zero;

    private static bool CanWriteClipping(IfcBooleanResult result) =>
        result.Operator == IfcBooleanOperator.Difference && result.SecondHalfSpace is not null
        && (result.FirstSolid is not null || result.FirstRevolved is { } revolution && CanWriteRevolution(revolution)
            || result.FirstBoolean is { } firstBoolean && CanWriteClipping(firstBoolean));

    private int WriteRevolvedAreaSolid(StepWriter w, IfcRevolvedAreaSolid solid)
    {
        int profileId = WriteProfileDef(w, solid.Profile);
        int solidPtId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(solid.Position.X), StepWriter.Real(solid.Position.Y), StepWriter.Real(solid.Position.Z)));
        int solidPlacementId = w.Add("IFCAXIS2PLACEMENT3D", $"{StepWriter.Ref(solidPtId)},$,$");

        // The authored axis is absolute; IFC expresses it relative to Position.
        var axisLocation = solid.Axis.Location - solid.Position;
        int axisLocPtId = w.Add("IFCCARTESIANPOINT", StepWriter.List(StepWriter.Real(axisLocation.X), StepWriter.Real(axisLocation.Y), StepWriter.Real(axisLocation.Z)));
        int axisDirId = WriteDirection(w, solid.Axis.Axis);
        int axisPlacementId = w.Add("IFCAXIS1PLACEMENT", $"{StepWriter.Ref(axisLocPtId)},{StepWriter.Ref(axisDirId)}");

        double angle = AngleUnit == IfcPlaneAngleUnit.Degree ? solid.Angle * (180.0 / Math.PI) : solid.Angle;
        string angleText = StepWriter.Real(angle);
        if (angle > 0 && angleText == "0.")
            angleText = angle.ToString("0.0##############E+0", CultureInfo.InvariantCulture);
        return w.Add("IFCREVOLVEDAREASOLID", $"{StepWriter.Ref(profileId)},{StepWriter.Ref(solidPlacementId)},{StepWriter.Ref(axisPlacementId)},{angleText}");
    }

    /// <summary>State shared while reading product geometry: caches for placements and mapped representations.</summary>
    private sealed class GeometryReadContext(
        StepFile step, Dictionary<int, IfcMaterial> geomToMaterialMap, IfcTransform root, double radiansPerAngleUnit,
        IfcSchema schema, bool omitUnsupportedPlacements, bool keepParametric)
    {
        public StepFile Step { get; } = step;
        public Dictionary<int, IfcMaterial> GeomToMaterialMap { get; } = geomToMaterialMap;

        /// <summary>Unit scale applied after the world coordinate system of the model context.</summary>
        public IfcTransform Root { get; } = root;
        public double RadiansPerAngleUnit { get; } = radiansPerAngleUnit;
        public IfcSchema Schema { get; } = schema;
        public bool OmitUnsupportedPlacements { get; } = omitUnsupportedPlacements;

        /// <summary>False when geometry is rescaled to metres: parametric solids stay in file units and would disagree with LengthUnit.</summary>
        public bool KeepParametric { get; } = keepParametric;
        public Dictionary<int, IfcTransform> Placements { get; } = [];
        public Dictionary<int, (IfcMesh? Mesh, IfcMaterial? Material, bool HasSkippedItems)> MappedRepresentations { get; } = [];
        public HashSet<int> MapsInProgress { get; } = [];
        public HashSet<string> BooleanWarnings { get; } = [];
        public Dictionary<int, IfcVector3?[]> TessellatedPointLists { get; } = [];

        /// <summary>
        /// Cap on vertices materialised while reading nested mapped items and Boolean bodies.
        /// </summary>
        public long VertexBudget { get; } = MinVertexBudget + (VerticesPerEntity * (long)step.EntityCount);

        private long _materialisedVertices;
        private long _remainingBooleanNodes = 256 + 8L * step.EntityCount;

        public void ChargeBooleanNode(int id)
        {
            if (_remainingBooleanNodes-- <= 0)
                throw new InvalidDataException($"Boolean operands at #{id} exceed the model-wide expansion limit.");
        }

        public void ChargeVertices(int count)
        {
            CheckVertexBudget(count);
            _materialisedVertices += count;
        }

        public void CheckVertexBudget(long count)
        {
            if (count > int.MaxValue || _materialisedVertices + count > VertexBudget)
            {
                throw new InvalidDataException(
                    $"IFC geometry expands to more than {VertexBudget} vertices (limit for {Step.EntityCount} entities); check for nested mapped items or Boolean results.");
            }
        }
    }

    private const int MaxMappingDepth = 64;
    private const long MinVertexBudget = 1_000_000;
    private const long VerticesPerEntity = 256;

    /// <summary>Parametric form of the first body item, in the item's own coordinates.</summary>
    private readonly record struct ParametricBody(IfcExtrudedAreaSolid? Solid, IfcRevolvedAreaSolid? Revolved, IfcBooleanResult? Boolean);

    /// <summary>
    /// Reads the product shape into one world-space mesh (all items of the body representation merged).
    /// Parametric outputs are kept only when they represent the complete body in model coordinates.
    /// </summary>
    private static IfcMesh? ParseGeometry(
        GeometryReadContext ctx,
        int shapeDefId,
        string meshName,
        IfcTransform placement,
        out IfcMaterial? material,
        out IfcExtrudedAreaSolid? solid,
        out IfcRevolvedAreaSolid? revolvedSolid,
        out IfcBooleanResult? booleanResult,
        out bool unwritableBooleanBody,
        bool bodyOnly)
    {
        material = null;
        solid = null;
        revolvedSolid = null;
        booleanResult = null;
        unwritableBooleanBody = false;
        var shapeDef = ctx.Step.Find(shapeDefId);
        if (shapeDef is null) return null;

        var reps = shapeDef.GetRefList(2)
            .Select(id => ctx.Step.Find(id))
            .OfType<StepEntity>()
            .Where(r => r.Type.Equals("IFCSHAPEREPRESENTATION", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A complete Body-FallBack can replace an incomplete Body, but not vice versa.
        // Never substitute e.g. Clearance for Body on newly discovered products.
        static bool IsBody(StepEntity rep) => string.Equals(rep.GetString(1), "Body", StringComparison.OrdinalIgnoreCase);
        static bool IsFallback(StepEntity rep) => string.Equals(rep.GetString(1), "Body-FallBack", StringComparison.OrdinalIgnoreCase);
        var fallbacks = reps.Where(IsFallback);
        bool hasFallback = reps.Any(IsFallback);
        var candidates = reps.Where(IsBody).Concat(fallbacks);
        if (!bodyOnly) candidates = candidates.Concat(reps.Where(rep => !IsBody(rep) && !IsFallback(rep)));
        (IfcMesh Mesh, IfcMaterial? Material, ParametricBody? Parametric, bool HasBoolean)? selected = null, incompleteBody = null;
        foreach (var rep in candidates)
        {
            if (incompleteBody is not null && !IsBody(rep) && !IsFallback(rep)) break;
            IfcMaterial? repMaterial = null;
            var mesh = ParseRepresentationItems(ctx, rep, meshName, ref repMaterial, out var parametric, out bool hasBoolean, out bool hasSkippedItems);
            if (mesh is null) continue;

            if (hasSkippedItems && hasFallback && (IsBody(rep) || IsFallback(rep)))
            {
                incompleteBody ??= (mesh, repMaterial, parametric, hasBoolean);
                continue;
            }
            selected = (mesh, repMaterial, parametric, hasBoolean);
            break;
        }

        if ((selected ?? incompleteBody) is not { } chosen) return null;
        material = chosen.Material;
        booleanResult = chosen.Parametric?.Boolean;
        unwritableBooleanBody = chosen.HasBoolean && booleanResult is null;
        if (chosen.Parametric is { } body && ctx.KeepParametric && placement.IsTranslationOnly)
        {
            solid = body.Solid;
            if (solid is not null) solid.Position += placement.Origin;
            revolvedSolid = body.Revolved;
            if (revolvedSolid is not null)
            {
                revolvedSolid.Position += placement.Origin;
                revolvedSolid.Axis.Location += placement.Origin;
            }
        }

        if (booleanResult is not null && !placement.IsIdentity)
            PlaceBooleanOperands(booleanResult, placement);
        var world = placement.ApplyTo(chosen.Mesh);
        return IfcMesh.Create(world.Id, meshName, world.Vertices, world.Indices);
    }

    /// <summary>Merges all readable items of a shape representation into one mesh in the representation's coordinates.</summary>
    private static IfcMesh? ParseRepresentationItems(
        GeometryReadContext ctx,
        StepEntity? repEnt,
        string meshName,
        ref IfcMaterial? material,
        out ParametricBody? parametric,
        out bool hasBoolean,
        out bool hasSkippedItems)
    {
        parametric = null;
        hasBoolean = false;
        hasSkippedItems = false;
        if (repEnt is null) return null;

        var itemIds = repEnt.GetRefList(3);
        var parts = new List<IfcMesh>(itemIds.Count);
        foreach (int itemId in itemIds)
        {
            var itemEnt = ctx.Step.Find(itemId);
            if (itemEnt is null)
            {
                hasSkippedItems = true;
                continue;
            }

            var itemMaterial = ctx.GeomToMaterialMap.GetValueOrDefault(itemId);
            var mesh = ParseRepresentationItem(ctx, itemEnt, meshName, ref itemMaterial, out var itemParametric, out bool skippedNestedItems);
            if (mesh is null)
            {
                hasSkippedItems = true;
                continue;
            }
            hasSkippedItems |= skippedNestedItems;
            material ??= itemMaterial;
            // The Boolean reader already charges leaf meshes and triangles emitted by each clip.
            if (itemParametric?.Boolean is null) ctx.ChargeVertices(mesh.Vertices.Count);
            hasBoolean |= itemParametric?.Boolean is not null;
            parametric = parts.Count == 0 ? itemParametric : null;
            parts.Add(mesh);
        }

        if (parts.Count == 0) return null;
        if (parts.Count == 1) return parts[0];

        var vertices = new List<IfcVector3>(parts.Sum(p => p.Vertices.Count));
        var indices = new List<int>(parts.Sum(p => p.Indices.Count));
        foreach (var part in parts)
        {
            int offset = vertices.Count;
            vertices.AddRange(part.Vertices);
            foreach (int index in part.Indices) indices.Add(index + offset);
        }
        return IfcMesh.Create(parts[0].Id, meshName, vertices, indices);
    }

    /// <summary>Reads one representation item into a mesh in the coordinates of its representation.</summary>
    private static IfcMesh? ParseRepresentationItem(
        GeometryReadContext ctx,
        StepEntity itemEnt,
        string meshName,
        ref IfcMaterial? material,
        out ParametricBody? parametric,
        out bool hasSkippedItems)
    {
        parametric = null;
        hasSkippedItems = false;
        var step = ctx.Step;

        // A/B. Meshes (Brep / TriangulatedFaceSet / surface models)
        var mesh = ParseMeshEntity(ctx, itemEnt, meshName);
        if (mesh is not null) return mesh;

        // C/D. IFCEXTRUDEDAREASOLID / IFCREVOLVEDAREASOLID
        var swept = ParseSweptSolid(step, itemEnt, meshName, ctx.RadiansPerAngleUnit, out var extruded, out var revolved);
        if (swept is not null)
        {
            if (extruded is not null || revolved is not null)
                parametric = new ParametricBody(extruded, revolved, null);
            return swept;
        }

        // E. IFCBOOLEANRESULT / IFCBOOLEANCLIPPINGRESULT
        if (IsBooleanEntity(itemEnt))
        {
            var boolRes = ParseBooleanResult(ctx, itemEnt, []);
            if (itemEnt.GetRef(1) is int firstOperand)
                material ??= ctx.GeomToMaterialMap.GetValueOrDefault(firstOperand);
            parametric = new ParametricBody(null, null, boolRes);
            return boolRes.ToMesh(meshName, ctx.ChargeVertices);
        }

        // F. IFCMAPPEDITEM: MappingTarget * MappingOrigin * mapped representation.
        if (itemEnt.Type.Equals("IFCMAPPEDITEM", StringComparison.OrdinalIgnoreCase))
        {
            int? mapId = itemEnt.GetRef(0);
            if (!mapId.HasValue) return null;

            var (mapMesh, mapMaterial, skippedMappedItems) = ParseMappedRepresentation(ctx, mapId.Value, meshName);
            hasSkippedItems = skippedMappedItems;
            material ??= mapMaterial;
            if (mapMesh is null) return null;
            return ParseCartesianTransformationOperator(step, itemEnt.GetRef(1)).ApplyTo(mapMesh);
        }

        return null;
    }

    /// <summary>
    /// Meshes a swept solid in representation coordinates and returns its parametric form
    /// only when the public solid can reproduce that geometry.
    /// </summary>
    /// <param name="step">The STEP file that holds the solid and the entities it references.</param>
    /// <param name="ent">The IfcExtrudedAreaSolid or IfcRevolvedAreaSolid entity.</param>
    /// <param name="meshName">The name given to the returned mesh.</param>
    /// <param name="radiansPerAngleUnit">Conversion from project plane angle units to radians.</param>
    /// <param name="extruded">The equivalent parametric extrusion, if any.</param>
    /// <param name="revolved">The equivalent parametric revolution, if any.</param>
    private static IfcMesh? ParseSweptSolid(
        StepFile step,
        StepEntity ent,
        string meshName,
        double radiansPerAngleUnit,
        out IfcExtrudedAreaSolid? extruded,
        out IfcRevolvedAreaSolid? revolved)
    {
        extruded = null;
        revolved = null;
        bool isExtruded = ent.Type.Equals("IFCEXTRUDEDAREASOLID", StringComparison.OrdinalIgnoreCase);
        if (!isExtruded && !ent.Type.Equals("IFCREVOLVEDAREASOLID", StringComparison.OrdinalIgnoreCase)) return null;

        var profile = ParseProfile(step, ent.GetRef(0), out var profileRotation);
        if (profile is null) return null;

        var position = ParseAxis2Placement(step, ent.GetRef(1));
        if (isExtruded)
        {
            double depth = ent.GetReal(3) ?? 0.0;
            var direction = ParseOptionalDirection(step, ent.GetRef(2)) ?? IfcVector3.UnitZ;
            if (direction == IfcVector3.UnitZ && profileRotation.IsIdentity)
            {
                var solid = new IfcExtrudedAreaSolid(profile, depth, position.Origin, position.Z);
                var frame = solid.GetProfileFrame();
                if ((frame.X - position.X).Length <= 1e-9 && (frame.Y - position.Y).Length <= 1e-9)
                {
                    extruded = solid;
                    return solid.ToMesh(16, meshName);
                }
            }

            // IFC keeps the profile in the XY plane of Position. Sweep along local Z, then shear Z onto ExtrudedDirection.
            var shear = new IfcTransform(IfcVector3.UnitX, IfcVector3.UnitY, direction, IfcVector3.Zero);
            var local = new IfcExtrudedAreaSolid(profile, depth).ToMesh(16, meshName);
            return position.Multiply(shear).Multiply(profileRotation).ApplyTo(local);
        }

        double angle = ent.GetReal(3) is double fileAngle ? fileAngle * radiansPerAngleUnit : 2.0 * Math.PI;
        var axis = ParseAxis1Placement(step, ent.GetRef(2));
        if (position.IsTranslationOnly && profileRotation.IsIdentity)
        {
            axis.Location += position.Origin;
            revolved = new IfcRevolvedAreaSolid(profile, angle, position.Origin, axis);
            return revolved.ToMesh(16, 24, meshName);
        }

        if (!profileRotation.IsIdentity)
            profile = new IfcArbitraryClosedProfileDef(profile.GetBoundaryPoints().Select(profileRotation.Apply), profile.ProfileName);
        return position.ApplyTo(new IfcRevolvedAreaSolid(profile, angle, IfcVector3.Zero, axis).ToMesh(16, 24, meshName));
    }

    /// <summary>Reads an IfcRepresentationMap once, in the coordinates of the mapping (MappingOrigin applied).</summary>
    private static (IfcMesh? Mesh, IfcMaterial? Material, bool HasSkippedItems) ParseMappedRepresentation(
        GeometryReadContext ctx, int mapId, string meshName)
    {
        if (ctx.MappedRepresentations.TryGetValue(mapId, out var cached)) return cached;

        var mapEnt = ctx.Step.Find(mapId);
        if (mapEnt is null || !mapEnt.Type.Equals("IFCREPRESENTATIONMAP", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, true);
        }

        if (ctx.MapsInProgress.Count >= MaxMappingDepth)
        {
            throw new InvalidDataException($"IFC representation map #{mapId} is nested more than {MaxMappingDepth} levels deep.");
        }

        if (!ctx.MapsInProgress.Add(mapId))
        {
            throw new InvalidDataException($"IFC representation map #{mapId} maps itself (IfcMappedItem cycle).");
        }

        IfcMaterial? material = null;
        var mesh = ParseRepresentationItems(ctx, ctx.Step.Find(mapEnt.GetRef(1)), meshName, ref material, out _, out _, out bool skippedItems);
        var result = (mesh is null ? null : ParseAxis2Placement(ctx.Step, mapEnt.GetRef(0)).ApplyTo(mesh), material, skippedItems);

        ctx.MapsInProgress.Remove(mapId);
        ctx.MappedRepresentations[mapId] = result;
        return result;
    }

    /// <summary>Resolves an IfcObjectPlacement (PlacementRelTo chain) into a world transform in model length units.</summary>
    private static IfcTransform? ResolveObjectPlacement(GeometryReadContext ctx, int? placementId)
    {
        // Walk up to a resolved or root placement, then compose downwards (iterative: chains may be deep).
        var chain = new List<StepEntity>();
        var visited = new HashSet<int>();
        var world = ctx.Root;
        for (int? id = placementId; id.HasValue;)
        {
            if (ctx.Placements.TryGetValue(id.Value, out var cached))
            {
                world = cached;
                break;
            }

            var ent = ctx.Step.Find(id.Value);
            if (ent is null || !ent.Type.Equals("IFCLOCALPLACEMENT", StringComparison.OrdinalIgnoreCase))
            {
                // A linear or grid placement cannot be reduced to a local origin.
                if (ctx.OmitUnsupportedPlacements) return null;
                break;
            }
            if (!visited.Add(id.Value))
            {
                throw new InvalidDataException($"IFC placement #{id.Value} is relative to itself (PlacementRelTo cycle).");
            }

            chain.Add(ent);
            id = ent.GetRef(0);
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            world = world.Multiply(ParseAxis2Placement(ctx.Step, chain[i].GetRef(1)));
            ctx.Placements[chain[i].Id] = world;
        }
        return world;
    }

    /// <summary>WorldCoordinateSystem of the 3D 'Model' context: absolute placements are relative to it.</summary>
    private static IfcTransform ParseWorldCoordinateSystem(StepFile step)
    {
        var contexts = step.IndexedOfType("IFCGEOMETRICREPRESENTATIONCONTEXT");
        var model = contexts.FirstOrDefault(c => string.Equals(c.GetString(1), "Model", StringComparison.OrdinalIgnoreCase))
            ?? contexts.FirstOrDefault(c => c.GetInt(2) == 3);
        return model is null ? IfcTransform.Identity : ParseAxis2Placement(step, model.GetRef(4));
    }

    /// <summary>IfcAxis2Placement3D / IfcAxis2Placement2D as a rigid transform (identity when absent).</summary>
    private static IfcTransform ParseAxis2Placement(StepFile step, int? placementId)
    {
        var ent = step.Find(placementId);
        if (ent is null) return IfcTransform.Identity;

        var location = ParseCoordinates(step.Find(ent.GetRef(0)));
        if (ent.Type.Equals("IFCAXIS2PLACEMENT2D", StringComparison.OrdinalIgnoreCase))
        {
            var x2 = FirstProjAxis(IfcVector3.UnitZ, ParseOptionalDirection(step, ent.GetRef(1)));
            return new IfcTransform(x2, IfcVector3.Cross(IfcVector3.UnitZ, x2), IfcVector3.UnitZ, location);
        }

        if (!ent.Type.Equals("IFCAXIS2PLACEMENT3D", StringComparison.OrdinalIgnoreCase)) return IfcTransform.Identity;

        var z = ParseOptionalDirection(step, ent.GetRef(1)) ?? IfcVector3.UnitZ;
        var x = FirstProjAxis(z, ParseOptionalDirection(step, ent.GetRef(2)));
        return new IfcTransform(x, IfcVector3.Cross(z, x), z, location);
    }

    /// <summary>
    /// IfcCartesianTransformationOperator (2D/3D, uniform and non-uniform) as an affine transform.
    /// Y follows Z x X and is reversed when Axis2 points the other way (mirroring).
    /// </summary>
    private static IfcTransform ParseCartesianTransformationOperator(StepFile step, int? operatorId)
    {
        var ent = step.Find(operatorId);
        if (ent is null) return IfcTransform.Identity;

        bool is3D = ent.Type.Contains("3D", StringComparison.OrdinalIgnoreCase);
        bool nonUniform = ent.Type.EndsWith("NONUNIFORM", StringComparison.OrdinalIgnoreCase);
        var axis1 = ParseOptionalDirection(step, ent.GetRef(0));
        var axis2 = ParseOptionalDirection(step, ent.GetRef(1));
        var origin = ParseCoordinates(step.Find(ent.GetRef(2)));
        double scale = ent.GetReal(3) ?? 1.0;

        var z = IfcVector3.UnitZ;
        double scaleY = scale, scaleZ = scale;
        if (is3D)
        {
            z = ParseOptionalDirection(step, ent.GetRef(4)) ?? IfcVector3.UnitZ;
            if (nonUniform)
            {
                scaleY = ent.GetReal(5) ?? scale;
                scaleZ = ent.GetReal(6) ?? scale;
            }
        }
        else if (nonUniform)
        {
            scaleY = ent.GetReal(4) ?? scale;
        }

        var x = FirstProjAxis(z, axis1);
        var y = IfcVector3.Cross(z, x);
        // IfcSecondProjAxis: in 3D an omitted Axis2 is (0,1,0); the side of Z x X it lies on decides mirroring.
        var yReference = axis2 ?? (is3D ? IfcVector3.UnitY : null);
        if (yReference.HasValue && IfcVector3.Dot(yReference.Value, y) < 0) y = -y;
        return new IfcTransform(x * scale, y * scaleY, z * scaleZ, origin);
    }

    /// <summary>IFC IfcFirstProjAxis: the reference direction projected onto the plane normal to <paramref name="z"/>.</summary>
    private static IfcVector3 FirstProjAxis(IfcVector3 z, IfcVector3? reference)
    {
        // Defaulted reference: (1,0,0), or (0,1,0) when Z is (1,0,0). A reference parallel to Z is invalid: use the default.
        var defaultReference = (z - IfcVector3.UnitX).Length < 1e-9 ? IfcVector3.UnitY : IfcVector3.UnitX;
        foreach (var candidate in new[] { reference ?? defaultReference, defaultReference, IfcVector3.UnitY })
        {
            var x = (candidate - (z * IfcVector3.Dot(candidate, z))).Normalized();
            if (x != IfcVector3.Zero) return x;
        }
        return IfcVector3.UnitY;
    }

    private static IfcVector3? ParseOptionalDirection(StepFile step, int? id)
    {
        var direction = ParseCoordinates(step.Find(id));
        double scale = Math.Max(Math.Abs(direction.X), Math.Max(Math.Abs(direction.Y), Math.Abs(direction.Z)));
        if (scale == 0 || !double.IsFinite(scale)) return null;
        // Direction ratios are scale-free, including values too small or large to normalize directly.
        return new IfcVector3(direction.X / scale, direction.Y / scale, direction.Z / scale).Normalized();
    }

    /// <summary>Coordinates of an IfcCartesianPoint or IfcDirection; missing components are 0.</summary>
    private static IfcVector3 ParseCoordinates(StepEntity? ent)
    {
        if (ent is null) return IfcVector3.Zero;
        var parts = StepEntity.ParseArguments((ent.GetRaw(0) ?? string.Empty).Trim('(', ')'));
        double Component(int i) =>
            i < parts.Count && double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        return new IfcVector3(Component(0), Component(1), Component(2));
    }

    private static int WriteLengthUnit(StepWriter w, IfcLengthUnit unit) => unit switch
    {
        IfcLengthUnit.Millimetre => w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,.MILLI.,.METRE."),
        IfcLengthUnit.Metre => w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,$,.METRE."),
        IfcLengthUnit.Foot or IfcLengthUnit.Inch => WriteImperialLengthUnit(w, unit),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported IFC length unit."),
    };

    private static int WriteImperialLengthUnit(StepWriter w, IfcLengthUnit unit)
    {
        int metre = w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,$,.METRE.");
        int dimensions = w.Add("IFCDIMENSIONALEXPONENTS", "1,0,0,0,0,0,0");
        int factor = w.Add("IFCMEASUREWITHUNIT", $"IFCLENGTHMEASURE({StepWriter.Real(unit.MetresPerUnit())}),{StepWriter.Ref(metre)}");
        string name = unit == IfcLengthUnit.Foot ? "FOOT" : "INCH";
        return w.Add("IFCCONVERSIONBASEDUNIT", $"{StepWriter.Ref(dimensions)},.LENGTHUNIT.,'{name}',{StepWriter.Ref(factor)}");
    }

    private static int WritePlaneAngleUnit(StepWriter w, IfcPlaneAngleUnit unit)
    {
        int radianId = w.Add("IFCSIUNIT", "*,.PLANEANGLEUNIT.,$,.RADIAN.");
        if (unit == IfcPlaneAngleUnit.Radian) return radianId;
        if (unit != IfcPlaneAngleUnit.Degree)
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported IFC plane angle unit.");
        int dimensions = w.Add("IFCDIMENSIONALEXPONENTS", "0,0,0,0,0,0,0");
        int factor = w.Add("IFCMEASUREWITHUNIT", $"IFCPLANEANGLEMEASURE({DegreeInRadians}),{StepWriter.Ref(radianId)}");
        return w.Add("IFCCONVERSIONBASEDUNIT", $"{StepWriter.Ref(dimensions)},.PLANEANGLEUNIT.,'DEGREE',{StepWriter.Ref(factor)}");
    }

    /// <summary>Metres or radians per project length or plane angle unit. Null when absent or unrecognised.</summary>
    private static double? ParseProjectUnitFactor(StepFile step, StepEntity? assignment, string unitType)
    {
        var unit = assignment?.GetRefList(0)
            .Select(id => step.Find(id))
            .FirstOrDefault(u => string.Equals(u?.GetRaw(1), unitType, StringComparison.OrdinalIgnoreCase));
        return PositiveUnitFactor(step, unit, unitType);
    }

    private static double? PositiveUnitFactor(StepFile step, StepEntity? unit, string unitType) =>
        ParseUnitFactor(step, unit, unitType, 0) is double value && double.IsFinite(value) && value > 0 ? value : null;

    /// <summary>Factor of a named unit relative to its SI base unit.</summary>
    private static double? ParseUnitFactor(StepFile step, StepEntity? unit, string unitType, int depth)
    {
        if (unit is null || depth > 8 || !string.Equals(unit.GetRaw(1), unitType, StringComparison.OrdinalIgnoreCase))
            return null;

        if (unit.Type.Equals("IFCSIUNIT", StringComparison.OrdinalIgnoreCase))
        {
            string baseUnit = unitType == ".LENGTHUNIT." ? ".METRE." : ".RADIAN.";
            if (!string.Equals(unit.GetRaw(3), baseUnit, StringComparison.OrdinalIgnoreCase)) return null;
            return unit.GetRaw(2)?.Trim('.').ToUpperInvariant() switch
            {
                null => 1.0,
                "EXA" => 1e18,
                "PETA" => 1e15,
                "TERA" => 1e12,
                "GIGA" => 1e9,
                "MEGA" => 1e6,
                "KILO" => 1e3,
                "HECTO" => 1e2,
                "DECA" => 1e1,
                "DECI" => 1e-1,
                "CENTI" => 1e-2,
                "MILLI" => 1e-3,
                "MICRO" => 1e-6,
                "NANO" => 1e-9,
                "PICO" => 1e-12,
                "FEMTO" => 1e-15,
                "ATTO" => 1e-18,
                _ => null,
            };
        }

        if (unit.Type.Equals("IFCCONVERSIONBASEDUNIT", StringComparison.OrdinalIgnoreCase)
            || unit.Type.Equals("IFCCONVERSIONBASEDUNITWITHOFFSET", StringComparison.OrdinalIgnoreCase) && unit.GetReal(4) == 0)
        {
            var measure = step.Find(unit.GetRef(3));
            if (measure is null || !measure.Type.Equals("IFCMEASUREWITHUNIT", StringComparison.OrdinalIgnoreCase)) return null;
            if (!double.TryParse(ExtractNominalValue(measure.GetRaw(0)), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.IsFinite(value) || value <= 0) return null;
            return value * ParseUnitFactor(step, step.Find(measure.GetRef(1)), unitType, depth + 1);
        }

        return null;
    }

    private static IfcLengthUnit? ToLengthUnit(double metresPerUnit)
    {
        foreach (var unit in new[] { IfcLengthUnit.Millimetre, IfcLengthUnit.Metre, IfcLengthUnit.Foot, IfcLengthUnit.Inch })
        {
            if (Math.Abs(metresPerUnit - unit.MetresPerUnit()) <= 1e-9 * unit.MetresPerUnit()) return unit;
        }
        return null;
    }

    private static IfcMesh? ParseMeshEntity(GeometryReadContext ctx, StepEntity itemEnt, string meshName = "Mesh")
    {
        var step = ctx.Step;
        // IFCFACETEDBREP (one closed shell), IFCFACEBASEDSURFACEMODEL (connected face sets), IFCSHELLBASEDSURFACEMODEL (shells)
        bool isBrep = itemEnt.Type.Equals("IFCFACETEDBREP", StringComparison.OrdinalIgnoreCase);
        if (isBrep ||
            itemEnt.Type.Equals("IFCFACEBASEDSURFACEMODEL", StringComparison.OrdinalIgnoreCase) ||
            itemEnt.Type.Equals("IFCSHELLBASEDSURFACEMODEL", StringComparison.OrdinalIgnoreCase))
        {
            var shellIds = isBrep && itemEnt.GetRef(0) is int shellId ? [shellId] : itemEnt.GetRefList(0);
            var faceIds = shellIds.SelectMany(id => step.Find(id)?.GetRefList(0) ?? []);
            return ParseFacetedFaces(step, itemEnt, faceIds, meshName);
        }

        if (itemEnt.Type.Equals("IFCTRIANGULATEDFACESET", StringComparison.OrdinalIgnoreCase))
            return ParseTriangulatedFaceSet(ctx, itemEnt, meshName);
        if (itemEnt.Type.Equals("IFCPOLYGONALFACESET", StringComparison.OrdinalIgnoreCase))
            return ParsePolygonalFaceSet(ctx, itemEnt, meshName);
        if (itemEnt.Type.Equals("IFCSWEPTDISKSOLID", StringComparison.OrdinalIgnoreCase)
            || itemEnt.Type.Equals("IFCSWEPTDISKSOLIDPOLYGONAL", StringComparison.OrdinalIgnoreCase))
            return ParseSweptDisk(ctx, itemEnt, meshName);

        return null;
    }

    private static List<string> ParseStepList(string? raw) =>
        raw is { Length: >= 2 } && raw[0] == '(' && raw[^1] == ')'
            && !raw.AsSpan(1, raw.Length - 2).TrimEnd().EndsWith(",", StringComparison.Ordinal)
            ? StepEntity.ParseArguments(raw[1..^1]) : [];

    private const int MaxBooleanDepth = 64;

    private static bool IsBooleanEntity(StepEntity entity) =>
        entity.Type.Equals("IFCBOOLEANRESULT", StringComparison.OrdinalIgnoreCase)
        || entity.Type.Equals("IFCBOOLEANCLIPPINGRESULT", StringComparison.OrdinalIgnoreCase);

    private static IfcBooleanResult ParseBooleanResult(GeometryReadContext ctx, StepEntity itemEnt, HashSet<int> active)
    {
        if (active.Contains(itemEnt.Id))
            throw new InvalidDataException($"Boolean operand cycle at #{itemEnt.Id}.");
        if (active.Count >= MaxBooleanDepth)
            throw new InvalidDataException($"Boolean operands at #{itemEnt.Id} exceed the depth limit ({MaxBooleanDepth}).");
        ctx.ChargeBooleanNode(itemEnt.Id);
        active.Add(itemEnt.Id);

        var step = ctx.Step;
        var op = itemEnt.GetRaw(0)?.ToUpperInvariant() switch
        {
            ".DIFFERENCE." => IfcBooleanOperator.Difference,
            ".UNION." => IfcBooleanOperator.Union,
            ".INTERSECTION." => IfcBooleanOperator.Intersection,
            _ => throw new InvalidDataException($"Boolean #{itemEnt.Id} has an invalid operator."),
        };

        int? firstOpId = itemEnt.GetRef(1);
        int? secondOpId = itemEnt.GetRef(2);

        var boolResult = new IfcBooleanResult { Operator = op };

        if (step.Find(firstOpId) is { } firstEnt)
        {
            if (IsBooleanEntity(firstEnt))
            {
                boolResult.FirstBoolean = ParseBooleanResult(ctx, firstEnt, active);
            }
            else
            {
                var sweptMesh = ParseSweptSolid(step, firstEnt, "BooleanOperand", ctx.RadiansPerAngleUnit,
                    out var extruded, out var revolved);
                boolResult.FirstSolid = extruded;
                boolResult.FirstRevolved = revolved;
                if (extruded is null && revolved is null)
                    boolResult.FirstMesh = sweptMesh ?? ParseMeshEntity(ctx, firstEnt);
                ctx.ChargeVertices((sweptMesh ?? boolResult.FirstMesh)?.Vertices.Count ?? 0);
            }
        }

        if (step.Find(secondOpId) is { } secondEnt)
        {
            if (IsBooleanEntity(secondEnt))
            {
                boolResult.SecondBoolean = ParseBooleanResult(ctx, secondEnt, active);
            }
            else if (secondEnt.Type.Equals("IFCHALFSPACESOLID", StringComparison.OrdinalIgnoreCase))
            {
                boolResult.SecondHalfSpace = ParseHalfSpaceSolid(step, secondEnt.Id);
            }
            else
            {
                var sweptMesh = ParseSweptSolid(step, secondEnt, "BooleanOperand", ctx.RadiansPerAngleUnit,
                    out var extruded, out var revolved);
                boolResult.SecondSolid = extruded;
                boolResult.SecondRevolved = revolved;
                if (extruded is null)
                    boolResult.SecondMesh = sweptMesh ?? ParseMeshEntity(ctx, secondEnt);
                ctx.ChargeVertices((sweptMesh ?? boolResult.SecondMesh)?.Vertices.Count ?? 0);
            }
        }

        if (boolResult.FirstSolid is null && boolResult.FirstRevolved is null && boolResult.FirstMesh is null && boolResult.FirstBoolean is null)
            WarnSkipped("first", firstOpId);
        if (boolResult.SecondHalfSpace is null && boolResult.SecondSolid is null && boolResult.SecondRevolved is null
            && boolResult.SecondMesh is null && boolResult.SecondBoolean is null)
            WarnSkipped("second", secondOpId);
        else if (boolResult.SecondHalfSpace is null || op == IfcBooleanOperator.Union)
            ctx.BooleanWarnings.Add($"Boolean #{itemEnt.Id}: {op} with this second operand is not evaluated in the mesh.");

        active.Remove(itemEnt.Id);
        return boolResult;

        void WarnSkipped(string operand, int? id)
        {
            var entity = step.Find(id);
            string description = entity is not null ? $"{entity.Type} #{entity.Id}" : id.HasValue ? $"missing entity #{id}" : "missing reference";
            ctx.BooleanWarnings.Add($"Boolean #{itemEnt.Id}: skipped {operand} operand ({description}).");
        }
    }

    private static void PlaceBooleanOperands(IfcBooleanResult result, IfcTransform placement)
    {
        // The exporter writes identity product placements, so both operands need the same world transform.
        if (placement.IsTranslationOnly)
        {
            if (result.FirstSolid is { } first) first.Position += placement.Origin;
            if (result.SecondSolid is { } second) second.Position += placement.Origin;
            if (result.FirstRevolved is { } revolved)
            {
                revolved.Position += placement.Origin;
                revolved.Axis.Location += placement.Origin;
            }
            if (result.SecondRevolved is { } secondRevolved)
            {
                secondRevolved.Position += placement.Origin;
                secondRevolved.Axis.Location += placement.Origin;
            }
        }
        else
        {
            result.FirstMesh ??= result.FirstSolid?.ToMesh() ?? result.FirstRevolved?.ToMesh();
            result.SecondMesh ??= result.SecondSolid?.ToMesh() ?? result.SecondRevolved?.ToMesh();
            result.FirstSolid = null;
            result.FirstRevolved = null;
            result.SecondSolid = null;
            result.SecondRevolved = null;
        }
        if (result.FirstMesh is { } firstMesh) result.FirstMesh = placement.ApplyTo(firstMesh);
        if (result.SecondMesh is { } secondMesh) result.SecondMesh = placement.ApplyTo(secondMesh);

        if (result.SecondHalfSpace?.BaseSurface is { } plane)
            result.SecondHalfSpace.BaseSurface = new IfcPlane(placement.Apply(plane.Location), placement.ApplyVector(plane.Normal));
        if (result.FirstBoolean is { } firstBoolean) PlaceBooleanOperands(firstBoolean, placement);
        if (result.SecondBoolean is { } secondBoolean) PlaceBooleanOperands(secondBoolean, placement);
    }

    private static IfcPlane? ParsePlane(StepFile step, int? planeId)
    {
        var planeEnt = step.Find(planeId);
        if (planeEnt is null || !planeEnt.Type.Equals("IFCPLANE", StringComparison.OrdinalIgnoreCase)
            || step.Find(planeEnt.GetRef(0)) is not { } placementEntity
            || !placementEntity.Type.Equals("IFCAXIS2PLACEMENT3D", StringComparison.OrdinalIgnoreCase)
            || step.Find(placementEntity.GetRef(0)) is not { } point
            || !point.Type.Equals("IFCCARTESIANPOINT", StringComparison.OrdinalIgnoreCase)) return null;

        var placement = ParseAxis2Placement(step, placementEntity.Id);
        return new IfcPlane(placement.Origin, placement.Z);
    }

    private static IfcHalfSpaceSolid? ParseHalfSpaceSolid(StepFile step, int? halfSpaceId)
    {
        if (!halfSpaceId.HasValue) return null;
        var hsEnt = step.Find(halfSpaceId.Value);
        if (hsEnt is null) return null;

        var plane = ParsePlane(step, hsEnt.GetRef(0));
        if (plane is null) return null;
        return hsEnt.GetRaw(1)?.ToUpperInvariant() switch
        {
            ".T." => new IfcHalfSpaceSolid(plane, agreementFlag: true),
            ".F." => new IfcHalfSpaceSolid(plane, agreementFlag: false),
            _ => null,
        };
    }

    private static IfcProfileDef? ParseProfile(StepFile step, int? profileId, out IfcTransform rotation)
    {
        rotation = IfcTransform.Identity;
        var profEnt = step.Find(profileId);
        if (profEnt is null) return null;

        string pName = profEnt.GetString(1) ?? "Profile";
        if (profEnt.Type.Equals("IFCARBITRARYCLOSEDPROFILEDEF", StringComparison.OrdinalIgnoreCase))
        {
            var pts = ParsePolylinePoints(step, profEnt.GetRef(2));
            return new IfcArbitraryClosedProfileDef(pts, pName);
        }

        var placement = ParseAxis2Placement(step, profEnt.GetRef(2));
        var pos2d = placement.Origin;
        // The parameterized profile already includes its offset. Rotate about that offset, not about (0,0).
        rotation = placement.Multiply(IfcTransform.Translation(-pos2d));
        if (profEnt.Type.Equals("IFCRECTANGLEPROFILEDEF", StringComparison.OrdinalIgnoreCase))
        {
            double xDim = profEnt.GetReal(3) ?? 0.0;
            double yDim = profEnt.GetReal(4) ?? 0.0;
            return new IfcRectangleProfileDef(xDim, yDim, pos2d, pName);
        }

        if (profEnt.Type.Equals("IFCCIRCLEPROFILEDEF", StringComparison.OrdinalIgnoreCase))
        {
            double radius = profEnt.GetReal(3) ?? 0.0;
            return new IfcCircleProfileDef(radius, pos2d, pName);
        }

        if (profEnt.Type.Equals("IFCISHAPEPROFILEDEF", StringComparison.OrdinalIgnoreCase))
        {
            double overallWidth = profEnt.GetReal(3) ?? 0.0;
            double overallDepth = profEnt.GetReal(4) ?? 0.0;
            double webThickness = profEnt.GetReal(5) ?? 0.0;
            double flangeThickness = profEnt.GetReal(6) ?? 0.0;
            double? filletRadius = profEnt.GetReal(7);
            return new IfcIShapeProfileDef(overallWidth, overallDepth, webThickness, flangeThickness, filletRadius, pos2d, pName);
        }

        if (profEnt.Type.Equals("IFCLSHAPEPROFILEDEF", StringComparison.OrdinalIgnoreCase))
        {
            double depth = profEnt.GetReal(3) ?? 0.0;
            double width = profEnt.GetReal(4) ?? depth;
            double thickness = profEnt.GetReal(5) ?? 0.0;
            double? filletRadius = profEnt.GetReal(6);
            return new IfcLShapeProfileDef(depth, width, thickness, filletRadius, pos2d, pName);
        }

        if (profEnt.Type.Equals("IFCUSHAPEPROFILEDEF", StringComparison.OrdinalIgnoreCase))
        {
            double depth = profEnt.GetReal(3) ?? 0.0;
            double flangeWidth = profEnt.GetReal(4) ?? 0.0;
            double webThickness = profEnt.GetReal(5) ?? 0.0;
            double flangeThickness = profEnt.GetReal(6) ?? 0.0;
            double? filletRadius = profEnt.GetReal(7);
            return new IfcUShapeProfileDef(depth, flangeWidth, webThickness, flangeThickness, filletRadius, pos2d, pName);
        }

        return null;
    }

    private static IfcAxis1Placement ParseAxis1Placement(StepFile step, int? axisPlacementId)
    {
        if (!axisPlacementId.HasValue) return new IfcAxis1Placement();
        var ent = step.Find(axisPlacementId.Value);
        if (ent is null) return new IfcAxis1Placement();

        int? locId = ent.GetRef(0);
        int? dirId = ent.GetRef(1);

        var loc = ParseCoordinates(step.Find(locId));
        var dir = ParseOptionalDirection(step, dirId) ?? IfcVector3.UnitZ;
        return new IfcAxis1Placement(loc, dir);
    }

    private static List<IfcVector3> ParsePolylinePoints(StepFile step, int? curveId)
    {
        var result = new List<IfcVector3>();
        if (!curveId.HasValue) return result;
        var curveEnt = step.Find(curveId.Value);
        if (curveEnt is null) return result;

        var ptIds = curveEnt.GetRefList(0);
        foreach (int ptId in ptIds)
        {
            var ptEnt = step.Find(ptId);
            if (ptEnt is null) continue;
            string coordStr = ptEnt.GetRaw(0) ?? string.Empty;
            var coords = StepEntity.ParseArguments(coordStr.Trim('(', ')'));
            double x = 0.0, y = 0.0, z = 0.0;
            if (coords.Count > 0 && double.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx)) x = cx;
            if (coords.Count > 1 && double.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy)) y = cy;
            if (coords.Count > 2 && double.TryParse(coords[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cz)) z = cz;
            result.Add(new IfcVector3(x, y, z));
        }
        return result;
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

    // Preserve metadata-only imports for the original set of supported occurrences.
    private static readonly HashSet<string> ProductsImportedWithoutBody = new(StringComparer.OrdinalIgnoreCase)
    {
        "IFCWALL", "IFCWALLSTANDARDCASE", "IFCWALLELEMENTEDCASE",
        "IFCSLAB", "IFCSLABSTANDARDCASE", "IFCSLABELEMENTEDCASE",
        "IFCCOLUMN", "IFCCOLUMNSTANDARDCASE", "IFCBEAM", "IFCBEAMSTANDARDCASE",
        "IFCWINDOW", "IFCWINDOWSTANDARDCASE", "IFCDOOR", "IFCDOORSTANDARDCASE",
        "IFCFOOTING", "IFCREINFORCINGBAR", "IFCSTAIR", "IFCSTAIRFLIGHT", "IFCRAILING", "IFCROOF",
        "IFCMEMBER", "IFCMEMBERSTANDARDCASE", "IFCPLATE", "IFCPLATESTANDARDCASE",
        "IFCCOVERING", "IFCFURNISHINGELEMENT", "IFCOPENINGELEMENT", "IFCSPACE", "IFCBUILDINGELEMENTPROXY",
        // Generic IFC2x3 flow classes and the supported typed IFC4 distribution occurrences.
        "IFCFLOWSEGMENT", "IFCFLOWFITTING", "IFCFLOWTERMINAL", "IFCFLOWCONTROLLER",
        "IFCFLOWMOVINGDEVICE", "IFCFLOWSTORAGEDEVICE", "IFCFLOWTREATMENTDEVICE", "IFCENERGYCONVERSIONDEVICE",
        "IFCDISTRIBUTIONCHAMBERELEMENT", "IFCDISTRIBUTIONCONTROLELEMENT",
        "IFCPIPESEGMENT", "IFCDUCTSEGMENT", "IFCPIPEFITTING", "IFCDUCTFITTING",
    };

    private readonly record struct ProductImportInfo(int TagIndex, bool IsOpening, bool NeedsBody);

    private static void InheritAggregateStoreys(
        StepFile step, Dictionary<int, IfcBuildingStorey> storeys, Dictionary<int, IfcBuildingStorey> assignments)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var rel in step.IndexedOfType("IFCRELAGGREGATES"))
        {
            if (rel.GetRef(4) is not int parent) continue;
            if (!children.TryGetValue(parent, out var group)) children[parent] = group = [];
            group.AddRange(rel.GetRefList(5));
        }
        if (children.Count == 0) return;

        var pending = new Queue<int>();
        foreach (int id in assignments.Keys)
            if (children.ContainsKey(id)) pending.Enqueue(id);
        foreach (var (id, storey) in storeys)
            if (children.ContainsKey(id) && assignments.TryAdd(id, storey)) pending.Enqueue(id);

        while (pending.TryDequeue(out int parent))
        {
            var storey = assignments[parent];
            foreach (int child in children[parent])
                if (assignments.TryAdd(child, storey) && children.ContainsKey(child)) pending.Enqueue(child);
        }
    }

    private static HashSet<int> FindVoidHosts(StepFile step, Dictionary<string, ProductImportInfo> productTypes)
    {
        var hosts = new HashSet<int>();
        foreach (var relation in step.IndexedOfType("IFCRELVOIDSELEMENT"))
        {
            if (step.Find(relation.GetRef(4)) is { } host
                && productTypes.TryGetValue(host.Type, out var hostType) && hostType.TagIndex == 7
                && step.Find(relation.GetRef(5)) is { } opening
                && productTypes.TryGetValue(opening.Type, out var occurrence) && occurrence.IsOpening
                && (!occurrence.NeedsBody || HasBodyRepresentation(step, opening.GetRef(6))))
                hosts.Add(host.Id);
        }
        return hosts;
    }

    private static Dictionary<string, ProductImportInfo> GetProductImportTypes(StepFile step, IfcSchema schema)
    {
        var productTypes = new Dictionary<string, ProductImportInfo>(StringComparer.OrdinalIgnoreCase);
        // Classify each STEP type once, then visit the entities in their original order.
        foreach (string type in step.IndexedTypeNames)
        {
            if (ProductsImportedWithoutBody.Contains(type))
                productTypes.Add(type, new(7, type.Equals("IFCOPENINGELEMENT", StringComparison.OrdinalIgnoreCase), NeedsBody: false));
            else if (IfcProductOccurrences.TryGetInfo(schema, type, out int tagIndex, out bool subtracts))
                productTypes.Add(type, new(tagIndex, subtracts, NeedsBody: true));
        }
        return productTypes;
    }

    private static bool HasBodyRepresentation(StepFile step, int? shapeId)
    {
        if (step.Find(shapeId) is not { } shape
            || !shape.Type.Equals("IFCPRODUCTDEFINITIONSHAPE", StringComparison.OrdinalIgnoreCase)) return false;

        foreach (int repId in shape.GetRefList(2))
        {
            if (step.Find(repId) is { } rep
                && rep.Type.Equals("IFCSHAPEREPRESENTATION", StringComparison.OrdinalIgnoreCase)
                && string.Equals(rep.GetString(1), "Body", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int WriteMeshTriangulatedFaceSet(StepWriter w, IReadOnlyList<IfcVector3> vertices, int[] indices, bool closed)
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
        int triCount = indices.Length / 3;
        for (int i = 0; i < triCount; i++)
        {
            if (i > 0) coordIndex.Append(',');
            coordIndex.Append('(')
                      .Append(indices[i * 3] + 1).Append(',')
                      .Append(indices[i * 3 + 1] + 1).Append(',')
                      .Append(indices[i * 3 + 2] + 1).Append(')');
        }

        return w.Add("IFCTRIANGULATEDFACESET", $"{StepWriter.Ref(pointListId)},$,{(closed ? ".T." : ".F.")},({coordIndex}),$");
    }

    private static int WriteMeshShell(StepWriter w, IReadOnlyList<IfcVector3> vertices, List<int> indices, bool closed)
    {
        var pointIds = new Dictionary<int, int>();
        int PointId(int index)
        {
            if (pointIds.TryGetValue(index, out int id)) return id;
            var pt = vertices[index];
            return pointIds[index] = w.Add("IFCCARTESIANPOINT",
                StepWriter.List(StepWriter.Real(pt.X), StepWriter.Real(pt.Y), StepWriter.Real(pt.Z)));
        }

        var faceIds = new List<int>(indices.Count / 3);
        for (int i = 0; i <= indices.Count - 3; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            int loopId = w.Add("IFCPOLYLOOP",
                StepWriter.List(StepWriter.Ref(PointId(i0)), StepWriter.Ref(PointId(i1)), StepWriter.Ref(PointId(i2))));
            int boundId = w.Add("IFCFACEOUTERBOUND", $"{StepWriter.Ref(loopId)},.T.");
            faceIds.Add(w.Add("IFCFACE", StepWriter.List(StepWriter.Ref(boundId))));
        }

        return w.Add(closed ? "IFCCLOSEDSHELL" : "IFCOPENSHELL", StepWriter.List(faceIds.Select(StepWriter.Ref)));
    }

    private readonly record struct SurfaceStyleKey(string Name, double R, double G, double B, double Alpha);

    private int GetOrCreateSurfaceStyle(StepWriter w, IfcMaterial mat, Dictionary<SurfaceStyleKey, int> cache)
    {
        var key = new SurfaceStyleKey(mat.Name, mat.R, mat.G, mat.B, mat.Alpha);
        if (cache.TryGetValue(key, out int existingId))
        {
            return existingId;
        }

        int colorId = w.Add("IFCCOLOURRGB",
            $"$,{StepWriter.Real(mat.R)},{StepWriter.Real(mat.G)},{StepWriter.Real(mat.B)}");
        string colour = StepWriter.Ref(colorId);
        int shadingId = mat.Alpha == 1.0
            ? w.Add("IFCSURFACESTYLESHADING", Schema == IfcSchema.Ifc2x3 ? colour : $"{colour},$")
            : w.Add("IFCSURFACESTYLERENDERING", $"{colour},{StepWriter.Real(1.0 - mat.Alpha)},$,$,$,$,$,$,.NOTDEFINED.");
        int styleId = w.Add("IFCSURFACESTYLE",
            $"{StepWriter.Str(mat.Name)},.BOTH.,({StepWriter.Ref(shadingId)})");

        if (Schema == IfcSchema.Ifc2x3)
            styleId = w.Add("IFCPRESENTATIONSTYLEASSIGNMENT", $"({StepWriter.Ref(styleId)})");
        cache[key] = styleId;
        return styleId;
    }

    private int AddIfcProduct(
        StepWriter w,
        string ifcType,
        string guid,
        int ownerHistoryId,
        string name,
        int placementId,
        int shapeId,
        string tag)
    {
        string shapeRef = shapeId > 0 ? StepWriter.Ref(shapeId) : StepWriter.Null;
        string commonArgs = $"{StepWriter.Str(guid)},{StepWriter.Ref(ownerHistoryId)},{StepWriter.Str(name)},$,$,{StepWriter.Ref(placementId)},{shapeRef},{StepWriter.Str(tag)}";

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
            "IFCOPENINGELEMENT" => w.Add("IFCOPENINGELEMENT", Schema == IfcSchema.Ifc4 ? $"{commonArgs},.OPENING." : commonArgs),
            _ => w.Add("IFCBUILDINGELEMENTPROXY", Schema == IfcSchema.Ifc4
                ? $"{commonArgs},.NOTDEFINED."
                : $"{commonArgs},.ELEMENT."),
        };
    }

    public static string ClassifyElement(string name, string? materialName = null)
    {
        string target = $"{name} {materialName}";

        if (ContainsAny(target, "проем", "отверстие", "opening", "cutout", "void"))
            return "IFCOPENINGELEMENT";
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
