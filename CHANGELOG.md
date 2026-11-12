# Changelog

All notable changes to the `IfcIO` library are documented in this file.

## [Unreleased]

### Added
- **`IfcIO.Scene`**: `IfcSceneReader` converts an IFC file or `IfcModel` into an `IOScene` `SceneDocument` in metres with Z up (GlobalIds as `SourceId` and `IfcGuid`, storeys as levels and tree nodes, property and quantity sets keyed `ifc:`, materials, 4D schedules), with diagnostics for elements without geometry. See `docs/scene-adapter.md`.
- **`IfcIO.Scene` NuGet package**: published on the `IfcIO` release tag and depends on exactly the same `IfcIO` version (`[X.Y.Z]`). It needs the released `IOScene` 0.1.0; the release fails while it would depend on an `IOScene` preview.

### Fixed
- **World placement of geometry read from IFC (`IfcModel.Open`)**: element meshes are now in world coordinates. Previously object placements were ignored and every element sat at its local origin.
  - `IfcLocalPlacement` chains (`PlacementRelTo`) are resolved up to the root, relative to the `WorldCoordinateSystem` of the 3D `Model` representation context.
  - `IfcMappedItem` applies `MappingOrigin` and `MappingTarget` (`IfcCartesianTransformationOperator` 2D/3D, uniform and non-uniform scale, mirroring).
  - The full `IfcAxis2Placement3D` of extruded and revolved solids is applied (it was location only), as are rotated `IfcAxis2Placement2D` profile positions of extrusions and non-vertical extrusion directions. `IfcAxis1Placement` locations of revolution axes are read (they were always zero).
  - The first operand of `IfcBooleanResult` / `IfcBooleanClippingResult` honours a rotated `Position`; such an operand is kept as `FirstMesh` instead of a misoriented `FirstSolid`.
  - All items of the `Body` representation are merged (only the first item was read). `IfcFaceBasedSurfaceModel` and `IfcShellBasedSurfaceModel` are read.
  - `IfcDistributionPort.Position` and `Direction` are the port's world placement (they were always the origin and +Z).
- **Length unit read from IFC**: `IfcModel.LengthUnit` comes from the project's `IfcUnitAssignment` (`IfcSIUnit` with any prefix, `IfcConversionBasedUnit`). It was always `Millimetre`. Units with no `IfcLengthUnit` value (for example centimetres) are converted to metres while reading, together with storey elevations.
- **MEP elements are read**: IFC2x3 `IfcFlowSegment`, `IfcFlowFitting`, `IfcFlowTerminal`, `IfcFlowController`, `IfcFlowMovingDevice`, `IfcFlowStorageDevice`, `IfcFlowTreatmentDevice`, `IfcEnergyConversionDevice`, `IfcDistributionChamberElement`, `IfcDistributionControlElement` and IFC4 `IfcPipeFitting`, `IfcDuctFitting` were skipped.

### Changed
- **GLB export follows glTF 2.0 units and axes**: positions are written in metres (converted from `IfcModel.LengthUnit`) with +Y up (IFC `(x, y, z)` becomes `(x, z, -y)`). Vertices are stored relative to the centre of the model, and the root node carries that centre as `translation`, so georeferenced models keep millimetre precision in 32-bit floats. GLB files of millimetre models were previously 1000 times too large and lying on their side.
- OBJ export is unchanged: model length units, Z up, now with world-placed geometry.
- `IfcElement.Solid`, `RevolvedSolid` and `BooleanResult` of read elements still describe the first body item in its own coordinates (only the `Mesh` is world-placed). Writing a read model back therefore still places those solids without the element placement, as before.
- Malformed placement data (a `PlacementRelTo` cycle, an `IfcMappedItem` cycle or mapping nested deeper than 64 levels) makes `IfcModel.Open` throw `InvalidDataException`. So does geometry that expands to more than 1,000,000 + 256 vertices per STEP entity, which guards against nested `IfcMappedItem` amplification.
- For length units without an `IfcLengthUnit` value, only geometry and storey elevations are converted to metres; quantities and property values stay in file units. Such elements get no `Solid` / `RevolvedSolid` / `BooleanResult` (those would stay in file units), so saving writes the metre `Mesh`.
- **Documentation**: the README lists the frozen extras (HTML viewer, GLB/OBJ export, clash, measure, grids, diff, BCF), which get bug fixes only, and their planned replacements.

## [0.10.0] - 2026-11-11

