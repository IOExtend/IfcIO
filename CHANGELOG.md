# Changelog

All notable changes to the `IfcIO` library are documented in this file.

## [0.4.0] - 2026-11-10

### Major Additions & Capabilities
- **Base Quantities & Quantity Takeoff Sets (`IfcQuantitySet` / `IFCELEMENTQUANTITY`)**:
  - Strongly-typed model hierarchy for buildingSMART physical simple quantities: `IfcQuantityLength`, `IfcQuantityArea`, `IfcQuantityVolume`, `IfcQuantityCount`, `IfcQuantityWeight`.
  - Standard buildingSMART Quantity Takeoff builders (`StandardQtoSets`):
    - `WallBaseQuantities` / `Qto_WallBaseQuantities` (Length, Width, Height, GrossFootprintArea, NetSideArea, GrossVolume, NetVolume).
    - `SlabBaseQuantities` / `Qto_SlabBaseQuantities` (Width, Length, Depth, Perimeter, GrossArea, NetArea, GrossVolume, NetVolume).
    - `ColumnBaseQuantities` / `Qto_ColumnBaseQuantities` (Length, CrossSectionArea, OuterSurfaceArea, TotalSurfaceArea, GrossVolume, NetVolume).
    - `BeamBaseQuantities` / `Qto_BeamBaseQuantities` (Length, CrossSectionArea, OuterSurfaceArea, GrossVolume, NetVolume).
    - `WindowBaseQuantities` / `Qto_WindowBaseQuantities`, `DoorBaseQuantities` / `Qto_DoorBaseQuantities`.
  - Automatic geometric quantity calculation engine (`IfcQuantityCalculator`): exact enclosed mesh volume via tetrahedral divergence theorem, total face surface area, cross-section area, profile perimeter, footprint area, and automatic deduction of openings for net volumes.
  - Model-level helper `model.CalculateAllBaseQuantities()` to calculate and attach standard buildingSMART Qto sets across all elements in the entire model.
  - Full ISO 10303-21 STEP physical file serialization (`IFCELEMENTQUANTITY`, `IFCQUANTITYLENGTH`, `IFCQUANTITYAREA`, `IFCQUANTITYVOLUME`, `IFCQUANTITYCOUNT`, `IFCQUANTITYWEIGHT`) linked via `IFCRELDEFINESBYPROPERTIES` and deserialization.
- **Parametric Revolved Swept Solids (`IfcRevolvedAreaSolid`)**:
  - Revolves 2D cross-section profiles (`IfcRectangleProfileDef`, `IfcCircleProfileDef`, `IfcArbitraryClosedProfileDef`) around an axis line (`IfcAxis1Placement`) by a revolution angle $\theta \in (0, 2\pi]$.
  - Automatic on-demand mesh generation (`solid.ToMesh()`) supporting full revolutions ($2\pi$) and partial revolutions with end caps.
  - Model-level helper `model.AddRevolvedSolid(...)`.
  - Full serialization and deserialization of `IFCREVOLVEDAREASOLID` and `IFCAXIS1PLACEMENT`.
- **Model Validation Enhancements (`IfcValidator`)**:
  - Added rule `IFC_QUANTITY_POSITIVE`: verifies that length, area, volume, count, and weight values are non-negative and finite numbers.
  - Added rule `IFC_QUANTITY_NAME`: validates non-whitespace quantity and quantity set names.
  - Added rule `IFC_REVOLVED_ANGLE`: validates that revolution angle is finite and in $(0, 2\pi]$.

## [0.3.0] - 2026-11-10

### Major Additions & Capabilities
- **Parametric Swept Solids (`IfcExtrudedAreaSolid`)**:
  - `IfcProfileDef` base hierarchy with parameterized profiles:
    - `IfcRectangleProfileDef` (`XDim`, `YDim`, optional 2D position placement).
    - `IfcCircleProfileDef` (`Radius`, optional 2D position placement).
    - `IfcArbitraryClosedProfileDef` (boundary polygon points).
  - Sweeps profiles along 3D extrusion direction vectors with custom depths.
  - Automatic on-demand polygon mesh generation (`solid.ToMesh()`) supporting robust ear-clipping polygon triangulation.
  - Fluent model helpers: `model.AddExtrudedRectangle(...)` and `model.AddExtrudedCircle(...)`.
  - Full ISO 10303-21 STEP physical file serialization (`IFCEXTRUDEDAREASOLID`, `IFCRECTANGLEPROFILEDEF`, `IFCCIRCLEPROFILEDEF`, `IFCARBITRARYCLOSEDPROFILEDEF`) under `'SweptSolid'` representation and deserialization.
