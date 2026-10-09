using Horyzont.Constructs.Content;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Meshing;

/// <summary>What the mesher needs to know about one block type.</summary>
public readonly struct BlockRenderInfo
{
    /// <summary>Creates render info for a non-empty type.</summary>
    /// <param name="shape">Block shape.</param>
    /// <param name="materialId">Content-local material id, from 1.</param>
    /// <param name="hasCollision">Whether the block contributes to the collision mesh.</param>
    /// <param name="isMissing">Whether the type is drawn as core:missing.</param>
    /// <param name="mesh">Prefab mesh for <see cref="BlockShape.Mesh"/>.</param>
    public BlockRenderInfo(BlockShape shape, byte materialId, bool hasCollision, bool isMissing, ShapeMesh? mesh)
    {
        Shape = shape;
        MaterialId = materialId;
        HasCollision = hasCollision;
        IsMissing = isMissing;
        Mesh = mesh;
    }

    /// <summary>Shape; 0 (default) is an empty cell.</summary>
    public BlockShape Shape { get; }

    /// <summary>Layer in the block texture array is <c>MaterialId - 1</c>.</summary>
    public byte MaterialId { get; }

    /// <summary>Whether the block is part of the collision mesh.</summary>
    public bool HasCollision { get; }

    /// <summary>True for cubes: they hide the faces of their neighbours.</summary>
    public bool Occludes => Shape == BlockShape.Cube;

    /// <summary>True when the registry key is unknown to the content (rendered as core:missing).</summary>
    public bool IsMissing { get; }

    /// <summary>Prefab mesh for <see cref="BlockShape.Mesh"/>, otherwise null.</summary>
    public ShapeMesh? Mesh { get; }

    /// <summary>True for an empty cell.</summary>
    public bool IsEmpty => Shape == 0;
}
