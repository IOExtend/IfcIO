using System.Diagnostics;
using System.Globalization;
using IfcIO.Clash;
using IfcIO.Core;
using IfcIO.Model;
using IfcIO.Schema;

namespace IfcIO.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        if (args[0] is "-v" or "--version" or "version")
        {
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            Console.WriteLine($"IfcIO.Cli v{version} (.NET {Environment.Version})");
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] cmdArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "info" => ExecuteInfo(cmdArgs),
                "export" => ExecuteExport(cmdArgs),
                "pack" => ExecutePack(cmdArgs),
                "unpack" => ExecuteUnpack(cmdArgs),
                "validate" => ExecuteValidate(cmdArgs),
                "validate-ids" => ExecuteValidateIds(cmdArgs),
                "query" => ExecuteQuery(cmdArgs),
                "patch" => ExecutePatch(cmdArgs),
                "export-csv" => ExecuteExportCsv(cmdArgs),
                "import-csv" => ExecuteImportCsv(cmdArgs),
                "clash" => ExecuteClash(cmdArgs),
                "diff" => ExecuteDiff(cmdArgs),
                "schedule" or "4d" => ExecuteSchedule(cmdArgs),
                "cost" or "5d" => ExecuteCost(cmdArgs),
                "bcf" => ExecuteBcf(cmdArgs),
                "view" or "html" => ExecuteView(cmdArgs),
                _ when File.Exists(args[0]) => ExecuteDefault(args),
                _ => PrintUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"IfcIO - Pure Managed Industry Foundation Classes (IFC) CLI Tool
Usage:
  ifcio <command> [options]

Commands:
  info <file>           Inspect IFC model metadata, spatial hierarchy, and geometry statistics
  export <file>         Export model to glTF 2.0 (.glb), Wavefront OBJ (.obj), or re-encode IFC
  pack <file.ifc>       Compress an .ifc physical file into a standard .ifczip archive
  unpack <file.ifczip>  Extract the .ifc physical file from an .ifczip archive
  validate <file>       Validate ISO 10303-21 STEP structure, header, and spatial relationships
  validate-ids <file> <spec.ids> Validate model against buildingSMART IDS 1.0 XML specification
  query <file> <query>  Filter and inspect elements using IfcOpenShell-style selector expressions
  patch <file> [opts]   Apply cleanup, coordinate reset, or extraction recipes to an IFC model
  export-csv <file> <out.csv> Export elements and properties to tabular CSV format
  import-csv <file> <in.csv> -o <out.ifc> Update property sets from a CSV spreadsheet
  clash <fileA> [fileB] Run 3D geometric clash detection (cross-model or self-clash)
  diff <baseline> <rev> Compare two IFC model revisions (added, deleted, modified)
  schedule <file.ifc>   Inspect 4D construction schedule, simulation progress, and export CSV
  cost <file.ifc>       Inspect 5D cost schedule, budgets, takeoffs, and export CSV
  bcf <file.bcfzip>     Inspect buildingSMART BCF 2.1 zip archive topics, viewpoints, comments
  view <file.ifc> [-o out.html] Export standalone 3D WebGL HTML viewer (.html)

Clash & Diff Options:
  --tolerance <meters>  Distance tolerance in meters (clash clearance or diff geometric threshold)
  --group <mode>        Group clashes: proximity, element, storey, grid
  --radius <meters>     Cluster radius in meters for proximity grouping (default: 1.0)
  --bcf <path.bcfzip>   Export clash or diff issues directly to buildingSMART BCF 2.1 archive
  --selector-a <query>  Filter Model A elements for clash detection (e.g. 'IfcWall, IfcColumn')
  --selector-b <query>  Filter Model B elements for clash detection (e.g. 'IfcFlowSegment')
  --filter <query>      Filter elements to compare in diff command
  -o, --output <path>   Output JSON report or HTML viewer file path

Patch Options:
  --recipe <name>       Apply named patch recipe: migrate, scale, offset, purge, reset
  --scale <factor>      Uniformly scale model geometry (e.g. 0.001 to convert mm to m)
  --migrate <schema>    Migrate model schema version (ifc2x3 or ifc4)
  --purge               Remove empty/duplicate property sets, quantity sets, and empty storeys
  --reset-coords        Shift large UTM coordinates to local origin (0,0,0) to prevent jitter
  --offset <x,y,z>      Translate model geometry by given coordinate offset
  --legacy-ifcio-radian-sweeps
                       Repair old IfcIO files that stored sweep angles in radians despite declaring degrees
  --extract <query>     Extract matching elements into a separate IFC model
  -o, --output <path>   Output file path for patched IFC model

Schedule, Cost & View Options:
  --sim-date <date>     Simulate schedule progress on a date (schedule only, YYYY-MM-DD)
  --export-csv <path>   Export schedule or cost data to CSV (schedule and cost)
  --title <title>       Set the HTML page title (view only)

Options:
  --grids <path.xml>    Load a spatial grid system from XML (clash and view)
  --export-grids <path> Export the extracted or loaded grid system to XML (clash and view)
  --glb <path>          Output binary glTF 2.0 file (.glb)
  --obj <path>          Output Wavefront OBJ file (.obj)
  --ifc <path>          Output IFC STEP physical file (.ifc)
  --ifczip <path>       Output compressed IFC archive (.ifczip)
  --bcf <path>          Export validation issues to buildingSMART BCF 2.1 zip archive (.bcfzip)
  --schema <ifc2x3|ifc4> Target schema for IFC export
  --props <names>       Comma-separated property names for export-csv (e.g. 'FireRating,LoadBearing')
  --delimiter <delim>   CSV delimiter character (default: ';')
  --spaces              Display architectural spaces, rooms, and boundaries
  --systems             Display MEP systems and grouped elements
  --report              Display detailed category and storey breakdown in info command
  -h, --help            Show this help message
  -v, --version         Show tool version

