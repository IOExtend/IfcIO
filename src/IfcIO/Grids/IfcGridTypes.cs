using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using IfcIO.Clash;
using IfcIO.Geometry;
using IfcIO.Model;

namespace IfcIO.Grids;

/// <summary>
/// A straight spatial grid axis (line) representing an architectural or structural grid in an IFC model.
/// Conforms to buildingSMART IFC standard IFCGRIDAXIS.
/// </summary>
public sealed class IfcGridAxis
{
    public string Tag { get; set; } = string.Empty;

    public IfcVector3 Start { get; set; }

    public IfcVector3 End { get; set; }

    public double Elevation { get; set; }

    public string? GridName { get; set; }

    public double Length => (End - Start).Length;

    public IfcVector3 Direction => (End - Start).Normalized();

    public IfcGridAxis() { }

    public IfcGridAxis(string tag, IfcVector3 start, IfcVector3 end, double elevation = 0.0, string? gridName = null)
    {
        Tag = tag;
        Start = start;
        End = end;
        Elevation = elevation;
        GridName = gridName;
    }

    /// <summary>
    /// Computes the point on this grid axis closest to the specified 3D point.
    /// </summary>
    public IfcVector3 ClosestPoint(IfcVector3 point, bool segmentOnly = false)
    {
        var v = End - Start;
        double lenSq = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
        if (lenSq < 1e-12) return Start;

        double t = IfcVector3.Dot(point - Start, v) / lenSq;
        if (segmentOnly)
        {
            t = Math.Clamp(t, 0.0, 1.0);
        }

        return Start + v * t;
    }

    /// <summary>
    /// Computes 3D Euclidean distance from the specified point to this grid axis.
    /// </summary>
    public double DistanceToPoint(IfcVector3 point, bool segmentOnly = false)
    {
        var closest = ClosestPoint(point, segmentOnly);
        return (point - closest).Length;
    }

    /// <summary>
    /// Computes horizontal 2D distance (XY plane) from the specified point to this grid axis.
    /// </summary>
    public double DistanceToPoint2D(IfcVector3 point, bool segmentOnly = false)
    {
        var p2 = new IfcVector3(point.X, point.Y, 0.0);
        var s2 = new IfcVector3(Start.X, Start.Y, 0.0);
        var e2 = new IfcVector3(End.X, End.Y, 0.0);

        var v = e2 - s2;
        double lenSq = v.X * v.X + v.Y * v.Y;
        if (lenSq < 1e-12) return (p2 - s2).Length;

        double t = IfcVector3.Dot(p2 - s2, v) / lenSq;
        if (segmentOnly)
        {
            t = Math.Clamp(t, 0.0, 1.0);
        }

        var closest = s2 + v * t;
        return (p2 - closest).Length;
    }

    /// <summary>
    /// Computes signed offset distance in the horizontal XY plane (positive in increasing grid coordinate direction).
    /// </summary>
    public double SignedDistance2D(IfcVector3 point)
    {
        var v = End - Start;
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len < 1e-12) return 0.0;

        double nx, ny;
        if (Math.Abs(v.Y) >= Math.Abs(v.X))
        {
            // Vertical or near-vertical line: orient normal towards +X (increasing grid coordinates)
            nx = v.Y / len;
            ny = -v.X / len;
            if (nx < 0) { nx = -nx; ny = -ny; }
        }
        else
        {
            // Horizontal or near-horizontal line: orient normal towards +Y (increasing grid coordinates)
            nx = -v.Y / len;
            ny = v.X / len;
            if (ny < 0) { nx = -nx; ny = -ny; }
        }

