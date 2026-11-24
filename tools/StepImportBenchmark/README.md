# STEP import benchmark

Build with the repository's .NET SDK, generate the fixture outside the checkout, then run the import in a new process:

```sh
dotnet build tools/StepImportBenchmark/StepImportBenchmark.csproj -c Release
dotnet tools/StepImportBenchmark/bin/Release/net10.0/StepImportBenchmark.dll generate <temporary-dir>/synthetic.ifc
dotnet tools/StepImportBenchmark/bin/Release/net10.0/StepImportBenchmark.dll open <temporary-dir>/synthetic.ifc
```

The generator writes over 110 MiB of STEP text with one wall, a reachable 20,000-triangle mesh, and many unreferenced
property records. It is a controlled exercise of STEP parsing and type scans, not a representative project.
Pass a real IFC path to `open` for measurements on your workload.

`open` reports elapsed wall time, process-wide managed allocation and peak working set. Peak working set includes
runtime overhead. Use fresh processes and several alternating runs for before/after comparisons; file-system
cache and process state can affect both time and memory.
