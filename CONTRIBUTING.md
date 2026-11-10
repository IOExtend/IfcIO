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

## Releases & NuGet Publishing

Push a tag matching the package version, such as `v0.1.0`, to trigger the `release.yml` workflow.
The workflow builds and tests the solution, packs packages, publishes the GitHub Release, and publishes packages to NuGet.org via Trusted Publishing (GitHub Actions OIDC).
The NuGet policy on NuGet.org must name this repository (`IOExtend/IfcIO`), workflow `release.yml`, publisher `GitHubActions`, package pattern `IfcIO*`, and owner `mahach`.

## Licensing Contributions

All contributions are licensed under the [MIT License](LICENSE). By submitting a pull request, you confirm that you have the right to contribute your code under that license.
