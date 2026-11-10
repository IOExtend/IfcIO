---
title: IfcIO
---

# IfcIO

**IfcIO** is a fast, 100% managed .NET library for building, reading, and exporting **Industry Foundation Classes** (`.ifc`) files compliant with **IFC2x3 Coordination View V2.0** and **IFC4 Design Transfer / Reference View** without third-party dependencies or native code.

Built clean-room under the [IOExtend](https://github.com/IOExtend) organization as the shared open-standards IFC foundation for AEC/BIM projects (including [NwcIO](https://github.com/IOExtend/NwcIO) and [RvtIO](https://github.com/IOExtend/RvtIO)).

## Key Features

- **Full Model Ingestion & Export (`IfcModel`)**:
  - Reconstruct complete spatial hierarchy (Project, Site, Building, Storeys), elements, properties, materials, and 3D meshes from any `.ifc` or `.ifczip` file.
  - Multi-line STEP statement accumulation and quote-aware tokenization in `StepReader` and high-throughput streaming in `StepWriter`.
- **IFC4 Modern Mesh Tessellation (`IfcTriangulatedFaceSet`)**:
  - Encodes geometry using compact `IfcTriangulatedFaceSet` and `IfcCartesianPointList3D` under representation type `'Tessellation'` for IFC4 exports (>60% smaller file sizes than legacy faceted B-Rep).
- **Direct 3D Exporters (glTF 2.0 & Wavefront OBJ)**:
  - Instant conversion of any IFC model to binary glTF 2.0 (`.glb`) with PBR metallic-roughness materials and vertex normals, or Wavefront OBJ (`.obj` + `.mtl`).
- **PKZip Compressed IFC (`.ifczip`)**:
  - Built-in transparent compression and decompression for standard `.ifczip` archives with zero native dependencies.
- **Cross-Platform CLI Tool (`ifcio`)**:
  - Global .NET CLI tool (`IfcIO.Cli` on NuGet) providing `info`, `export`, `pack`, `unpack`, `clash`, `diff`, `view`, and `validate` commands.
- **buildingSMART 22-Character GlobalId (`IfcGuid`)**:
  - Standard base64-like buildingSMART encoding (`0-9, A-Z, a-z, _, $`). Bidirectional conversion between 128-bit `.NET Guid` and 22-character IFC GlobalId.
- **3D Geometric Clash Detection & Revision Diff**:
  - Built-in Möller triangle collision, proximity clustering, structural grid location assignment, revision comparison, and buildingSMART BCF 2.1 zip reporting.

The library targets .NET 8 and .NET 10, uses 100% pure managed code with zero native dependencies, and supports Windows, Linux, and macOS.

## Where to go next

- [Getting started](getting-started.md): install the package or CLI tool and run a first export or model query.
- [Public API](public-api.md): supported surface and key entry points of the library.
- [API reference](api/IfcIO.yml): complete API documentation generated directly from XML source docstrings.
- [Source on GitHub](https://github.com/IOExtend/IfcIO) and [DeepWiki](https://deepwiki.com/IOExtend/IfcIO) for an automated architectural overview of the repository.

IfcIO is licensed under the MIT License and is an independent implementation of the ISO 10303-21 STEP and buildingSMART specifications. buildingSMART and IFC are registered trademarks of buildingSMART International Ltd.