### Major Additions & Capabilities
- **4D Construction Sequencing & Temporal Simulation (`IfcWorkSchedule`, `IfcTask`, `IfcTaskTime` / `IfcIO.Model.Sequence`)**:
  - Full support for construction work schedules (`IFCWORKSCHEDULE`), WBS hierarchies, task durations, milestones, and predecessor/successor relationships (`IFCRELSEQUENCE` with `FinishToStart`, `StartToStart`, `FinishToFinish`, `StartToFinish`).
  - Elements assigned to construction tasks via `IFCRELASSIGNSTOPROCESS`.
  - Temporal 4D simulation engine: query active tasks (`GetActiveTasksAt`), element simulation states (`GetElementStateAt`: `NotStarted`, `Planned`, `InProgress`, `Completed`), and overall schedule progress (`GetProgressAt`) at any given simulation date.
  - Interactive 4D timeline simulation playback integrated into the standalone 3D HTML viewer (`IfcHtmlViewerExporter`).
- **5D Cost Planning & CBS Budgeting (`IfcCostSchedule`, `IfcCostItem`, `IfcCostValue` / `IfcIO.Model.Cost`)**:
  - Hierarchical Cost Breakdown Structure (CBS) supporting predefined schedule types (`Budget`, `CostPlan`, `Estimate`, `Tender`, `Actual`, `Valuation`, `Pricing`).
  - Unit rates, quantities, and automatic cost rollups across hierarchical tree nodes (`item.TotalCost`, `cs.GetTotalCost()`).
  - Applied monetary value breakdown components conforming to `IFCAPPLIEDVALUE`.
  - Element assignments and physical quantity bindings via `IFCRELASSIGNSTOCONTROL`.
  - Full STEP physical file roundtrip serialization and deserialization for IFC4 and IFC2x3.
- **Spatial Zones (`IfcZone` / `IfcIO.Model`)**:
  - Functional spatial zone grouping conforming to `IFCZONE` (inherits `IfcGroup`) for architectural spaces and rooms.
  - Support for thermal, fire safety, acoustic, security, and ventilation zone classifications.
- **Bi-directional buildingSMART BCF 2.1 Archive Container (`BcfArchive` / `IfcIO.Bcf`)**:
  - Full in-memory or stream-based container for reading, querying, editing, and saving `.bcfzip` archives.
  - High-level queries by element GUID (`archive.FindTopicsByElement`), status (`archive.FindTopicsByStatus`), and topic manipulation (`AddTopic`).
  - Complete snapshot image encoding/decoding (`snapshot.png`) and viewpoint camera handling.
- **High-Level IfcOpenShell-Compatible API Facade (`IfcApi` / `IfcIO.Api`)**:
  - High-level, script-like API facade inspired by IfcOpenShell API conventions:
    - `IfcApi.Sequence`: `AddWorkSchedule`, `AddTask`, `AssignProcess`, `AddSequence`.
    - `IfcApi.Cost`: `AddCostSchedule`, `AddCostItem`, `AssignCostControl`, `AssignQuantity`.
    - `IfcApi.Spatial`: `AddZone`, `AssignContainer`.
    - `IfcApi.Root`: `CreateProject`, `CreateStorey`, `CreateWall`, `CreateSlab`, `CreateColumn`, `CreateBeam`.
- **CLI Command Expansion (`IfcIO.Cli`)**:
  - `ifcio schedule`: temporal 4D construction schedule simulation with `--sim-date <date>` and `--export-csv <file.csv>`.
  - `ifcio cost`: 5D cost breakdown and budget reporting with `--export-csv <file.csv>`.
  - `ifcio bcf`: inspect BCF 2.1 coordination archive topics, status, and viewpoints.

## [0.9.0] - 2026-11-11

### Major Additions & Capabilities
- **Spatial Grid System (`IfcGridSystem`, `IfcGridAxis`, `IfcGridIntersection` / `IfcIO.Grids`)**:
  - Full support for straight and multi-axis spatial grids, 2D/3D intersections, nearest grid axis/intersection lookup, and signed directional offset formatting (e.g. `"A (+0.50m) / 1 (-0.20m)"`).
  - Automatic extraction of grid systems from `IFCGRID` and `IFCGRIDAXIS` entities (`model.ExtractGridSystem()`).
  - Element and clash location assignment (`model.AssignGridLocations()`, `report.AssignGridLocations()`).
  - Standalone XML grid definition roundtrip serialization (`grids.SaveXml`, `IfcGridSystem.ReadXml` / `LoadXml`).
