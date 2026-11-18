using System.Globalization;
using System.Text;

namespace IfcIO.Model;

/// <summary>
/// Provides high-speed CSV tabular export and roundtrip property import for IFC models,
/// conforming to the IfcOpenShell `ifccsv` specification.
/// </summary>
public static class IfcCsvBridge
{
    /// <summary>
    /// Exports elements and selected property set parameters to a CSV spreadsheet file.
    /// </summary>
    public static void ExportFile(
        IfcModel model,
        string path,
        IEnumerable<string> propertyNames,
        string delimiter = ";")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(path);

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        Export(model, writer, propertyNames, delimiter);
    }

    /// <summary>
    /// Exports elements and selected property set parameters to a CSV <see cref="TextWriter"/>.
    /// </summary>
    public static void Export(
        IfcModel model,
        TextWriter writer,
        IEnumerable<string> propertyNames,
        string delimiter = ";")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(writer);

        var props = propertyNames?.ToList() ?? [];

        // 1. Header row
        var header = new List<string> { "Id", "GlobalId", "Type", "Name", "Storey" };
        header.AddRange(props);
        writer.WriteLine(string.Join(delimiter, header.Select(EscapeCsv)));

        // 2. Data rows
        foreach (var storey in model.Storeys)
        {
            foreach (var elem in storey.Elements)
            {
                var row = new List<string>
                {
                    elem.Id.ToString(CultureInfo.InvariantCulture),
                    elem.GlobalId ?? string.Empty,
                    elem.IfcType,
                    elem.Name ?? string.Empty,
                    storey.Name,
                };

                foreach (var propKey in props)
                {
                    row.Add(GetPropertyValue(elem, propKey));
                }

                writer.WriteLine(string.Join(delimiter, row.Select(EscapeCsv)));
            }
        }
    }

    /// <summary>
    /// Imports tabular CSV properties into matching model elements by GlobalId or Id.
    /// Adds or updates property values inside matching or custom property sets.
    /// </summary>
    /// <returns>A tuple containing the number of updated elements and the total number of updated properties.</returns>
    public static (int ElementsUpdated, int PropertiesUpdated) ImportFile(
        IfcModel model,
        string path,
        string delimiter = ";")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(path);

        using var reader = new StreamReader(path, Encoding.UTF8);
        return Import(model, reader, delimiter);
    }

    /// <summary>
    /// Imports tabular CSV properties from a <see cref="TextReader"/> into matching model elements.
    /// </summary>
    public static (int ElementsUpdated, int PropertiesUpdated) Import(
        IfcModel model,
        TextReader reader,
        string delimiter = ";")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(reader);

        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine)) return (0, 0);

        var headers = ParseCsvLine(headerLine, delimiter);
        int idCol = headers.FindIndex(h => h.Equals("Id", StringComparison.OrdinalIgnoreCase));
        int guidCol = headers.FindIndex(h => h.Equals("GlobalId", StringComparison.OrdinalIgnoreCase));

        if (idCol == -1 && guidCol == -1)
        {
            throw new FormatException("CSV header must contain either 'GlobalId' or 'Id' column for element identification.");
        }

        // Map column indices to property targets
        var propCols = new List<(int ColIndex, string? PsetName, string PropName)>();
        for (int i = 0; i < headers.Count; i++)
        {
            if (i == idCol || i == guidCol) continue;
            string colName = headers[i];
            if (colName.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
                colName.Equals("Storey", StringComparison.OrdinalIgnoreCase) ||
                colName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Reserved metadata columns
            }

            string prop = colName;
            string? pset = null;
            if (colName.Contains('.'))
            {
                var s = colName.Split('.', 2);
                pset = s[0].Trim();
                prop = s[1].Trim();
            }

            propCols.Add((i, pset, prop));
        }

        // Index elements for rapid lookup
        var elementsByGuid = new Dictionary<string, IfcElement>(StringComparer.OrdinalIgnoreCase);
        var elementsById = new Dictionary<int, IfcElement>();
        foreach (var e in model.AllElements)
        {
            if (!string.IsNullOrEmpty(e.GlobalId)) elementsByGuid[e.GlobalId] = e;
            elementsById[e.Id] = e;
        }

        int elementsUpdated = 0;
        int propsUpdated = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line, delimiter);

            IfcElement? targetElem = null;
            if (guidCol >= 0 && guidCol < fields.Count && !string.IsNullOrEmpty(fields[guidCol]))
            {
                elementsByGuid.TryGetValue(fields[guidCol], out targetElem);
            }

            if (targetElem is null && idCol >= 0 && idCol < fields.Count && int.TryParse(fields[idCol], out int elemId))
            {
                elementsById.TryGetValue(elemId, out targetElem);
            }

            if (targetElem is null) continue;

            bool elemModified = false;
            foreach (var (colIdx, psetName, propName) in propCols)
            {
                if (colIdx >= fields.Count) continue;
                string rawVal = fields[colIdx].Trim();
                if (string.IsNullOrEmpty(rawVal)) continue;

                object typedVal = ParseTypedValue(rawVal);
                SetElementProperty(targetElem, psetName, propName, typedVal);
                elemModified = true;
                propsUpdated++;
            }

            if (elemModified) elementsUpdated++;
        }

        return (elementsUpdated, propsUpdated);
    }

    private static string GetPropertyValue(IfcElement element, string propKey)
    {
        string propName = propKey;
        string? psetName = null;
        if (propKey.Contains('.'))
        {
            var s = propKey.Split('.', 2);
            psetName = s[0].Trim();
            propName = s[1].Trim();
        }

        foreach (var pset in element.PropertySets)
        {
            if (!string.IsNullOrEmpty(psetName) && !pset.Name.Equals(psetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? strVal = pset.GetValue(propName);
            if (strVal is not null) return strVal;
        }

        foreach (var qto in element.QuantitySets)
        {
            if (!string.IsNullOrEmpty(psetName) && !qto.Name.Equals(psetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var q = qto.Quantities.FirstOrDefault(x => x.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));
            if (q is not null)
            {
                double qVal = q switch
                {
                    IfcQuantityLength l => l.LengthValue,
                    IfcQuantityArea a => a.AreaValue,
                    IfcQuantityVolume v => v.VolumeValue,
                    IfcQuantityCount c => c.CountValue,
                    IfcQuantityWeight w => w.WeightValue,
                    _ => 0.0
                };
                return qVal.ToString("G", CultureInfo.InvariantCulture);
            }
        }

        return string.Empty;
    }

    private static void SetElementProperty(IfcElement element, string? psetName, string propName, object value)
    {
        psetName ??= "Pset_Custom";
        var pset = element.PropertySets.FirstOrDefault(p => p.Name.Equals(psetName, StringComparison.OrdinalIgnoreCase));
        if (pset is null)
        {
            pset = new IfcPropertySet(psetName);
            element.PropertySets.Add(pset);
        }

        var prop = pset.Properties.FirstOrDefault(p => p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));
        if (prop is null)
        {
            if (value is int intVal) pset.Add(propName, intVal);
            else if (value is double dVal) pset.Add(propName, dVal);
            else if (value is bool bVal) pset.Add(propName, bVal);
            else pset.Add(propName, value.ToString() ?? string.Empty);
        }
        else
        {
            prop.Value = value switch
            {
                bool b => b ? ".T." : ".F.",
                int i => i.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString("G15", CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }
    }

    private static object ParseTypedValue(string raw)
    {
        if (bool.TryParse(raw, out bool b)) return b;
        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return false;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;

        return raw;
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(';') || value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static List<string> ParseCsvLine(string line, string delimiter)
    {
        var result = new List<string>();
        bool insideQuotes = false;
        var cur = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (!insideQuotes && line.AsSpan(i).StartsWith(delimiter.AsSpan(), StringComparison.Ordinal))
            {
                result.Add(cur.ToString());
                cur.Clear();
                i += delimiter.Length - 1;
            }
            else
            {
                cur.Append(c);
            }
        }

        result.Add(cur.ToString());
        return result;
    }
}
