---
title: Public API
---

# Public API Surface

The `IfcIO` package is the supported surface listed here.

## Core Model & Hierarchy

| Type | Description |
|---|---|
| `IfcModel` | Central model container managing spatial hierarchy (Project, Site, Building, Storey), elements, materials, property sets, and geometry representations. Opens and writes `.ifc` and `.ifczip` files. |
| `IfcElement` | Base BIM product element representing walls, slabs, columns, beams, doors, windows, pipes, ducts, and generic components. |
| `IfcStorey`, `IfcBuilding`, `IfcSite`, `IfcProject` | Spatial structure hierarchy components with coordinates, elevations, and relationships. |
| `IfcGuid` | buildingSMART 22-character compressed GlobalId encoder/decoder with bidirectional conversion to 128-bit .NET `Guid`. |

## Geometry & Representation

| Type | Description |
|---|---|
| `IfcMeshGeometry` | 3D polygonal triangle mesh data structure holding double-precision vertices, normals, and face index buffers. |
| `IfcTriangulatedFaceSet` | Modern IFC4 compact tessellation geometry generator (`IfcCartesianPointList3D`). |
| `IfcFacetedBrep` | Standard IFC2x3 faceted boundary representation (`IfcClosedShell`, `IfcFace`, `IfcPolyLoop`). |
| `IfcProfileDef` | 2D parametric profile definitions (rectangle, circle, I-shape, L-shape, composite). |

## Import & Export

| Type | Description |
|---|---|
| `StepReader` | Low-level ISO 10303-21 STEP physical file parser supporting multi-line statements and quote escapes. |
| `StepWriter` | High-throughput ISO 10303-21 STEP physical file serializer. |
| `IfcGlbExporter` | Binary glTF 2.0 exporter generating specification-compliant `.glb` files with PBR materials. |
| `IfcObjExporter` | Wavefront OBJ geometry exporter with MTL material library generation. |
| `IfcZipArchive` | Pure managed PKZip container compression and decompression for `.ifczip` files. |

## Coordination & Validation

| Type | Description |
|---|---|
| `IfcClashDetector` | Broad-phase spatial index and narrow-phase Möller triangle collision engine for IFC models. |
| `IfcDiffEngine` | Revision comparison engine detecting added, deleted, and modified IFC elements and geometry shifts. |
| `IfcValidator` | Structural STEP syntax and buildingSMART schema conformance validator. |
| `IfcBcfExporter` | buildingSMART BIM Collaboration Format (BCF 2.1 `.bcfzip`) issue and viewpoint generator. |
| `BcfArchive` | Bidirectional BCF 2.1 archive container for reading, querying, editing, and saving `.bcfzip` files. |

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
