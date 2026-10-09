using System.Numerics;

namespace Horyzont.Constructs.Content;

/// <summary>Immutable triangle mesh in block space [0,1]³, Godot winding (clockwise front faces).</summary>
public sealed class ShapeMesh
{
    private const float Tolerance = 0.001f;

    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly Vector2[] _uvs;
    private readonly int[] _indices;

    /// <summary>Creates a mesh; the arrays are owned by the mesh afterwards.</summary>
    /// <param name="positions">Vertex positions.</param>
    /// <param name="normals">Vertex normals (same count).</param>
    /// <param name="uvs">Vertex texture coordinates (same count).</param>
    /// <param name="indices">Triangle indices, three per triangle.</param>
    public ShapeMesh(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uvs);
        ArgumentNullException.ThrowIfNull(indices);
        _positions = positions;
        _normals = normals;
        _uvs = uvs;
        _indices = indices;
    }

    /// <summary>Vertex positions.</summary>
    public ReadOnlySpan<Vector3> Positions => _positions;

    /// <summary>Vertex normals.</summary>
    public ReadOnlySpan<Vector3> Normals => _normals;

    /// <summary>Vertex texture coordinates.</summary>
    public ReadOnlySpan<Vector2> Uvs => _uvs;

    /// <summary>Triangle indices.</summary>
    public ReadOnlySpan<int> Indices => _indices;

    /// <summary>Checks the mesh against the content rules.</summary>
    /// <returns>A description of the first problem, or null when the mesh is valid.</returns>
    public string? Validate()
    {
        if (_positions.Length == 0 || _indices.Length == 0)
        {
            return "the mesh is empty";
        }

        if (_normals.Length != _positions.Length || _uvs.Length != _positions.Length)
        {
            return "positions, normals and uvs differ in length";
        }

        if (_indices.Length % 3 != 0)
        {
            return "the index count is not divisible by 3";
        }

        foreach (var index in _indices)
        {
            if ((uint)index >= (uint)_positions.Length)
            {
                return $"index {index} is out of range";
            }
        }

        foreach (var p in _positions)
        {
            if (!InRange(p.X) || !InRange(p.Y) || !InRange(p.Z))
            {
                return $"vertex {p} is not finite or lies outside the unit cube";
            }
        }

        foreach (var n in _normals)
        {
            if (!float.IsFinite(n.X) || !float.IsFinite(n.Y) || !float.IsFinite(n.Z))
            {
                return "a normal is not finite";
            }
        }

        foreach (var uv in _uvs)
        {
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                return "a uv is not finite";
            }
        }

        return null;
    }

    private static bool InRange(float value) => float.IsFinite(value) && value >= -Tolerance && value <= 1f + Tolerance;
}
