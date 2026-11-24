---
title: Public API
---

# Public API Surface

This page summarizes the public types and entry points of the `IfcIO` package.

## Core Model & Hierarchy

| Type | Description |
|---|---|
| `IfcModel` | Model container with project and building names, storeys, materials, and type products. `AllElements` enumerates elements across storeys. `Open`, `Save`, and `SaveZip` handle `.ifc` and `.ifczip`. |
| `IfcElement` | Base BIM product element representing walls, slabs, columns, beams, doors, windows, pipes, ducts, and generic components. |
| `IfcBuildingStorey`, `IfcSpace` | Storeys hold elements and spaces and carry elevations. The model does not expose separate `IfcSite`, `IfcBuilding`, or `IfcProject` objects. |
| `IfcGuid` | buildingSMART 22-character compressed GlobalId encoder/decoder with bidirectional conversion to 128-bit .NET `Guid`. |

`IfcModel.SourceSchemaIdentifier` preserves the file's `FILE_SCHEMA` identifier, including any appended numeric
object identifier. `IfcSchema.Ifc4x3` means subset import only: not every IFC4.3 entity is interpreted.
Representations and ports with unsupported placements are omitted and counted in `UnsupportedPlacementCount`.
IFC output and schema migration are not supported for these models, including after extraction.
Unknown, missing or multiple schema identifiers fail model import.

`IfcModel.LengthUnit` controls the declared unit of geometry and storey elevations. IFC import keeps recognised
millimetres, metres, feet and inches in the model; other supported length scales are converted to metres.
`IfcModel.AngleUnit` controls the IFC plane angle unit, but `IfcRevolvedAreaSolid.Angle` is always in radians in memory.
If the file has no usable plane angle unit, the reader assumes radians. Length quantities are read in the same
length unit as geometry; area and volume quantity handling is unchanged.

Older IfcIO exports declared degrees but stored revolved-solid angles in radians. Normal reads trust the IFC unit
declaration. For a file known to have this legacy error, use
`IfcModel.Open(path, new IfcReadOptions { LegacyIfcIoRevolvedAnglesInRadians = true })`, then save the model, or run
`ifcio patch old.ifc --legacy-ifcio-radian-sweeps -o repaired.ifc`.

Factory methods allocate element IDs across a model's storeys, including after import. Create storeys with
`IfcModel.AddStorey`, or pass a caller-created storey to a model factory before calling its own factories.
Factory calls reserve IDs already in the registered storeys' element and space lists. Export avoids collisions
among generated product GlobalIds and does not rewrite explicit GlobalIds.

## Geometry & Representation

| Type | Description |
|---|---|
| `IfcMesh` | Triangle mesh with double-precision vertices, triangle indices, and a bounding box. |
| `IfcModel.UseTessellationInIfc4` | Controls whether mesh export uses the STEP entity `IFCTRIANGULATEDFACESET` in IFC4. Meshes can also be written as `IFCFACETEDBREP`. These are STEP entity names, not public .NET types. |
| `IfcProfileDef` | Base type for rectangular, circular, arbitrary closed polygon, I-, L-, and U-shaped profiles. |
| `IfcExtrudedAreaSolid` | Extrudes a profile and exposes a triangulated mesh through `ToMesh`. |

For faceted faces, the reader triangulates planar concave bounds and holes without adding vertices. Disjoint
bounds remain separate regions. Invalid contours or faces that exceed the triangulation work limit are omitted.

Imported meshes use world coordinates. If a swept body cannot be represented by one equivalent parametric
solid, the reader keeps its placed mesh instead; IFC export writes that mesh rather than a partial or
misplaced solid. Boolean operands also use placed meshes where necessary. Applying arbitrary solid-to-solid
Boolean operations to the mesh is not supported; the mesher clips against half-spaces only.
IFC export rejects compound Boolean bodies and Boolean results with missing operands rather than
writing a mesh that drops the operations or an invalid STEP reference.

The reader uses standard IFC sweep coordinates. Older IfcIO exports that encoded a tilted authored extrusion
without its profile frame, or a translated revolution's axis in absolute coordinates, may reopen differently.
New exports write the extrusion frame and the revolution's local axis explicitly.
Revolutions whose axis does not lie in the authored profile plane are exported as a mesh.
For non-tessellated mesh export, disconnected closed shells are written separately and open meshes use a surface model.

## Import & Export

| Type | Description |
|---|---|
| `StepReader`, `StepFile`, `StepEntity` | Read and inspect STEP headers and entity records. Model interpretation is handled by `IfcModel.Open`. |
| `StepWriter` | Writes STEP headers and entity records. |
| `IfcGlbExporter` | Binary glTF 2.0 exporter generating specification-compliant `.glb` files with PBR materials. |
| `IfcObjExporter` | Wavefront OBJ geometry exporter with MTL material library generation. |

Model import builds a case-insensitive index for its type queries. Public `StepFile.OfType` follows the mutable
`StepFile.Entities` dictionary, including edits during enumeration. Typed `StepEntity` accessors parse slices
lazily; `Arguments` materializes the full string list on demand. Model import does not retain the temporary
`StepFile` or stream model objects incrementally.

## Coordination & Validation

| Type | Description |
|---|---|
| `IfcClashDetector` | Checks candidate element pairs with axis-aligned bounding boxes, then tests triangles for intersection. It does not use a spatial index. |
| `IfcDiffEngine` | Revision comparison engine detecting added, deleted, and modified IFC elements and geometry shifts. |
| `IfcValidator` | Runs the implemented model validation rules and structural checks on parsed STEP records. It is not a complete IFC schema conformance validator. |
| `BcfZipExporter` | Reads and writes buildingSMART BCF 2.1 issue archives. |
| `BcfArchive` | Bidirectional BCF 2.1 archive container for reading, querying, editing, and saving `.bcfzip` files. |

The viewer and coordination APIs are maintained for compatibility; see [scope and frozen features](https://github.com/IOExtend/IfcIO#scope-and-frozen-features).

## 4D Construction Sequencing & Temporal Simulation

| Type | Description |
|---|---|
| `IfcWorkSchedule` | Construction schedule container managing WBS hierarchies, baseline dates, temporal simulation, and progress tracking. |
| `IfcTask` | Individual construction activity or milestone with duration, planned/actual dates, and predecessor/successor links. |
| `IfcTaskTime` | Time entity conforming to `IFCTASKTIME` capturing early/late dates, durations, completion ratio, and milestones. |

## 5D Cost Planning & CBS Budgeting

| Type | Description |
|---|---|
| `IfcCostSchedule` | 5D budget, tender, estimate, or cost plan conforming to `IFCCOSTSCHEDULE` with currency and rollups. |
| `IfcCostItem` | Cost breakdown structure (CBS) node with unit rates, quantities, linked elements, and sub-items. |
| `IfcCostValue` | Applied monetary value breakdown conforming to `IFCAPPLIEDVALUE`. |

## High-Level IfcOpenShell-Compatible API Facade

| Type | Description |
|---|---|
| `IfcApi` | Unified top-level facade conforming to IfcOpenShell high-level API conventions (`IfcApi.Sequence`, `IfcApi.Cost`, `IfcApi.Spatial`, `IfcApi.Root`). |
| `IfcZone` | Functional spatial zone grouping spaces into thermal, fire safety, acoustic, or ventilation zones (`IFCZONE`). |