- **3D Geometric Measurement Engine (`IfcMeasureEngine` / `IfcIO.Measure`)**:
  - Point-to-point 3D measurement with $\Delta X, \Delta Y, \Delta Z$, horizontal distance, pitch, and azimuth projections (`IfcMeasureEngine.MeasureDistance`).
  - Point-to-mesh shortest distance using Christer Ericson's Voronoi region algorithm (`IfcMeasureEngine.MeasurePointToMesh`).
  - Mesh-to-mesh clearance measurement (`IfcMeasureEngine.MeasureMeshToMesh`).
  - Polyline path cumulative length and segment breakdowns (`IfcMeasureEngine.MeasurePath`).
  - Triangle surface area, normal, and closest point calculations.
  - Mesh surface area, closed volume (tetrahedron divergence theorem), and 3D centroid calculation.
- **Intelligent Clash Grouping & Clustering (`IfcClashGrouper` / `IfcIO.Clash`)**:
  - Spatial 3D proximity grouping using Disjoint-Set (Union-Find) clustering (`report.GroupByProximity`).
  - Primary MEP/structural element grouping (`report.GroupByPrimaryElement`).
  - Building storey grouping (`report.GroupByStorey`).
  - Structural grid location grouping (`report.GroupByGrid`).
  - Group dissolving (`report.UngroupAll`).
  - Grouped buildingSMART BCF 2.1 zip archive export with cluster camera viewpoints.
- **Enhanced Model Repair & Patching Recipes (`IfcPatch` / `IfcIO.Patch`)**:
  - `MigrateSchema`: upgrades IFC2x3 models to IFC4 and enables modern tessellated geometries.
  - `ScaleModel`: uniformly scales model geometry (e.g. converting mm to meters).
  - `SetProperties`: batch property assignment across elements matching an `IfcSelector` query.
- **Standalone 3D WebGL HTML Viewer Enhancements (`IfcHtmlViewerExporter` / `IfcIO.Exporters`)**:
  - Interactive 2-point 3D measurement tool with live distance, delta coordinates, and pitch overlay.
  - 2D canvas overlay drawing of architectural grid axes and bubble labels.
- **CLI Enhancements (`IfcIO.Cli`)**:
  - `ifcio clash`: added `--group <proximity|element|storey|grid>`, `--radius <meters>`, and `--grids <grids.xml>`.
  - `ifcio patch`: added `--recipe <migrate|scale|offset|purge|reset>`, `--scale <factor>`, and `--migrate <schema>`.
  - `ifcio view`, `ifcio info`: added `--grids <grids.xml>` and `--export-grids <out.xml>`.

## [0.8.0] - 2026-11-10

### Major Additions & Capabilities
- **IfcOpenShell-Compatible 3D Geometric Clash Detection (`IfcClashDetector` / `IfcIO.Clash`)**:
  - High-performance collision engine executing broad-phase AABB bounding box pruning and narrow-phase Möller triangle-to-triangle intersection testing in pure managed C#.
  - Cross-model clash detection between contributing discipline models (e.g. Structure vs MEP) and intra-model self-clash detection.
  - Configurable clearance and penetration tolerance, discipline filtering via `IfcSelector` queries, and storey/type exclusions.
  - Direct export of clash reports to structured JSON (`report.SaveJson`) and buildingSMART BCF 2.1 zip archives (`report.SaveBcf`) with perspective camera framing and selected IFC GUIDs.
  - Integrated into `IfcModel.DetectClashes(...)`.
- **IfcOpenShell-Compatible Revision Diff & Comparison (`IfcDiffEngine` / `IfcIO.Diff`)**:
  - Full semantic and geometric comparison between two IFC model revisions (baseline vs revision).
  - Detects Added, Deleted, and Modified elements with detailed change sub-categorization:
    - 3D Geometry changes: vertex differences, bounding box shifts, translation vectors.
    - Property changes: added, removed, and modified property set parameters with old and new values.
    - Quantity changes: delta in physical base quantities (lengths, areas, volumes).
    - Spatial container re-assignments (moved between building storeys).
    - Type changes: modified `IfcTypeProduct` definitions.
  - Direct export of diff reports to structured JSON (`report.SaveJson`) and buildingSMART BCF 2.1 archives (`report.SaveBcf`).
  - Integrated into `IfcModel.Compare(...)`.
- **Standalone 3D WebGL HTML Viewer Export (`IfcHtmlViewerExporter` / `IfcIO.Exporters`)**:
  - Direct export of any IFC model to a self-contained, interactive single-file 3D HTML application (`.html`) with zero external dependencies, CDNs, or server requirements.
  - WebGL rendering with orbit camera controls (rotate, pan, zoom), mesh instancing, and material colors.
  - Live fragment-shader section clipping planes (Cut X, Cut Y, Cut Z sliders).
  - Spatial Tree Navigator (Storeys &rarr; Elements) with isolate / highlight.
  - Property Inspector displaying full element identity, property sets, and physical quantities.
  - Integrated into `IfcModel.SaveHtmlViewer(...)`.
