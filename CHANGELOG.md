# Changelog

All notable changes to the `IfcIO` library are documented in this file.

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
