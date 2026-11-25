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
    private List<Range>? _argumentRanges;

    /// <summary>Parsed top-level arguments of the entity, respecting nested parentheses and quoted strings.</summary>
    public IReadOnlyList<string> Arguments
    {
        get
        {
            _arguments ??= ParseArguments(RawArgs);
            _argumentRanges = null;
            return _arguments;
        }
    }

    private bool TryGetArgument(int index, out ReadOnlySpan<char> arg)
    {
        arg = default;
        if (index < 0) return false;
        if (_arguments is { } materialized)
        {
            if (index >= materialized.Count) return false;
            arg = materialized[index];
            return true;
        }

        var ranges = _argumentRanges;
        if (ranges is null)
        {
            // Numeric and reference accessors can use slices of RawArgs without retaining a string per field.
            ranges = new List<Range>();
            var scanner = new ArgumentScanner(RawArgs);
            while (scanner.Next(out var range)) ranges.Add(range);
            _argumentRanges = ranges;
        }
        if (index >= ranges.Count) return false;
        arg = RawArgs.AsSpan(ranges[index]);
        return true;
    }

    public string? GetString(int index) => TryGetArgument(index, out var arg) ? DecodeString(arg) : null;

    internal static string? DecodeString(ReadOnlySpan<char> arg)
    {
        if (Omitted(arg)) return null;
        if (arg.Length >= 2 && arg[0] == '\'' && arg[^1] == '\'') arg = arg[1..^1];
        return arg.IndexOfAny('\'', '\\') < 0 ? arg.ToString() : StepWriter.Unescape(arg.ToString());
    }

    public int? GetRef(int index) => TryGetArgument(index, out var arg) && ParseRef(arg) is int reference ? reference : null;

    private static int? ParseRef(ReadOnlySpan<char> arg) => !arg.IsEmpty && arg[0] == '#'
        && int.TryParse(arg[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : null;

    public List<int> GetRefList(int index)
    {
        var result = new List<int>();
        if (!TryGetArgument(index, out var arg) || arg.Length < 2 || arg[0] != '(' || arg[^1] != ')') return result;

        var inner = arg[1..^1];
        var scanner = new ArgumentScanner(inner);
        while (scanner.Next(out var range))
            if (ParseRef(inner[range]) is int reference) result.Add(reference);
        return result;
    }

    public double? GetReal(int index) => TryGetArgument(index, out var arg)
        && double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    public int? GetInt(int index) => TryGetArgument(index, out var arg)
        && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    public bool? GetBool(int index)
    {
        if (!TryGetArgument(index, out var arg)) return null;
        if (arg.Equals(".T.", StringComparison.OrdinalIgnoreCase)) return true;
        if (arg.Equals(".F.", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    public string? GetRaw(int index)
    {
        if (_arguments is { } materialized)
        {
            if (index < 0 || index >= materialized.Count) return null;
            string value = materialized[index];
            return Omitted(value) ? null : value;
        }
        if (!TryGetArgument(index, out var arg) || Omitted(arg)) return null;
        return arg.Length == RawArgs.Length ? RawArgs : arg.ToString();
    }

    private static bool Omitted(ReadOnlySpan<char> arg) => arg.Length == 1 && arg[0] is '$' or '*';

    internal bool IsNullParameter(int index) =>
        TryGetArgument(index, out var arg) && arg.Length == 1 && arg[0] == '$';

    public static List<string> ParseArguments(string args)
    {
        var result = new List<string>();
        var scanner = new ArgumentScanner(args);
        while (scanner.Next(out var range)) result.Add(args[range]);
        return result;
    }

    private ref struct ArgumentScanner
    {
        private readonly ReadOnlySpan<char> _text;
        private int _next;

        internal ArgumentScanner(ReadOnlySpan<char> text)
        {
            _text = text;
            _next = text.IsWhiteSpace() ? text.Length : 0;
        }

        internal bool Next(out Range range)
        {
            if (_next >= _text.Length)
            {
                range = default;
                return false;
            }

            int depth = 0;
            var state = new StepStringState();
            int end = _next;
            for (; end < _text.Length; end++)
            {
                char c = _text[end];
                if (state.InString || c == '\'')
                    state.Read(c);
                else
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0) break;
                }
            }

            int start = _next;
            _next = end < _text.Length ? end + 1 : end;
            while (start < end && char.IsWhiteSpace(_text[start])) start++;
            while (end > start && char.IsWhiteSpace(_text[end - 1])) end--;
            range = start..end;
            return true;
        }
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

    private readonly Dictionary<int, StepEntity> _entities = new(4096);
    private Dictionary<string, List<StepEntity>>? _typeIndex;

    /// <summary>The editable entity dictionary.</summary>
    public Dictionary<int, StepEntity> Entities => _entities;

    internal Dictionary<int, StepEntity>.ValueCollection EntityValues => _entities.Values;
    internal int EntityCount => _entities.Count;

    internal void SetEntity(StepEntity entity) => _entities[entity.Id] = entity;

    internal void IndexTypes()
    {
        var index = new Dictionary<string, List<StepEntity>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in _entities.Values)
        {
            if (!index.TryGetValue(entity.Type, out var bucket))
                index.Add(entity.Type, bucket = []);
            bucket.Add(entity);
        }
        _typeIndex = index;
    }

    /// <summary>Returns all entities matching a given IFC entity type name.</summary>
    public IEnumerable<StepEntity> OfType(string entityType)
    {
        foreach (var entity in _entities.Values)
        {
            if (entity.Type.Equals(entityType, StringComparison.OrdinalIgnoreCase))
            {
                yield return entity;
            }
        }
    }

    // Only used by the model importer, which neither exposes nor edits the parsed dictionary.
    internal IEnumerable<StepEntity> IndexedOfType(string entityType) =>
        _typeIndex is not null && _typeIndex.TryGetValue(entityType, out var bucket) ? bucket : [];
    internal IEnumerable<string> IndexedTypeNames => _typeIndex is { } index ? index.Keys : [];

    /// <summary>Finds an entity by its #ID, or null if not found.</summary>
    public StepEntity? Find(int? id) => id.HasValue && _entities.TryGetValue(id.Value, out var e) ? e : null;
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

    public static StepFile Read(TextReader reader) => Read(reader, indexTypes: false);

    internal static StepFile Read(TextReader reader, bool indexTypes)
    {
        var file = new StepFile();
        bool inHeader = false;
        bool inData = false;
        var stringState = new StepStringState();
        bool inComment = false;
        bool commentStar = false;
        bool possibleComment = false;
        var statement = new StringBuilder(256);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
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
                            ParseDataStatement(text, file, names);
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

        if (indexTypes) file.IndexTypes();
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

    private static void ParseDataStatement(string statement, StepFile file, Dictionary<string, string> names)
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

        string type = InternTypeName(statement.AsSpan(eqIdx + 1, openParen - eqIdx - 1).Trim(), names);
        string rawArgs = statement[(openParen + 1)..closeParen];

        file.SetEntity(new StepEntity
        {
            Id = id,
            Type = type,
            RawArgs = rawArgs,
        });
    }

    private static string InternTypeName(ReadOnlySpan<char> name, Dictionary<string, string> names)
    {
#if NET9_0_OR_GREATER
        if (names.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out string? existing))
            return existing;
        string value = name.ToString();
#else
        string value = name.ToString();
        if (names.TryGetValue(value, out string? existing)) return existing;
#endif
        names.Add(value, value);
        return value;
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
