namespace IfcIO.Validation;

/// <summary>
/// Severity level of a validation issue.
/// </summary>
public enum IfcIssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Represents a single validation rule violation or diagnostic finding.
/// </summary>
public sealed class IfcValidationIssue
{
    public required string RuleId { get; init; }
    public required IfcIssueSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? EntityName { get; init; }
    public int? ElementId { get; init; }
    public string? GlobalId { get; init; }

    public override string ToString() =>
        $"[{Severity.ToString().ToUpperInvariant()}] ({RuleId}) {(EntityName is not null ? $"'{EntityName}': " : "")}{Message}";
}
