using System.Globalization;

namespace IfcIO.Core;

/// <summary>
/// A single parsed entity from the DATA section of an ISO 10303-21 STEP physical file.
/// </summary>
public sealed class StepEntity
{
    public required int Id { get; init; }
    public required string Type { get; init; }
    public required string RawArgs { get; init; }

    private List<string>? _arguments;

    /// <summary>Parsed top-level arguments of the entity, respecting nested parentheses and quoted strings.</summary>
    public IReadOnlyList<string> Arguments => _arguments ??= ParseArguments(RawArgs);

    public string? GetString(int index)
    {
        if (index < 0 || index >= Arguments.Count) return null;
        string arg = Arguments[index];
        if (arg == "$" || arg == "*") return null;
        if (arg.StartsWith('\'') && arg.EndsWith('\'') && arg.Length >= 2)
        {
            return StepWriter.Unescape(arg[1..^1]);
        }
        return StepWriter.Unescape(arg);
    }

    public int? GetRef(int index)
    {
        if (index < 0 || index >= Arguments.Count) return null;
        string arg = Arguments[index];
        if (arg.StartsWith('#') && int.TryParse(arg.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int refId))
        {
            return refId;
        }
        return null;
    }

    public List<int> GetRefList(int index)
    {
        var result = new List<int>();
        if (index < 0 || index >= Arguments.Count) return result;
        string arg = Arguments[index];
        if (!arg.StartsWith('(') || !arg.EndsWith(')')) return result;

        string inner = arg[1..^1];
        foreach (var item in ParseArguments(inner))
        {
            if (item.StartsWith('#') && int.TryParse(item.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int refId))
            {
                result.Add(refId);
            }
        }
        return result;
    }

    public double? GetReal(int index)
    {
        if (index < 0 || index >= Arguments.Count) return null;
        string arg = Arguments[index];
        if (arg == "$" || arg == "*") return null;
        if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }
        return null;
    }

    public int? GetInt(int index)
    {
        if (index < 0 || index >= Arguments.Count) return null;
        string arg = Arguments[index];
        if (arg == "$" || arg == "*") return null;
        if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
        {
            return val;
        }
        return null;
    }

    public bool? GetBool(int index)
    {
        if (index < 0 || index >= Arguments.Count) return null;
        string arg = Arguments[index];
        if (arg.Equals(".T.", StringComparison.OrdinalIgnoreCase)) return true;
        if (arg.Equals(".F.", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    public string? GetRaw(int index)
    {
        if (index < 0 || index >= Arguments.Count) return null;
        string arg = Arguments[index];
        return (arg == "$" || arg == "*") ? null : arg;
    }

    public static List<string> ParseArguments(string args)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(args)) return result;

        int depth = 0;
        bool inQuotes = false;
        int start = 0;

        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c == '\'')
            {
                if (inQuotes && i + 1 < args.Length && args[i + 1] == '\'')
                {
                    i++; // Skip escaped quote ''
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (!inQuotes)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(args[start..i].Trim());
                    start = i + 1;
                }
            }
        }

        if (start < args.Length)
        {
            result.Add(args[start..].Trim());
        }

        return result;
    }
}

/// <summary>
/// Represents a parsed ISO 10303-21 STEP physical file containing header data and numbered entities.
/// </summary>
public sealed class StepFile
{
    public string? Description { get; set; }
    public string? ImplementationLevel { get; set; }
    public string? FileName { get; set; }
    public string? TimeStamp { get; set; }
    public string? Author { get; set; }
    public string? Organization { get; set; }
    public string? PreprocessorVersion { get; set; }
    public string? OriginatingSystem { get; set; }
    public string? Schema { get; set; }

    public Dictionary<int, StepEntity> Entities { get; } = new(4096);

