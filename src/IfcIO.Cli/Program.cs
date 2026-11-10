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
            Console.WriteLine($"IfcIO.Cli v0.2.0 (.NET {Environment.Version})");
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

Options:
  --glb <path>          Output binary glTF 2.0 file (.glb)
  --obj <path>          Output Wavefront OBJ file (.obj)
  --ifc <path>          Output IFC STEP physical file (.ifc)
  --ifczip <path>       Output compressed IFC archive (.ifczip)
  --schema <ifc2x3|ifc4> Target schema for IFC export
  --report              Display detailed category and storey breakdown in info command
  -h, --help            Show this help message
  -v, --version         Show tool version

Examples:
  ifcio info Building.ifc --report
  ifcio export Building.ifc --glb scene.glb
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
                Console.WriteLine($"    - {storey.Name,-24} Elevation: {storey.Elevation,8:F2} mm | Elements: {storey.Elements.Count}");
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
}