        return (point.X - Start.X) * nx + (point.Y - Start.Y) * ny;
    }

    /// <summary>
    /// Calculates 2D intersection point in the XY plane between this grid axis and another.
    /// </summary>
    public bool Intersect2D(IfcGridAxis other, out IfcVector3 intersectionPoint)
    {
        ArgumentNullException.ThrowIfNull(other);

        double x1 = Start.X, y1 = Start.Y;
        double x2 = End.X, y2 = End.Y;
        double x3 = other.Start.X, y3 = other.Start.Y;
        double x4 = other.End.X, y4 = other.End.Y;

        double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denom) < 1e-9)
        {
            intersectionPoint = IfcVector3.Zero;
            return false;
        }

        double px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
        double py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
        double pz = (Elevation + other.Elevation) * 0.5;

        intersectionPoint = new IfcVector3(px, py, pz);
        return true;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"IfcGridAxis '{Tag}': ({Start.X:F1},{Start.Y:F1}) -> ({End.X:F1},{End.Y:F1})");
}

/// <summary>
/// A 2D/3D intersection between two crossing IFC grid axes (e.g. "A / 1").
/// </summary>
public sealed class IfcGridIntersection
{
    public required string Axis1Tag { get; init; }

    public required string Axis2Tag { get; init; }

    public required IfcVector3 IntersectionPoint { get; init; }

    public double Elevation => IntersectionPoint.Z;

    public string? GridName { get; init; }

    public string DisplayName => $"{Axis1Tag} / {Axis2Tag}";

    public double DistanceTo(IfcVector3 point) => (IntersectionPoint - point).Length;

    public double DistanceTo2D(IfcVector3 point)
    {
        double dx = IntersectionPoint.X - point.X;
        double dy = IntersectionPoint.Y - point.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{DisplayName} at ({IntersectionPoint.X:F2}, {IntersectionPoint.Y:F2}, {IntersectionPoint.Z:F2})");
}

/// <summary>
/// Spatial grid system containing reference axes and lines (e.g. A, B, C / 1, 2, 3),
/// capable of computing intersections, locating 3D coordinates relative to grids,
/// and populating clash grid location tags.
/// </summary>
public sealed class IfcGridSystem
{
    public string Name { get; set; } = "Grids";

    public List<IfcGridAxis> Axes { get; } = [];

    private List<IfcGridIntersection>? _cachedIntersections;

    public IfcGridSystem() { }

    public IfcGridSystem(string name)
    {
        Name = name;
    }

    /// <summary>Adds a grid axis to the system.</summary>
    public IfcGridAxis AddAxis(IfcGridAxis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        Axes.Add(axis);
        _cachedIntersections = null;
        return axis;
    }

    /// <summary>Adds a grid axis by tag and endpoints.</summary>
    public IfcGridAxis AddAxis(string tag, IfcVector3 start, IfcVector3 end, double elevation = 0.0, string? gridName = null)
    {
        var axis = new IfcGridAxis(tag, start, end, elevation, gridName ?? Name);
        return AddAxis(axis);
    }

    /// <summary>Calculates all 2D/3D intersection points between crossing grid axes.</summary>
    public IReadOnlyList<IfcGridIntersection> GetIntersections()
    {
        if (_cachedIntersections is not null)
        {
            return _cachedIntersections;
        }

        var result = new List<IfcGridIntersection>();

        for (int i = 0; i < Axes.Count; i++)
        {
            for (int j = i + 1; j < Axes.Count; j++)
            {
                var a1 = Axes[i];
                var a2 = Axes[j];

                if (a1.Intersect2D(a2, out var pt))
                {
                    result.Add(new IfcGridIntersection
                    {
                        Axis1Tag = a1.Tag,
                        Axis2Tag = a2.Tag,
                        IntersectionPoint = pt,
                        GridName = a1.GridName ?? Name,
                    });
                }
            }
        }

        _cachedIntersections = result;
        return result;
    }

    /// <summary>Finds the nearest grid axis to the specified 3D point.</summary>
    public IfcGridAxis? FindNearestAxis(IfcVector3 point, out double distance, bool segmentOnly = false)
    {
        if (Axes.Count == 0)
        {
            distance = double.PositiveInfinity;
            return null;
        }

        IfcGridAxis? best = null;
        double minDst = double.PositiveInfinity;

        for (int i = 0; i < Axes.Count; i++)
        {
            double d = Axes[i].DistanceToPoint2D(point, segmentOnly);
            if (d < minDst)
            {
                minDst = d;
                best = Axes[i];
            }
        }

        distance = minDst;
        return best;
    }

