# Changelog

All notable changes to the `IfcIO` library are documented in this file.

## [0.6.0] - 2026-11-10

### Major Additions & Capabilities
- **Constructive Solid Geometry (CSG) & Boolean Clipping (`IfcBooleanResult` / `IfcBooleanClippingResult`)**:
  - Exact 3D mesh evaluation (`ToMesh()`) via Sutherland-Hodgman polygon plane clipping without external dependencies.
  - Planar primitives: `IfcPlane` (reference point & normal vector, signed perpendicular distance) and `IfcHalfSpaceSolid` (infinite clipping plane with `AgreementFlag`).
  - Convenient model builder `model.AddClippedSolid(...)` for rafters, slanted roof cuts, foundation trenches, and arbitrary boolean shapes.
  - Full ISO 10303-21 STEP physical file serialization (`IFCBOOLEANCLIPPINGRESULT`, `IFCBOOLEANRESULT`, `IFCHALFSPACESOLID`, `IFCPLANE`) and deserialization.
- **Standard Structural Steel Profiles (`IfcProfileDef` Subtypes)**:
  - `IfcIShapeProfileDef`: parameterized I-beam / H-beam cross-section (`OverallWidth`, `OverallDepth`, `WebThickness`, `FlangeThickness`, `FilletRadius`).
  - `IfcLShapeProfileDef`: parameterized L-angle cross-section (`Depth`, `Width`, `Thickness`, `FilletRadius`).
  - `IfcUShapeProfileDef`: parameterized U-channel cross-section (`Depth`, `FlangeWidth`, `WebThickness`, `FlangeThickness`, `FilletRadius`).
  - Accurate 2D boundary polygons with 3D extrusion (`ToMesh()`) and STEP roundtrip.
- **Topological MEP Ports & Network Connectivity (`IfcDistributionPort`)**:
  - Strongly-typed `IfcDistributionPort` with `IfcFlowDirection` (`Source`, `Sink`, `SourceAndSink`, `NotDefined`).
  - Port-to-element association via `IFCRELCONNECTSPORTTOELEMENT`.
  - Port-to-port topological continuity via `IFCRELCONNECTSPORTS`.
  - Breadth-First Search (BFS) network traversal via `IfcModel.TraceConnectedElements(...)` to navigate connected duct, pipe, and cable networks.
  - Full STEP serialization and deserialization.
- **Multi-layer Material Systems & Structural Profiles (`IfcIO.Model`)**:
  - Layered composite constructions: `IfcMaterialLayer`, `IfcMaterialLayerSet`, `IfcMaterialLayerSetUsage` with reference line offset.
  - Structural profile materials: `IfcMaterialProfile`, `IfcMaterialProfileSet`, `IfcMaterialProfileSetUsage` with cardinal point alignment.
  - Material assignment to building elements via `IFCRELASSOCIATESMATERIAL`.
  - Model helpers: `model.AddMaterialLayerSet(...)`, `model.AssignMaterialLayerSet(...)`, `model.AddMaterialProfileSet(...)`, `model.AssignMaterialProfileSet(...)`.
- **Validation Engine Enhancements (`IfcValidator`)**:
  - `IFC_CSG_OPERAND_MISSING`: verifies operands in boolean results.
  - `IFC_PROFILE_SECTION`: verifies geometric validity constraints ($2 t_f < H$ and $t_w < B$ for I-beams; $t < B, H$ for L-angles; $2 t_f < H$ and $t_w < B$ for U-channels).
  - `IFC_PORT_DIRECTION`: flags ports with undefined flow directions.
  - `IFC_LAYER_THICKNESS`: ensures layer thicknesses are positive and finite.

## [0.5.0] - 2026-11-10

### Major Additions & Capabilities
- **Architectural & MEP Spaces (`IfcSpace` / `IFCSPACE`)**:
  - Strongly-typed `IfcSpace` model representing spatial structures, rooms, zones, and MEP compartments.
  - Attributes: `SpaceNumber` (tag/description), `LongName`, `GrossFloorArea`, `NetFloorArea`, `Height`, `GrossVolume`, `InteriorOrExteriorSpace`.
  - Containment in `IfcBuildingStorey.Spaces` and model aggregation via `model.AllSpaces`.
  - Convenient model builder `model.AddSpace(name, spaceNumber, longName, grossFloorArea, height, storey, mesh)`.
  - Full ISO 10303-21 STEP physical file serialization (`IFCSPACE`) and deserialization.
- **Space Boundaries (`IfcSpaceBoundary` / `IFCRELSPACEBOUNDARY`)**:
  - Strongly-typed `IfcSpaceBoundary` linking spaces to enclosing architectural and structural elements (walls, slabs, columns).
  - Classification: `PhysicalOrVirtual` (`Physical`, `Virtual`), `InternalOrExternal` (`Internal`, `External`).
  - Full STEP serialization and deserialization via `IFCRELSPACEBOUNDARY`.
- **Building Systems & Groups (`IfcSystem` / `IfcGroup`)**:
  - `IfcSystem` representing MEP distribution networks (`IFCDISTRIBUTIONSYSTEM`) and functional building systems (`IFCSYSTEM`).
  - `IfcGroup` representing generic element groups (`IFCGROUP`).
  - Relationship assignment via `IFCRELASSIGNSTOGROUP` with bidirectional referencing.
  - Model-level helpers: `model.AddSystem(...)`, `model.AddGroup(...)`, `model.Systems`, `model.Groups`.
- **buildingSMART BCF 2.1 Issue Management (`IfcIO.Bcf`)**:
  - Complete, zero-dependency read/write implementation of the buildingSMART BIM Collaboration Format 2.1 specification (`.bcfzip`).
  - Strongly-typed models: `BcfTopic`, `BcfComment`, `BcfViewpoint`.
  - Viewpoint camera parameters (`CameraPosition`, `CameraDirection`, `CameraUpVector`, `FieldOfView`) and selected IFC element GUIDs.
  - Serialization engine `BcfZipExporter`: generates compliant `bcf.version`, `markup.bcf`, and `viewpoint.bcfv` XML documents.
  - Validation-to-BCF pipeline: `model.ExportValidationBcf("issues.bcfzip")` converts all validation errors and warnings into actionable BCF topics with attached IFC GUIDs.
- **Model Validation Engine Updates (`IfcValidator`)**:
  - Space validation rules: `IFC_SPACE_AREA` (verifies positive/finite area), `IFC_SPACE_HEIGHT` (verifies positive/finite height).
  - System and group rules: `IFC_SYSTEM_NAME`, `IFC_SYSTEM_EMPTY`, `IFC_GROUP_NAME`.
- **CLI Enhancements (`IfcIO.Cli`)**:
  - Added `--bcf <issues.bcfzip>` to `validate` command for automatic BCF report generation.
  - Added `--spaces` and `--systems` flags to `info` command to inspect spatial structures and MEP systems.

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
