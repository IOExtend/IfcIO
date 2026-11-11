<p align="center">
  <img src="docs/images/ifcio-banner.png" alt="IfcIO" width="640">
</p>

# IfcIO

[![Docs](https://img.shields.io/badge/Docs-Online-A855F7?style=flat-square&logo=github)](https://ioextend.github.io/IfcIO/)
[![NuGet](https://img.shields.io/nuget/v/IfcIO?style=flat-square&color=A855F7&logo=nuget)](https://www.nuget.org/packages/IfcIO)
[![Release](https://img.shields.io/github/v/release/IOExtend/IfcIO?style=flat-square&color=A855F7)](https://github.com/IOExtend/IfcIO/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

**IfcIO** is a fast, 100% managed .NET library for building, reading, and exporting **Industry Foundation Classes** (`.ifc`) files compliant with **IFC2x3 Coordination View V2.0** and **IFC4 Design Transfer / Reference View** without third-party dependencies or native code.

Built clean-room under the [IOExtend](https://github.com/IOExtend) organization as the shared open-standards IFC foundation for AEC/BIM projects (including [NwcIO](https://github.com/IOExtend/NwcIO) and [RvtIO](https://github.com/IOExtend/RvtIO)).

---

## Features

- **Full High-Level Model Ingestion (`IfcModel.Open`)**:
  - Reconstructs complete spatial hierarchy (Project, Site, Building, Storeys), elements, properties, materials, and 3D meshes from any `.ifc` or `.ifczip` file.
  - Multi-line STEP statement accumulation and quote-aware tokenization in `StepReader`.
- **IFC4 Modern Tessellation (`IfcTriangulatedFaceSet`)**:
  - Encodes geometry using compact `IfcTriangulatedFaceSet` and `IfcCartesianPointList3D` under representation type `'Tessellation'` for IFC4 exports (>60% smaller files).
- **Direct 3D Exporters (glTF 2.0 & Wavefront OBJ)**:
  - Instant conversion of any IFC model to binary glTF 2.0 (`.glb`) with PBR metallic-roughness materials and vertex normals, or Wavefront OBJ (`.obj` + `.mtl`).
- **PKZip Compressed IFC (`.ifczip`)**:
  - Built-in transparent compression and decompression for standard `.ifczip` archives with zero native dependencies.
- **Cross-Platform CLI Tool (`ifcio`)**:
  - Global .NET CLI tool (`IfcIO.Cli` on NuGet) providing `info`, `export`, `pack`, `unpack`, and `validate` commands.
- **buildingSMART 22-Character GlobalId (`IfcGuid`)**:
  - Standard base64-like buildingSMART encoding (`0-9, A-Z, a-z, _, $`).
  - Bidirectional conversion between 128-bit `.NET Guid` and 22-character IFC GlobalId.
  - Deterministic GUID generation via SHA-256 namespace hashing for elements and models.
- **3D Faceted BRep Geometry (`IfcFacetedBrep`)**:
  - Encodes 3D meshes as `IfcFacetedBrep` &rarr; `IfcClosedShell` &rarr; `IfcFace` &rarr; `IfcFaceOuterBound` &rarr; `IfcPolyLoop` &rarr; `IfcCartesianPoint`.
  - Vertex deduplication via shared per-mesh `IfcCartesianPoint` coordinates.
- **BIM Spatial Containment**:
  - Standard spatial aggregation: `IfcProject` &rarr; `IfcSite` &rarr; `IfcBuilding` &rarr; `IfcBuildingStorey` via `IfcRelAggregates`.
  - Elements assigned to storeys via `IfcRelContainedInSpatialStructure`.
- **Semantic Classification & Properties**:
  - Automatic classification for standard BIM entities: `IfcWall`, `IfcSlab`, `IfcColumn`, `IfcBeam`, `IfcReinforcingBar`, `IfcWindow`, `IfcDoor`, `IfcFooting`, `IfcStair`, `IfcRailing`, `IfcRoof`, `IfcPipeSegment`, `IfcDuctSegment`, `IfcBuildingElementProxy`.
  - Property sets (`IfcPropertySet`, `IfcPropertySingleValue`) with typed values (`IfcText`, `IfcInteger`, `IfcReal`, `IfcBoolean`).
  - Reusable Type Products (`IfcTypeProduct`) linked via `IfcRelDefinesByType`.
  - Surface styles and materials (`IfcSurfaceStyle`, `IfcSurfaceStyleShading`, `IfcColourRgb`, `IfcStyledItem`).
- **Constructive Solid Geometry (CSG) & Boolean Clipping (`IfcBooleanResult`)**:
  - Full parametric plane clipping (`IFCBOOLEANCLIPPINGRESULT`, `IFCHALFSPACESOLID`, `IFCPLANE`) with exact 3D polygon mesh evaluation (`ToMesh()`).
- **Standard Structural Steel Profiles (`IfcProfileDef`)**:
  - Parameterized cross-sections for structural framing: `IfcIShapeProfileDef` (I-beam/H-beam), `IfcLShapeProfileDef` (angle), and `IfcUShapeProfileDef` (channel) with boundary points and solid extrusion.
- **Topological MEP Ports & Flow Networks (`IfcDistributionPort`)**:
  - Strongly-typed ports with `IfcFlowDirection`, `IFCRELCONNECTSPORTTOELEMENT`, `IFCRELCONNECTSPORTS`, and BFS network tracing (`TraceConnectedElements`).
- **Multi-layer Material Systems & Profile Sets (`IfcIO.Model`)**:
  - Composite layer assemblies (`IfcMaterialLayer`, `IfcMaterialLayerSet`, `IfcMaterialLayerSetUsage`) and structural profile sets (`IfcMaterialProfile`, `IfcMaterialProfileSet`, `IfcMaterialProfileSetUsage`) linked via `IFCRELASSOCIATESMATERIAL`.
- **Architectural & MEP Spaces (`IfcSpace`) & BCF 2.1 Coordination**:
  - High-level spaces, space boundaries (`IfcSpaceBoundary`), MEP systems (`IfcSystem`), and full BIM Collaboration Format 2.1 zip read/write (`IfcIO.Bcf`).
- **IfcOpenShell-Style Query Selector Engine (`IfcSelector`)**:
  - Declarative filtering syntax for entities, spatial storeys, property/attribute constraints (`[LoadBearing=TRUE]`, `[FireRating>=60]`, `[Name*='Conc']`), and union lists (`IfcWall, IfcColumn`).
- **Model Repair, Cleanup & Recipes (`IfcPatch`)**:
  - `Purge`: removes empty/duplicate property sets and empty storeys.
  - `ResetCoordinates`: detects large UTM coordinates (>10,000m) and centers model geometry at origin (0, 0, 0) to prevent precision jitter.
  - `Extract`: extracts element subsets into self-contained models.
  - `Merge`: combines multiple models reconciling storeys and re-indexing IDs.
- **buildingSMART IDS 1.0 Compliance Engine (`IfcIdsValidator`)**:
  - Audits models against Information Delivery Specification (`.ids`) XML files and exports violations to BCF 2.1 archives (`.bcfzip`).
- **Tabular Property CSV Roundtrip Bridge (`IfcCsvBridge`)**:
  - Fast CSV spreadsheet export and bulk property import conforming to `ifccsv`.
- **3D Geometric Clash Detection & Clustering (`IfcClashDetector`, `IfcClashGrouper`)**:
  - Pure managed collision engine executing broad-phase bounding box pruning and narrow-phase Möller triangle-to-triangle intersection testing for cross-model (Structure vs MEP) and intra-model interference analysis conforming to `ifcclash`.
  - Intelligent clustering and grouping algorithms: 3D spatial proximity (Disjoint-Set / Union-Find), primary MEP run / structural element, building storey, and structural grid intersections.
  - Direct export to structured JSON and buildingSMART BCF 2.1 zip archives with focused viewpoints.
- **Spatial Grid System (`IfcGridSystem`, `IfcGridAxis`, `IfcGridIntersection`)**:
  - Full support for straight and multi-axis spatial grids, 2D/3D intersections, nearest grid axis/intersection lookup, signed directional offset formatting (e.g. `"A (+0.50m) / 1 (-0.20m)"`), XML serialization, and automatic extraction from `IFCGRID` entities.
- **3D Geometric Measurement Engine (`IfcMeasureEngine`)**:
  - Point-to-point 3D distance and coordinate projections ($\Delta X, \Delta Y, \Delta Z$, horizontal distance, pitch, azimuth).
  - Point-to-mesh shortest distance using Christer Ericson's Voronoi region algorithm.
  - Mesh-to-mesh clearance measurement, polyline cumulative path length, and triangle calculations (area, normal, closest point).
  - Full mesh surface area, closed volume (tetrahedron divergence theorem), and 3D centroid computation.
- **Enhanced Model Repair & Patching Recipes (`IfcPatch`)**:
  - Conforms to IfcOpenShell `ifcpatch` recipe architecture: `Purge`, `ResetCoordinates` (UTM origin shift), `Extract`, `Merge`, `MigrateSchema` (upgrade IFC2x3 to IFC4), `ScaleModel` (uniform scaling factor), and `SetProperties`.
- **Semantic & Geometric Revision Comparison (`IfcDiffEngine`)**:
  - Full revision comparison between two IFC models detecting added, deleted, and modified elements (3D geometry displacement, property set deltas, quantity variations, and storey re-assignments) conforming to `ifcdiff`. Direct export to JSON and BCF 2.1 archives.
- **Standalone 3D WebGL HTML Viewer (`IfcHtmlViewerExporter`)**:
  - Export any IFC model to an interactive, self-contained single-file 3D HTML viewer (`.html`) with zero external scripts, CDNs, or server dependencies.
  - Includes orbit camera controls, live multi-axis section clipping planes (Cut X, Cut Y, Cut Z), spatial tree navigation, element property inspector, interactive 2-point 3D measurement tool, 2D canvas overlay drawing of architectural grid axes and bubble labels, and interactive 4D timeline simulation playback.
- **4D Construction Sequencing & Temporal Simulation (`IfcWorkSchedule`, `IfcTask`, `IfcTaskTime`, `IFCRELSEQUENCE`)**:
  - Full support for construction work schedules, WBS hierarchies, task durations, milestones, and predecessor/successor relationships (`FinishToStart`, `StartToStart`, `FinishToFinish`, `StartToFinish`).
  - Temporal 4D simulation engine: query active tasks, completed tasks, overall progress ratio, and element states (`Planned`, `InProgress`, `Completed`, `NotStarted`) at any given simulation date.
  - Interactive 4D timeline playback in the standalone HTML viewer (`ifcio view`).
- **5D Cost Planning & Budgeting (`IfcCostSchedule`, `IfcCostItem`, `IfcCostValue`, `IFCAPPLIEDVALUE`)**:
  - Hierarchical Cost Breakdown Structure (CBS), unit rates, quantities, and automatic cost rollup across tree nodes.
  - Elements and physical quantities linked to cost items via `IFCRELASSIGNSTOCONTROL`.
  - CLI command `ifcio cost` with CSV export.
- **Spatial Zones (`IfcZone`, `IFCZONE`)**:
  - Grouping architectural spaces and rooms into thermal, fire safety, acoustic, security, and ventilation zones.
- **Bi-directional BCF 2.1 Archive Container (`BcfArchive`)**:
  - Full in-memory or stream-based container for reading, inspecting, modifying, and writing buildingSMART BCF 2.1 zip archives (`.bcfzip`).
  - High-level queries by element GUID (`FindTopicsByElement`), status (`FindTopicsByStatus`), and snapshot image attachments.
  - CLI command `ifcio bcf` for inspection and topic listing.
- **High-Level IfcOpenShell-Compatible API Facade (`IfcApi`)**:
  - Pythonic, clean API: `IfcApi.Sequence`, `IfcApi.Cost`, `IfcApi.Spatial`, and `IfcApi.Root` for rapid automation.
- **Zero Dependencies**: Pure C# targeting `.NET 8` and `.NET 10`.

---

## Install

Add the package to your .NET project:

```sh
dotnet add package IfcIO
```

---

## Library Usage (C#)

### Creating and Exporting an IFC Model

```csharp
using IfcIO;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;

// Create model (IFC2X3 or IFC4)
var model = IfcModel.Create("Residential Building Block 1", IfcSchema.Ifc2x3);
model.Author = "mahach666";
model.Organization = "IOExtend";

// Add storeys
var floor1 = model.AddStorey("Level 1", elevation: 0.0);
var floor2 = model.AddStorey("Level 2", elevation: 3300.0);

// Materials & colors
var concrete = new IfcMaterial { Name = "Concrete B25", R = 0.72, G = 0.72, B = 0.75 };

// Add 3D polygon mesh element
var vertices = new IfcVector3[]
{
    new(0, 0, 0),
    new(2000, 0, 0),
    new(2000, 200, 0),
    new(0, 200, 0),
    new(0, 0, 2800),
    new(2000, 0, 2800),
    new(2000, 200, 2800),
    new(0, 200, 2800)
};
var indices = new int[]
{
    0, 2, 1, 0, 3, 2, // bottom
    4, 5, 6, 4, 6, 7, // top
    0, 1, 5, 0, 5, 4, // front
    1, 2, 6, 1, 6, 5, // right
    2, 3, 7, 2, 7, 6, // back
    3, 0, 4, 3, 4, 7  // left
};

var wall = model.AddMesh("Стена монолитная 200", vertices, indices, concrete, floor1);

// Add parametric swept solids (walls, columns)
var paramWall = model.AddExtrudedRectangle(
    name: "ExtrudedWall",
    xDim: 300.0,
    yDim: 6000.0,
    depth: 3000.0,
    material: concrete,
    storey: floor1);

// Add opening and window filling
var openingSolid = new IfcExtrudedAreaSolid(
    new IfcRectangleProfileDef(300.0, 1200.0),
    depth: 1500.0,
    position: new IfcVector3(0, 2000, 900));

var window = new IfcElement
{
    Id = 101,
    Name = "Window1",
    IfcType = "IFCWINDOW",
    Solid = new IfcExtrudedAreaSolid(new IfcRectangleProfileDef(100.0, 1180.0), depth: 1480.0),
};

model.AddOpening(paramWall, "WallOpening", solid: openingSolid, filling: window);

// Attach standard buildingSMART Property Sets
paramWall.PropertySets.Add(StandardPsets.WallCommon(
    reference: "W-01",
    loadBearing: true,
    isExternal: true,
    fireRating: "REI 120",
    thermalTransmittance: 0.28));

// Add parametric revolved solids (curved pipes, fittings, flanged connections)
var pipeProfile = new IfcCircleProfileDef(radius: 100.0, position: new IfcVector3(500, 0, 0));
model.AddRevolvedSolid("90DegElbow", pipeProfile, angle: Math.PI * 0.5, storey: floor1, ifcType: "IFCPIPEFITTING");

// Automatic buildingSMART Quantity Takeoff (QTO) calculation directly from 3D geometry
model.CalculateAllBaseQuantities();

// Attach explicit custom quantity sets
paramWall.QuantitySets.Add(StandardQtoSets.WallBaseQuantities(
    length: 6000.0,
    width: 300.0,
    height: 3000.0,
    grossVolume: 5.4,
    netVolume: 4.8));

// Add architectural space (room) with space boundary
var office = model.AddSpace(
    name: "Office 101",
    spaceNumber: "101",
    longName: "Open Plan Office",
    grossFloorArea: 45.0,
    height: 3.0,
    storey: floor1);

office.Boundaries.Add(new IfcSpaceBoundary
{
    Name = "Wall Boundary",
    PhysicalOrVirtual = IfcPhysicalOrVirtual.Physical,
    InternalOrExternal = IfcInternalOrExternal.Internal,
    RelatedBuildingElement = paramWall,
});

// Group elements into HVAC system
var hvac = model.AddSystem("Supply Air AHU-01", isDistribution: true);
hvac.Elements.Add(paramWall);

// Validate model and export detected issues as buildingSMART BCF 2.1 archive
model.ExportValidationBcf("Building_Issues.bcfzip");

// Validate model integrity
var validation = model.Validate();
if (!validation.IsValid)
{
    foreach (var issue in validation.Issues)
        Console.WriteLine($"[{issue.Severity}] {issue.RuleId}: {issue.Message}");
}

// Save to multiple formats with one call:
model.Save("Building.ifc");          // Standard STEP file (IFCEXTRUDEDAREASOLID, IFCRELVOIDSELEMENT)
model.SaveZip("Building.ifczip");    // Compressed PKZip archive
model.SaveGlb("Building.glb");       // Binary glTF 2.0 with PBR materials
model.SaveObj("Building.obj");       // Wavefront OBJ + MTL
```

### Loading and Inspecting an IFC Model

```csharp
using IfcIO;

// Load an existing .ifc or .ifczip model
var model = IfcModel.Open("Building.ifc");

Console.WriteLine($"Project:  {model.ProjectName}");
Console.WriteLine($"Schema:   {model.Schema}");
Console.WriteLine($"Storeys:  {model.Storeys.Count}");
Console.WriteLine($"Elements: {model.AllElements.Count()}");

// Convert directly to glTF 2.0
model.SaveGlb("Building_Converted.glb");
```

### Querying Elements (`IfcSelector`)

Filter elements using IfcOpenShell-compatible selector syntax:

```csharp
// Select load-bearing walls on Level 1
var walls = model.Select("IfcWall#Level 1[LoadBearing=TRUE]");

// Select high-fire-rating elements across multiple types
var fireResistant = model.Select("IfcWall, IfcColumn[FireRating>=60]");

// Check if a single element matches a query condition
bool isCandidate = element.Matches("[Name*='Concrete']");
```

### Model Cleanup, Coordinate Shifting & Merging (`IfcPatch`)

```csharp
// 1. Purge unreferenced and empty property sets and empty storeys
var purgeReport = model.Purge();

// 2. Center models located at large absolute UTM coordinates (>10,000 m)
if (model.ResetCoordinates(out var offsetVector))
{
    Console.WriteLine($"Recentered model to origin (offset applied: {offsetVector})");
}

// 3. Extract a standalone model containing only structural elements
var structureModel = model.Extract("IfcWall, IfcColumn, IfcBeam", "Structural Package");
structureModel.Save("Structure_Only.ifc");

// 4. Merge multiple disciplinary models into a coordinated model
var merged = IfcPatch.Merge([archModel, structModel, mepModel], "Campus Combined");
merged.Save("Campus_Coordinated.ifc");
```

### Information Delivery Specification (IDS 1.0) & BCF Export

```csharp
// Audit IFC model against buildingSMART IDS 1.0 specification
var idsReport = model.ValidateIds("Requirements.ids");

if (!idsReport.IsCompliant)
{
    Console.WriteLine($"Audit failed with {idsReport.Failures.Count} violations.");

    // Export non-compliant elements directly to BCF 2.1 for issue tracking
    idsReport.SaveBcf("IDS_Compliance_Issues.bcfzip");
}
```

### Tabular CSV Property Schedules (`IfcCsvBridge`)

```csharp
// Export elements and custom property sets to CSV spreadsheet
model.ExportCsv("Schedules.csv", ["Pset_WallCommon.FireRating", "LoadBearing", "Material"]);

// Re-import modified properties back into matching elements
var (elementsUpdated, propertiesUpdated) = model.ImportCsv("Schedules_Edited.csv");
model.Save("Building_Updated.ifc");
```

### 3D Geometric Clash Detection (`IfcClashDetector`)

```csharp
using IfcIO.Clash;

// Run cross-model clash detection between architectural and MEP models
var clashReport = archModel.DetectClashes(mepModel, new IfcClashOptions
{
    Tolerance = 0.002, // 2mm penetration tolerance
    SelectorA = "IfcWall, IfcColumn, IfcBeam",
    SelectorB = "IfcFlowSegment, IfcFlowFitting",
});

Console.WriteLine($"Detected {clashReport.TotalClashesCount} clashes.");

// Save detailed JSON report
clashReport.SaveJson("Clashes.json");

// Export directly to buildingSMART BCF 2.1 zip archive for Revit / Solibri / BIMcollab
clashReport.SaveBcf("Clashes.bcfzip");
```

### Semantic & Geometric Revision Diff (`IfcDiffEngine`)

```csharp
using IfcIO.Diff;

// Compare baseline model against revision
var diffReport = baselineModel.Compare(revisionModel, new IfcDiffOptions
{
    GeometricTolerance = 0.005, // 5mm displacement threshold
    CompareProperties = true,
    CompareQuantities = true,
});

Console.WriteLine($"Total Changes: {diffReport.TotalChangesCount}");
Console.WriteLine($" - Added:    {diffReport.Added.Count}");
Console.WriteLine($" - Deleted:  {diffReport.Deleted.Count}");
Console.WriteLine($" - Modified: {diffReport.Modified.Count}");

// Export diff report to JSON
diffReport.SaveJson("ModelDiff.json");

// Export changes to BCF 2.1 with camera viewpoints framing each modified element
diffReport.SaveBcf("RevisionChanges.bcfzip");
```

### Spatial Grid System (`IfcGridSystem`)

```csharp
using IfcIO.Grids;

// Define or extract building grid lines
var grids = new IfcGridSystem("Campus Grids");
grids.AddAxis("A", new IfcVector3(0, -20, 0), new IfcVector3(0, 20, 0));
grids.AddAxis("B", new IfcVector3(6, -20, 0), new IfcVector3(6, 20, 0));
grids.AddAxis("1", new IfcVector3(-10, 0, 0), new IfcVector3(20, 0, 0));
grids.AddAxis("2", new IfcVector3(-10, 8, 0), new IfcVector3(20, 8, 0));

// Find nearest intersection and format human-readable offset location
string location = grids.FormatLocation(new IfcVector3(0.5, -0.2, 0.0));
// => "A (+0.50m) / 1 (-0.20m)"

// Assign grid locations across all model elements and clashes
model.AssignGridLocations(grids);
clashReport.AssignGridLocations(grids);

// Save and load XML grid definitions
grids.SaveXml("CampusGrids.xml");
var loadedGrids = IfcGridSystem.LoadXml("CampusGrids.xml");
```

### 3D Geometric Measurement Engine (`IfcMeasureEngine`)

```csharp
using IfcIO.Measure;

// Point-to-point 3D measurement with coordinate projections
var dist = IfcMeasureEngine.MeasureDistance(new IfcVector3(0, 0, 0), new IfcVector3(3, 4, 12));
Console.WriteLine($"Distance: {dist.Distance:F2} m, Horiz: {dist.HorizontalDistance:F2} m, Pitch: {dist.PitchDegrees:F1}°");

// Point-to-mesh shortest distance using Ericson Voronoi regions
var pointToMesh = IfcMeasureEngine.MeasurePointToMesh(queryPoint, wallMesh);
Console.WriteLine($"Shortest clearance: {pointToMesh.Distance:F3} m to point {pointToMesh.ClosestPoint}");

// Mesh-to-mesh clearance
var clearance = IfcMeasureEngine.MeasureMeshToMesh(ductMesh, beamMesh);
Console.WriteLine($"Clearance between duct and beam: {clearance.Distance:F3} m");

// Surface area, volume (tetrahedron divergence), and centroid
double area = IfcMeasureEngine.ComputeMeshSurfaceArea(mesh);
double volume = IfcMeasureEngine.ComputeMeshVolume(mesh);
var centroid = IfcMeasureEngine.ComputeMeshCentroid(mesh);
```

### Intelligent Clash Grouping & Clustering (`IfcClashGrouper`)

```csharp
using IfcIO.Clash;

// Detect clashes
var report = archModel.DetectClashes(mepModel, options);
report.AssignGridLocations(grids);

// Group clashes by spatial 3D proximity using Union-Find clustering
report.GroupByProximity(clusterDistance: 1.0, minGroupSize: 2);

// Or group clashes by primary MEP run or structural element
// report.GroupByPrimaryElement();

// Or group by structural grid location (e.g. "A / 1", "B / 2")
// report.GroupByGrid();

// Export grouped BCF 2.1 zip archive with focused perspective viewpoints
report.SaveBcf("GroupedClashes.bcfzip");
```

### Advanced Model Patching Recipes (`IfcPatch`)

```csharp
using IfcIO.Patch;
using IfcIO.Schema;

// Upgrade model schema to IFC4 and enable modern tessellation
IfcPatch.MigrateSchema(model, IfcSchema.Ifc4);

// Uniformly scale model geometry (e.g. convert mm to meters)
IfcPatch.ScaleModel(model, 0.001);

// Batch-set properties across elements matching an IfcSelector query
IfcPatch.SetProperties(model, "IfcWall[LoadBearing=TRUE]", "Pset_WallCommon", new Dictionary<string, string>
{
    ["FireRating"] = "120",
    ["Combustible"] = "FALSE"
});

// Purge empty property sets, quantity sets, and empty storeys
var stats = model.Purge();

// Shift large UTM coordinates to local origin (0, 0)
model.ResetCoordinates(out var offset);
```

### Standalone 3D WebGL HTML Viewer Export

```csharp
using IfcIO.Exporters;

// Export IFC model to an interactive, zero-dependency 3D HTML viewer with 3D measure & grid overlay
model.SaveHtmlViewer("BuildingModel.html", new IfcHtmlViewerOptions
{
    Title = "Residential Complex - Coordination Viewer",
    IncludeProperties = true,
    IncludeQuantities = true,
    IncludeSpatialTree = true,
    IncludeSectioning = true,
    IncludeMeasure = true,
    IncludeGrids = true,
});
```

### 4D Construction Sequencing & Simulation

```csharp
using IfcIO.Model.Sequence;

// Create 4D schedule with tasks, WBS, and element assignments
var schedule = model.AddWorkSchedule("Tower Schedule");
var task = schedule.AddTask("Construct Walls", "WBS-1.1", new DateTime(2026, 2, 1), new DateTime(2026, 3, 1));
task.AssignElement(wall);

// Query temporal state during construction
var activeTasks = schedule.GetActiveTasksAt(new DateTime(2026, 2, 15));
var state = schedule.GetElementStateAt(wall, new DateTime(2026, 2, 15)); // => IfcElementSimulationState.InProgress
```

### 5D Cost Planning & CBS Budgeting

```csharp
using IfcIO.Model.Cost;

// Create 5D cost schedule with CBS items, unit rates, and quantities
var budget = model.AddCostSchedule("Master Budget", "USD", IfcCostScheduleType.Budget);
var masonryItem = budget.AddCostItem("Wall Masonry", "04.20.00", unitRate: 150.0, quantity: 200.0, unitBasis: "m2");
masonryItem.AssignElement(wall);

Console.WriteLine($"Total Item Cost: {masonryItem.TotalCost} USD"); // => 30000 USD
Console.WriteLine($"Grand Budget: {budget.GetTotalCost()} USD");
```

### High-Level IfcOpenShell-Compatible API Facade (`IfcApi`)

```csharp
using IfcIO.Api;

var model = IfcApi.Root.CreateProject("Commercial Office", schema: IfcIO.Schema.IfcSchema.Ifc4);
var storey = IfcApi.Root.CreateStorey(model, "Ground Floor", 0.0);
var wall = IfcApi.Root.CreateWall(model, storey, "Exterior Wall");

// High-level 4D and 5D workflows
var ws = IfcApi.Sequence.AddWorkSchedule(model, "Main Schedule");
var task = IfcApi.Sequence.AddTask(model, ws, "Wall Framing", new DateTime(2026, 1, 10), new DateTime(2026, 2, 10));
IfcApi.Sequence.AssignProcess(task, wall);

var cs = IfcApi.Cost.AddCostSchedule(model, "Base Budget");
var ci = IfcApi.Cost.AddCostItem(cs, "Framing Labor", unitRate: 75.0, quantity: 120.0);
IfcApi.Cost.AssignCostControl(ci, wall);
```

### buildingSMART BCF 2.1 Archive Container (`BcfArchive`)

```csharp
using IfcIO.Bcf;

// Read, query, and modify BCF zip archives directly
using var bcf = BcfArchive.Open("Issues.bcfzip");
var activeTopics = bcf.FindTopicsByStatus("Active");
var wallIssues = bcf.FindTopicsByElement(wall.GlobalId);

// Add topic and export updated archive
bcf.AddTopic("Verify concrete cover thickness", "Architectural Review", priority: "High");
bcf.Save("Issues_Updated.bcfzip");
```

---

## Global CLI Tool (`ifcio`)

Install the cross-platform command-line tool globally via NuGet:

```sh
dotnet tool install --global IfcIO.Cli
```

### CLI Commands

```sh
# Inspect model metadata, storeys, spaces, systems, and element distribution
ifcio info Building.ifc --report --spaces --systems --export-grids Grids.xml

# 4D construction schedule temporal simulation and CSV export
ifcio schedule Building.ifc --sim-date 2026-06-01 --export-csv ScheduleReport.csv

# 5D cost breakdown and budget reporting
ifcio cost Building.ifc --export-csv CostBreakdown.csv

# Inspect BCF 2.1 archive topics and viewpoints
ifcio bcf Issues.bcfzip

# Query elements matching IfcOpenShell-style selector
ifcio query Building.ifc "IfcWall[LoadBearing=TRUE]"

# Model repair, scaling, schema migration, and coordinate recipes
ifcio patch Building.ifc -o Cleaned.ifc --purge --reset-coords
ifcio patch Building.ifc -o Scaled.ifc --scale 0.001
ifcio patch Building.ifc -o Migrated.ifc --migrate ifc4

# Audit compliance against buildingSMART IDS 1.0 and export BCF issues
ifcio validate-ids Building.ifc Requirements.ids --bcf AuditIssues.bcfzip

# Export tabular CSV property schedules
ifcio export-csv Building.ifc -o Schedules.csv -p "FireRating,LoadBearing"

# Import updated property schedules back into IFC
ifcio import-csv Building.ifc --csv Schedules_Edited.csv -o Building_Updated.ifc

# Convert IFC to binary glTF 2.0 and Wavefront OBJ
ifcio export Building.ifc --glb scene.glb --obj scene.obj

# Compress IFC to .ifczip
ifcio pack Building.ifc -o Building.ifczip

# Extract IFC from .ifczip
ifcio unpack Building.ifczip -o Building.ifc

# Run 3D geometric clash detection with proximity clustering and grid assignment
ifcio clash Arch.ifc Mep.ifc -o Clashes.json --bcf Clashes.bcfzip --group proximity --radius 1.5 --grids Grids.xml

# Compare two IFC model revisions (added, deleted, modified)
ifcio diff Baseline.ifc Revision.ifc -o Changes.json --bcf RevisionChanges.bcfzip

# Export standalone 3D WebGL HTML viewer (.html) with interactive 3D measurement and grid overlays
ifcio view Building.ifc -o BuildingViewer.html --grids Grids.xml

# Validate ISO 10303-21 structure and export BCF 2.1 issues for coordination
ifcio validate Building.ifc --bcf Issues.bcfzip
```

---

## Documentation

The documentation site with API references, schema guides, and CLI usage is available at <https://ioextend.github.io/IfcIO/>. [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/IOExtend/IfcIO) offers an automatically generated overview of the repository.

---

## License and trademarks

IfcIO is licensed under the [MIT License](LICENSE). Copyright &copy; 2026 [IOExtend](https://github.com/IOExtend).

buildingSMART and IFC are registered trademarks of buildingSMART International Ltd.

IfcIO is an independent implementation of the ISO 10303-21 STEP and buildingSMART specifications. No proprietary code or binaries are included.
