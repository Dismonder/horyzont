using System.Numerics;

namespace Horyzont.Constructs.Meshing;

/// <summary>One render surface of a chunk: parallel vertex arrays and triangle indices (clockwise front faces).</summary>
public sealed class MeshSurface : IDisposable
{
    private readonly PooledBuffer<Vector3> _positions = new();
    private readonly PooledBuffer<Vector3> _normals = new();
    private readonly PooledBuffer<Vector2> _uvs = new();
    private readonly PooledBuffer<Vector2> _uv2 = new();
    private readonly PooledBuffer<int> _indices = new();

    internal MeshSurface()
    {
    }

    /// <summary>Vertex positions relative to the chunk origin (0 … 16).</summary>
    public ReadOnlySpan<Vector3> Positions => _positions.AsSpan();

    /// <summary>Vertex normals.</summary>
    public ReadOnlySpan<Vector3> Normals => _normals.AsSpan();

    /// <summary>Texture coordinates (cell units in the plane of a cube face).</summary>
    public ReadOnlySpan<Vector2> Uvs => _uvs.AsSpan();

    /// <summary>Uv2.X = layer in the block texture array (material id − 1).</summary>
    public ReadOnlySpan<Vector2> Uv2 => _uv2.AsSpan();

    /// <summary>Triangle indices, three per triangle.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan();

    /// <summary>Number of vertices.</summary>
    public int VertexCount => _positions.Count;

    /// <summary>True when the surface has no triangles.</summary>
    public bool IsEmpty => _indices.Count == 0;

    internal void Clear()
    {
        _positions.Clear();
        _normals.Clear();
        _uvs.Clear();
        _uv2.Clear();
        _indices.Clear();
    }

    internal void AddVertex(Vector3 position, Vector3 normal, Vector2 uv, float layer)
    {
        _positions.Add(position);
        _normals.Add(normal);
        _uvs.Add(uv);
        _uv2.Add(new Vector2(layer, 0));
    }

    internal void AddIndex(int index) => _indices.Add(index);

    /// <inheritdoc />
    public void Dispose()
    {
        _positions.Dispose();
        _normals.Dispose();
        _uvs.Dispose();
        _uv2.Dispose();
        _indices.Dispose();
    }
}
