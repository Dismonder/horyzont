using System.Collections.Frozen;

using Godot;

namespace Horyzont.Game.Planet.Generators;

/// <summary>Output port of a graph node.</summary>
public readonly record struct GraphPort(uint Node, uint Index);

/// <summary>Parameters of a scalar FastNoiseLite node. Every other field is set explicitly so module defaults never matter.</summary>
public readonly record struct NoiseSpec(
    int Seed,
    float Frequency,
    Zn_FastNoiseLite.FractalTypeEnum Fractal,
    int Octaves);

/// <summary>
/// Thin wrapper over the <see cref="VoxelGraphFunction"/> API that creates only node types from <see cref="AllowedNodeTypes"/>:
/// arithmetic, comparisons and scalar noise, so the output does not depend on the C library or the instruction set
/// (see the determinism rules of K2-08). Main thread only; the graph is not changed after <see cref="Compile"/>.
/// </summary>
public sealed class GraphBuilder
{
    /// <summary>Node types a collision-relevant generator may use (no trigonometry, Pow, Exp, Log or FastNoise2).</summary>
    public static readonly FrozenSet<VoxelGraphFunction.NodeTypeId> AllowedNodeTypes = new[]
    {
        VoxelGraphFunction.NodeTypeId.Constant,
        VoxelGraphFunction.NodeTypeId.InputX,
        VoxelGraphFunction.NodeTypeId.InputY,
        VoxelGraphFunction.NodeTypeId.InputZ,
        VoxelGraphFunction.NodeTypeId.OutputSdf,
        VoxelGraphFunction.NodeTypeId.OutputSingleTexture,
        VoxelGraphFunction.NodeTypeId.Add,
        VoxelGraphFunction.NodeTypeId.Subtract,
        VoxelGraphFunction.NodeTypeId.Multiply,
        VoxelGraphFunction.NodeTypeId.Divide,
        VoxelGraphFunction.NodeTypeId.Min,
        VoxelGraphFunction.NodeTypeId.Max,
        VoxelGraphFunction.NodeTypeId.Clamp,
        VoxelGraphFunction.NodeTypeId.Abs,
        VoxelGraphFunction.NodeTypeId.Sqrt,
        VoxelGraphFunction.NodeTypeId.Distance3D,
        VoxelGraphFunction.NodeTypeId.Normalize3D,
        VoxelGraphFunction.NodeTypeId.Select,
        VoxelGraphFunction.NodeTypeId.Remap,
        VoxelGraphFunction.NodeTypeId.Smoothstep,
        VoxelGraphFunction.NodeTypeId.FastNoise3D,
    }.ToFrozenSet();

    private readonly VoxelGeneratorGraph _generator;
    private readonly VoxelGraphFunction _graph;
    private readonly Dictionary<float, GraphPort> _constants = [];
    private int _nodeCount;
    private GraphPort? _x;
    private GraphPort? _y;
    private GraphPort? _z;

    /// <summary>Starts an empty graph in <paramref name="generator"/>.</summary>
    public GraphBuilder(VoxelGeneratorGraph generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
        _graph = generator.GetMainFunction();
        _generator.Clear();
    }

    /// <summary>Voxel X position input.</summary>
    public GraphPort X => _x ??= Create(VoxelGraphFunction.NodeTypeId.InputX);

    /// <summary>Voxel Y position input.</summary>
    public GraphPort Y => _y ??= Create(VoxelGraphFunction.NodeTypeId.InputY);

    /// <summary>Voxel Z position input.</summary>
    public GraphPort Z => _z ??= Create(VoxelGraphFunction.NodeTypeId.InputZ);

    /// <summary>Checks the graph of a generator: only allowed node types, noise nodes only with scalar FastNoiseLite. Returns the problems found.</summary>
    public static IReadOnlyList<string> FindViolations(VoxelGraphFunction graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var problems = new List<string>();
        foreach (var id in graph.GetNodeIds().Order())
        {
            var type = graph.GetNodeTypeId((uint)id);
            if (!AllowedNodeTypes.Contains(type))
            {
                problems.Add($"node {id}: type {type} is not allowed");
            }
            else if (type == VoxelGraphFunction.NodeTypeId.FastNoise3D
                && graph.GetNodeParam((uint)id, 0).As<GodotObject>() is not Zn_FastNoiseLite)
            {
                problems.Add($"node {id}: noise resource is not ZN_FastNoiseLite");
            }
        }

        return problems;
    }

