using Godot;

using Horyzont.Constructs.Meshing;

namespace Horyzont.Game.Constructs;

/// <summary>
/// Copies mesher output into Godot resources. Godot needs managed arrays of exact length, so every upload allocates: that is a
/// cost per change, not per frame (ZASADY pkt 6 applies to the mesher, which is allocation-free).
/// </summary>
public static class ChunkMeshUploader
{
    /// <summary>Creates a mesh with one surface per non-empty render surface (cubes, then prefab shapes), or null when none.</summary>
    /// <param name="data">Mesher output.</param>
    /// <returns>The mesh.</returns>
    public static ArrayMesh? CreateMesh(ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Cubes.IsEmpty && data.Shapes.IsEmpty)
        {
            return null;
        }

        var mesh = new ArrayMesh();
        AddSurface(mesh, data.Cubes);
        AddSurface(mesh, data.Shapes);
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

    private static void AddSurface(ArrayMesh mesh, MeshSurface surface)
    {
        if (surface.IsEmpty)
        {
            return;
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
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
