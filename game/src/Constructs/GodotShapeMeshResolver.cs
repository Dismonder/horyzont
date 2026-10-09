using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Constructs.Content;

namespace Horyzont.Game.Constructs;

/// <summary>
/// Resolves <c>res://</c> mesh references of <c>blocks.json</c> (<c>.tres</c>, <c>.obj</c>, any <see cref="Mesh"/> resource) into
/// <see cref="ShapeMesh"/> on the main thread, at start. The built-in shapes are handled by the catalog itself.
/// </summary>
public sealed class GodotShapeMeshResolver : IShapeMeshResolver
{
    /// <inheritdoc />
    public bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh)
    {
        mesh = null;
        if (!meshRef.StartsWith("res://", StringComparison.Ordinal) || !ResourceLoader.Exists(meshRef))
        {
            return false;
        }

        if (ResourceLoader.Load(meshRef) is not Mesh resource || resource.GetSurfaceCount() == 0)
        {
            return false;
        }

        var arrays = resource.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normalSource = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvSource = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indexSource = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

        var positions = new System.Numerics.Vector3[vertices.Length];
        var normals = new System.Numerics.Vector3[vertices.Length];
        var uvs = new System.Numerics.Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            positions[i] = new System.Numerics.Vector3((float)vertices[i].X, (float)vertices[i].Y, (float)vertices[i].Z);
            if (i < normalSource.Length)
            {
                normals[i] = new System.Numerics.Vector3((float)normalSource[i].X, (float)normalSource[i].Y, (float)normalSource[i].Z);
            }

            if (i < uvSource.Length)
            {
                uvs[i] = new System.Numerics.Vector2((float)uvSource[i].X, (float)uvSource[i].Y);
            }
        }

        var indices = indexSource.Length > 0 ? (int[])indexSource.Clone() : [.. Enumerable.Range(0, vertices.Length)];
        mesh = new ShapeMesh(positions, normals, uvs, indices);
        return true;
    }
}