    /// <summary>Returns all entities matching a given IFC entity type name.</summary>
    public IEnumerable<StepEntity> OfType(string entityType)
    {
        foreach (var entity in Entities.Values)
        {
            if (entity.Type.Equals(entityType, StringComparison.OrdinalIgnoreCase))
            {
                yield return entity;
            }
        }
    }

    /// <summary>Finds an entity by its #ID, or null if not found.</summary>
    public StepEntity? Find(int? id) => id.HasValue && Entities.TryGetValue(id.Value, out var e) ? e : null;
}

/// <summary>
/// Fast ISO 10303-21 STEP physical file reader with multi-line entity support.
/// </summary>
public static class StepReader
{
    public static StepFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return Read(reader);
    }

    public static StepFile Read(TextReader reader)
    {
        var file = new StepFile();
        string? rawLine;
        bool inHeader = false;
        bool inData = false;

        var statementBuilder = new System.Text.StringBuilder(256);

        while ((rawLine = reader.ReadLine()) is not null)
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("HEADER;", StringComparison.OrdinalIgnoreCase))
            {
                inHeader = true;
                inData = false;
                continue;
            }

            if (line.StartsWith("DATA;", StringComparison.OrdinalIgnoreCase))
            {
                inHeader = false;
                inData = true;
                continue;
            }

            if (line.StartsWith("ENDSEC;", StringComparison.OrdinalIgnoreCase))
            {
                inHeader = false;
                inData = false;
                statementBuilder.Clear();
                continue;
            }

            if (inHeader)
            {
                ParseHeaderLine(line, file);
            }
            else if (inData)
            {
                statementBuilder.Append(line);
                if (line.EndsWith(';'))
                {
                    string fullStatement = statementBuilder.ToString();
                    statementBuilder.Clear();
                    ParseDataStatement(fullStatement, file);
                }
                else
                {
                    statementBuilder.Append(' ');
                }
            }
        }

        return file;
    }

    private static void ParseHeaderLine(string line, StepFile file)
    {
        if (line.StartsWith("FILE_SCHEMA", StringComparison.OrdinalIgnoreCase))
        {
            int q1 = line.IndexOf('\'');
            if (q1 >= 0)
            {
                int q2 = line.IndexOf('\'', q1 + 1);
                if (q2 > q1)
                {
                    file.Schema = line[(q1 + 1)..q2];
                }
            }
        }
        else if (line.StartsWith("FILE_DESCRIPTION", StringComparison.OrdinalIgnoreCase))
        {
            int q1 = line.IndexOf('\'');
            if (q1 >= 0)
            {
                int q2 = line.IndexOf('\'', q1 + 1);
                if (q2 > q1)
                {
                    file.Description = StepWriter.Unescape(line[(q1 + 1)..q2]);
                }
            }
        }
        else if (line.StartsWith("FILE_NAME", StringComparison.OrdinalIgnoreCase))
        {
            int q1 = line.IndexOf('\'');
            if (q1 >= 0)
            {
                int q2 = line.IndexOf('\'', q1 + 1);
                if (q2 > q1)
                {
                    file.FileName = StepWriter.Unescape(line[(q1 + 1)..q2]);
                }
            }
        }
    }

    private static void ParseDataStatement(string statement, StepFile file)
    {
        statement = statement.Trim();
        if (statement.EndsWith(';')) statement = statement[..^1].Trim();
        if (!statement.StartsWith('#')) return;

        int eqIdx = statement.IndexOf('=');
        if (eqIdx <= 1) return;

        if (!int.TryParse(statement.AsSpan(1, eqIdx - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            return;
        }

        int openParen = statement.IndexOf('(', eqIdx);
        if (openParen <= eqIdx) return;

        int closeParen = statement.LastIndexOf(')');
        if (closeParen <= openParen) return;

        string type = statement[(eqIdx + 1)..openParen].Trim();
        string rawArgs = statement[(openParen + 1)..closeParen];

        file.Entities[id] = new StepEntity
        {
            Id = id,
            Type = type,
            RawArgs = rawArgs,
        };
    }
}
