using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IfcIO;

if (args is not [("generate" or "open"), var path])
{
    Console.Error.WriteLine("Usage: StepImportBenchmark <generate|open> <path.ifc>");
    return 1;
}

const int MiB = 1024 * 1024;
if (args[0] == "generate")
{
    // Generate the large fixture separately: fixture generation should not affect the import process's peak memory.
    using var stream = new FileStream(path, FileMode.CreateNew);
    using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
    writer.WriteLine("ISO-10303-21;");
    writer.WriteLine("HEADER;");
    writer.WriteLine("FILE_DESCRIPTION(('Synthetic import benchmark'),'2;1');");
    writer.WriteLine("FILE_NAME('benchmark.ifc','2026-01-01T00:00:00',(''),(''),'','','');");
    writer.WriteLine("FILE_SCHEMA(('IFC4'));");
    writer.WriteLine("ENDSEC;");
    writer.WriteLine("DATA;");
    writer.WriteLine("#1=IFCPROJECT('01vctVUKr0kugbFTf53O9L',$,'Benchmark',$,$,$,$,(#5),$);");
    writer.WriteLine("#2=IFCWALL('02vctVUKr0kugbFTf53O9L',$,'Wall',$,$,$,#6,$,.STANDARD.);");
    writer.WriteLine("#3=IFCCARTESIANPOINT((0.,0.,0.));");
    writer.WriteLine("#4=IFCAXIS2PLACEMENT3D(#3,$,$);");
    writer.WriteLine("#5=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#4,$);");
    writer.WriteLine("#6=IFCPRODUCTDEFINITIONSHAPE($,$,(#7));");
    writer.WriteLine("#7=IFCSHAPEREPRESENTATION(#5,'Body','SurfaceModel',(#8));");
    writer.WriteLine("#8=IFCSHELLBASEDSURFACEMODEL((#9));");
    const int grid = 100;
    const int stride = grid + 1;
    const int triangles = grid * grid * 2;
    const int firstFace = 10;
    const int firstPoint = firstFace + 3 * triangles;
    writer.Write("#9=IFCOPENSHELL((");
    for (int i = 0; i < triangles; i++)
    {
        if (i > 0) writer.Write(',');
        writer.Write(FormattableString.Invariant($"#{firstFace + 3 * i}"));
    }
    writer.WriteLine("));");
    for (int y = 0; y <= grid; y++)
        for (int x = 0; x <= grid; x++)
            writer.WriteLine(FormattableString.Invariant($"#{firstPoint + stride * y + x}=IFCCARTESIANPOINT(({x}.,{y}.,0.));"));
    for (int y = 0; y < grid; y++)
    {
        for (int x = 0; x < grid; x++)
        {
            int face = firstFace + 6 * (y * grid + x);
            int point = firstPoint + stride * y + x;
            WriteFace(face, point, point + 1, point + stride + 1);
            WriteFace(face + 3, point, point + stride + 1, point + stride);
        }
    }
    void WriteFace(int id, int a, int b, int c)
    {
        writer.WriteLine(FormattableString.Invariant($"#{id}=IFCFACE((#{id + 1}));"));
        writer.WriteLine(FormattableString.Invariant($"#{id + 1}=IFCFACEOUTERBOUND(#{id + 2},.T.);"));
        writer.WriteLine(FormattableString.Invariant($"#{id + 2}=IFCPOLYLOOP((#{a},#{b},#{c}));"));
    }
    const string payload = "=IFCPROPERTYSINGLEVALUE('Synthetic attribute',$,IFCTEXT('abcdefghijklmnopqrstuvwxyz0123456789'),$);";
    long written = 0;
    for (int id = firstPoint + stride * stride; written < 110L * MiB; id++)
    {
        string number = id.ToString(CultureInfo.InvariantCulture);
        writer.Write('#');
        writer.Write(number);
        writer.WriteLine(payload);
        written += number.Length + payload.Length + 2;
    }
    writer.WriteLine("ENDSEC;");
    writer.WriteLine("END-ISO-10303-21;");
    return 0;
}

long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
var timer = Stopwatch.StartNew();
var model = IfcModel.Open(path);
timer.Stop();
long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
using var process = Process.GetCurrentProcess();
Console.WriteLine(JsonSerializer.Serialize(new
{
    Runtime = Environment.Version.ToString(),
    InputMiB = new FileInfo(path).Length / (double)MiB,
    Elements = model.AllElements.Count(),
    Triangles = model.AllElements.Sum(element => element.Mesh?.TriangleCount ?? 0),
    ElapsedMs = timer.Elapsed.TotalMilliseconds,
    AllocatedMiB = allocated / (double)MiB,
    PeakWorkingSetMiB = process.PeakWorkingSet64 / (double)MiB,
}));
return 0;
