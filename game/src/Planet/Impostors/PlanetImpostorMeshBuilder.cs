using System.Diagnostics;

using Godot;

using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Planet.Surface;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Planet.Impostors;

/// <summary>The arrays of an impostor mesh, built off the main thread (planet frame, metres — never world positions).</summary>
public sealed class PlanetImpostorMeshData
{
    internal PlanetImpostorMeshData(int quadsPerSide, int vertices, int indices)
    {
        QuadsPerSide = quadsPerSide;
        Vertices = new Vector3[vertices];
        Normals = new Vector3[vertices];
        Colors = new Color[vertices];
        Indices = new int[indices];
    }

    /// <summary>Gets the quads along a face side (<see cref="PlanetImpostorMeshBuilder.QuadsPerSide"/> or <see cref="PlanetImpostorMeshBuilder.FarQuadsPerSide"/>).</summary>
    public int QuadsPerSide { get; }

    /// <summary>Gets the vertex positions, planet-local.</summary>
    public Vector3[] Vertices { get; }

    /// <summary>Gets the vertex normals.</summary>
    public Vector3[] Normals { get; }

    /// <summary>Gets the vertex colours (the base colour of the surface material).</summary>
    public Color[] Colors { get; }

    /// <summary>Gets the triangle indices (clockwise seen from outside, the front faces of Godot).</summary>
    public int[] Indices { get; }

    /// <summary>Gets the build time in milliseconds.</summary>
    public double BuildMilliseconds { get; internal set; }
}

/// <summary>
/// The impostor of a body (K5-22 decision 12): a cube-sphere of 6 faces × 64 × 64 quads (65 × 65 vertices per face; 32 × 32 quads,
/// 33 × 33 vertices, for a body whose camera altitude is over 2 R — <see cref="FarQuadsPerSide"/>), each vertex at
/// <c>d · (R + h(d))</c> from <see cref="PlanetSurfaceSampler"/>, coloured with the base colour of the surface material one metre under the
/// surface (read from the generator through <see cref="GeneratorProbe"/>), normals from the neighbouring triangles. Faces are built in
/// parallel, each with its own probe; any thread.
/// </summary>
public static class PlanetImpostorMeshBuilder
{
    /// <summary>Quads along a face side.</summary>
    public const int QuadsPerSide = 64;

    /// <summary>Quads along a face side of the impostor of a far body (camera altitude over 2 R, the impostor alone; Opus review of K5-22).</summary>
    public const int FarQuadsPerSide = 32;

    /// <summary>Vertices along a face side.</summary>
    public const int VerticesPerSide = QuadsPerSide + 1;

    /// <summary>Faces of the cube.</summary>
    public const int Faces = 6;

    /// <summary>Vertices of the mesh.</summary>
    public const int VertexCount = Faces * VerticesPerSide * VerticesPerSide;

    /// <summary>Triangles of the mesh.</summary>
    public const int TriangleCount = Faces * QuadsPerSide * QuadsPerSide * 2;

    /// <summary>The unit direction of a vertex of a face (cube-sphere).</summary>
    /// <param name="face">Face 0–5 (+X, −X, +Y, −Y, +Z, −Z).</param>
    /// <param name="i">Column 0–64.</param>
    /// <param name="j">Row 0–64.</param>
    /// <returns>The direction.</returns>
    public static NumVector3 Direction(int face, int i, int j) => Direction(face, i, j, QuadsPerSide);

    /// <summary>The unit direction of a vertex of a face (cube-sphere) of a given resolution.</summary>
    /// <param name="face">Face 0–5 (+X, −X, +Y, −Y, +Z, −Z).</param>
    /// <param name="i">Column 0–<paramref name="quads"/>.</param>
    /// <param name="j">Row 0–<paramref name="quads"/>.</param>
    /// <param name="quads">Quads along a face side.</param>
    /// <returns>The direction.</returns>
    public static NumVector3 Direction(int face, int i, int j, int quads)
    {
        var u = (2f * i / quads) - 1f;
        var v = (2f * j / quads) - 1f;
        var p = face switch
        {
            0 => new NumVector3(1f, v, -u),
            1 => new NumVector3(-1f, v, u),
            2 => new NumVector3(u, 1f, -v),
            3 => new NumVector3(u, -1f, v),
            4 => new NumVector3(u, v, 1f),
            _ => new NumVector3(-u, v, -1f),
        };
        return NumVector3.Normalize(p);
    }

