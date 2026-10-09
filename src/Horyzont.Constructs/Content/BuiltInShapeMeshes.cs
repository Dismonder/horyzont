using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Horyzont.Constructs.Content;

/// <summary>Prefab shapes shipped with the game: <c>builtin:slab</c> and <c>builtin:slope</c>.</summary>
public static class BuiltInShapeMeshes
{
    /// <summary>Reference of the half-height slab (identity orientation: the lower half of the cell).</summary>
    public const string SlabRef = "builtin:slab";

    /// <summary>Reference of the slope (identity orientation: height 1 at the −Z edge, 0 at the +Z edge).</summary>
    public const string SlopeRef = "builtin:slope";

    /// <summary>Resolves <c>builtin:slab</c> and <c>builtin:slope</c>.</summary>
    /// <param name="meshRef">Mesh reference.</param>
    /// <param name="mesh">A new mesh when the reference is built in.</param>
    /// <returns>True for the built-in references.</returns>
    public static bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh)
    {
        switch (meshRef)
        {
            case SlabRef:
                mesh = Slab();
                return true;
            case SlopeRef:
                mesh = Slope();
                return true;
            default:
                mesh = null;
                return false;
        }
    }

    private static ShapeMesh Slab()
    {
        var b = new Builder();
        const float h = 0.5f;
        b.Quad(new(1, 0, 0), new(1, h, 0), new(1, h, 1), new(1, 0, 1)); // +X
        b.Quad(new(0, 0, 1), new(0, h, 1), new(0, h, 0), new(0, 0, 0)); // -X
        b.Quad(new(0, h, 0), new(0, h, 1), new(1, h, 1), new(1, h, 0)); // +Y
        b.Quad(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1)); // -Y
        b.Quad(new(1, 0, 1), new(1, h, 1), new(0, h, 1), new(0, 0, 1)); // +Z
        b.Quad(new(0, 0, 0), new(0, h, 0), new(1, h, 0), new(1, 0, 0)); // -Z
        return b.Build();
    }

    private static ShapeMesh Slope()
    {
        var b = new Builder();
        b.Quad(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1)); // -Y
        b.Quad(new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0)); // -Z (the tall edge)
        b.Quad(new(0, 1, 0), new(0, 0, 1), new(1, 0, 1), new(1, 1, 0)); // slope, normal (0,1,1)
        b.Triangle(new(1, 0, 0), new(1, 1, 0), new(1, 0, 1)); // +X
        b.Triangle(new(0, 0, 1), new(0, 1, 0), new(0, 0, 0)); // -X
        return b.Build();
    }

    // Corners are given counter-clockwise seen from outside (right-handed); the mesh is emitted clockwise (Godot front faces).
    private sealed class Builder
    {
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<Vector2> _uvs = [];
        private readonly List<int> _indices = [];

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var start = _positions.Count;
            Add(a, normal);
            Add(b, normal);
            Add(c, normal);
            Add(d, normal);
            _indices.AddRange([start, start + 2, start + 1, start, start + 3, start + 2]);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var start = _positions.Count;
            Add(a, normal);
            Add(b, normal);
            Add(c, normal);
            _indices.AddRange([start, start + 2, start + 1]);
        }

        public ShapeMesh Build() => new([.. _positions], [.. _normals], [.. _uvs], [.. _indices]);

        private void Add(Vector3 p, Vector3 normal)
        {
            _positions.Add(p);
            _normals.Add(normal);

            // Planar projection along the dominant axis of the normal.
            var ax = MathF.Abs(normal.X);
            var ay = MathF.Abs(normal.Y);
            var az = MathF.Abs(normal.Z);
            _uvs.Add(ax >= ay && ax >= az ? new Vector2(p.Z, 1 - p.Y) : ay >= az ? new Vector2(p.X, p.Z) : new Vector2(p.X, 1 - p.Y));
        }
    }
}