    /// <summary>Finds the nearest grid intersection (e.g. "A / 1") to the specified 3D point.</summary>
    public IfcGridIntersection? FindNearestIntersection(IfcVector3 point, out double distance)
    {
        var intersections = GetIntersections();
        if (intersections.Count == 0)
        {
            distance = double.PositiveInfinity;
            return null;
        }

        IfcGridIntersection? best = null;
        double minDst = double.PositiveInfinity;

        for (int i = 0; i < intersections.Count; i++)
        {
            double d = intersections[i].DistanceTo2D(point);
            if (d < minDst)
            {
                minDst = d;
                best = intersections[i];
            }
        }

        distance = minDst;
        return best;
    }

    /// <summary>
    /// Formats a human-readable grid location descriptor for a 3D coordinate (e.g. "A / 1", "A (+1.20m) / 2 (-0.50m)").
    /// </summary>
    public string FormatLocation(IfcVector3 point, double snapTolerance = 0.05)
    {
        if (Axes.Count == 0) return string.Empty;

        var inter = FindNearestIntersection(point, out double interDist);
        if (inter is not null)
        {
            var a1 = Axes.FirstOrDefault(a => string.Equals(a.Tag, inter.Axis1Tag, StringComparison.OrdinalIgnoreCase));
            var a2 = Axes.FirstOrDefault(a => string.Equals(a.Tag, inter.Axis2Tag, StringComparison.OrdinalIgnoreCase));

            if (a1 is not null && a2 is not null)
            {
                double off1 = a1.SignedDistance2D(point);
                double off2 = a2.SignedDistance2D(point);

                bool snap1 = Math.Abs(off1) <= snapTolerance;
                bool snap2 = Math.Abs(off2) <= snapTolerance;

                if (snap1 && snap2)
                {
                    return $"{a1.Tag} / {a2.Tag}";
                }

                string part1 = snap1 ? a1.Tag : string.Create(CultureInfo.InvariantCulture, $"{a1.Tag} ({off1:+0.00;-0.00;0.00}m)");
                string part2 = snap2 ? a2.Tag : string.Create(CultureInfo.InvariantCulture, $"{a2.Tag} ({off2:+0.00;-0.00;0.00}m)");
                return $"{part1} / {part2}";
            }

            return inter.DisplayName;
        }

        var nearestAxis = FindNearestAxis(point, out double axisDist);
        if (nearestAxis is not null)
        {
            double off = nearestAxis.SignedDistance2D(point);
            if (Math.Abs(off) <= snapTolerance)
            {
                return $"Grid {nearestAxis.Tag}";
            }
            return string.Create(CultureInfo.InvariantCulture, $"Grid {nearestAxis.Tag} ({off:+0.00;-0.00;0.00}m)");
        }

        return string.Empty;
    }