- **CLI Enhancements (`IfcIO.Cli`)**:
  - `ifcio clash <modelA.ifc> [modelB.ifc] [-o <clashes.json>] [--bcf <clashes.bcfzip>] [--tolerance <meters>]`: run geometric clash detection.
  - `ifcio diff <baseline.ifc> <revision.ifc> [-o <diff.json>] [--bcf <changes.bcfzip>] [--tolerance <meters>]`: compare two model revisions.
  - `ifcio view <model.ifc> [-o <model.html>]`: export standalone 3D WebGL HTML viewer.

## [0.7.0] - 2026-11-10

### Major Additions & Capabilities
- **IfcOpenShell-Compatible Query Selector Engine (`IfcSelector` / `IfcIO.Query`)**:
  - Declarative filtering syntax matching IfcOpenShell query selectors:
    - Entity types (`IfcWall`, `IFCWALLSTANDARDCASE`, `*`).
    - Spatial hierarchy containers (`#Level 1`, `@Level 2`).
    - Property, attribute, and quantity filters with operators: `=`, `!=`, `*=`, `^=`, `$=`, `>`, `<`, `>=`, `<=`, and existence checks (`[LoadBearing=TRUE]`, `[FireRating>=60]`, `[Name*='Conc']`).
    - Comma-separated union lists (`IfcWall, IfcColumn`).
  - Native integration: `model.Select(query)` and element extension method `element.Matches(query, model)`.
- **Model Repair, Cleanup, Coordinate Shifting & Merging Recipes (`IfcPatch` / `IfcIO.Patch`)**:
  - Direct 100% managed .NET implementation of IfcOpenShell `ifcpatch` recipe architecture.
  - `Purge`: removes unreferenced, empty, and duplicate property sets, quantity sets, and orphaned empty storeys.
  - `ResetCoordinates`: detects large absolute UTM/Civil coordinates (> 10,000m) and centers geometry at local (0, 0, 0) to eliminate 32-bit/64-bit floating point precision jitter.
  - `OffsetCoordinates`: translates all 3D mesh vertices by an offset vector.
  - `Extract`: slices a subset of elements matching an `IfcSelector` query into a new, self-contained IFC model preserving spatial hierarchy, units, and materials.
  - `Merge`: combines multiple IFC models into a single coordinated model, reconciling storeys by name and elevation and re-indexing IDs sequentially.
- **buildingSMART Information Delivery Specification (IDS 1.0) Audit Engine (`IfcIdsValidator` / `IfcIO.Validation.Ids`)**:
  - Implements the buildingSMART IDS 1.0 specification standard mirroring IfcOpenShell / IfcTester audit pipelines.
  - Validates `<applicability>` criteria (entities, properties, materials) and `<requirements>` facets (properties with minOccurs/values, materials, attributes like Name, Tag, Description).
  - Direct BCF 2.1 integration: `report.SaveBcf(path)` exports compliance issues and failed element GUIDs into a standard `.bcfzip` for BIM issue tracking in Revit, Solibri, and BIMcollab.
- **Tabular Property CSV Roundtrip Bridge (`IfcCsvBridge` / `IfcIO.Model`)**:
  - Mirroring `ifccsv` functionality in pure managed .NET.
  - `ExportCsv`: exports element metadata and custom property set parameters to delimiter-separated spreadsheets (`.csv`).
  - `ImportCsv`: bulk property updates and additions back into IFC model elements matched by `GlobalId` or `Id`.
- **CLI Enhancements (`IfcIO.Cli`)**:
  - `ifcio query <model.ifc> <selector>`: inspect elements matching selector query.
  - `ifcio patch <model.ifc> -o <out.ifc> [--purge] [--reset-coords] [--offset X,Y,Z] [--extract <query>]`: run model repair and coordinate recipes.
  - `ifcio validate-ids <model.ifc> <audit.ids> [--bcf <issues.bcfzip>]`: perform automated IDS 1.0 audit and export BCF topics.
  - `ifcio export-csv <model.ifc> -o <out.csv> [--props P1,P2]`: export tabular property schedules.
  - `ifcio import-csv <model.ifc> --csv <in.csv> -o <out.ifc>`: import modified property parameters into an IFC model.

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
