namespace IfcIO.Validation;

/// <summary>
/// Contains the aggregated results and diagnostics of an IFC model validation.
/// </summary>
public sealed class IfcValidationResult
{
    public required IReadOnlyList<IfcValidationIssue> Issues { get; init; }

    /// <summary>
    /// Returns true if there are zero error-level issues in the model.
    /// </summary>
    public bool IsValid => Issues.All(i => i.Severity != IfcIssueSeverity.Error);

    public int ErrorCount => Issues.Count(i => i.Severity == IfcIssueSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == IfcIssueSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == IfcIssueSeverity.Info);

    public IEnumerable<IfcValidationIssue> Errors => Issues.Where(i => i.Severity == IfcIssueSeverity.Error);
    public IEnumerable<IfcValidationIssue> Warnings => Issues.Where(i => i.Severity == IfcIssueSeverity.Warning);

    public static IfcValidationResult Empty { get; } = new() { Issues = [] };
}
