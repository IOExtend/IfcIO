using System.Globalization;

namespace IfcIO;

/// <summary>A missing representation or an item that could not be read.</summary>
/// <param name="RepresentationId">STEP entity ID of the shape representation.</param>
/// <param name="ItemId">STEP entity ID of the item, or null if the representation itself is missing.</param>
/// <param name="ItemType">STEP entity type, or null if the referenced entity is missing.</param>
public readonly record struct IfcRepresentationImportWarning(int RepresentationId, int? ItemId, string? ItemType)
{
    /// <summary>Source model label or STEP header filename, when available.</summary>
    public string? SourceName { get; init; }

    public override string ToString()
    {
        string message = ItemId is null
            ? string.Create(CultureInfo.InvariantCulture, $"Missing representation #{RepresentationId}.")
            : ItemType is null
                ? string.Create(CultureInfo.InvariantCulture, $"Representation #{RepresentationId}: missing item #{ItemId}.")
                : string.Create(CultureInfo.InvariantCulture, $"Representation #{RepresentationId}: skipped item #{ItemId} ({ItemType}).");
        if (string.IsNullOrWhiteSpace(SourceName)) return message;
        string label = SourceName.Any(char.IsControl)
            ? new string(SourceName.Select(c => char.IsControl(c) ? ' ' : c).ToArray())
            : SourceName;
        return $"{message} Source: {label}";
    }
}
