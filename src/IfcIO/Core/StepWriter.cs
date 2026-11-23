using System.Globalization;
using System.Text;

namespace IfcIO.Core;

/// <summary>
/// Low-level IFC STEP physical file writer (ISO 10303-21).
/// Manages entity IDs, argument formatting, Unicode character escaping, and file structure.
/// </summary>
public sealed class StepWriter
{
    private int _nextId = 1;
    private readonly List<(int Id, string Type, string Args)> _entities = new(4096);

    public int NextId() => _nextId++;

    /// <summary>Adds an entity and returns its assigned #ID.</summary>
    public int Add(string type, string args)
    {
        int id = _nextId++;
        _entities.Add((id, type, args));
        return id;
    }

    /// <summary>Adds an entity with a pre-assigned ID.</summary>
    public void AddWithId(int id, string type, string args)
    {
        _entities.Add((id, type, args));
        if (id >= _nextId)
        {
            _nextId = id + 1;
        }
    }

    public int EntityCount => _entities.Count;

    public (int Id, string Type, string Args) EntityAt(int index) => _entities[index];

    public void ReplaceArgs(int index, string args)
    {
        var (id, type, _) = _entities[index];
        _entities[index] = (id, type, args);
    }

    /// <summary>Writes the complete IFC STEP file to a <see cref="TextWriter"/>.</summary>
    public void WriteTo(
        TextWriter w,
        string fileName,
        string schema = "IFC2X3",
        string description = "ViewDefinition [CoordinationView_V2.0]",
        string application = "IfcIO",
        string organization = "IOExtend")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        w.WriteLine("ISO-10303-21;");
        w.WriteLine("HEADER;");
        w.WriteLine($"FILE_DESCRIPTION(('{Escape(description)}'),'2;1');");
        w.WriteLine($"FILE_NAME('{Escape(fileName)}','{now}',('{Escape(organization)}'),('{Escape(organization)}'),'{Escape(application)}','IfcIO','');");
        w.WriteLine($"FILE_SCHEMA(('{Escape(schema)}'));");
        w.WriteLine("ENDSEC;");
        w.WriteLine("DATA;");

        for (int i = 0; i < _entities.Count; i++)
        {
            var (id, type, args) = _entities[i];
            w.Write('#');
            w.Write(id);
            w.Write('=');
            w.Write(type);
            w.Write('(');
            w.Write(args);
            w.WriteLine(");");
        }