    /// <summary>Builds the mesh arrays of a body.</summary>
    /// <param name="sampler">The surface sampler of the body.</param>
    /// <param name="quadsPerSide">Quads along a face side (<see cref="QuadsPerSide"/> near, <see cref="FarQuadsPerSide"/> far).</param>
    /// <returns>The arrays.</returns>
    public static PlanetImpostorMeshData Build(PlanetSurfaceSampler sampler, int quadsPerSide = QuadsPerSide)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentOutOfRangeException.ThrowIfLessThan(quadsPerSide, 1);
        var start = Stopwatch.GetTimestamp();
        var side = quadsPerSide + 1;
        var data = new PlanetImpostorMeshData(quadsPerSide, Faces * side * side, Faces * quadsPerSide * quadsPerSide * 2 * 3);
        Parallel.For(0, Faces, face =>
        {
            using var probe = new GeneratorProbe(sampler.Graph);
            BuildFace(face, sampler, probe, data);
        });
        data.BuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return data;
    }

    /// <summary>Creates the Godot mesh from the arrays; main thread.</summary>
    /// <param name="data">The arrays.</param>
    /// <returns>The mesh.</returns>
    public static ArrayMesh ToMesh(PlanetImpostorMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static void BuildFace(int face, PlanetSurfaceSampler sampler, GeneratorProbe probe, PlanetImpostorMeshData data)
    {
        var quads = data.QuadsPerSide;
        var side = quads + 1;
        var first = face * side * side;
        for (var j = 0; j < side; j++)
        {
            for (var i = 0; i < side; i++)
            {
                var direction = Direction(face, i, j, quads);
                var radius = sampler.SurfaceRadius(direction, PlanetSurfaceSampler.ImpostorCoarseStrideMeters, PlanetSurfaceSampler.ImpostorFineStrideMeters);
                var point = direction * radius;
                var under = direction * (radius - 1f);
                var material = probe.Sample((int)MathF.Round(under.X), (int)MathF.Round(under.Y), (int)MathF.Round(under.Z)).Material;
                var index = first + (j * side) + i;
                data.Vertices[index] = new Vector3(point.X, point.Y, point.Z);
                data.Colors[index] = PlanetTerrainMaterial.BaseColor(material);
            }
        }

        var cursor = face * quads * quads * 6;
        for (var j = 0; j < quads; j++)
        {
            for (var i = 0; i < quads; i++)
            {
                var a = first + (j * side) + i;
                var b = a + 1;
                var c = a + side;
                var d = c + 1;
                cursor = Triangle(data, cursor, a, b, d);
                cursor = Triangle(data, cursor, a, d, c);
            }
        }

        var last = first + (side * side);
        for (var v = first; v < last; v++)
        {
            data.Normals[v] = data.Normals[v].LengthSquared() > 0 ? data.Normals[v].Normalized() : data.Vertices[v].Normalized();
        }
    }

    /// <summary>Writes one triangle clockwise seen from outside and adds its area-weighted outward normal to its vertices.</summary>
    private static int Triangle(PlanetImpostorMeshData data, int cursor, int a, int b, int c)
    {
        var pa = data.Vertices[a];
        var normal = (data.Vertices[b] - pa).Cross(data.Vertices[c] - pa);
        if (normal.Dot(pa) > 0)
        {
            // Counter-clockwise from outside: swap to make it a Godot front face; the outward normal stays.
            (b, c) = (c, b);
        }
        else
        {
            normal = -normal;
        }

        data.Indices[cursor] = a;
        data.Indices[cursor + 1] = b;
        data.Indices[cursor + 2] = c;
        data.Normals[a] += normal;
        data.Normals[b] += normal;
        data.Normals[c] += normal;
        return cursor + 3;
    }
}
