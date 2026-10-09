using Godot;

using Horyzont.Game.Constructs;
using Horyzont.Game.Testing;

namespace Horyzont.Game.Tests.Constructs;

public sealed class GodotShapeMeshResolverTests : EngineTestBase
{
    [EngineTest]
    public async Task TryResolve_TwoSurfaces_RejectsWithClearError()
    {
        await NextFrame();
        const string path = "res://tests/Constructs/review-two-surfaces.tres";
        using var resource = new ArrayMesh();
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { Vector3.Zero, Vector3.Right, Vector3.Up };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { Vector2.Zero, Vector2.Right, Vector2.Up };
        resource.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        resource.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        EngineAssert.Equal(Error.Ok, ResourceSaver.Save(resource, path), "save the two-surface mesh fixture");
        try
        {
            var rejected = false;
            try
            {
                new GodotShapeMeshResolver().TryResolve(path, out _);
            }
            catch (InvalidDataException e)
            {
                rejected = e.Message.Contains(path, StringComparison.Ordinal) && e.Message.Contains("surface", StringComparison.OrdinalIgnoreCase);
            }

            EngineAssert.True(rejected, "unsupported multi-surface meshes must fail with the resource path and surface error");
        }
        finally
        {
            DirAccess.RemoveAbsolute(path);
        }
    }
}