    /// <summary>
    /// Assigns grid locations to all clash results in an <see cref="IfcClashReport"/>.
    /// </summary>
    public int AssignGridLocations(IfcClashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        int count = 0;

        foreach (var clash in report.Clashes)
        {
            clash.GridLocation = FormatLocation(clash.ClashPoint);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Assigns grid locations across elements in an <see cref="IfcModel"/>.
    /// </summary>
    public int AssignGridLocations(IfcModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int count = 0;

        foreach (var elem in model.AllElements)
        {
            var mesh = elem.GetEffectiveMesh();
            if (mesh is not null && mesh.Vertices.Count > 0)
            {
                var box = mesh.BoundingBox;
                if (!box.IsEmpty)
                {
                    elem.GridLocation = FormatLocation(box.Center);
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Extracts grid axes from an IFC model by scanning for IFCGRID entities or grid proxy elements.
    /// </summary>
    public static IfcGridSystem ExtractFromModel(IfcModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var system = new IfcGridSystem(model.ProjectName);

        // Scan all elements for IFCGRID or grid proxies
        foreach (var elem in model.AllElements)
        {
            bool isGrid = elem.IfcType.Equals("IFCGRID", StringComparison.OrdinalIgnoreCase) ||
                          elem.IfcType.Equals("IFCGRIDAXIS", StringComparison.OrdinalIgnoreCase) ||
                          elem.Name.StartsWith("Grid", StringComparison.OrdinalIgnoreCase);

            if (isGrid)
            {
                string tag = elem.Name;
                if (tag.StartsWith("Grid", StringComparison.OrdinalIgnoreCase))
                {
                    tag = tag.Substring(4).Trim(' ', ':', '-');
                }

                var mesh = elem.GetEffectiveMesh();
                if (mesh is not null && mesh.Vertices.Count >= 2)
                {
                    system.AddAxis(tag, mesh.Vertices[0], mesh.Vertices[^1], (mesh.Vertices[0].Z + mesh.Vertices[^1].Z) * 0.5);
                }
            }
        }

        return system;
    }

    /// <summary>Saves the grid system to an XML file.</summary>
    public void SaveXml(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        SaveXml(fs);
    }

    /// <summary>Saves the grid system to an XML stream.</summary>
    public void SaveXml(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };

        using var xml = XmlWriter.Create(stream, settings);
        xml.WriteStartDocument();
        xml.WriteStartElement("exchange");
        xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

        xml.WriteStartElement("grids");
        xml.WriteStartElement("grid_system");
        xml.WriteAttributeString("name", Name);

        foreach (var axis in Axes)
        {
            xml.WriteStartElement("grid_line");
            xml.WriteAttributeString("name", axis.Tag);
            xml.WriteAttributeString("elevation", axis.Elevation.ToString("F4", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(axis.GridName))
            {
                xml.WriteAttributeString("system", axis.GridName);
            }

            xml.WriteStartElement("start");
            xml.WriteAttributeString("x", axis.Start.X.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("y", axis.Start.Y.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("z", axis.Start.Z.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteEndElement();

            xml.WriteStartElement("end");
            xml.WriteAttributeString("x", axis.End.X.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("y", axis.End.Y.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("z", axis.End.Z.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteEndElement();

            xml.WriteEndElement(); // grid_line
        }

        xml.WriteEndElement(); // grid_system
        xml.WriteEndElement(); // grids
        xml.WriteEndElement(); // exchange
        xml.WriteEndDocument();
    }

    /// <summary>Loads a grid system from an XML file.</summary>
    public static IfcGridSystem LoadXml(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return ReadXml(fs);
    }

    /// <summary>Loads a grid system from an XML stream.</summary>
    public static IfcGridSystem ReadXml(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);

        var sysElem = doc.Descendants("grid_system").FirstOrDefault();
        string sysName = sysElem?.Attribute("name")?.Value ?? "Grids";

        var system = new IfcGridSystem(sysName);

        var lineElems = doc.Descendants("grid_line");
        foreach (var elem in lineElems)
        {
            string name = elem.Attribute("name")?.Value ?? "Grid";
            double elevation = 0.0;
            if (double.TryParse(elem.Attribute("elevation")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double elevVal))
            {
                elevation = elevVal;
            }

            string? lineSys = elem.Attribute("system")?.Value;

            var startElem = elem.Element("start");
            var endElem = elem.Element("end");

            var start = IfcVector3.Zero;
            var end = IfcVector3.Zero;

            if (startElem is not null)
            {
                double.TryParse(startElem.Attribute("x")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                double.TryParse(startElem.Attribute("y")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                double.TryParse(startElem.Attribute("z")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                start = new IfcVector3(x, y, z);
            }

            if (endElem is not null)
            {
                double.TryParse(endElem.Attribute("x")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                double.TryParse(endElem.Attribute("y")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                double.TryParse(endElem.Attribute("z")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                end = new IfcVector3(x, y, z);
            }

            system.AddAxis(new IfcGridAxis(name, start, end, elevation, lineSys));
        }

        return system;
    }
}
