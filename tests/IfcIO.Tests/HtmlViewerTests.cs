using IfcIO.Exporters;
using IfcIO.Geometry;
using IfcIO.Model;
using IfcIO.Schema;
using Xunit;

namespace IfcIO.Tests;

public sealed class HtmlViewerTests
{
    [Fact]
    public void SaveHtmlViewer_GeneratesSelfContainedHtmlViewer()
    {
        var model = IfcModel.Create("Hospital Campus", IfcSchema.Ifc4);
        var storey = model.AddStorey("Level 1 Ground", 0.0);

        var mat = new IfcMaterial { Name = "Concrete", R = 0.8, G = 0.8, B = 0.8 };
        var wall = model.AddExtrudedRectangle("Exterior Wall 300mm", 8.0, 0.3, 3.5, position: new IfcVector3(0, 0, 0), material: mat, storey: storey, ifcType: "IFCWALL");
        wall.GlobalId = "03X9Wk_0f8pQ123456789A";
        wall.GetOrCreatePropertySet("Pset_WallCommon").Add("FireRating", "120");
        wall.GetOrCreateQuantitySet("Qto_WallBaseQuantities").AddVolume("NetVolume", 8.4);

        var col = model.AddExtrudedRectangle("Column C1", 0.4, 0.4, 3.5, position: new IfcVector3(4.0, 0, 0), storey: storey, ifcType: "IFCCOLUMN");
        col.GlobalId = "15Y9Wk_0f8pQ123456789B";

        string tempPath = Path.Combine(Path.GetTempPath(), $"ifc_viewer_{Guid.NewGuid():N}.html");
        try
        {
            model.SaveHtmlViewer(tempPath, new IfcHtmlViewerOptions
            {
                Title = "Hospital Complex - Level 1",
                IncludeProperties = true,
                IncludeQuantities = true,
                IncludeSpatialTree = true,
                IncludeSectioning = true,
            });

            Assert.True(File.Exists(tempPath));
            var info = new FileInfo(tempPath);
            Assert.True(info.Length > 2000, "Generated HTML viewer should be a comprehensive standalone application.");

            string html = File.ReadAllText(tempPath);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.Contains("<title>Hospital Complex - Level 1</title>", html, StringComparison.Ordinal);
            Assert.Contains("<canvas id=\"glcanvas\">", html, StringComparison.Ordinal);
            Assert.Contains("<script id=\"ifc-data\"", html, StringComparison.Ordinal);
            Assert.Contains("Exterior Wall 300mm", html, StringComparison.Ordinal);
            Assert.Contains("Column C1", html, StringComparison.Ordinal);
            Assert.Contains("Pset_WallCommon", html, StringComparison.Ordinal);
            Assert.Contains("Qto_WallBaseQuantities", html, StringComparison.Ordinal);
            Assert.Contains("gl.createShader", html, StringComparison.Ordinal);
            Assert.Contains("clip-x-en", html, StringComparison.Ordinal);
            Assert.Contains("tab-tree", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Cli_View_Execution_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ifc_cli_view_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string ifcPath = Path.Combine(tempDir, "model.ifc");
            string htmlPath = Path.Combine(tempDir, "viewer.html");

            var model = IfcModel.Create("Office", IfcSchema.Ifc4);
            model.AddExtrudedRectangle("Slab", 10.0, 10.0, 0.3);
            model.Save(ifcPath);

            int exitCode = Cli.Program.Main([
                "view",
                ifcPath,
                "-o", htmlPath,
                "--title", "Office Building Viewer",
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(htmlPath));
            string html = File.ReadAllText(htmlPath);
            Assert.Contains("Office Building Viewer", html, StringComparison.Ordinal);
            Assert.Contains("<canvas id=\"glcanvas\">", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
