using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Constructs.Sources;
using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Game.Constructs;

/// <summary>A construct in the planet frame; its children are the chunk nodes.</summary>
public sealed partial class ConstructNode : Node3D
{
    private readonly Dictionary<ChunkPos, ConstructChunkNode> _chunks = [];

    /// <summary>Creates a node for a construct.</summary>
    /// <param name="id">Construct entity.</param>
    public ConstructNode(EntityId id)
    {
        ConstructId = id;
        Name = $"Construct_{id.Value}";
    }

    /// <summary>Entity id of the construct.</summary>
    public EntityId ConstructId { get; }

    /// <summary>Number of chunk nodes.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>The chunk nodes (unordered).</summary>
    public IReadOnlyCollection<ConstructChunkNode> Chunks => _chunks.Values;

    /// <summary>
    /// The one place that converts a planet-frame vector to Godot space. In subproject 1 the home planet is centred on the Godot
    /// origin (K2-13 decision 10); later subprojects replace the frame here.
    /// </summary>
    /// <param name="value">Planet-frame vector.</param>
    /// <returns>Godot vector.</returns>
    public static Vector3 ToGodot(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Rotation conversion, see <see cref="ToGodot(System.Numerics.Vector3)"/>.</summary>
    /// <param name="value">Planet-frame rotation.</param>
    /// <returns>Godot quaternion.</returns>
    public static Quaternion ToGodot(System.Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    /// <summary>Places the node from the construct pose (origin is the corner of cell (0,0,0)).</summary>
    /// <param name="pose">The pose.</param>
    public void SetPose(ConstructPose pose) =>
        Transform = new Transform3D(new Basis(ToGodot(pose.Rotation).Normalized()), ToGodot(pose.Origin));

    /// <summary>Finds a chunk node.</summary>
    /// <param name="chunk">Chunk position.</param>
    /// <param name="node">The node when present.</param>
    /// <returns>True when present.</returns>
    public bool TryGetChunk(ChunkPos chunk, [NotNullWhen(true)] out ConstructChunkNode? node) => _chunks.TryGetValue(chunk, out node);

    /// <summary>Returns the chunk node, creating it when absent.</summary>
    /// <param name="chunk">Chunk position.</param>
    /// <returns>The node.</returns>
    public ConstructChunkNode GetOrCreateChunk(ChunkPos chunk)
    {
        if (!_chunks.TryGetValue(chunk, out var node))
        {
            node = new ConstructChunkNode(chunk);
            _chunks.Add(chunk, node);
            AddChild(node);
        }

        return node;
    }

    /// <summary>Frees a chunk node.</summary>
    /// <param name="chunk">Chunk position.</param>
    public void RemoveChunk(ChunkPos chunk)
    {
        if (_chunks.Remove(chunk, out var node))
        {
            RemoveChild(node);
            node.Free();
        }
    }

    /// <summary>Removes the collision bodies of every chunk (the construct left the collision range).</summary>
    public void ClearCollision()
    {
        foreach (var chunk in _chunks.Values)
        {
            chunk.ClearCollision();
        }
    }
}