        w.WriteLine("ENDSEC;");
        w.WriteLine("END-ISO-10303-21;");
    }

    public static string Ref(int id) => $"#{id}";

    public const string Null = "$";

    public static string Number(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Number(int? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "$";

    public static string Str(string? s) => s is null ? "$" : $"'{Escape(s)}'";

    public static string Enum(string v) => $".{v}.";

    public static string List(params string[] items) => $"({string.Join(",", items)})";

    public static string List(IEnumerable<string> items) => $"({string.Join(",", items)})";

    public static string Real(double v)
    {
        string text = v.ToString("0.######", CultureInfo.InvariantCulture);
        return text.Contains('.') ? text : text + ".";
    }

    public static string RealExact(double v)
    {
        string text = v.ToString("G15", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e'))
        {
            text += ".";
        }
        return text;
    }

    /// <summary>
    /// Escapes string according to ISO 10303-21 STEP rules:
    /// - Extended Unicode characters (e.g. Cyrillic) encoded as \X2\HHHH...\X0\
    /// - Latin-1 control characters encoded as \X\HH
    /// - Single quotes escaped as ''
    /// - Backslashes escaped as \\
    /// </summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length * 2);
        bool inUnicode = false;

        foreach (char c in s)
        {
            if (c < 0x100 && (c > 0x7E || c < 0x20))
            {
                if (inUnicode) { sb.Append("\\X0\\"); inUnicode = false; }
                sb.Append("\\X\\").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else if (c > 0x7E || c < 0x20)
            {
                if (!inUnicode) { sb.Append("\\X2\\"); inUnicode = true; }
                sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                if (inUnicode) { sb.Append("\\X0\\"); inUnicode = false; }
                if (c == '\\') sb.Append("\\\\");
                else if (c == '\'') sb.Append("''");
                else sb.Append(c);
            }
        }

        if (inUnicode)
        {
            sb.Append("\\X0\\");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Unescapes an ISO 10303-21 STEP string, including ISO 8859 page directives and
    /// the two- and four-byte Unicode forms.
    /// </summary>
    public static string? Unescape(string escaped)
    {
        if (string.IsNullOrEmpty(escaped)) return string.Empty;

        var sb = new StringBuilder(escaped.Length);
        Encoding page = Encoding.Latin1;
        Span<byte> pageByte = stackalloc byte[1];
        Span<char> pageChar = stackalloc char[1];
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (c == '\'')
            {
                if (i + 1 < escaped.Length && escaped[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i++;
                }
                else
                {
                    sb.Append('\'');
                }
            }
            else if (c == '\\')
            {
                ReadOnlySpan<char> suffix = escaped.AsSpan(i);
                if (suffix.StartsWith("\\\\", StringComparison.Ordinal))
                {
                    sb.Append('\\');
                    i++;
                }
                else if (suffix.StartsWith("\\N\\", StringComparison.Ordinal)
                    || suffix.StartsWith("\\F\\", StringComparison.Ordinal))
                {
                    i += 2;
                }
                else if (suffix.Length >= 4 && suffix[1] == 'P' && suffix[2] is >= 'A' and <= 'I' && suffix[3] == '\\')
                {
                    var encoding = suffix[2] == 'A' ? Encoding.Latin1
                        : CodePagesEncodingProvider.Instance.GetEncoding(28591 + suffix[2] - 'A',
                            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    if (encoding is not null) page = encoding;
                    else sb.Append(suffix[..4]);
                    i += 3;
                }
                else if (suffix.StartsWith("\\S\\", StringComparison.Ordinal) && suffix.Length >= 4
                    && suffix[3] is >= ' ' and <= '~')
                {
                    pageByte[0] = (byte)(suffix[3] + 128);
                    try
                    {
                        int read = page.GetChars(pageByte, pageChar);
                        sb.Append(pageChar[..read]);
                    }
                    catch (DecoderFallbackException)
                    {
                        sb.Append(suffix[..4]);
                    }
                    i += 3;
                }
                else if (suffix.StartsWith("\\X\\", StringComparison.Ordinal))
                {
                    if (i + 5 <= escaped.Length &&
                        byte.TryParse(escaped.AsSpan(i + 3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte latin1))
                    {
                        sb.Append((char)latin1);
                        i += 4;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (suffix.Length >= 4 && suffix[1] == 'X' && suffix[2] is '2' or '4' && suffix[3] == '\\')
                {
                    int end = suffix[4..].IndexOf('\\');
                    if (end >= 0 && suffix[(end + 4)..].StartsWith("\\X0\\", StringComparison.Ordinal))
                    {
                        int length = end + 8;
                        int checkpoint = sb.Length;
                        if (!AppendExtended(suffix.Slice(4, end), suffix[2] == '2' ? 4 : 8, sb))
                        {
                            sb.Length = checkpoint;
                            sb.Append(suffix[..length]);
                        }
                        i += length - 1;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool AppendExtended(ReadOnlySpan<char> hex, int digits, StringBuilder output)
    {
        if (hex.IsEmpty || hex.Length % digits != 0) return false;
        Span<char> scalar = stackalloc char[2];
        for (int i = 0; i < hex.Length; i += digits)
        {
            if (!int.TryParse(hex.Slice(i, digits), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out int value)) return false;
            if (digits == 4)
            {
                // Some STEP writers, including ours, use X2 for UTF-16 surrogate pairs.
                output.Append((char)value);
            }
            else if (Rune.TryCreate(value, out Rune rune))
            {
                int written = rune.EncodeToUtf16(scalar);
                output.Append(scalar[..written]);
            }
            else return false;
        }
        return true;
    }
}
