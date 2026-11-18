# Contributing to IfcIO

## Reporting Bugs

Open a [GitHub issue](https://github.com/IOExtend/IfcIO/issues) with:
- The IFC schema version (IFC2x3 or IFC4).
- The `IfcIO` version.
- A minimal code snippet or STEP physical file sample that reproduces the issue.
- Expected and actual results.

Report security vulnerabilities privately through the **Security** tab.

## Code Conventions & Standards

- Follow ISO 10303-21 (STEP) and buildingSMART IFC specification standards strictly.
- Pure managed .NET with zero third-party or native dependencies.
- Keep nullable reference types enabled.
- Warnings are treated as errors.

## Clean-room rule

All code is written independently from the ISO 10303-21 and buildingSMART
specifications. Do not add vendor code, SDKs, SDK internals, headers or
binaries, decompiled or leaked source, or code copied from other IFC
libraries. Do not add customer models or other confidential data, or names,
paths, sizes, GUIDs or other details of private sample files to the repository,
including tests, comments and documentation. Test inputs are built
synthetically in the tests; keep private test files outside version control.

Do not put personal data in code, documentation, test data or commit metadata. Use your GitHub noreply address as the commit email.

## Scene adapter

The frozen viewer, non-IFC exporters and coordination implementations live in
`src/IfcIO/Compatibility`. The regular build keeps their public namespaces,
signatures and assembly identity. Native IFC validation, including IDS, stays
with the engine. BCF reporting uses the compatibility implementation.

CI also builds the native engine and its scene adapter with the compatibility
sources excluded. This check is non-packable and uses a separate artifact path.

```sh
dotnet build src/IfcIO.Scene/IfcIO.Scene.csproj -c Release -p:BuildNativeCoreOnly=true --artifacts-path ./artifacts/native-core-check
```

`src/IfcIO.Scene` converts IfcIO models into `IOScene` documents and uses only the public API of `IfcIO`; the `IfcIO`
package itself keeps no dependencies. `IOScene` is referenced with the range `[0.1.0, 0.2.0-0.0)`, and restore takes the
lowest version in it, 0.1.0 from NuGet.org. To build against an `IOScene` version that is not on NuGet.org yet, start
the range in `Directory.Packages.props` at that version (for a new minor, move the upper bound too, for example
`[0.2.0-preview.1, 0.3.0-0.0)`) and restore from a local folder of packages:
`dotnet build IfcIO.slnx -p:RestoreAdditionalProjectSources=<folder>`. Changing the `SceneModel.Key`, `SourceId` or
`IfcGuid` the adapter writes, its `ifc:` key format, or the value form (`Text`, `Kind`, `Unit`) it writes under a key
fails the golden identity test and is a breaking release.

## Releases & NuGet Publishing

Push a tag matching the package version, such as `v0.1.0`, to trigger the `release.yml` workflow.
The workflow builds and tests the solution, packs packages, publishes the GitHub Release, and publishes packages to NuGet.org via Trusted Publishing (GitHub Actions OIDC).
The NuGet policy on NuGet.org must name this repository (`IOExtend/IfcIO`), workflow `release.yml`, publisher `GitHubActions`, package pattern `IfcIO*`, and owner `mahach`.

## Licensing Contributions

All contributions are licensed under the [MIT License](LICENSE). By submitting a pull request, you confirm that you have the right to contribute your code under that license.
