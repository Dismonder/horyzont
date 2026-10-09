using System.Numerics;

using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Constructs.Meshing;

/// <summary>
/// Greedy mesher of one 16³ chunk: merged cube faces for rendering and for collision (two passes) and rotated prefab
/// shapes. One instance per worker thread; no allocation after construction (ZASADY pkt 6) once the output buffers are warm.
/// </summary>
public sealed class ChunkMesher
{
    private const int Size = ConstructChunk.Size;
    private const int Padded = ChunkMeshInput.Padded;
    private const byte KindEmpty = 0;
    private const byte KindCube = 1;
    private const byte KindShape = 2;

    // Faces in Face order (PosX, NegX, PosY, NegY, PosZ, NegZ). The in-plane axes (a, b) are chosen so that a x b is the
    // outward normal of every face, which makes (p0, p1, p2) counter-clockwise seen from outside.
    private static ReadOnlySpan<byte> LayerAxis => [0, 0, 1, 1, 2, 2];

    private static ReadOnlySpan<byte> AAxis => [1, 2, 2, 0, 0, 1];

    private static ReadOnlySpan<byte> BAxis => [2, 1, 0, 2, 1, 0];

    private static int AxisStride(int axis) => axis switch { 0 => Padded * Padded, 1 => Padded, _ => 1 };

    private readonly byte[] _kind = new byte[Padded * Padded * Padded];
    private readonly byte[] _material = new byte[Padded * Padded * Padded];
    private readonly byte[] _solid = new byte[Padded * Padded * Padded];
    private readonly byte[] _mask = new byte[Size * Size];

    /// <summary>Builds render surfaces and collision triangles of the chunk in <paramref name="input"/>.</summary>
    /// <param name="input">Chunk with border.</param>
    /// <param name="table">Render table of the world session.</param>
    /// <param name="output">Output buffers; cleared first.</param>
    public void Build(ChunkMeshInput input, BlockRenderTable table, ChunkMeshData output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        var cells = input.Cells;
        Classify(cells, table);
        for (var face = 0; face < 6; face++)
        {
            for (var layer = 0; layer < Size; layer++)
            {
                if (BuildMask(face, layer, collision: false))
                {
                    Merge(face, layer, collision: false, output);
                }

                if (BuildMask(face, layer, collision: true))
                {
                    Merge(face, layer, collision: true, output);
                }
            }
        }

        EmitShapes(cells, table, output);
    }

    private static Vector3 Normal(Face face) => face switch
    {
        Face.PosX => Vector3.UnitX,
        Face.NegX => -Vector3.UnitX,
        Face.PosY => Vector3.UnitY,
        Face.NegY => -Vector3.UnitY,
        Face.PosZ => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };

    private static Vector3 Corner(int face, float layerCoordinate, float a, float b)
    {
        Span<float> c = stackalloc float[3];
        c[LayerAxis[face]] = layerCoordinate;
        c[AAxis[face]] = a;
        c[BAxis[face]] = b;
        return new Vector3(c[0], c[1], c[2]);
    }

    private void Classify(ReadOnlySpan<uint> cells, BlockRenderTable table)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            var raw = cells[i];
            if (raw == 0)
            {
                _kind[i] = KindEmpty;
                _material[i] = 0;
                _solid[i] = 0;
                continue;
            }

            var state = new BlockState(raw);

            // A malformed value (orientation >= 24, unknown bits, type 0 with bits) is drawn as core:missing.
            ref readonly var info = ref (state.IsWellFormed ? ref table.Get(state.TypeId) : ref table.Missing);
            switch (info.Shape)
            {
                case BlockShape.Cube:
                    _kind[i] = KindCube;
                    _solid[i] = info.HasCollision ? (byte)1 : (byte)0;
                    break;
                case BlockShape.Mesh:
                    _kind[i] = KindShape;
                    _solid[i] = 0;
                    break;
                default:
                    _kind[i] = KindEmpty;
                    _solid[i] = 0;
                    break;
            }

