namespace IfcIO.Validation.Ids;

/// <summary>
/// A detailed violation record for an element failing a buildingSMART IDS specification requirement.
/// </summary>
public sealed record IfcIdsFailure
{
    public string SpecificationName { get; init; } = string.Empty;
    public int ElementId { get; init; }
    public string? ElementGuid { get; init; }
    public string? ElementName { get; init; }
    public string ElementType { get; init; } = string.Empty;
    public string FacetType { get; init; } = "Property";
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Comprehensive validation audit report produced by evaluating a model against buildingSMART IDS 1.0 rules.
/// </summary>
public sealed partial class IfcIdsValidationReport
{
    public string Title { get; init; } = "IDS Compliance Audit";
    public string? IdsVersion { get; init; } = "1.0";
    public string? Author { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public int TotalSpecifications { get; set; }
    public int PassedSpecifications { get; set; }
    public int FailedSpecifications { get; set; }
    public int TotalApplicableElements { get; set; }

    public bool IsCompliant => FailedSpecifications == 0 && Failures.Count == 0;
    public bool IsPassed => IsCompliant;

    public List<IfcIdsFailure> Failures { get; } = [];
}
