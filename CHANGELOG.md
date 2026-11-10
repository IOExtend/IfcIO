# Changelog

All notable changes to the `IfcIO` library are documented in this file.

## [0.2.0] - 2026-11-10

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