            _material[i] = info.MaterialId;
        }
    }

    private bool BuildMask(int face, int layer, bool collision)
    {
        var layerAxis = LayerAxis[face];
        var strideLayer = AxisStride(layerAxis);
        var strideA = AxisStride(AAxis[face]);
        var strideB = AxisStride(BAxis[face]);
        var delta = (face & 1) == 0 ? strideLayer : -strideLayer;
        var origin = ((layer + 1) * strideLayer) + strideA + strideB;
        var any = false;
        for (var ib = 0; ib < Size; ib++)
        {
            for (var ia = 0; ia < Size; ia++)
            {
                var index = origin + (ia * strideA) + (ib * strideB);
                byte value;
                if (collision)
                {
                    value = _solid[index] != 0 && _solid[index + delta] == 0 ? (byte)1 : (byte)0;
                }
                else
                {
                    value = _kind[index] == KindCube && _kind[index + delta] != KindCube ? _material[index] : (byte)0;
                }

                _mask[(ib * Size) + ia] = value;
                any |= value != 0;
            }
        }

        return any;
    }

    private void Merge(int face, int layer, bool collision, ChunkMeshData output)
    {
        for (var ib = 0; ib < Size; ib++)
        {
            for (var ia = 0; ia < Size; ia++)
            {
                var value = _mask[(ib * Size) + ia];
                if (value == 0)
                {
                    continue;
                }

                var width = 1;
                while (ia + width < Size && _mask[(ib * Size) + ia + width] == value)
                {
                    width++;
                }

                var height = 1;
                var grow = true;
                while (grow && ib + height < Size)
                {
                    for (var k = 0; k < width; k++)
                    {
                        if (_mask[((ib + height) * Size) + ia + k] != value)
                        {
                            grow = false;
                            break;
                        }
                    }

                    if (grow)
                    {
                        height++;
                    }
                }

                for (var y = 0; y < height; y++)
                {
                    _mask.AsSpan(((ib + y) * Size) + ia, width).Clear();
                }

                EmitQuad(face, layer, ia, ib, width, height, value, collision, output);
            }
        }
    }

    private static void EmitQuad(int face, int layer, int ia, int ib, int width, int height, byte value, bool collision, ChunkMeshData output)
    {
        float layerCoordinate = (face & 1) == 0 ? layer + 1 : layer;
        var p0 = Corner(face, layerCoordinate, ia, ib);
        var p1 = Corner(face, layerCoordinate, ia + width, ib);
        var p2 = Corner(face, layerCoordinate, ia + width, ib + height);
        var p3 = Corner(face, layerCoordinate, ia, ib + height);
        if (collision)
        {
            // Clockwise seen from outside, like the render faces.
            output.AddCollisionVertex(p0);
            output.AddCollisionVertex(p2);
            output.AddCollisionVertex(p1);
            output.AddCollisionVertex(p0);
            output.AddCollisionVertex(p3);
            output.AddCollisionVertex(p2);
            return;
        }

        var surface = output.Cubes;
        var normal = Normal((Face)face);
        var start = surface.VertexCount;
        float materialLayer = value - 1;
        surface.AddVertex(p0, normal, new Vector2(ia, ib), materialLayer);
        surface.AddVertex(p1, normal, new Vector2(ia + width, ib), materialLayer);
        surface.AddVertex(p2, normal, new Vector2(ia + width, ib + height), materialLayer);
        surface.AddVertex(p3, normal, new Vector2(ia, ib + height), materialLayer);
        surface.AddIndex(start);
        surface.AddIndex(start + 2);
        surface.AddIndex(start + 1);
        surface.AddIndex(start);
        surface.AddIndex(start + 3);
        surface.AddIndex(start + 2);
    }

    private void EmitShapes(ReadOnlySpan<uint> cells, BlockRenderTable table, ChunkMeshData output)
    {
        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                for (var z = 0; z < Size; z++)
                {
                    var index = ChunkMeshInput.Index(x, y, z);
                    if (_kind[index] != KindShape)
                    {
                        continue;
                    }

                    var state = new BlockState(cells[index]);
                    ref readonly var info = ref table.Get(state.TypeId);
                    if (info.Mesh is { } mesh)
                    {
                        EmitShape(mesh, in info, state.Orientation, new Vector3(x, y, z), output);
                    }
                }
            }
        }
    }

    private static void EmitShape(Content.ShapeMesh mesh, in BlockRenderInfo info, byte orientation, Vector3 cell, ChunkMeshData output)
    {
        var right = Normal(BlockOrientation.Right(orientation));
        var up = Normal(BlockOrientation.Up(orientation));
        var back = -Normal(BlockOrientation.Forward(orientation));
        var surface = output.Shapes;
        var start = surface.VertexCount;
        float layer = info.MaterialId - 1;
        var positions = mesh.Positions;
        var normals = mesh.Normals;
        var uvs = mesh.Uvs;
        var center = cell + new Vector3(0.5f);
        for (var i = 0; i < positions.Length; i++)
        {
            var c = positions[i] - new Vector3(0.5f);
            var n = normals[i];
            surface.AddVertex(
                (right * c.X) + (up * c.Y) + (back * c.Z) + center,
                (right * n.X) + (up * n.Y) + (back * n.Z),
                uvs[i],
                layer);
        }

        var indices = mesh.Indices;
        var worldPositions = surface.Positions;
        for (var i = 0; i < indices.Length; i++)
        {
            surface.AddIndex(start + indices[i]);
        }

        if (info.HasCollision)
        {
            for (var i = 0; i < indices.Length; i++)
            {
                output.AddCollisionVertex(worldPositions[start + indices[i]]);
            }
        }
    }
}