    /// <summary>Constant node (one per distinct value).</summary>
    public GraphPort Const(float value)
    {
        if (_constants.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var port = Create(VoxelGraphFunction.NodeTypeId.Constant);
        _graph.SetNodeParam(port.Node, 0, value);
        _constants[value] = port;
        return port;
    }

    /// <summary>a + b.</summary>
    public GraphPort Add(GraphPort a, GraphPort b) => Binary(VoxelGraphFunction.NodeTypeId.Add, a, b);

    /// <summary>a - b.</summary>
    public GraphPort Sub(GraphPort a, GraphPort b) => Binary(VoxelGraphFunction.NodeTypeId.Subtract, a, b);

    /// <summary>a * b.</summary>
    public GraphPort Mul(GraphPort a, GraphPort b) => Binary(VoxelGraphFunction.NodeTypeId.Multiply, a, b);

    /// <summary>a / b.</summary>
    public GraphPort Div(GraphPort a, GraphPort b) => Binary(VoxelGraphFunction.NodeTypeId.Divide, a, b);

    /// <summary>min(a, b).</summary>
    public GraphPort Min(GraphPort a, GraphPort b) => Binary(VoxelGraphFunction.NodeTypeId.Min, a, b);

    /// <summary>max(a, b).</summary>
    public GraphPort Max(GraphPort a, GraphPort b) => Binary(VoxelGraphFunction.NodeTypeId.Max, a, b);

    /// <summary>|x|.</summary>
    public GraphPort Abs(GraphPort x) => Unary(VoxelGraphFunction.NodeTypeId.Abs, x);

    /// <summary>sqrt(x), exactly rounded (IEEE).</summary>
    public GraphPort Sqrt(GraphPort x) => Unary(VoxelGraphFunction.NodeTypeId.Sqrt, x);

    /// <summary>clamp(x, min, max) with constant bounds.</summary>
    public GraphPort Clamp(GraphPort x, float min, float max)
    {
        var node = Create(VoxelGraphFunction.NodeTypeId.Clamp);
        Connect(x, node, 0);
        Connect(Const(min), node, 1);
        Connect(Const(max), node, 2);
        return node;
    }

    /// <summary>Distance from (x, y, z) to the origin.</summary>
    public GraphPort Length(GraphPort x, GraphPort y, GraphPort z)
    {
        var node = Create(VoxelGraphFunction.NodeTypeId.Distance3D);
        Connect(x, node, 0);
        Connect(y, node, 1);
        Connect(z, node, 2);
        Connect(Const(0f), node, 3);
        Connect(Const(0f), node, 4);
        Connect(Const(0f), node, 5);
        return node;
    }

    /// <summary>Smoothstep(edge0, edge1, x) with constant edges.</summary>
    public GraphPort Smoothstep(GraphPort x, float edge0, float edge1)
    {
        var node = Create(VoxelGraphFunction.NodeTypeId.Smoothstep);
        Connect(x, node, 0);
        _graph.SetNodeParam(node.Node, 0, edge0);
        _graph.SetNodeParam(node.Node, 1, edge1);
        return node;
    }

    /// <summary><paramref name="ifLess"/> when <paramref name="tested"/> is below <paramref name="threshold"/>, otherwise <paramref name="otherwise"/>.</summary>
    public GraphPort Select(GraphPort ifLess, GraphPort otherwise, GraphPort tested, float threshold)
    {
        var node = Create(VoxelGraphFunction.NodeTypeId.Select);
        Connect(ifLess, node, 0);
        Connect(otherwise, node, 1);
        Connect(tested, node, 2);
        _graph.SetNodeParam(node.Node, 0, threshold);
        return node;
    }

    /// <summary>Scalar FastNoiseLite OpenSimplex2 noise at (x, y, z); output in about [-1, 1].</summary>
    public GraphPort Noise3D(GraphPort x, GraphPort y, GraphPort z, NoiseSpec spec)
    {
        var noise = new Zn_FastNoiseLite();
        noise.SetNoiseType(Zn_FastNoiseLite.NoiseTypeEnum.OpenSimplex2);
        noise.SetSeed(spec.Seed);
        noise.SetPeriod(1f / spec.Frequency);
        noise.SetWarpNoise(null!);
        noise.SetFractalType(spec.Fractal);
        noise.SetFractalOctaves(spec.Octaves);
        noise.SetFractalLacunarity(2f);
        noise.SetFractalGain(0.5f);
        noise.SetFractalPingPongStrength(2f);
        noise.SetFractalWeightedStrength(0f);
        noise.SetRotationType3D(Zn_FastNoiseLite.RotationType3DEnum.ImproveXyPlanes);
        var node = Create(VoxelGraphFunction.NodeTypeId.FastNoise3D);
        _graph.SetNodeParam(node.Node, 0, noise);
        Connect(x, node, 0);
        Connect(y, node, 1);
        Connect(z, node, 2);
        return node;
    }

    /// <summary>Signed distance output (negative in matter).</summary>
    public void OutputSdf(GraphPort sdf) => Connect(sdf, Create(VoxelGraphFunction.NodeTypeId.OutputSdf), 0);

    /// <summary>Single texture index output (one index per voxel).</summary>
    public void OutputTexture(GraphPort index) => Connect(index, Create(VoxelGraphFunction.NodeTypeId.OutputSingleTexture), 0);

    /// <summary>Compiles the graph; afterwards it must not be changed.</summary>
    /// <exception cref="InvalidOperationException">The module reported compile errors.</exception>
    public void Compile()
    {
        var result = _generator.Compile();
        if (!result.TryGetValue("success", out var success) || !(bool)success)
        {
            var message = result.TryGetValue("message", out var m) ? (string)m : "no message";
            throw new InvalidOperationException($"Voxel graph did not compile: {message}");
        }
    }

    private GraphPort Create(VoxelGraphFunction.NodeTypeId type)
    {
        if (!AllowedNodeTypes.Contains(type))
        {
            throw new InvalidOperationException($"Node type {type} is not on the allowed list.");
        }

        var id = _graph.CreateNode(type, new Vector2(_nodeCount * 6f, 0f));
        _nodeCount++;
        return new GraphPort(id, 0);
    }

    private GraphPort Unary(VoxelGraphFunction.NodeTypeId type, GraphPort x)
    {
        var node = Create(type);
        Connect(x, node, 0);
        return node;
    }

    private GraphPort Binary(VoxelGraphFunction.NodeTypeId type, GraphPort a, GraphPort b)
    {
        var node = Create(type);
        Connect(a, node, 0);
        Connect(b, node, 1);
        return node;
    }

    private void Connect(GraphPort from, GraphPort to, uint inputIndex) =>
        _graph.AddConnection(from.Node, from.Index, to.Node, inputIndex);
}
