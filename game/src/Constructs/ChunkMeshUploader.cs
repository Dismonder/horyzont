using Godot;

using Horyzont.Constructs.Meshing;

namespace Horyzont.Game.Constructs;

/// <summary>
/// Copies mesher output into Godot resources. Godot needs managed arrays of exact length, so every upload allocates: that is a
/// cost per change, not per frame (ZASADY pkt 6 applies to the mesher, which is allocation-free). The array copies are made by
/// <see cref="Prepare"/> on a worker thread (plain managed memory and Godot value arrays, no server calls); everything that talks
/// to a Godot server (mesh surfaces, collision shapes) stays on the main thread.
/// </summary>
public static class ChunkMeshUploader
{
    /// <summary>Everything a worker thread can build from one mesher result without touching the scene.</summary>
    public sealed class PreparedChunk : IDisposable
    {
        /// <summary>Surface arrays of the cubes, or null when there are none (or no mesh was requested).</summary>
        public Godot.Collections.Array? Cubes { get; init; }

        /// <summary>Surface arrays of the prefab shapes, or null.</summary>
        public Godot.Collections.Array? Shapes { get; init; }

        /// <summary>The collision triangle points (three per triangle), or null when there are none (or none were requested).</summary>
        public Vector3[]? Points { get; set; }

        /// <summary>True when the mesher output had collision faces (whether or not a shape was requested).</summary>
        public bool HasCollisionFaces { get; init; }

        /// <summary>True when there is a render surface to upload.</summary>
        public bool HasMesh => Cubes is not null || Shapes is not null;

        /// <inheritdoc />
        public void Dispose()
        {
            Cubes?.Dispose();
            Shapes?.Dispose();
        }
    }

    /// <summary>Builds the surface arrays and the collision points from mesher output (safe on a worker thread).</summary>
    /// <param name="data">Mesher output.</param>
    /// <param name="withMesh">Build the surface arrays.</param>
    /// <param name="withCollision">Copy the collision points.</param>
    /// <returns>The prepared objects.</returns>
    public static PreparedChunk Prepare(ChunkMeshData data, bool withMesh, bool withCollision)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new PreparedChunk
        {
            Cubes = withMesh ? BuildArrays(data.Cubes) : null,
            Shapes = withMesh ? BuildArrays(data.Shapes) : null,
            Points = withCollision ? CopyCollisionPoints(data) : null,
            HasCollisionFaces = !data.CollisionFaces.IsEmpty,
        };
    }

    /// <summary>Creates a mesh with one surface per non-empty render surface (cubes, then prefab shapes), or null when none.</summary>
    /// <param name="data">Mesher output.</param>
    /// <returns>The mesh.</returns>
    public static ArrayMesh? CreateMesh(ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CreateMesh(BuildArrays(data.Cubes), BuildArrays(data.Shapes));
    }

    /// <summary>Creates a mesh from prepared surface arrays (main thread), or null when both are null.</summary>
    /// <param name="cubes">Cube surface arrays.</param>
    /// <param name="shapes">Prefab shape surface arrays.</param>
    /// <returns>The mesh.</returns>
    public static ArrayMesh? CreateMesh(Godot.Collections.Array? cubes, Godot.Collections.Array? shapes)
    {
        if (cubes is null && shapes is null)
        {
            return null;
        }

        var mesh = new ArrayMesh();
        if (cubes is not null)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, cubes);
        }

        if (shapes is not null)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, shapes);
        }

        return mesh;
    }

    /// <summary>Copies the collision triangles into the array Godot needs (three points per triangle), or null when there are none.</summary>
    /// <param name="data">Mesher output.</param>
    /// <returns>The points.</returns>
    public static Vector3[]? CopyCollisionPoints(ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var faces = data.CollisionFaces;
        if (faces.Length == 0)
        {
            return null;
        }

        var points = new Vector3[faces.Length];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new Vector3(faces[i].X, faces[i].Y, faces[i].Z);
        }

        return points;
    }

    /// <summary>Creates the one-sided concave collision shape (Godot builds its acceleration structure here).</summary>
    /// <param name="points">Triangle points from <see cref="CopyCollisionPoints"/>.</param>
    /// <returns>The shape.</returns>
    public static ConcavePolygonShape3D CreateCollisionShape(Vector3[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new ConcavePolygonShape3D { Data = points, BackfaceCollision = false };
    }

    private static Godot.Collections.Array? BuildArrays(MeshSurface surface)
    {
        if (surface.IsEmpty)
        {
            return null;
        }

        var count = surface.VertexCount;
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];
        var uv2 = new Vector2[count];
        var sourcePositions = surface.Positions;
        var sourceNormals = surface.Normals;
        var sourceUvs = surface.Uvs;
        var sourceUv2 = surface.Uv2;
        for (var i = 0; i < count; i++)
        {
            positions[i] = new Vector3(sourcePositions[i].X, sourcePositions[i].Y, sourcePositions[i].Z);
            normals[i] = new Vector3(sourceNormals[i].X, sourceNormals[i].Y, sourceNormals[i].Z);
            uvs[i] = new Vector2(sourceUvs[i].X, sourceUvs[i].Y);
            uv2[i] = new Vector2(sourceUv2[i].X, sourceUv2[i].Y);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = surface.Indices.ToArray();
        return arrays;
    }
}
