using System.Globalization;
using System.Text;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Exporters;

/// <summary>
/// Exports IFC elements and 3D meshes to Wavefront OBJ format (.obj + .mtl).
/// </summary>
public static class IfcObjExporter
{
    public static void Save(IfcModel model, string objPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(objPath);

        string mtlPath = Path.ChangeExtension(objPath, ".mtl");
        string mtlFileName = Path.GetFileName(mtlPath);

        using var objFs = File.Create(objPath);
        using var objWriter = new StreamWriter(objFs, Encoding.UTF8);

        using var mtlFs = File.Create(mtlPath);
        using var mtlWriter = new StreamWriter(mtlFs, Encoding.UTF8);

        Write(model, objWriter, mtlWriter, mtlFileName);
    }

    public static void Write(IfcModel model, TextWriter objWriter, TextWriter mtlWriter, string? mtlLibName = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(objWriter);
        ArgumentNullException.ThrowIfNull(mtlWriter);

        var inv = CultureInfo.InvariantCulture;

        // 1. Write MTL Header and Materials
        mtlWriter.WriteLine("# Wavefront Material Library exported by IfcIO (https://github.com/IOExtend/IfcIO)");
        var materials = model.Materials;
        var writtenMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mat in materials)
        {
            if (writtenMaterials.Add(mat.Name))
            {
                WriteMaterial(mtlWriter, mat, inv);
            }
        }

        // 2. Write OBJ Header
        objWriter.WriteLine("# Wavefront OBJ exported by IfcIO (https://github.com/IOExtend/IfcIO)");
        objWriter.WriteLine($"# Project: {model.ProjectName}");
        if (!string.IsNullOrEmpty(mtlLibName))
        {
            objWriter.WriteLine($"mtllib {mtlLibName}");
        }

        int vertexOffset = 1;

        foreach (var storey in model.Storeys)
        {
            objWriter.WriteLine();
            objWriter.WriteLine($"# Storey: {storey.Name} (Elevation: {storey.Elevation.ToString("F2", inv)})");

            foreach (var elem in storey.Elements)
            {
                if (elem.Mesh is null || elem.Mesh.Vertices.Count == 0 || elem.Mesh.Indices.Count == 0)
                {
                    continue;
                }

                objWriter.WriteLine();
                string safeName = SanitizeName(elem.Name);
                objWriter.WriteLine($"g {safeName}_{elem.Id}");
                objWriter.WriteLine($"o {safeName}_{elem.Id}");

                if (elem.Material is not null)
                {
                    if (writtenMaterials.Add(elem.Material.Name))
                    {
                        WriteMaterial(mtlWriter, elem.Material, inv);
                    }
                    objWriter.WriteLine($"usemtl {SanitizeName(elem.Material.Name)}");
                }

                var vertices = elem.Mesh.Vertices;
                for (int v = 0; v < vertices.Count; v++)
                {
                    var pt = vertices[v];
                    objWriter.WriteLine($"v {pt.X.ToString("F4", inv)} {pt.Y.ToString("F4", inv)} {pt.Z.ToString("F4", inv)}");
                }

                var indices = elem.Mesh.Indices;
                for (int i = 0; i <= indices.Count - 3; i += 3)
                {
                    int i0 = vertexOffset + indices[i];
                    int i1 = vertexOffset + indices[i + 1];
                    int i2 = vertexOffset + indices[i + 2];
                    objWriter.WriteLine($"f {i0} {i1} {i2}");
                }

                vertexOffset += vertices.Count;
            }
        }
    }

    private static void WriteMaterial(TextWriter writer, IfcMaterial mat, CultureInfo inv)
    {
        writer.WriteLine();
        writer.WriteLine($"newmtl {SanitizeName(mat.Name)}");
        writer.WriteLine($"Kd {mat.R.ToString("F4", inv)} {mat.G.ToString("F4", inv)} {mat.B.ToString("F4", inv)}");
        writer.WriteLine($"d {mat.Alpha.ToString("F4", inv)}");
        writer.WriteLine("illum 2");
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Element";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}
