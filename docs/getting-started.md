---
title: Getting started
---

# Getting Started with IfcIO

## Installation

### NuGet Package

Install the `IfcIO` library package in your .NET project:

```bash
dotnet add package IfcIO
```

### Global CLI Tool

Install the `ifcio` command line tool globally:

```bash
dotnet tool install --global IfcIO.Cli
```

---

## Quick Examples

### Reading an IFC File

```csharp
using IfcIO;

// Open any .ifc or compressed .ifczip file
var model = IfcModel.Open("Building.ifc");

Console.WriteLine($"Project: {model.Project?.Name}");
Console.WriteLine($"Total elements: {model.Elements.Count}");

foreach (var storey in model.Storeys)
{
    Console.WriteLine($"Storey: {storey.Name} (Elevation: {storey.Elevation}m)");
}
```

### Building an IFC4 Model Programmatically

```csharp
using IfcIO;
using IfcIO.Geometry;

var model = new IfcModel(IfcSchemaVersion.Ifc4)
{
    ProjectName = "Commercial Office Tower",
    Author = "Engineering Team",
    Organization = "IOExtend"
};

var groundFloor = model.AddStorey("Ground Floor", elevation: 0.0);

// Add a wall with modern IFC4 mesh tessellation
groundFloor.AddWall("Exterior Wall 1", new IfcMeshGeometry(vertices, indices));

// Export to ISO 10303-21 STEP physical file
model.Save("OfficeTower.ifc");

// Or save compressed as .ifczip
model.SaveZip("OfficeTower.ifczip");
```

### Exporting to glTF 2.0 (GLB) and Wavefront OBJ

```csharp
using IfcIO;
using IfcIO.Exporters;

var model = IfcModel.Open("Building.ifc");

// Binary glTF 2.0 for 3D web viewers and game engines
IfcGlbExporter.Export(model, "Building.glb");

// Wavefront OBJ
IfcObjExporter.Export(model, "Building.obj");
```

---

## CLI Usage

Run standard operations from your terminal:

```bash
# Display summary info, schema version, and element counts
ifcio info Building.ifc

# Convert IFC to binary glTF 2.0 and Wavefront OBJ
ifcio export Building.ifc --glb scene.glb --obj scene.obj

# Compress IFC to .ifczip
ifcio pack Building.ifc -o Building.ifczip

# Extract IFC from .ifczip
ifcio unpack Building.ifczip -o Building.ifc

# Run 3D geometric clash detection with proximity clustering
ifcio clash Arch.ifc Mep.ifc -o Clashes.json --bcf Clashes.bcfzip --group proximity

# Compare two IFC model revisions (added, deleted, modified)
ifcio diff Baseline.ifc Revision.ifc -o Changes.json --bcf RevisionChanges.bcfzip

# Validate ISO 10303-21 STEP structure and export BCF issues
ifcio validate Building.ifc --bcf Issues.bcfzip
```