Examples:
  ifcio info Building.ifc --report --spaces --systems
  ifcio query Building.ifc ""IfcWall[LoadBearing=TRUE]""
  ifcio patch Building.ifc --purge --reset-coords -o Cleaned.ifc
  ifcio patch Building.ifc --extract ""IfcWall"" -o Walls.ifc
  ifcio validate-ids Building.ifc Rules.ids --bcf AuditIssues.bcfzip
  ifcio export-csv Building.ifc Params.csv --props ""Pset_WallCommon.FireRating,LoadBearing""
  ifcio export Building.ifc --glb scene.glb
  ifcio validate Building.ifc --bcf issues.bcfzip
  ifcio pack Building.ifc -o Building.ifczip
");
    }

    private static int PrintUnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: '{cmd}'. Run 'ifcio --help' for usage.");
        return 1;
    }

    private static int ExecuteDefault(string[] args)
    {
        // Direct file passed: 'ifcio model.ifc' -> defaults to info
        return ExecuteInfo(args);
    }

    private static int ExecuteInfo(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing input file path.");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        if (args.Skip(1).Any(arg => arg.Equals("--grids", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--export-grids", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Error: --grids and --export-grids are supported by clash and view, not info.");
            return 1;
        }

        bool detailedReport = args.Contains("--report", StringComparer.OrdinalIgnoreCase);
        bool showSpaces = args.Contains("--spaces", StringComparer.OrdinalIgnoreCase);
        bool showSystems = args.Contains("--systems", StringComparer.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        sw.Stop();

        int elementCount = 0;
        int meshCount = 0;
        int triCount = 0;

        var typeDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var elem in model.AllElements)
        {
            elementCount++;
            string type = elem.IfcType;
            typeDistribution[type] = typeDistribution.GetValueOrDefault(type, 0) + 1;

            if (elem.Mesh is not null)
            {
                meshCount++;
                triCount += elem.Mesh.TriangleCount;
            }
        }

        var fileInfo = new FileInfo(inputPath);
        double sizeMb = fileInfo.Length / (1024.0 * 1024.0);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine($" IfcIO Model Summary: {Path.GetFileName(inputPath)}");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        Console.WriteLine($"  File size:         {sizeMb:F2} MB ({fileInfo.Length:N0} bytes)");
        Console.WriteLine($"  Parsed in:         {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Schema:            {model.Schema}");
        Console.WriteLine($"  Project Name:      {model.ProjectName}");
        Console.WriteLine($"  Building Name:     {model.BuildingName}");
        Console.WriteLine($"  Storeys:           {model.Storeys.Count}");
        Console.WriteLine($"  Total Elements:    {elementCount:N0}");
        Console.WriteLine($"  Spaces:            {model.AllSpaces.Count():N0}");
        Console.WriteLine($"  Systems:           {model.Systems.Count}");
        Console.WriteLine($"  Groups:            {model.Groups.Count}");
        Console.WriteLine($"  Meshes:            {meshCount:N0}");
        Console.WriteLine($"  Triangles:         {triCount:N0}");
        Console.WriteLine($"  Materials:         {model.Materials.Count}");

        if (model.Storeys.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Spatial Hierarchy (Storeys):");
            Console.ResetColor();
            string elevationUnit = model.LengthUnit switch
            {
                IfcLengthUnit.Millimetre => "mm",
                IfcLengthUnit.Metre => "m",
                IfcLengthUnit.Foot => "ft",
                IfcLengthUnit.Inch => "in",
                _ => "?",
            };
            foreach (var storey in model.Storeys)
            {
                Console.WriteLine($"    - {storey.Name,-24} Elevation: {storey.Elevation,8:F2} {elevationUnit} | Elements: {storey.Elements.Count} | Spaces: {storey.Spaces.Count}");
            }
        }

        if ((detailedReport || showSpaces) && model.AllSpaces.Any())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  Architectural Spaces & Rooms:");
            Console.ResetColor();
            foreach (var space in model.AllSpaces)
            {
                Console.WriteLine($"    - [{space.SpaceNumber ?? $"{space.Id}"}] {space.Name,-20} Area: {space.GrossFloorArea,6:F1} m² | Height: {space.Height:F1} m | Boundaries: {space.Boundaries.Count}");
            }
        }

        if ((detailedReport || showSystems) && model.Systems.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  MEP & Building Systems:");
            Console.ResetColor();
            foreach (var sys in model.Systems)
            {
                Console.WriteLine($"    - {sys.Name,-24} Type: {sys.SystemType,-12} | Members: {sys.Elements.Count}");
            }
        }

        if (detailedReport && typeDistribution.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  BIM Element Classification Breakdown:");
            Console.ResetColor();
            foreach (var (type, count) in typeDistribution.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"    - {type,-30} {count,6} items");
            }
        }

        return 0;
    }

    private static int ExecuteExport(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing input file path.");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        string? glbPath = GetOptionValue(args, "--glb");
        string? objPath = GetOptionValue(args, "--obj");
        string? ifcPath = GetOptionValue(args, "--ifc");
        string? ifczipPath = GetOptionValue(args, "--ifczip");
        string? schemaOpt = GetOptionValue(args, "--schema");

        if (glbPath is null && objPath is null && ifcPath is null && ifczipPath is null)
        {
            // Default: export to .glb next to input
            glbPath = Path.ChangeExtension(inputPath, ".glb");
        }

        Console.WriteLine($"Reading {Path.GetFileName(inputPath)}...");
        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);

        if (schemaOpt is not null)
        {
            model.Schema = schemaOpt.Equals("ifc4", StringComparison.OrdinalIgnoreCase)
                ? IfcSchema.Ifc4
                : IfcSchema.Ifc2x3;
        }

        if (glbPath is not null)
        {
            var exportSw = Stopwatch.StartNew();
            model.SaveGlb(glbPath);
            exportSw.Stop();
            var info = new FileInfo(glbPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Exported glTF 2.0 (GLB): {glbPath} ({info.Length / 1024.0:F1} KB) in {exportSw.ElapsedMilliseconds} ms");
            Console.ResetColor();
        }

        if (objPath is not null)
        {
            var exportSw = Stopwatch.StartNew();
            model.SaveObj(objPath);
            exportSw.Stop();
            var info = new FileInfo(objPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Exported Wavefront OBJ: {objPath} ({info.Length / 1024.0:F1} KB) in {exportSw.ElapsedMilliseconds} ms");
            Console.ResetColor();
        }

        if (ifcPath is not null)
        {
            var exportSw = Stopwatch.StartNew();
            model.Save(ifcPath);
            exportSw.Stop();
            var info = new FileInfo(ifcPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Exported IFC: {ifcPath} ({info.Length / 1024.0:F1} KB) in {exportSw.ElapsedMilliseconds} ms");
            Console.ResetColor();
        }

        if (ifczipPath is not null)
        {
            var exportSw = Stopwatch.StartNew();
            model.SaveZip(ifczipPath);
            exportSw.Stop();
            var info = new FileInfo(ifczipPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Exported IFC Zip: {ifczipPath} ({info.Length / 1024.0:F1} KB) in {exportSw.ElapsedMilliseconds} ms");
            Console.ResetColor();
        }

        sw.Stop();
        Console.WriteLine($"Total elapsed: {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    private static int ExecutePack(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing input file path.");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        string outputPath = GetOptionValue(args, "-o") ?? Path.ChangeExtension(inputPath, ".ifczip");

        Console.WriteLine($"Packaging '{inputPath}' into '{outputPath}'...");
        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        model.SaveZip(outputPath);
        sw.Stop();

        var inInfo = new FileInfo(inputPath);
        var outInfo = new FileInfo(outputPath);
        double ratio = (1.0 - (double)outInfo.Length / inInfo.Length) * 100.0;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Compressed {inInfo.Length / 1024.0:F1} KB -> {outInfo.Length / 1024.0:F1} KB ({ratio:F1}% compression) in {sw.ElapsedMilliseconds} ms");
        Console.ResetColor();
        return 0;
    }

    private static int ExecuteUnpack(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing input .ifczip file path.");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        string outputPath = GetOptionValue(args, "-o") ?? Path.ChangeExtension(inputPath, ".ifc");

        Console.WriteLine($"Extracting '{inputPath}' to '{outputPath}'...");
        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        model.Save(outputPath);
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Extracted to '{outputPath}' in {sw.ElapsedMilliseconds} ms");
        Console.ResetColor();
        return 0;
    }

    private static int ExecuteValidate(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing input file path.");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        string? bcfPath = GetOptionValue(args, "--bcf");
        if (args.Skip(1).Contains("--bcf", StringComparer.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(bcfPath) || bcfPath.StartsWith("--", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("Error: --bcf requires an output file path.");
            return 1;
        }

        Console.WriteLine($"Validating ISO 10303-21 structure for '{Path.GetFileName(inputPath)}'...");
        var sw = Stopwatch.StartNew();

        int errors = 0;
        int warnings = 0;

        StepFile step;
        try
        {
            if (inputPath.EndsWith(".ifczip", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(inputPath);
                using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("  [ERROR] Archive does not contain an .ifc file.");
                    Console.ResetColor();
                    return 1;
                }
                using var s = entry.Open();
                using var r = new StreamReader(s);
                step = StepReader.Read(r);
            }
            else
            {
                step = StepReader.Read(inputPath);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"  [ERROR] Failed to parse STEP syntax: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        // 1. Schema check
        if (string.IsNullOrEmpty(step.Schema))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine("  [WARN] FILE_SCHEMA header declaration is missing.");
            Console.ResetColor();
            warnings++;
        }
        else
        {
            Console.WriteLine($"  ✓ Schema declared: {step.Schema}");
        }

        // 2. Project check
        var projects = step.OfType("IFCPROJECT").ToList();
        if (projects.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("  [ERROR] Model contains no IFCPROJECT entity.");
            Console.ResetColor();
            errors++;
        }
        else if (projects.Count > 1)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine($"  [WARN] Multiple IFCPROJECT entities found ({projects.Count}). Expected exactly 1.");
            Console.ResetColor();
            warnings++;
        }
        else
        {
            Console.WriteLine($"  ✓ IFCPROJECT present (ID #{projects[0].Id})");
        }

        // 3. Geometry check
        int brepCount = step.OfType("IFCFACETEDBREP").Count();
        int tessCount = step.OfType("IFCTRIANGULATEDFACESET").Count();
        Console.WriteLine($"  ✓ Geometry items: {brepCount} FacetedBRep, {tessCount} TriangulatedFaceSet");

        // 4. In-depth model rules validation
        IfcModel model;
        try
        {
            model = IfcModel.Open(inputPath);
            var valResult = model.Validate();
            foreach (var issue in valResult.Issues)
            {
                if (issue.Severity == Validation.IfcIssueSeverity.Error)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"  [ERROR] ({issue.RuleId}) {(issue.EntityName is not null ? $"{issue.EntityName}: " : "")}{issue.Message}");
                    Console.ResetColor();
                    errors++;
                }
                else if (issue.Severity == Validation.IfcIssueSeverity.Warning)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine($"  [WARN] ({issue.RuleId}) {(issue.EntityName is not null ? $"{issue.EntityName}: " : "")}{issue.Message}");
                    Console.ResetColor();
                    warnings++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [ERROR] Model validation could not complete: {ex.Message}");
            return 1;
        }

        sw.Stop();
        Console.WriteLine();

        if (errors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Validation PASSED ({warnings} warnings, {step.Entities.Count:N0} entities checked) in {sw.ElapsedMilliseconds} ms.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Validation FAILED with {errors} errors and {warnings} warnings.");
            Console.ResetColor();
        }

        if (bcfPath is not null)
        {
            try
            {
                WriteValidationBcf(model, inputPath, bcfPath);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  ✓ Exported validation issues to BCF 2.1: '{bcfPath}'");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [ERROR] Failed to write the requested BCF report '{bcfPath}': {ex.Message}");
                return 1;
            }
        }

        return errors == 0 ? 0 : 1;
    }

    private static void WriteValidationBcf(IfcModel model, string inputPath, string outputPath)
    {
        string destination = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(destination)
            ?? throw new IOException("The BCF destination must be a file path.");
        string source = Path.GetFullPath(inputPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (PhysicalPath(source).Equals(PhysicalPath(destination), comparison))
            throw new IOException("The BCF destination cannot be the input IFC file.");

        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            model.ExportValidationBcf(temporary);
            // Moving into place does not truncate a pre-existing destination or follow file links.
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string PhysicalPath(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is null) return path;
        string candidate = Path.Combine(PhysicalPath(parent), Path.GetFileName(path));
        FileSystemInfo? target = Directory.Exists(candidate) ? Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)
            : File.Exists(candidate) ? File.ResolveLinkTarget(candidate, returnFinalTarget: true) : null;
        return target is null ? candidate : target.FullName;
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int ExecuteQuery(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: 'ifcio query <model.ifc> <selector>' requires input model and query expression.");
            return 1;
        }

        string inputPath = args[0];
        string query = args[1];

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input file '{inputPath}' does not exist.");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        var matched = model.Select(query).ToList();
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Query: \"{query}\" matched {matched.Count:N0} element(s) in {sw.ElapsedMilliseconds} ms:");
        Console.ResetColor();

        Console.WriteLine($"{"ID",-8} | {"TYPE",-24} | {"NAME",-30} | {"GLOBAL ID",-24}");
        Console.WriteLine(new string('-', 95));

        foreach (var elem in matched.Take(100))
        {
            string name = elem.Name.Length > 28 ? elem.Name[..25] + "..." : elem.Name;
            Console.WriteLine($"{elem.Id,-8} | {elem.IfcType,-24} | {name,-30} | {elem.GlobalId ?? "N/A",-24}");
        }

        if (matched.Count > 100)
        {
            Console.WriteLine($"... and {matched.Count - 100} more matching elements.");
        }

        return 0;
    }

    private static int ExecutePatch(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Error: 'ifcio patch <model.ifc> [options]' requires an input model.");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input file '{inputPath}' does not exist.");
            return 1;
        }

        string? outputPath = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output");
        if (string.IsNullOrEmpty(outputPath))
        {
            Console.Error.WriteLine("Error: Patch command requires output path specified via -o or --output.");
            return 1;
        }

        string? recipe = GetOptionValue(args, "--recipe");
        bool purge = args.Contains("--purge", StringComparer.OrdinalIgnoreCase) || string.Equals(recipe, "purge", StringComparison.OrdinalIgnoreCase);
        bool resetCoords = args.Contains("--reset-coords", StringComparer.OrdinalIgnoreCase) || string.Equals(recipe, "reset", StringComparison.OrdinalIgnoreCase);
        string? extractQuery = GetOptionValue(args, "--extract");
        string? offsetStr = GetOptionValue(args, "--offset");
        string? scaleStr = GetOptionValue(args, "--scale");
        string? schemaStr = GetOptionValue(args, "--migrate") ?? (string.Equals(recipe, "migrate", StringComparison.OrdinalIgnoreCase) ? GetOptionValue(args, "--schema") : null);

        Geometry.IfcVector3? manualOffset = null;
        if (args.Contains("--offset", StringComparer.OrdinalIgnoreCase))
        {
            var parts = offsetStr?.Split(',');
            if (parts is not { Length: 3 }
                || !TryParseFiniteDouble(parts[0], out double x)
                || !TryParseFiniteDouble(parts[1], out double y)
                || !TryParseFiniteDouble(parts[2], out double z))
            {
                Console.Error.WriteLine("Error: --offset requires three finite numbers in X,Y,Z format.");
                return 1;
            }
            manualOffset = new Geometry.IfcVector3(x, y, z);
        }

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath, new IfcReadOptions
        {
            LegacyIfcIoRevolvedAnglesInRadians = args.Contains("--legacy-ifcio-radian-sweeps", StringComparer.OrdinalIgnoreCase),
        });

        if (!string.IsNullOrEmpty(extractQuery))
        {
            Console.WriteLine($"  * Extracting elements matching query: \"{extractQuery}\"");
            model = model.Extract(extractQuery);
        }

        if (purge)
        {
            var stats = model.Purge();
            Console.WriteLine($"  * Purged: {stats.RemovedPropertySets} property sets, {stats.RemovedQuantitySets} quantity sets, {stats.RemovedEmptyStoreys} empty storeys.");
        }

        if (resetCoords)
        {
            if (model.ResetCoordinates(out var offset))
            {
                Console.WriteLine($"  * Reset coordinates: shifted model by ({offset.X:F2}, {offset.Y:F2}) m.");
            }
            else
            {
                Console.WriteLine("  * Coordinates already within local origin threshold.");
            }
        }

        if (manualOffset is { } translation)
        {
            model.OffsetCoordinates(translation);
            Console.WriteLine($"  * Applied manual offset: ({translation.X:F3}, {translation.Y:F3}, {translation.Z:F3}) m.");
        }

        if (!string.IsNullOrEmpty(scaleStr) && double.TryParse(scaleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double scaleFactor))
        {
            Patch.IfcPatch.ScaleModel(model, scaleFactor);
            Console.WriteLine($"  * Scaled model vertices by factor: {scaleFactor:G}");
        }

        if (!string.IsNullOrEmpty(schemaStr))
        {
            var targetSchema = schemaStr.Contains('4') ? IfcSchema.Ifc4 : IfcSchema.Ifc2x3;
            Patch.IfcPatch.MigrateSchema(model, targetSchema);
            Console.WriteLine($"  * Migrated model schema to: {targetSchema}");
        }

        model.Save(outputPath);
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Patched model successfully saved to '{outputPath}' in {sw.ElapsedMilliseconds} ms.");
        Console.ResetColor();
        return 0;
    }

    private static bool TryParseFiniteDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

    private static int ExecuteValidateIds(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: 'ifcio validate-ids <model.ifc> <rules.ids> [--bcf <issues.bcfzip>]' requires model and IDS file.");
            return 1;
        }

        string inputPath = args[0];
        string idsPath = args[1];

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input model '{inputPath}' does not exist.");
            return 1;
        }

        if (!File.Exists(idsPath))
        {
            Console.Error.WriteLine($"Error: IDS rules file '{idsPath}' does not exist.");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        var report = model.ValidateIds(idsPath);
        sw.Stop();

        Console.WriteLine($"IDS Specification: \"{report.Title}\" (v{report.IdsVersion ?? "1.0"})");
        Console.WriteLine($"Total specifications: {report.TotalSpecifications}, Applicable elements: {report.TotalApplicableElements:N0}");
        Console.WriteLine($"Passed: {report.PassedSpecifications}, Failed: {report.FailedSpecifications}");
        Console.WriteLine();

        foreach (var failure in report.Failures.Take(50))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] {failure.SpecificationName}: {failure.ElementType} #{failure.ElementId} - {failure.Message}");
            Console.ResetColor();
        }

        if (report.Failures.Count > 50)
        {
            Console.WriteLine($"... and {report.Failures.Count - 50} more failures.");
        }

        string? bcfPath = GetOptionValue(args, "--bcf");
        if (!string.IsNullOrEmpty(bcfPath) && report.Failures.Count > 0)
        {
            report.SaveBcf(bcfPath);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Exported {report.Failures.Count} compliance issues to BCF 2.1: '{bcfPath}'");
            Console.ResetColor();
        }

        if (report.IsCompliant)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"IDS Validation PASSED (100% compliant) in {sw.ElapsedMilliseconds} ms.");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"IDS Validation FAILED with {report.Failures.Count} requirement violations.");
            Console.ResetColor();
            return 1;
        }
    }

    private static int ExecuteExportCsv(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Error: 'ifcio export-csv <model.ifc> [-o <out.csv>] [--props <P1,P2...>]' requires model path.");
            return 1;
        }

        string inputPath = args[0];
        string? outPath = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output");
        if (string.IsNullOrEmpty(outPath) && args.Length >= 2 && !args[1].StartsWith('-'))
        {
            outPath = args[1];
        }

        if (string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("Error: export-csv requires output path specified via -o/--output or as second argument.");
            return 1;
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input file '{inputPath}' does not exist.");
            return 1;
        }

        string? propsRaw = GetOptionValue(args, "-p") ?? GetOptionValue(args, "--props");
        var props = !string.IsNullOrEmpty(propsRaw) ? propsRaw.Split(',').Select(p => p.Trim()) : Array.Empty<string>();
        string delimiter = GetOptionValue(args, "--delimiter") ?? ";";

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        model.ExportCsv(outPath, props, delimiter);
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Exported {model.AllElements.Count():N0} elements to CSV '{outPath}' in {sw.ElapsedMilliseconds} ms.");
        Console.ResetColor();
        return 0;
    }

    private static int ExecuteImportCsv(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Error: 'ifcio import-csv <model.ifc> <in.csv> -o <out.ifc>' requires model and CSV input path.");
            return 1;
        }

        string inputPath = args[0];
        string? csvPath = GetOptionValue(args, "--csv");
        if (string.IsNullOrEmpty(csvPath) && args.Length >= 2 && !args[1].StartsWith('-'))
        {
            csvPath = args[1];
        }

        if (string.IsNullOrEmpty(csvPath))
        {
            Console.Error.WriteLine("Error: import-csv requires CSV input path specified via --csv or as second argument.");
            return 1;
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input file '{inputPath}' does not exist.");
            return 1;
        }

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"Error: CSV file '{csvPath}' does not exist.");
            return 1;
        }

        string? outPath = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output");
        if (string.IsNullOrEmpty(outPath))
        {
            Console.Error.WriteLine("Error: import-csv requires output path specified via -o or --output.");
            return 1;
        }

        string delimiter = GetOptionValue(args, "--delimiter") ?? ";";

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        var (elems, props) = model.ImportCsv(csvPath, delimiter);
        model.Save(outPath);
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Imported {props:N0} properties across {elems:N0} elements. Saved to '{outPath}' in {sw.ElapsedMilliseconds} ms.");
        Console.ResetColor();
        return 0;
    }

    private static int ExecuteClash(string[] args)
    {
        var inputs = new List<string>(2);
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!token.StartsWith('-'))
            {
                inputs.Add(token);
                continue;
            }
            if (token.ToLowerInvariant() is not ("--tolerance" or "--selector-a" or "--selector-b" or "--grids"
                or "--group" or "--clash-group" or "--radius" or "--clash-radius" or "--export-grids" or "-o" or "--output" or "--bcf"))
            {
                Console.Error.WriteLine($"Error: Unknown clash option '{token}'.");
                return 1;
            }
            if (++i == args.Length || args[i].StartsWith('-') || string.IsNullOrWhiteSpace(args[i]))
            {
                Console.Error.WriteLine($"Error: {token} requires a value.");
                return 1;
            }
        }
        if (inputs.Count is < 1 or > 2)
        {
            Console.Error.WriteLine("Error: clash requires one or two input IFC files.");
            return 1;
        }

        string fileA = inputs[0];
        string? fileB = inputs.Count == 2 ? inputs[1] : null;

        if (!File.Exists(fileA))
        {
            Console.Error.WriteLine($"Error: Input file '{fileA}' does not exist.");
            return 1;
        }
        if (fileB is not null && !File.Exists(fileB))
        {
            Console.Error.WriteLine($"Error: Input file '{fileB}' does not exist.");
            return 1;
        }

        if (!TryGetGridPath(args, out string? gridsPath)) return 1;

        double tol = 0.001;
        string? tolStr = GetOptionValue(args, "--tolerance");
        if (!string.IsNullOrEmpty(tolStr))
        {
            double.TryParse(tolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out tol);
        }

        var options = new Clash.IfcClashOptions
        {
            Tolerance = tol,
            SelectorA = GetOptionValue(args, "--selector-a"),
            SelectorB = GetOptionValue(args, "--selector-b"),
        };

        var modelA = IfcModel.Open(fileA);
        Clash.IfcClashReport report;
        if (fileB is not null)
        {
            var modelB = IfcModel.Open(fileB);
            report = modelA.DetectClashes(modelB, options);
        }
        else
        {
            report = modelA.DetectClashes(options);
        }

        if (gridsPath is not null)
        {
            var gridSystem = Grids.IfcGridSystem.LoadXml(gridsPath);
            modelA.GridSystem = gridSystem;
            modelA.AssignGridLocations(gridSystem);
            report.AssignGridLocations(gridSystem);
            Console.WriteLine($"  * Loaded grid system '{gridSystem.Name}' ({gridSystem.Axes.Count} axes). Assigned grid locations to clashes.");
        }
        else
        {
            var extractedGrids = modelA.ExtractGridSystem();
            if (extractedGrids.Axes.Count > 0)
            {
                report.AssignGridLocations(extractedGrids);
            }
        }

        string? groupMode = GetOptionValue(args, "--group") ?? GetOptionValue(args, "--clash-group");
        string? radiusStr = GetOptionValue(args, "--radius") ?? GetOptionValue(args, "--clash-radius");
        double radius = 1.0;
        if (!string.IsNullOrEmpty(radiusStr) && double.TryParse(radiusStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
        {
            radius = r;
        }

        if (!string.IsNullOrEmpty(groupMode))
        {
            int groupCount = groupMode.ToLowerInvariant() switch
            {
                "proximity" => report.GroupByProximity(radius),
                "element" or "elements" => report.GroupByPrimaryElement(),
                "storey" or "storeys" or "level" => report.GroupByStorey(modelA),
                "grid" or "grids" => report.GroupByGrid(),
                _ => 0
            };
            Console.WriteLine($"  * Grouped clashes into {groupCount} groups using mode '{groupMode}'.");
        }

        string? exportGridsPath = GetOptionValue(args, "--export-grids");
        if (!string.IsNullOrEmpty(exportGridsPath))
        {
            var grids = modelA.GridSystem ?? modelA.ExtractGridSystem();
            grids.SaveXml(exportGridsPath);
            Console.WriteLine($"  * Exported grid system '{grids.Name}' ({grids.Axes.Count} axes) to '{exportGridsPath}'.");
        }

        Console.ForegroundColor = report.TotalClashesCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
        Console.WriteLine($"Clash detection completed: {report.TotalClashesCount} clashes found in {report.Elapsed.TotalMilliseconds:F0} ms.");
        Console.ResetColor();

        string? outJson = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output");
        if (!string.IsNullOrEmpty(outJson))
        {
            report.SaveJson(outJson);
            Console.WriteLine($"Clash report saved to '{outJson}'.");
        }

        string? bcfPath = GetOptionValue(args, "--bcf");
        if (!string.IsNullOrEmpty(bcfPath))
        {
            report.SaveBcf(bcfPath);
            Console.WriteLine($"BCF 2.1 clash archive saved to '{bcfPath}'.");
        }

        return 0;
    }

    private static int ExecuteDiff(string[] args)
    {
        var nonOptions = args.Where(a => !a.StartsWith('-')).ToList();
        if (nonOptions.Count < 2)
        {
            Console.Error.WriteLine("Error: diff command requires baseline and revision IFC files: 'ifcio diff <baseline.ifc> <revision.ifc>'.");
            return 1;
        }

        string baselineFile = nonOptions[0];
        string revisionFile = nonOptions[1];

        if (!File.Exists(baselineFile))
        {
            Console.Error.WriteLine($"Error: Baseline file '{baselineFile}' does not exist.");
            return 1;
        }
        if (!File.Exists(revisionFile))
        {
            Console.Error.WriteLine($"Error: Revision file '{revisionFile}' does not exist.");
            return 1;
        }

        double tol = 0.005;
        string? tolStr = GetOptionValue(args, "--tolerance");
        if (!string.IsNullOrEmpty(tolStr))
        {
            double.TryParse(tolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out tol);
        }

        var options = new Diff.IfcDiffOptions
        {
            GeometricTolerance = tol,
            FilterSelector = GetOptionValue(args, "--filter"),
        };

        var baselineModel = IfcModel.Open(baselineFile);
        var revisionModel = IfcModel.Open(revisionFile);
        var report = baselineModel.Compare(revisionModel, options);

        Console.ForegroundColor = report.TotalChangesCount > 0 ? ConsoleColor.Cyan : ConsoleColor.Green;
        Console.WriteLine($"Diff completed in {report.Elapsed.TotalMilliseconds:F0} ms: {report.TotalChangesCount} changes (Added: {report.Added.Count}, Deleted: {report.Deleted.Count}, Modified: {report.Modified.Count}, Unchanged: {report.Unchanged.Count}).");
        Console.ResetColor();

        string? outJson = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output");
        if (!string.IsNullOrEmpty(outJson))
        {
            report.SaveJson(outJson);
            Console.WriteLine($"Diff report saved to '{outJson}'.");
        }

        string? bcfPath = GetOptionValue(args, "--bcf");
        if (!string.IsNullOrEmpty(bcfPath))
        {
            report.SaveBcf(bcfPath);
            Console.WriteLine($"BCF 2.1 revision archive saved to '{bcfPath}'.");
        }

        return 0;
    }

    private static int ExecuteView(string[] args)
    {
        var nonOptions = args.Where(a => !a.StartsWith('-')).ToList();
        if (nonOptions.Count == 0)
        {
            Console.Error.WriteLine("Error: view command requires an input IFC file.");
            return 1;
        }

        string inputPath = nonOptions[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input file '{inputPath}' does not exist.");
            return 1;
        }

        if (!TryGetGridPath(args, out string? gridsPath)
            || !TryGetPathOption(args, "--export-grids", out string? exportGridsPath)) return 1;

        string outPath = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output") ?? Path.ChangeExtension(inputPath, ".html");
        string title = GetOptionValue(args, "--title") ?? Path.GetFileNameWithoutExtension(inputPath);

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);

        if (gridsPath is not null)
        {
            model.GridSystem = Grids.IfcGridSystem.LoadXml(gridsPath);
        }
        else if (model.GridSystem is null)
        {
            model.ExtractGridSystem();
        }

        if (exportGridsPath is not null)
        {
            var grids = model.GridSystem ?? model.ExtractGridSystem();
            grids.SaveXml(exportGridsPath);
            Console.WriteLine($"Grid system exported to '{exportGridsPath}'.");
        }

        model.SaveHtmlViewer(outPath, new Exporters.IfcHtmlViewerOptions { Title = title });
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"3D WebGL HTML viewer exported to '{outPath}' in {sw.ElapsedMilliseconds} ms.");
        Console.ResetColor();
        return 0;
    }

    private static bool TryGetGridPath(string[] args, out string? path)
    {
        if (!TryGetPathOption(args, "--grids", out path)) return false;
        if (path is null || File.Exists(path)) return true;
        Console.Error.WriteLine($"Error: Grid file '{path}' does not exist.");
        return false;
    }

    private static bool TryGetPathOption(string[] args, string option, out string? path)
    {
        path = GetOptionValue(args, option);
        if (!args.Contains(option, StringComparer.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith('-')) return true;
        Console.Error.WriteLine($"Error: {option} requires a file path.");
        return false;
    }

    private static int ExecuteSchedule(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing IFC file path. Usage: ifcio schedule <file.ifc> [--sim-date <YYYY-MM-DD>] [--export-csv <out.csv>]");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        string? simDateStr = GetOptionValue(args, "--sim-date");
        DateTime? simDate = null;
        if (args.Contains("--sim-date", StringComparer.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParse(simDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                Console.Error.WriteLine("Error: --sim-date requires a date (YYYY-MM-DD).");
                return 1;
            }
            simDate = parsedDate;
        }

        var model = IfcModel.Open(inputPath);
        if (model.WorkSchedules.Count == 0)
        {
            Console.WriteLine($"Model '{Path.GetFileName(inputPath)}' contains no 4D construction work schedules.");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"4D Construction Work Schedules: {model.WorkSchedules.Count} schedule(s)");
        Console.ResetColor();

        foreach (var ws in model.WorkSchedules)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Schedule: {ws.Name} (GlobalId: {ws.GlobalId})");
            Console.ResetColor();
            if (ws.StartTime.HasValue || ws.FinishTime.HasValue)
            {
                Console.WriteLine($"  Planned: {ws.StartTime:yyyy-MM-dd} -> {ws.FinishTime:yyyy-MM-dd}");
            }
            Console.WriteLine($"  Total tasks: {ws.AllTasks.Count()}");

            if (simDate.HasValue)
            {
                double prog = ws.GetProgressAt(simDate.Value);
                var active = ws.GetActiveTasksAt(simDate.Value);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Simulation Progress at {simDate.Value:yyyy-MM-dd}: {prog:F1}% (Active tasks: {active.Count})");
                Console.ResetColor();
                foreach (var at in active)
                {
                    Console.WriteLine($"    * [{at.Identification ?? "WBS"}] {at.Name} ({at.TaskType}) Elements: {at.AssignedElements.Count}");
                }
            }
            else
            {
                foreach (var task in ws.AllTasks)
                {
                    var start = task.Time?.ActualStartDate ?? task.Time?.PlannedStartDate;
                    var finish = task.Time?.ActualFinishDate ?? task.Time?.PlannedFinishDate;
                    string dates = (start.HasValue && finish.HasValue) ? $"[{start:yyyy-MM-dd} -> {finish:yyyy-MM-dd}]" : "";
                    Console.WriteLine($"    - [{task.Identification ?? "Task"}] {task.Name,-30} {dates,-26} Elements: {task.AssignedElements.Count}");
                }
            }
        }

        string? exportCsvPath = GetOptionValue(args, "--export-csv");
        if (!string.IsNullOrEmpty(exportCsvPath))
        {
            using var writer = new StreamWriter(exportCsvPath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Schedule;TaskName;WBS;TaskType;StartDate;FinishDate;AssignedElements");
            foreach (var ws in model.WorkSchedules)
            {
                foreach (var t in ws.AllTasks)
                {
                    var start = t.Time?.ActualStartDate ?? t.Time?.PlannedStartDate;
                    var finish = t.Time?.ActualFinishDate ?? t.Time?.PlannedFinishDate;
                    writer.WriteLine($"{ws.Name};{t.Name};{t.Identification};{t.TaskType};{start:yyyy-MM-dd};{finish:yyyy-MM-dd};{t.AssignedElements.Count}");
                }
            }
            Console.WriteLine($"Schedule exported to CSV: '{exportCsvPath}'");
        }

        return 0;
    }

    private static int ExecuteCost(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing IFC file path. Usage: ifcio cost <file.ifc> [--export-csv <out.csv>]");
            return 1;
        }

        string inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File '{inputPath}' not found.");
            return 1;
        }

        var model = IfcModel.Open(inputPath);
        if (model.CostSchedules.Count == 0)
        {
            Console.WriteLine($"Model '{Path.GetFileName(inputPath)}' contains no 5D cost schedules.");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"5D Cost Schedules & Budgets: {model.CostSchedules.Count} schedule(s)");
        Console.WriteLine($"Grand Total Cost: {model.CalculateTotalCost():N2}");
        Console.ResetColor();

        foreach (var cs in model.CostSchedules)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Schedule: {cs.Name} ({cs.ScheduleType}) - Currency: {cs.Currency}");
            Console.WriteLine($"  Total Cost: {cs.GetTotalCost():N2} {cs.Currency}");
            Console.ResetColor();

            foreach (var item in cs.AllCostItems)
            {
                string rateStr = item.UnitRate.HasValue ? $"Rate: {item.UnitRate.Value:N2}/{item.UnitBasis ?? "unit"}" : "";
                string qtyStr = item.Quantity.HasValue ? $"Qty: {item.Quantity.Value:N2}" : "";
                Console.WriteLine($"    - [{item.CostCode ?? "CBS"}] {item.Name,-28} Cost: {item.TotalCost,12:N2} {cs.Currency} | {rateStr} {qtyStr} | Elements: {item.AssignedElements.Count}");
            }
        }

        string? exportCsvPath = GetOptionValue(args, "--export-csv");
        if (!string.IsNullOrEmpty(exportCsvPath))
        {
            using var writer = new StreamWriter(exportCsvPath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Schedule;CostCode;Name;UnitRate;Quantity;UnitBasis;TotalCost;Currency;AssignedElements");
            foreach (var cs in model.CostSchedules)
            {
                foreach (var item in cs.AllCostItems)
                {
                    writer.WriteLine($"{cs.Name};{item.CostCode};{item.Name};{item.UnitRate?.ToString(CultureInfo.InvariantCulture)};{item.Quantity?.ToString(CultureInfo.InvariantCulture)};{item.UnitBasis};{item.TotalCost.ToString(CultureInfo.InvariantCulture)};{cs.Currency};{item.AssignedElements.Count}");
                }
            }
            Console.WriteLine($"Cost schedule exported to CSV: '{exportCsvPath}'");
        }

        return 0;
    }

    private static int ExecuteBcf(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing BCF command. Usage: ifcio bcf <read|info> <path.bcfzip>");
            return 1;
        }

        string subCmd = args[0].ToLowerInvariant();
        string bcfPath = (subCmd is "read" or "info" && args.Length > 1) ? args[1] : args[0];

        if (!File.Exists(bcfPath))
        {
            Console.Error.WriteLine($"Error: BCF archive '{bcfPath}' not found.");
            return 1;
        }

        var archive = Bcf.BcfArchive.Open(bcfPath);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine($" buildingSMART BCF Archive: {Path.GetFileName(bcfPath)}");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        Console.WriteLine($"  Total Topics: {archive.Topics.Count}");
        foreach (var topic in archive.Topics)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Topic: [{topic.TopicStatus}] {topic.Title} ({topic.TopicType})");
            Console.ResetColor();
            Console.WriteLine($"    Guid:        {topic.TopicGuid}");
            Console.WriteLine($"    Priority:    {topic.Priority}");
            Console.WriteLine($"    Author:      {topic.CreationAuthor} on {topic.CreationDate:yyyy-MM-dd HH:mm}");
            if (!string.IsNullOrEmpty(topic.AssignedTo))
            {
                Console.WriteLine($"    Assigned To: {topic.AssignedTo}");
            }
            if (!string.IsNullOrEmpty(topic.Description))
            {
                Console.WriteLine($"    Description: {topic.Description}");
            }
            Console.WriteLine($"    Viewpoints:  {topic.Viewpoints.Count}");
            Console.WriteLine($"    Comments:    {topic.Comments.Count}");

            foreach (var comment in topic.Comments)
            {
                Console.WriteLine($"      * [{comment.Date:yyyy-MM-dd HH:mm}] {comment.Author}: {comment.Comment}");
            }
        }

        return 0;
    }
}
