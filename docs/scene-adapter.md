---
title: Scene adapter
---

# IfcIO.Scene: IFC to IOScene

`IfcIO.Scene` converts an IFC file read by IfcIO into an `IOScene` `SceneDocument`: one scene model in metres with
Z up, with elements, storeys, property sets, quantities, materials and 4D schedules. Viewers and coordination tools
that read scenes work from it. The scene model and its rules are specified in the
[IOScene contract](https://github.com/IOExtend/IOScene/blob/main/docs/contract.md); property keys are registered in
its [key registry](https://github.com/IOExtend/IOScene/blob/main/docs/keys.md).

Status: preview. The `IfcIO.Scene` package is released with IfcIO, on the same tag, and depends on exactly the same
`IfcIO` version. It references `IOScene` with the range `[0.1.0, 0.2.0-0.0)`: any 0.1 release from 0.1.0 on, and no
preview of the next breaking line except `0.2.0-0`, which IOScene does not publish. Its first release is 0.11.1.

## Usage

```csharp
using IfcIO.Scene;
using IOScene;

var result = IfcSceneReader.Read("house.ifc");
foreach (var diagnostic in result.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code} {diagnostic.SourceId}: {diagnostic.Message}");
}
SceneJson.Save(result.Document, "house.ioscene.json");
```

`IfcSceneReader.Read(path, options)` opens `.ifc` and `.ifczip` files with `IfcModel.Open`. A model already in memory
is converted with `IfcSceneReader.Read(model, options)`; it has no file name, so `IfcSceneOptions.ModelKey` is
required. An empty `ModelKey` throws `ArgumentException` in both methods. `IfcSceneOptions.IncludeSpaces` also converts spaces (off by default, because closed room volumes hide the
elements inside them).

`IfcSceneResult` holds the `Document`, the `Diagnostics` of the conversion, the model's `LengthUnit` and the exact
`MetresPerUnit` factor, and the counts `ElementCount`, `ElementsWithGeometry`, `GeometryCount` and `SkippedTasks`.

## Mapping

| IFC, as IfcIO reads it | Scene |
|---|---|
| the file | one `SceneModel`: `Key` = `ModelKey`, else the file name without directory (`house.ifc`); `SourceFormat` `ifc`; `SourceLengthUnit` `mm`, `m`, `ft` or `in`; `Producer` `IfcIO.Scene <version>` |
| storey | a `SceneLevel` (elevation in metres) and a tree node of kind `Level` with the storey's GlobalId |
| element (`IfcModel.AllElements`) | a `SceneElement` of kind `Element`, child of its storey node: `Id` = STEP id, `SourceId` = `IfcGuid` = GlobalId, `Category` = IFC class in upper case, `TypeName` = type product name |
| property set, quantity set | a `ScenePropertySet` with key `ifc:<SetName>`, properties `ifc:<SetName>.<Name>`; a set shared by elements in the file is one scene set. `PropertySetIds` lists the element's property sets, then its quantity sets |
| property value | `Text`, as IfcIO reads it |
| count quantity | `Number` with `Unit` null (dimensionless) |
| length, area, volume, weight quantity | `Text` only: the number as IfcIO reads it, in the file's units, not converted |
| element mesh | one `SceneGeometry` (triangles, metres, world coordinates) placed once by a `SceneInstance` without transform |
| surface style colour | a `SceneMaterial` (alpha is the opacity); elements without one get a colour for their IFC class on the instance |
| work schedule | a `SceneSchedule` per work schedule with tasks; task `Id` = task GlobalId, elements by scene id |

`(SceneModel.Key, SourceId)` is the identity that may be stored outside the scene. How this adapter derives both, and
`IfcGuid`, the `ifc:` key format and the value form (`Text`, `Kind`, `Unit`) it writes under each key, are frozen and
covered by a golden identity test (`tests/IfcIO.Scene.Tests/Fixtures/placement.identity.txt`); changing any of them
is a breaking release.

### Values

When a `SceneValue` has a `Number`, its `Unit` is authoritative and a null `Unit` means dimensionless. The adapter
converts no property or quantity value to SI, so it writes a number only when the value is dimensionless:

- A count quantity (`IfcQuantityCount`) is `Number` with `Unit` null; `Text` is the same number.
- Length, area, volume and weight quantities are in the file's units, which IfcIO does not report for quantities, so
  they are `Kind = Text` with the number only in `Text` (invariant culture, as IfcIO reads it). IfcIO does not read
  time quantities.
- Property values are `Text` as IfcIO reads them. IfcIO keeps a value read from a file as text without its measure
  type (`IfcLengthMeasure`, `IfcInteger`, ...), so the adapter cannot tell a dimensionless value from a dimensioned
  one and does not type it.

The value form written under each key (`Text`, `Kind`, `Unit`) is frozen with the key format.

### Coordinates

Vertices are in world coordinates, in metres, and every instance has no transform, which the contract allows. A file
placed far from the origin (for example at map coordinates) therefore gives large vertex coordinates; a consumer
rebases them, for example on `ComputeBounds().Center`, before narrowing them to float32.

## Limits

- **IFC4 tessellated geometry is not read.** IfcIO's reader returns no mesh for `IfcTriangulatedFaceSet`, including
  the one IfcIO itself writes for IFC4 models: the point and index lists lose their inner brackets while being parsed,
  so no triangle is found. Such elements are in the scene without geometry and reported as `no-geometry`. Verified
  with an IFC4 model saved by IfcIO and opened again; a test pins this behaviour.
- Mapped items (`IfcMappedItem`) are baked into each element's world mesh, so shared geometry is not shared in the
  scene.
- IfcIO places an element in a storey only through `IfcRelContainedInSpatialStructure`. Elements contained in the site
  or building, and spaces aggregated to a storey (`IfcRelAggregates`), are in the first storey. A file without storeys
  gets IfcIO's default storey `Level 1`, which has no GlobalId. Site, building and project are not tree nodes.
- IfcIO lists type objects (`IfcWallType`, `IfcDoorStyle`) and door and window lining and panel properties among the
  elements; the adapter skips them. Type property sets are not read yet; when they are, they will use the same
  `ifc:` keys and come after the occurrence sets in `PropertySetIds`, so a lookup by key finds the occurrence value
  first.
- Property values are text even when the IFC value is a number or a boolean: IfcIO keeps only the text.
- Length, area, volume and weight quantities are text in file units. For a file whose length unit IfcIO converts to
  metres while reading (for example centimetres), geometry is in metres but quantities stay in file units.
- An element without a GlobalId in the file gets a new random GlobalId from IfcIO each time it is read, so its
  `SourceId` is not stable.
- Transparency is not read from files by IfcIO; materials built in memory keep their alpha.
- Tasks without planned start and finish are left out (IfcIO reads task times from IFC4 files only); their subtasks
  are kept. Task costs and progress are not converted. Openings, ports, grids, zones, systems and cost schedules are
  not converted.
- The application, author and creation date of the file are not read by IfcIO, so the scene model leaves them empty.
