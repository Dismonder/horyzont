using Godot;

using Horyzont.Core.World.Constructs;
using Horyzont.Game.Physics;

namespace Horyzont.Game.Constructs;

/// <summary>One 16³ chunk of a construct: an optional render mesh and an optional static collision body.</summary>
public sealed partial class ConstructChunkNode : Node3D
{
    private readonly List<ChunkLamp> _lamps = [];
    private MeshInstance3D? _meshInstance;
    private StaticBody3D? _body;
    private CollisionShape3D? _shape;

    /// <summary>Creates the node at the chunk's position inside its construct.</summary>
    /// <param name="chunk">Chunk position.</param>
    public ConstructChunkNode(ChunkPos chunk)
    {
        Chunk = chunk;
        Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}";
        Position = new Vector3(chunk.X * ConstructChunk.Size, chunk.Y * ConstructChunk.Size, chunk.Z * ConstructChunk.Size);
    }

    /// <summary>Position of the chunk in its construct.</summary>
    public ChunkPos Chunk { get; }

    /// <summary>The render mesh instance, null in collision-only mode or for chunks without visible faces.</summary>
    public MeshInstance3D? MeshInstance => _meshInstance;

    /// <summary>The collision body, null while the chunk has no collision.</summary>
    public StaticBody3D? Body => _body;

    /// <summary>Number of render surfaces (0, 1 or 2).</summary>
    public int SurfaceCount => _meshInstance?.Mesh?.GetSurfaceCount() ?? 0;

    /// <summary>Number of collision triangles.</summary>
    public int CollisionTriangleCount => (_shape?.Shape as ConcavePolygonShape3D)?.Data.Length / 3 ?? 0;

    /// <summary>True when the node holds neither a mesh nor a collision body.</summary>
    public bool IsEmpty => _meshInstance is null && _body is null;

    /// <summary>The lit lamps of this chunk (K6-30); <see cref="Client.Blocks.BlockLights"/> turns the nearest ones into lights.</summary>
    public IReadOnlyList<ChunkLamp> Lamps => _lamps;

    /// <summary>Replaces the lit lamps of the chunk (main thread).</summary>
    /// <param name="lamps">The lamps from the latest mesher result.</param>
    public void SetLamps(ReadOnlySpan<ChunkLamp> lamps)
    {
        _lamps.Clear();
        foreach (var lamp in lamps)
        {
            _lamps.Add(lamp);
        }
    }

    /// <summary>Replaces the render mesh with the mesher output (main thread). Without visuals (collision-only mode) no mesh is kept.</summary>
    /// <param name="cubes">Prepared cube surface arrays, or null.</param>
    /// <param name="shapes">Prepared prefab shape surface arrays, or null.</param>
    /// <param name="visuals">Materials; null in collision-only mode.</param>
    public void ApplyMesh(Godot.Collections.Array? cubes, Godot.Collections.Array? shapes, BlockVisuals? visuals)
    {
        var mesh = visuals is null ? null : ChunkMeshUploader.CreateMesh(cubes, shapes);
        if (mesh is null)
        {
            ClearMesh();
            return;
        }

        if (_meshInstance is null)
        {
            _meshInstance = new MeshInstance3D { Name = "Mesh", MaterialOverride = visuals!.Material };
            AddChild(_meshInstance);
        }

        _meshInstance.Mesh = mesh;
    }

    /// <summary>Replaces the collision shape (main thread); building the shape is the costliest step of a chunk update.</summary>
    /// <param name="points">Triangle points from <see cref="ChunkMeshUploader.Prepare"/>.</param>
    public void ApplyCollision(Vector3[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var shape = ChunkMeshUploader.CreateCollisionShape(points);
        if (_body is null)
        {
            _body = new StaticBody3D { Name = "Body", CollisionLayer = CollisionLayers.ConstructsMask, CollisionMask = 0 };
            _shape = new CollisionShape3D { Name = "Shape" };
            _body.AddChild(_shape);
            AddChild(_body);
        }

        _shape!.Shape = shape;
    }

    /// <summary>Removes the render mesh.</summary>
    public void ClearMesh()
    {
        if (_meshInstance is null)
        {
            return;
        }

        RemoveChild(_meshInstance);
        _meshInstance.Free();
        _meshInstance = null;
    }

    /// <summary>Removes the collision body.</summary>
    public void ClearCollision()
    {
        if (_body is null)
        {
            return;
        }

        RemoveChild(_body);
        _body.Free();
        _body = null;
        _shape = null;
    }
}
