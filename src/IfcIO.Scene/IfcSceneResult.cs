using IfcIO.Schema;
using IOScene;

namespace IfcIO.Scene;

/// <summary>
/// The result of <see cref="IfcSceneReader"/>: the scene and what happened to the model's data.
/// </summary>
public sealed class IfcSceneResult
{
    internal IfcSceneResult(
        SceneDocument document,
        IReadOnlyList<SceneDiagnostic> diagnostics,
        IfcLengthUnit lengthUnit,
        double metresPerUnit,
        int elementCount,
        int elementsWithGeometry,
        int skippedTasks)
    {
        Document = document;
        Diagnostics = diagnostics;
        LengthUnit = lengthUnit;
        MetresPerUnit = metresPerUnit;
        ElementCount = elementCount;
        ElementsWithGeometry = elementsWithGeometry;
        SkippedTasks = skippedTasks;
    }

    /// <summary>The scene, in metres with Z up.</summary>
    public SceneDocument Document { get; }

    /// <summary>
    /// Findings of the conversion. Each converted element without geometry has one entry with its GlobalId as
    /// <see cref="SceneDiagnostic.SourceId"/>: <c>no-geometry</c> (information: IfcIO returned no mesh),
    /// <c>empty-mesh</c> or <c>invalid-mesh</c> (warnings: a mesh without triangles, or with a non-finite coordinate,
    /// an index out of range or an index count not divisible by 3). These elements stay in the scene without geometry.
    /// </summary>
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; }

    /// <summary>The model's length unit (<see cref="IfcModel.LengthUnit"/>) the geometry was converted from.</summary>
    public IfcLengthUnit LengthUnit { get; }

    /// <summary>The exact factor applied to every coordinate and storey elevation: one <see cref="LengthUnit"/> in metres.</summary>
    public double MetresPerUnit { get; }

    /// <summary>Number of converted IFC elements, with or without geometry. The storey nodes of the tree are not counted.</summary>
    public int ElementCount { get; }

    /// <summary>Number of converted elements that have a mesh.</summary>
    public int ElementsWithGeometry { get; }

    /// <summary>
    /// Number of geometries. IfcIO bakes mapped items (<c>IfcMappedItem</c>) into each element's world mesh and does not
    /// expose the shared representation, so every geometry is used by one instance, one per element with geometry.
    /// </summary>
    public int GeometryCount => Document.Geometries.Count;

    /// <summary>
    /// Number of 4D tasks left out of the schedules because they have no planned start and finish (IfcIO reads task
    /// times from IFC4 files only). Their subtasks are kept.
    /// </summary>
    public int SkippedTasks { get; }
}
