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

## Scene adapter

`src/IfcIO.Scene` converts IfcIO models into `IOScene` documents and uses only the public API of `IfcIO`; the `IfcIO`
package itself keeps no dependencies. `IOScene` is referenced with the range `[0.1.0-preview.2, 0.2.0-0)`. To build
against an `IOScene` preview that is not on NuGet.org yet, restore from a local folder of packages:
`dotnet build IfcIO.slnx -p:RestoreAdditionalProjectSources=<folder>`. Changing the `SceneModel.Key` or `SourceId`
the adapter writes, or its `ifc:` key format, fails the golden identity test and is a breaking release.

## Releases & NuGet Publishing

Push a tag matching the package version, such as `v0.1.0`, to trigger the `release.yml` workflow.
The workflow builds and tests the solution, packs packages, publishes the GitHub Release, and publishes packages to NuGet.org via Trusted Publishing (GitHub Actions OIDC).
The NuGet policy on NuGet.org must name this repository (`IOExtend/IfcIO`), workflow `release.yml`, publisher `GitHubActions`, package pattern `IfcIO*`, and owner `mahach`.

## Licensing Contributions

All contributions are licensed under the [MIT License](LICENSE). By submitting a pull request, you confirm that you have the right to contribute your code under that license.
