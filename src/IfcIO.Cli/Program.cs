using System.Diagnostics;
using System.Globalization;
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
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.5.0";
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
  view <file.ifc> [-o out.html] Export standalone 3D WebGL HTML viewer (.html)

Clash & Diff Options:
  --tolerance <meters>  Distance tolerance in meters (clash clearance or diff geometric threshold)
  --bcf <path.bcfzip>   Export clash or diff issues directly to buildingSMART BCF 2.1 archive
  --selector-a <query>  Filter Model A elements for clash detection (e.g. 'IfcWall, IfcColumn')
  --selector-b <query>  Filter Model B elements for clash detection (e.g. 'IfcFlowSegment')
  --filter <query>      Filter elements to compare in diff command
  -o, --output <path>   Output JSON report or HTML viewer file path

Patch Options:
  --purge               Remove empty/duplicate property sets, quantity sets, and empty storeys
  --reset-coords        Shift large UTM coordinates to local origin (0,0,0) to prevent jitter
  --offset <x,y,z>      Translate model geometry by given coordinate offset
  --extract <query>     Extract matching elements into a separate IFC model
  -o, --output <path>   Output file path for patched IFC model

Options:
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
            foreach (var storey in model.Storeys)
            {
                Console.WriteLine($"    - {storey.Name,-24} Elevation: {storey.Elevation,8:F2} mm | Elements: {storey.Elements.Count} | Spaces: {storey.Spaces.Count}");
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
                    Console.WriteLine("  [ERROR] Archive does not contain an .ifc file.");
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
            Console.WriteLine($"  [ERROR] Failed to parse STEP syntax: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        // 1. Schema check
        if (string.IsNullOrEmpty(step.Schema))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  [WARN] FILE_SCHEMA header declaration is missing.");
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
            Console.WriteLine("  [ERROR] Model contains no IFCPROJECT entity.");
            Console.ResetColor();
            errors++;
        }
        else if (projects.Count > 1)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [WARN] Multiple IFCPROJECT entities found ({projects.Count}). Expected exactly 1.");
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
        try
        {
            var model = IfcModel.Open(inputPath);
            var valResult = model.Validate();
            foreach (var issue in valResult.Issues)
            {
                if (issue.Severity == Validation.IfcIssueSeverity.Error)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [ERROR] ({issue.RuleId}) {(issue.EntityName is not null ? $"{issue.EntityName}: " : "")}{issue.Message}");
                    Console.ResetColor();
                    errors++;
                }
                else if (issue.Severity == Validation.IfcIssueSeverity.Warning)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [WARN] ({issue.RuleId}) {(issue.EntityName is not null ? $"{issue.EntityName}: " : "")}{issue.Message}");
                    Console.ResetColor();
                    warnings++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [WARN] High-level ingestion warning: {ex.Message}");
            Console.ResetColor();
        }

        sw.Stop();
        Console.WriteLine();

        string? bcfPath = GetOptionValue(args, "--bcf");
        if (!string.IsNullOrEmpty(bcfPath))
        {
            try
            {
                var model = IfcModel.Open(inputPath);
                model.ExportValidationBcf(bcfPath);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  ✓ Exported validation issues to BCF 2.1: '{bcfPath}'");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [WARN] Failed to export BCF issues: {ex.Message}");
                Console.ResetColor();
            }
        }

        if (errors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Validation PASSED ({warnings} warnings, {step.Entities.Count:N0} entities checked) in {sw.ElapsedMilliseconds} ms.");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Validation FAILED with {errors} errors and {warnings} warnings.");
            Console.ResetColor();
            return 1;
        }
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

        bool purge = args.Contains("--purge", StringComparer.OrdinalIgnoreCase);
        bool resetCoords = args.Contains("--reset-coords", StringComparer.OrdinalIgnoreCase);
        string? extractQuery = GetOptionValue(args, "--extract");
        string? offsetStr = GetOptionValue(args, "--offset");

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);

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

        if (!string.IsNullOrEmpty(offsetStr))
        {
            var parts = offsetStr.Split(',');
            if (parts.Length == 3 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double ox) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double oy) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double oz))
            {
                model.OffsetCoordinates(new Geometry.IfcVector3(ox, oy, oz));
                Console.WriteLine($"  * Applied manual offset: ({ox:F3}, {oy:F3}, {oz:F3}) m.");
            }
        }

        model.Save(outputPath);
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Patched model successfully saved to '{outputPath}' in {sw.ElapsedMilliseconds} ms.");
        Console.ResetColor();
        return 0;
    }

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
        var nonOptions = args.Where(a => !a.StartsWith('-')).ToList();
        if (nonOptions.Count == 0)
        {
            Console.Error.WriteLine("Error: clash command requires at least one input IFC file.");
            return 1;
        }

        string fileA = nonOptions[0];
        string? fileB = nonOptions.Count > 1 ? nonOptions[1] : null;

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

        string outPath = GetOptionValue(args, "-o") ?? GetOptionValue(args, "--output") ?? Path.ChangeExtension(inputPath, ".html");
        string title = GetOptionValue(args, "--title") ?? Path.GetFileNameWithoutExtension(inputPath);

        var sw = Stopwatch.StartNew();
        var model = IfcModel.Open(inputPath);
        model.SaveHtmlViewer(outPath, new Exporters.IfcHtmlViewerOptions { Title = title });
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"3D WebGL HTML viewer exported to '{outPath}' in {sw.ElapsedMilliseconds} ms.");
        Console.ResetColor();
        return 0;
    }
}
