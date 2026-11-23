using System.Globalization;
using System.Text;

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
        return DecodeString(Arguments[index]);
    }

    internal static string? DecodeString(string arg)
    {
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
        var text = new StepStringState();
        int start = 0;

        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (text.InString || c == '\'')
                text.Read(c);
            else
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
    /// <summary>All entries in the FILE_DESCRIPTION list, in their original order.</summary>
    public IReadOnlyList<string> Descriptions { get; internal set; } = [];
    public string? ImplementationLevel { get; set; }
    public string? FileName { get; set; }
    public string? TimeStamp { get; set; }
    public string? Author { get; set; }
    public string? Organization { get; set; }
    public string? PreprocessorVersion { get; set; }
    public string? OriginatingSystem { get; set; }
    public string? Authorization { get; set; }
    public string? Schema { get; set; }
    /// <summary>All identifiers declared in FILE_SCHEMA.</summary>
    public IReadOnlyList<string> SchemaIdentifiers { get; internal set; } = [];
    internal bool HasSchemaDeclaration { get; set; }

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
        bool inHeader = false;
        bool inData = false;
        var stringState = new StepStringState();
        bool inComment = false;
        bool commentStar = false;
        bool possibleComment = false;
        var statement = new StringBuilder(256);
        // Physical line breaks are not characters in STEP. Do not trim a line:
        // spaces can be significant when a string continues on the next line.
        while (reader.ReadLine() is { } line)
        {
            foreach (char c in line)
            {
                if (inComment)
                {
                    if (commentStar && c == '/')
                        inComment = commentStar = false;
                    else
                        commentStar = c == '*';
                    continue;
                }
                if (possibleComment)
                {
                    possibleComment = false;
                    if (c == '*')
                    {
                        inComment = true;
                        if (statement.Length != 0) statement.Append(' ');
                        continue;
                    }
                    statement.Append('/');
                }
                if (stringState.InString || c == '\'')
                    stringState.Read(c);
                else
                {
                    if (c == '/')
                    {
                        possibleComment = true;
                        continue;
                    }
                    if (c == ';')
                    {
                        string text = statement.ToString().TrimEnd();
                        statement.Clear();
                        if (text.Equals("HEADER", StringComparison.OrdinalIgnoreCase))
                            (inHeader, inData) = (true, false);
                        else if (text.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                            (inHeader, inData) = (false, true);
                        else if (text.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
                            (inHeader, inData) = (false, false);
                        else if (inHeader)
                            ParseHeaderStatement(text, file);
                        else if (inData)
                            ParseDataStatement(text, file);
                        continue;
                    }
                    if (statement.Length == 0 && char.IsWhiteSpace(c)) continue;
                }
                statement.Append(c);
            }
        }

        if (inComment)
            throw new InvalidDataException("Unterminated STEP comment.");
        if (stringState.InString)
            throw new InvalidDataException("Unterminated STEP string.");
        if (statement.Length != 0 || possibleComment || inHeader || inData)
            throw new InvalidDataException("Incomplete STEP statement or section.");

        return file;
    }

    private static void ParseHeaderStatement(string statement, StepFile file)
    {
        int open = statement.IndexOf('(');
        if (open <= 0 || !statement.EndsWith(')')) return;
        var arguments = StepEntity.ParseArguments(statement[(open + 1)..^1]);
        string? Scalar(int index) => index < arguments.Count ? StepEntity.DecodeString(arguments[index]) : null;
        IEnumerable<string> Strings(int index)
        {
            if (index < arguments.Count && arguments[index] is { Length: >= 2 } list
                && list[0] == '(' && list[^1] == ')')
                foreach (string item in StepEntity.ParseArguments(list[1..^1]))
                    if (StepEntity.DecodeString(item) is { } decoded)
                        yield return decoded;
        }
        string? Names(int index)
        {
            string[] names = Strings(index).ToArray();
            return names.Length == 0 ? null : string.Join(", ", names);
        }

        switch (statement[..open].TrimEnd().ToUpperInvariant())
        {
            case "FILE_SCHEMA":
                if (file.HasSchemaDeclaration)
                    throw new InvalidDataException("The STEP header contains more than one FILE_SCHEMA declaration.");
                file.HasSchemaDeclaration = true;
                if (arguments.Count != 1 || arguments[0] is not { Length: >= 2 } list
                    || list[0] != '(' || list[^1] != ')')
                    throw new InvalidDataException("FILE_SCHEMA must contain one list of schema identifiers.");
                var schemaTokens = StepEntity.ParseArguments(list[1..^1]);
                if (schemaTokens.Count == 0 || list.AsSpan(1, list.Length - 2).TrimEnd().EndsWith(","))
                    throw new InvalidDataException("FILE_SCHEMA must contain a nonempty list of schema identifiers.");
                var identifiers = new List<string>(schemaTokens.Count);
                foreach (string token in schemaTokens)
                {
                    if (token.Length < 2 || token[0] != '\'' || token[^1] != '\''
                        || StepEntity.DecodeString(token) is not { Length: > 0 } identifier)
                        throw new InvalidDataException("FILE_SCHEMA identifiers must be nonempty STEP strings.");
                    identifiers.Add(identifier);
                }
                file.SchemaIdentifiers = identifiers.AsReadOnly();
                file.Schema = identifiers[0];
                break;
            case "FILE_DESCRIPTION":
                file.Descriptions = Strings(0).ToArray();
                file.Description = file.Descriptions.Count == 0 ? null : file.Descriptions[0];
                file.ImplementationLevel = Scalar(1);
                break;
            case "FILE_NAME":
                file.FileName = Scalar(0);
                file.TimeStamp = Scalar(1);
                file.Author = Names(2);
                file.Organization = Names(3);
                file.PreprocessorVersion = Scalar(4);
                file.OriginatingSystem = Scalar(5);
                file.Authorization = Scalar(6);
                break;
        }
    }

    private static void ParseDataStatement(string statement, StepFile file)
    {
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

/// <summary>Tracks whether separators belong to a STEP string. A quote used as the operand of \S\ is
/// a byte to shift, not a string delimiter. The same rule is used for statements and argument lists.</summary>
internal struct StepStringState
{
    private enum Directive { None, Start, ShiftSlash, ShiftByte, Page, Unicode, EndSlash }
    private Directive _directive;
    internal bool InString { get; private set; }

    internal void Read(char c)
    {
        if (InString)
        {
            Directive pending = _directive;
            _directive = Directive.None;
            switch (pending)
            {
                case Directive.Start:
                    switch (c)
                    {
                        case '\\': return;
                        case 'S': _directive = Directive.ShiftSlash; return;
                        case 'P': _directive = Directive.Page; return;
                        case 'X': _directive = Directive.Unicode; return;
                        case 'N' or 'F': _directive = Directive.EndSlash; return;
                    }
                    break;
                case Directive.ShiftSlash when c == '\\':
                    _directive = Directive.ShiftByte;
                    return;
                case Directive.ShiftByte when c is >= ' ' and <= '~':
                    return;
                case Directive.Page when c is >= 'A' and <= 'I':
                case Directive.Unicode when c is '0' or '2' or '4':
                    _directive = Directive.EndSlash;
                    return;
                case Directive.Unicode or Directive.EndSlash when c == '\\':
                    return;
            }
            if (c == '\\') _directive = Directive.Start;
        }
        // Doubled apostrophes toggle twice; there is no separator between them.
        if (c == '\'') InString = !InString;
    }
}
