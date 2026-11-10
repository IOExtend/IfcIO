<p align="center">
  <img src="docs/images/ifcio-banner.png" alt="IfcIO" width="640">
</p>

# IfcIO

**IfcIO** is a fast, 100% managed .NET library for building, reading, and exporting **Industry Foundation Classes** (`.ifc`) files compliant with **IFC2x3 Coordination View V2.0** and **IFC4 Design Transfer / Reference View** without third-party dependencies or native code.

Built clean-room under the [IOExtend](https://github.com/IOExtend) organization as the shared open-standards IFC foundation for AEC/BIM projects (including [NwcIO](https://github.com/IOExtend/NwcIO) and [RvtIO](https://github.com/mahach666/RvtIO)).

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

---

## Global CLI Tool (`ifcio`)

Install the cross-platform command-line tool globally via NuGet:

```sh
dotnet tool install --global IfcIO.Cli
```

### CLI Commands

```sh
# Inspect model metadata, storeys, spaces, systems, and element distribution
ifcio info Building.ifc --report --spaces --systems

# Query elements matching IfcOpenShell-style selector
ifcio query Building.ifc "IfcWall[LoadBearing=TRUE]"

# Model repair and coordinate recipes (purge, reset UTM coordinates, extract)
ifcio patch Building.ifc -o Cleaned.ifc --purge --reset-coords

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

# Validate ISO 10303-21 structure and export BCF 2.1 issues for coordination
ifcio validate Building.ifc --bcf Issues.bcfzip
```

---

## License and trademarks

IfcIO is licensed under the [MIT License](LICENSE). Copyright &copy; 2026 [IOExtend](https://github.com/IOExtend).

buildingSMART and IFC are registered trademarks of buildingSMART International Ltd.

IfcIO is an independent implementation of the ISO 10303-21 STEP and buildingSMART specifications. No proprietary code or binaries are included.
