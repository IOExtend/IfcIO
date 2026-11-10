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

// Attach Property Sets
var pset = wall.GetOrCreatePropertySet("Pset_WallCommon");
pset.Add("LoadBearing", true);
pset.Add("Thickness", 200.0);
pset.Add("FireRating", "REI 120");

// Save to multiple formats with one call:
model.Save("Building.ifc");          // Standard STEP file
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

---

## Global CLI Tool (`ifcio`)

Install the cross-platform command-line tool globally via NuGet:

```sh
dotnet tool install --global IfcIO.Cli
```

### CLI Commands

```sh
# Inspect model metadata, storeys, and element distribution
ifcio info Building.ifc --report

# Convert IFC to binary glTF 2.0 and Wavefront OBJ
ifcio export Building.ifc --glb scene.glb --obj scene.obj

# Compress IFC to .ifczip
ifcio pack Building.ifc -o Building.ifczip

# Extract IFC from .ifczip
ifcio unpack Building.ifczip -o Building.ifc

# Validate ISO 10303-21 structure and schema
ifcio validate Building.ifc
```

---

## License and trademarks

IfcIO is licensed under the [MIT License](LICENSE). Copyright &copy; 2026 [IOExtend](https://github.com/IOExtend).

buildingSMART and IFC are registered trademarks of buildingSMART International Ltd.

IfcIO is an independent implementation of the ISO 10303-21 STEP and buildingSMART specifications. No proprietary code or binaries are included.
