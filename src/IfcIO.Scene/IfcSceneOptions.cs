namespace IfcIO.Scene;

/// <summary>
/// Options for <see cref="IfcSceneReader"/>.
/// </summary>
public sealed class IfcSceneOptions
{
    /// <summary>
    /// The key of the scene model (<c>SceneModel.Key</c>), which with each element's <c>SourceId</c> forms the identity
    /// that may be stored outside the scene. Null uses the file name without directory, for example <c>house.ifc</c>.
    /// Required when the model is converted from memory, where there is no file name.
    /// </summary>
    public string? ModelKey { get; set; }

    /// <summary>
    /// Also converts spaces (<c>IfcSpace</c>, <see cref="IfcModel.AllSpaces"/>), drawn translucent unless their
    /// material says otherwise. False by default, because closed room volumes hide the elements inside them.
    /// </summary>
    public bool IncludeSpaces { get; set; }
}
