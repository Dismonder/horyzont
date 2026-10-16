using System.Numerics;

using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Meshing;

/// <summary>Output buffers of the mesher; reusable, grow through the array pool and never shrink below the high-water mark.</summary>
public sealed class ChunkMeshData : IDisposable
{
    private readonly PooledBuffer<Vector3> _collision = new();
    private readonly PooledBuffer<CellPos> _lights = new();

    /// <summary>Greedy-merged cube faces (one render surface per chunk).</summary>
    public MeshSurface Cubes { get; } = new();

    /// <summary>Prefab shapes (<c>BlockShape.Mesh</c>), rotated to their orientation (second render surface).</summary>
    public MeshSurface Shapes { get; } = new();

    /// <summary>Collision triangles, three vertices per triangle (<c>ConcavePolygonShape3D.Data</c>), clockwise front faces.</summary>
    public ReadOnlySpan<Vector3> CollisionFaces => _collision.AsSpan();

    /// <summary>Cells of the lit lamps of the chunk, in construct cells (K6-30 D7); the game puts a light on each.</summary>
    public ReadOnlySpan<CellPos> LightCells => _lights.AsSpan(); // K6-30 B5: the mesher fills it.

    /// <summary>True when there is nothing to render and nothing to collide with.</summary>
    public bool IsEmpty => Cubes.IsEmpty && Shapes.IsEmpty && _collision.Count == 0;

    /// <summary>Empties the buffers, keeping their capacity.</summary>
    public void Clear()
    {
        Cubes.Clear();
        Shapes.Clear();
        _collision.Clear();
        _lights.Clear();
    }

    internal void AddCollisionVertex(Vector3 position) => _collision.Add(position);

    /// <inheritdoc />
    public void Dispose()
    {
        Cubes.Dispose();
        Shapes.Dispose();
        _collision.Dispose();
        _lights.Dispose();
    }
}
