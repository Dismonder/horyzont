using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Constructs.Content;

/// <summary>Resolves the mesh reference of a block type (for example a Godot resource path) into a <see cref="ShapeMesh"/>.</summary>
public interface IShapeMeshResolver
{
    /// <summary>Resolves a mesh reference.</summary>
    /// <param name="meshRef">The reference from the content file.</param>
    /// <param name="mesh">The mesh when resolved.</param>
    /// <returns>True when the reference is known.</returns>
    bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh);
}
