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

## Geometry & Representation

| Type | Description |
|---|---|
| `IfcMesh` | Triangle mesh with double-precision vertices, triangle indices, and a bounding box. |
| `IfcModel.UseTessellationInIfc4` | Controls whether mesh export uses the STEP entity `IFCTRIANGULATEDFACESET` in IFC4. Meshes can also be written as `IFCFACETEDBREP`. These are STEP entity names, not public .NET types. |
| `IfcProfileDef` | Base type for rectangular, circular, arbitrary closed polygon, I-, L-, and U-shaped profiles. |
| `IfcExtrudedAreaSolid` | Extrudes a profile and exposes a triangulated mesh through `ToMesh`. |

## Import & Export

| Type | Description |
|---|---|
| `StepReader`, `StepFile`, `StepEntity` | Read and inspect STEP headers and entity records. Model interpretation is handled by `IfcModel.Open`. |
| `StepWriter` | Writes STEP headers and entity records. |
| `IfcGlbExporter` | Binary glTF 2.0 exporter generating specification-compliant `.glb` files with PBR materials. |
| `IfcObjExporter` | Wavefront OBJ geometry exporter with MTL material library generation. |

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
