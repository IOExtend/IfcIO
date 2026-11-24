# IFC product occurrence index

`generate_product_occurrences.py` reads buildingSMART EXPRESS entity declarations and generates
`src/IfcIO/Schema/IfcProductOccurrences.g.cs`. Only derived entity names, schema membership, STEP
tag slots and subtraction-feature classification are checked in. No schema file or generator dependency is needed at runtime.

Regenerate using these buildingSMART sources:

- [IFC2x3 TC1](https://github.com/buildingSMART/IFC4.x-development/blob/0f1c7acb287740d7d40e2d0021c216a9cdf638ab/reference_schemas/IFC2X3_TC1.exp)
- [IFC4 ADD2 TC1](https://github.com/buildingSMART/IFC4.x-development/blob/0f1c7acb287740d7d40e2d0021c216a9cdf638ab/reference_schemas/IFC4_ADD2_TC1.exp)
- [IFC4.3 ADD2](https://github.com/buildingSMART/IFC4.x-output/blob/610ee3f95db9d0380b1eb8de4dc6e17833531518/IFC/RELEASE/IFC4_3/HTML/IFC4X3_ADD2.exp)

```sh
python tools/generate_product_occurrences.py IFC2X3_TC1.exp IFC4_ADD2_TC1.exp IFC4X3_ADD2.exp
```

The classifier includes concrete descendants of `IfcProduct`, excluding ports and spatial products except `IfcSite` terrain.
The reader separately preserves its legacy metadata-only imports.
