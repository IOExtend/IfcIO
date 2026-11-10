using System.Globalization;
using System.Text.RegularExpressions;
using IfcIO.Model;

namespace IfcIO.Query;

/// <summary>
/// Comparison operator for IFC property and attribute selector expressions.
/// </summary>
public enum IfcSelectorOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Exists,
}

/// <summary>
/// A single property, quantity, or attribute filter predicate in a selector expression.
/// </summary>
public sealed record IfcSelectorCondition
{
    public string? PsetName { get; init; }
    public string PropertyName { get; init; } = string.Empty;
    public IfcSelectorOperator Operator { get; init; } = IfcSelectorOperator.Exists;
    public string? ExpectedValue { get; init; }

    public bool Matches(IfcElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // 1. Check direct element attributes
        if (string.IsNullOrEmpty(PsetName))
        {
            if (TryGetElementAttribute(element, PropertyName, out string? attrValue))
            {
                return EvaluateStringCondition(attrValue, Operator, ExpectedValue);
            }

            // Also check material name
            if (PropertyName.Equals("Material", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateStringCondition(element.Material?.Name, Operator, ExpectedValue);
            }
        }

        // 2. Check Property Sets
        foreach (var pset in element.PropertySets)
        {
            if (!string.IsNullOrEmpty(PsetName) && !pset.Name.Equals(PsetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? strVal = pset.GetValue(PropertyName);
            if (strVal is not null)
            {
                return EvaluateValueCondition(strVal, Operator, ExpectedValue);
            }
        }

        // 3. Check Quantity Sets
        foreach (var qto in element.QuantitySets)
        {
            if (!string.IsNullOrEmpty(PsetName) && !qto.Name.Equals(PsetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var q = qto.Quantities.FirstOrDefault(x => x.Name.Equals(PropertyName, StringComparison.OrdinalIgnoreCase));
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
                return EvaluateValueCondition(qVal, Operator, ExpectedValue);
            }
        }

        // If condition was "not equals", missing property might be considered matching if expected value is not null
        return Operator == IfcSelectorOperator.NotEquals && !string.IsNullOrEmpty(ExpectedValue);
    }

    private static bool TryGetElementAttribute(IfcElement element, string propName, out string? value)
    {
        value = null;
        if (propName.Equals("Name", StringComparison.OrdinalIgnoreCase)) { value = element.Name; return true; }
        if (propName.Equals("Type", StringComparison.OrdinalIgnoreCase) || propName.Equals("IfcType", StringComparison.OrdinalIgnoreCase)) { value = element.IfcType; return true; }
        if (propName.Equals("GlobalId", StringComparison.OrdinalIgnoreCase)) { value = element.GlobalId; return true; }
        if (propName.Equals("Tag", StringComparison.OrdinalIgnoreCase)) { value = element.Tag; return true; }
        if (propName.Equals("Description", StringComparison.OrdinalIgnoreCase)) { value = element.TypeProduct?.Description; return true; }
        if (propName.Equals("ObjectType", StringComparison.OrdinalIgnoreCase)) { value = element.TypeProduct?.Name; return true; }
        if (propName.Equals("Id", StringComparison.OrdinalIgnoreCase)) { value = element.Id.ToString(CultureInfo.InvariantCulture); return true; }
        return false;
    }

    private static bool EvaluateValueCondition(object? actual, IfcSelectorOperator op, string? expected)
    {
        if (op == IfcSelectorOperator.Exists) return actual is not null;
        if (actual is null) return false;
        if (expected is null) return op == IfcSelectorOperator.NotEquals;

        // Numeric comparison
        if (IsNumeric(actual, out double numActual) &&
            double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double numExpected))
        {
            return op switch
            {
                IfcSelectorOperator.Equals => Math.Abs(numActual - numExpected) < 1e-6,
                IfcSelectorOperator.NotEquals => Math.Abs(numActual - numExpected) >= 1e-6,
                IfcSelectorOperator.GreaterThan => numActual > numExpected,
                IfcSelectorOperator.GreaterThanOrEqual => numActual >= numExpected,
                IfcSelectorOperator.LessThan => numActual < numExpected,
                IfcSelectorOperator.LessThanOrEqual => numActual <= numExpected,
                _ => false,
            };
        }

        // Boolean comparison
        if (TryGetBoolean(actual, out bool boolActual) && TryGetBoolean(expected, out bool boolExpected))
        {
            return op switch
            {
                IfcSelectorOperator.Equals => boolActual == boolExpected,
                IfcSelectorOperator.NotEquals => boolActual != boolExpected,
                _ => false,
            };
        }

        // Fallback string comparison
        return EvaluateStringCondition(actual.ToString(), op, expected);
    }

    private static bool EvaluateStringCondition(string? actual, IfcSelectorOperator op, string? expected)
    {
        if (op == IfcSelectorOperator.Exists) return !string.IsNullOrEmpty(actual);
        if (actual is null) return op == IfcSelectorOperator.NotEquals && expected is not null;
        if (expected is null) return op == IfcSelectorOperator.NotEquals;

        return op switch
        {
            IfcSelectorOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            IfcSelectorOperator.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            IfcSelectorOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            IfcSelectorOperator.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            IfcSelectorOperator.EndsWith => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool IsNumeric(object value, out double number)
    {
        number = 0;
        return value switch
        {
            double d => (number = d) == d,
            float f => (number = f) == f,
            int i => (number = i) == i,
            long l => (number = l) == l,
            short s => (number = s) == s,
            decimal m => (number = (double)m) == (double)m,
            string str when double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => (number = parsed) == parsed,
            _ => false,
        };
    }

    private static bool TryGetBoolean(object? value, out bool result)
    {
        result = false;
        if (value is null) return false;
        if (value is bool b) { result = b; return true; }
        string s = value.ToString()?.Trim() ?? string.Empty;
        if (s.Equals(".T.", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }
        if (s.Equals(".F.", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }
        return false;
    }
}

/// <summary>
/// A parsed atomic selector matching elements by entity type, storey container, and condition filters.
/// </summary>
public sealed class IfcAtomicSelector
{
    public string? EntityType { get; init; }
    public string? StoreyName { get; init; }
    public List<IfcSelectorCondition> Conditions { get; } = [];

    public bool Matches(IfcElement element, IfcModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(element);

        // 1. Entity type check (case-insensitive, e.g. "IfcWall" or "IFCWALLSTANDARDCASE")
        if (!string.IsNullOrEmpty(EntityType) && EntityType != "*")
        {
            if (!element.IfcType.Equals(EntityType, StringComparison.OrdinalIgnoreCase) &&
                !element.IfcType.StartsWith(EntityType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 2. Storey container check
        if (!string.IsNullOrEmpty(StoreyName) && model is not null)
        {
            var storey = model.Storeys.FirstOrDefault(s => s.Elements.Contains(element));
            if (storey is null || !storey.Name.Equals(StoreyName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 3. Property / Attribute conditions (AND logic across all conditions)
        foreach (var cond in Conditions)
        {
            if (!cond.Matches(element))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// High-performance query selector engine for IFC elements conforming to IfcOpenShell query selector syntax.
/// Supports entity types (e.g. `IfcWall`), spatial storeys (e.g. `#Level 1`),
/// property filters (`[LoadBearing=TRUE]`, `[FireRating>=60]`, `[Name*='Conc']`), and union lists (`IfcWall, IfcColumn`).
/// </summary>
public sealed class IfcSelector
{
    private readonly List<IfcAtomicSelector> _selectors = [];

    public string QueryString { get; }

    public IReadOnlyList<IfcAtomicSelector> AtomicSelectors => _selectors;

    public IfcSelector(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        QueryString = query.Trim();
        ParseQuery(QueryString);
    }

    /// <summary>Compiles a selector query string into an executable <see cref="IfcSelector"/>.</summary>
    public static IfcSelector Parse(string query) => new(query);

    /// <summary>Returns true if the element matches the query selector.</summary>
    public bool Matches(IfcElement element, IfcModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_selectors.Count == 0) return true;

        // Union (OR) across atomic selectors separated by commas
        for (int i = 0; i < _selectors.Count; i++)
        {
            if (_selectors[i].Matches(element, model)) return true;
        }

        return false;
    }

    /// <summary>Selects all elements in the model matching this selector query.</summary>
    public IEnumerable<IfcElement> Evaluate(IfcModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.AllElements.Where(e => Matches(e, model));
    }

    private void ParseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _selectors.Add(new IfcAtomicSelector { EntityType = "*" });
            return;
        }

        // Comma-separated union clauses
        var parts = SplitUnion(query);
        foreach (var part in parts)
        {
            var atomic = ParseAtomic(part.Trim());
            if (atomic is not null)
            {
                _selectors.Add(atomic);
            }
        }
    }

    private static List<string> SplitUnion(string query)
    {
        var list = new List<string>();
        bool insideBrackets = false;
        int start = 0;

        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];
            if (c == '[') insideBrackets = true;
            else if (c == ']') insideBrackets = false;
            else if (c == ',' && !insideBrackets)
            {
                list.Add(query[start..i]);
                start = i + 1;
            }
        }

        if (start < query.Length)
        {
            list.Add(query[start..]);
        }

        return list;
    }

    private static IfcAtomicSelector? ParseAtomic(string clause)
    {
        if (string.IsNullOrWhiteSpace(clause)) return null;

        string? entityType = null;
        string? storeyName = null;
        var conditions = new List<IfcSelectorCondition>();

        // Extract bracketed conditions: [Prop=Val]
        var condRegex = new Regex(@"\[([^\]]+)\]");
        var matches = condRegex.Matches(clause);

        foreach (Match m in matches)
        {
            var cond = ParseCondition(m.Groups[1].Value.Trim());
            if (cond is not null) conditions.Add(cond);
        }

        // Strip bracketed conditions to get entity type or storey
        string remaining = condRegex.Replace(clause, string.Empty).Trim();

        int storeySepIdx = remaining.IndexOfAny(['#', '@']);
        if (storeySepIdx >= 0)
        {
            string ent = remaining[..storeySepIdx].Trim();
            if (!string.IsNullOrEmpty(ent)) entityType = ent;
            string st = remaining[(storeySepIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(st)) storeyName = st;
        }
        else if (!string.IsNullOrEmpty(remaining))
        {
            entityType = remaining;
        }

        var result = new IfcAtomicSelector
        {
            EntityType = entityType,
            StoreyName = storeyName,
        };

        result.Conditions.AddRange(conditions);
        return result;
    }

    private static IfcSelectorCondition? ParseCondition(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Operators: >=, <=, !=, *=, ^=, $=, ==, =, >, <
        string[] ops = [">=", "<=", "!=", "*=", "^=", "$=", "==", "=", ">", "<"];
        string? matchedOp = null;
        int opIdx = -1;

        foreach (var op in ops)
        {
            int idx = raw.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0)
            {
                matchedOp = op;
                opIdx = idx;
                break;
            }
        }

        if (matchedOp is null)
        {
            // Simple existence condition [PropertyName]
            string prop = raw.Trim();
            string? pset = null;
            if (prop.Contains('.'))
            {
                var s = prop.Split('.', 2);
                pset = s[0].Trim();
                prop = s[1].Trim();
            }

            return new IfcSelectorCondition
            {
                PsetName = pset,
                PropertyName = prop,
                Operator = IfcSelectorOperator.Exists,
            };
        }

        string left = raw[..opIdx].Trim();
        string right = raw[(opIdx + matchedOp.Length)..].Trim();

        // Strip enclosing quotes from right value if present
        if ((right.StartsWith('\'') && right.EndsWith('\'')) ||
            (right.StartsWith('"') && right.EndsWith('"')))
        {
            right = right[1..^1];
        }

        string propName = left;
        string? psetName = null;
        if (left.Contains('.'))
        {
            var s = left.Split('.', 2);
            psetName = s[0].Trim();
            propName = s[1].Trim();
        }

        var opEnum = matchedOp switch
        {
            ">=" => IfcSelectorOperator.GreaterThanOrEqual,
            "<=" => IfcSelectorOperator.LessThanOrEqual,
            "!=" => IfcSelectorOperator.NotEquals,
            "*=" => IfcSelectorOperator.Contains,
            "^=" => IfcSelectorOperator.StartsWith,
            "$=" => IfcSelectorOperator.EndsWith,
            ">" => IfcSelectorOperator.GreaterThan,
            "<" => IfcSelectorOperator.LessThan,
            _ => IfcSelectorOperator.Equals,
        };

        return new IfcSelectorCondition
        {
            PsetName = psetName,
            PropertyName = propName,
            Operator = opEnum,
            ExpectedValue = right,
        };
    }
}
