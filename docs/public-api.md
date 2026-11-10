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
