<p align="center">
  <img src="docs/images/ifcio-banner.png" alt="IfcIO" width="640">
</p>

# IfcIO

[![Docs](https://img.shields.io/badge/Docs-Online-A855F7?style=flat-square&logo=github)](https://ioextend.github.io/IfcIO/)
[![NuGet](https://img.shields.io/nuget/v/IfcIO?style=flat-square&color=A855F7&logo=nuget)](https://www.nuget.org/packages/IfcIO)
[![Release](https://img.shields.io/github/v/release/IOExtend/IfcIO?style=flat-square&color=A855F7)](https://github.com/IOExtend/IfcIO/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

IfcIO is a fast, 100% managed .NET library and command line tool for building, reading, and exporting Industry Foundation Classes (`.ifc`, `.ifczip`) compliant with IFC2x3 and IFC4 without native dependencies.

Built clean-room under the [IOExtend](https://github.com/IOExtend) organization alongside [RvtIO](https://github.com/IOExtend/RvtIO) and [NwcIO](https://github.com/IOExtend/NwcIO).

## What it does

- Reads and writes ISO 10303-21 STEP physical files compliant with IFC2x3 Coordination View V2.0 and modern compact IFC4 (`IfcTriangulatedFaceSet`).
- Reconstructs complete spatial hierarchy (Project, Site, Building, Storeys), elements, properties, materials, and 3D meshes.
- Direct conversion to binary glTF 2.0 (`.glb`), Wavefront OBJ, transparent `.ifczip` compression, and structured JSON.
- Coordinates 4D construction scheduling (`IfcWorkSchedule`) and 5D cost budgeting (`IfcCostSchedule`) with temporal simulation and CBS rollups.
- High-level, script-like `IfcApi` facade conforming to IfcOpenShell conventions (`IfcApi.Sequence`, `IfcApi.Cost`, `IfcApi.Spatial`, `IfcApi.Root`).
- Collision detection, revision comparison, buildingSMART IDS 1.0 audit, and bidirectional BCF 2.1 archive container (`BcfArchive`).
- Exports standalone, zero-dependency interactive 3D WebGL HTML viewers (`.html`).

## Install

Install the command line tool:

```sh
dotnet tool install --global IfcIO.Cli
```

Add the library to a .NET project:

```sh
dotnet add package IfcIO
```

The library targets .NET 8 and .NET 10. It uses managed code without native dependencies or Windows-only APIs and supports Windows, Linux, and macOS.

## Command line

Inspect, query, and export IFC models:

```sh
# Inspect model metadata, storeys, spaces, and systems
ifcio info Building.ifc --report

# Query elements matching IfcOpenShell-style selector
ifcio query Building.ifc "IfcWall[LoadBearing=TRUE]"

# Convert IFC to binary glTF 2.0 and Wavefront OBJ
ifcio export Building.ifc --glb scene.glb --obj scene.obj

# Compress or unpack .ifczip archives
ifcio pack Building.ifc -o Building.ifczip
ifcio unpack Building.ifczip -o Building.ifc
```

4D scheduling, 5D cost, and BCF coordination:

```sh
# 4D construction schedule temporal simulation and CSV export
ifcio schedule Building.ifc --sim-date 2026-06-01 --export-csv schedule.csv

# 5D cost breakdown and budget reporting
ifcio cost Building.ifc --export-csv cost.csv

# Inspect BCF 2.1 coordination archive topics and viewpoints
ifcio bcf Issues.bcfzip

# Run 3D geometric clash detection with proximity clustering
ifcio clash Arch.ifc Mep.ifc -o clashes.json --bcf clashes.bcfzip --group proximity

# Compare two model revisions (added, deleted, modified)
ifcio diff Baseline.ifc Revision.ifc -o changes.json --bcf changes.bcfzip

# Export standalone 3D WebGL HTML viewer with sectioning and measure tools
ifcio view Building.ifc -o viewer.html --grids grids.xml
```

Diagnostics and progress go to standard error. Run `ifcio --help` for the full list of options.

## Library

Create an IFC4 model and save to STEP and glTF:

```csharp
using IfcIO;
using IfcIO.Geometry;
using IfcIO.Schema;

// Create a new IFC4 model
var model = IfcModel.Create("Commercial Office", IfcSchema.Ifc4);
var storey = model.AddStorey("Level 1", elevation: 0.0);

// Add building element with geometry
var wall = storey.AddWall("Exterior Wall 1");
wall.PropertySets.Add(StandardPsets.WallCommon(loadBearing: true, fireRating: "REI 120"));

// Save directly to STEP, compressed .ifczip, and glTF 2.0
model.Save("Building.ifc");
model.SaveZip("Building.ifczip");
model.SaveGlb("Building.glb");
```

High-level 4D scheduling and 5D cost planning via `IfcApi`:

```csharp
using IfcIO;
using IfcIO.Api;

var model = IfcModel.Open("Building.ifc");
var wall = model.FindElementByName("Exterior Wall 1");

// 4D construction schedule
var schedule = IfcApi.Sequence.AddWorkSchedule(model, "Master Schedule");
var task = IfcApi.Sequence.AddTask(model, schedule, "Wall Framing", new DateTime(2026, 2, 1), new DateTime(2026, 3, 1));
IfcApi.Sequence.AssignProcess(task, wall!);

// 5D cost breakdown
var budget = IfcApi.Cost.AddCostSchedule(model, "Base Budget", currency: "USD");
var costItem = IfcApi.Cost.AddCostItem(budget, "Masonry", unitRate: 150.0, quantity: 200.0);
IfcApi.Cost.AssignCostControl(costItem, wall!);

model.Save("Building_4D_5D.ifc");
```

See the [public API](docs/public-api.md) for the supported surface and [getting started guide](docs/getting-started.md) for detailed workflows.

`IfcIO.Scene` (a separate package released with `IfcIO`, from `IOScene` 0.1.0 on) converts IFC models into `IOScene` scene documents for viewers and coordination tools; see the [scene adapter](docs/scene-adapter.md) page for usage and limits.

## Documentation

The documentation site with API references and guides is at <https://ioextend.github.io/IfcIO/>. [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/IOExtend/IfcIO) offers an automatically generated overview of the repository.

## Supported files

Reads and writes ISO 10303-21 STEP physical files conforming to **IFC2x3 Coordination View V2.0** and **IFC4 Design Transfer / Reference View**, compressed PKZip `.ifczip` archives, and buildingSMART BCF 2.1 `.bcfzip` packages.

## Limitations

- Version 0.11.0 is a preview. The API can evolve before 1.0.
- IFC5 (USD/JSON) is not yet supported.
- Boolean mesh cutting evaluates planar CSG half-spaces; non-planar curved intersections are tessellated.

## Scope and frozen features

IfcIO reads and writes IFC. The following extras are frozen: they get bug fixes only, no new features or options.

- HTML viewer (`IfcHtmlViewerExporter`, `ifcio view`). Viewing will move to [IOViewer](https://github.com/IOExtend/IOViewer) through the `IfcIO.Scene` adapter over the shared `IOScene` model.
- GLB and OBJ export (`IfcGlbExporter`, `IfcObjExporter`). A shared GLB writer is planned in `IOScene.Formats`.
- Clash detection, measure, grids, diff, and BCF archives (`IfcClashDetector`, `IfcClashGrouper`, `IfcMeasureEngine`, `IfcGridSystem`, `IfcDiffEngine`, `BcfArchive`, `BcfZipExporter`). These will move to a planned `IOCoord` package.
- IFC work-schedule (4D) sequence logic. It stays in IfcIO.

None of these replacements is published yet. The frozen features stay available and are removed no earlier than IfcIO 1.0, and only after a published replacement exists.

## Build from source and tests

Use the .NET SDK selected by `global.json`, which specifies 10.0.100 with `latestFeature` roll-forward.

From the repository root:

```sh
dotnet build IfcIO.slnx -c Release
dotnet test
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md). Release changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## Security

Report vulnerabilities through GitHub private vulnerability reporting in the repository's Security tab. Do not open public issues for security vulnerabilities. See [SECURITY.md](SECURITY.md).

## License and trademarks

IfcIO is licensed under the [MIT License](LICENSE). Copyright &copy; 2026 [IOExtend](https://github.com/IOExtend).

buildingSMART and IFC are registered trademarks of buildingSMART International Ltd.

IfcIO is an independent implementation of the ISO 10303-21 STEP and buildingSMART specifications. No proprietary code or binaries are included.