- **Parametric Openings & Voids (`IfcOpeningElement`)**:
  - `IfcOpeningElement` model type representing architectural voids and cutouts (walls, slabs).
  - Support for filling elements (`opening.Filling`, e.g. `IfcWindow`, `IfcDoor`) installed inside the void.
  - Serialization and deserialization of buildingSMART relationships: `IFCRELVOIDSELEMENT` and `IFCRELFILLSELEMENT`.
  - Helper method `model.AddOpening(...)` with automatic host element linkage.
- **Standard buildingSMART Property Sets (`StandardPsets`)**:
  - Strongly-typed builders for standard Psets:
    - `WallCommon` / `Pset_WallCommon`
    - `SlabCommon` / `Pset_SlabCommon`
    - `ColumnCommon` / `Pset_ColumnCommon`
    - `BeamCommon` / `Pset_BeamCommon`
    - `WindowCommon` / `Pset_WindowCommon`
    - `DoorCommon` / `Pset_DoorCommon`
- **Model Validation Engine (`IfcValidator`)**:
  - Validates spatial structure, 22-char buildingSMART GlobalId format and uniqueness, mesh integrity, and property sets.
  - Validates swept solids: finite positive depths (`IFC_SOLID_DEPTH`), valid profile dimensions (`IFC_PROFILE_DIMENSIONS`), and openings.
- **Direct 3D Rendering of Swept Solids**:
  - `GetEffectiveMesh()` integration into `IfcGlbExporter` and `IfcObjExporter`, enabling instant visualization of swept solids in glTF 2.0 / WebGL / OBJ without manual triangulation.

### Major Additions & Capabilities
- **Cross-Platform CLI (`ifcio`)**:
  - Global .NET CLI tool packaged as `IfcIO.Cli` on NuGet.
  - Subcommands: `info` (with `--report` breakdown), `export` (to GLB, OBJ, IFC, IFCZIP), `pack` (compress to `.ifczip`), `unpack` (extract `.ifc`), and `validate` (ISO 10303-21 structure validation).
- **Full IFC Ingestion & Deserialization (`IfcModel.Open`)**:
  - Comprehensive high-level reconstruction of IFC models from STEP files: Project, Site, Building, Storeys, Elements, Property Sets, Materials, and 3D Meshes.
  - Multi-line STEP statement accumulation and quote-aware tokenization in `StepReader`.
- **IFC4 Modern Tessellation**:
  - Serialization of `IfcTriangulatedFaceSet` and `IfcCartesianPointList3D` under representation type `'Tessellation'` for IFC4 exports (>60% smaller files and faster export).
  - Parsing and reconstruction of `IfcTriangulatedFaceSet` geometry on import.
- **Direct 3D Exporters**:
  - Binary glTF 2.0 exporter (`model.SaveGlb` / `IfcGlbExporter`): PBR materials, smooth/flat vertex normals, scene hierarchy, and double-sided rendering.
  - Wavefront OBJ exporter (`model.SaveObj` / `IfcObjExporter`): vertex geometry and `.mtl` material libraries.
- **IfcZip (.ifczip) Transparent Archive Support**:
  - Built-in compression and decompression for `.ifczip` files using pure managed `System.IO.Compression.ZipArchive`.
- **Type Products & RelDefinesByType**:
  - Reusable `IfcTypeProduct` definitions (`IFCWALLTYPE`, `IFCSLABTYPE`, `IFCCOLUMNTYPE`, `IFCBEAMTYPE`, etc.) linked via `IFCRELDEFINESBYTYPE`.

## [0.1.0] - 2026-11-10

### Initial Public Release

- **Pure Managed STEP Engine**:
  - Pure managed ISO 10303-21 STEP physical file serializer (`StepWriter`) and tokenizer/parser (`StepReader`).
  - Strict string escaping with full Unicode support (`\X2\HHHH...\X0\`).
- **Standard buildingSMART Schemas**:
  - Support for buildingSMART **IFC2x3** (Coordination View V2.0) and **IFC4** (Design Transfer & Reference View).
  - Standard 22-character `IfcGuid` bidirectional conversion and deterministic SHA-256 generation.
- **BIM Modeling API**:
  - High-level fluent builder API: `IfcModel`, `IfcStorey`, `IfcElement`, `IfcMaterial`.
  - 3D Faceted BRep geometry construction (`IfcFacetedBrep`, `IfcClosedShell`, `IfcFace`, `IfcPolyLoop`, `IfcCartesianPoint`).
  - Spatial containment structure: `IfcProject` &rarr; `IfcSite` &rarr; `IfcBuilding` &rarr; `IfcBuildingStorey`.
  - Typed Property Sets (`IfcPropertySet`, `IfcPropertySingleValue`) with `IfcText`, `IfcInteger`, `IfcReal`, `IfcBoolean`.
  - Surface styles and materials (`IfcSurfaceStyle`, `IfcSurfaceStyleShading`, `IfcColourRgb`).
- **Platform & Quality**:
  - Targets **.NET 8.0** and **.NET 10.0** with zero third-party dependencies.
  - Full code analysis with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
