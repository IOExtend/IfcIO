namespace IfcIO;

/// <summary>Options for reading IFC models.</summary>
public sealed class IfcReadOptions
{
    /// <summary>
    /// Treat revolved-solid sweep values as radians in older IfcIO files that incorrectly declared degrees.
    /// Use only when the source file is known to have been written by the affected exporter.
    /// </summary>
    public bool LegacyIfcIoRevolvedAnglesInRadians { get; init; }
}
