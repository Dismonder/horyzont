using GdUnit4;

using Godot;

using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

[TestSuite]
public class GraphBuilderTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void GraphBuilder_PlaneGraph_GeneratesExpectedSdfAndTexture()
    {
        var generator = new VoxelGeneratorGraph();
        var g = new GraphBuilder(generator);
        g.OutputSdf(g.Sub(g.X, g.Const(5f)));
        g.OutputTexture(g.Select(g.Const(2f), g.Const(1f), g.Y, 10f));
        g.Compile();
        generator.SetSdfClipThreshold(1000f);
        generator.SetTextureMode(VoxelGeneratorGraph.TextureModeEnum.Single);

        using var probe = new GeneratorProbe(generator);
        var (sdf, material) = probe.Sample(8, 3, 0);
        var (_, materialHigh) = probe.Sample(8, 12, 0);

        AssertThat(sdf).IsBetween(2.95f, 3.05f);  // 16-bit SDF quantization
        AssertThat((int)material).IsEqual(2);
        AssertThat((int)materialHigh).IsEqual(1);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GraphBuilder_SinNode_IsReportedAsViolation()
    {
        var generator = new VoxelGeneratorGraph();
        var function = generator.GetMainFunction();
        var sin = function.CreateNode(VoxelGraphFunction.NodeTypeId.Sin, new Vector2(0, 0));
        var pow = function.CreateNode(VoxelGraphFunction.NodeTypeId.Pow, new Vector2(10, 0));
        var fn2 = function.CreateNode(VoxelGraphFunction.NodeTypeId.FastNoise23D, new Vector2(20, 0));

        var violations = GraphBuilder.FindViolations(function);

        AssertThat(violations.Count).IsEqual(3);
        AssertThat(violations.Any(v => v.Contains($"node {sin}:"))).IsTrue();
        AssertThat(violations.Any(v => v.Contains($"node {pow}:"))).IsTrue();
        AssertThat(violations.Any(v => v.Contains($"node {fn2}:"))).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GraphBuilder_BuilderGraph_HasNoViolations()
    {
        var generator = new VoxelGeneratorGraph();
        var g = new GraphBuilder(generator);
        var noise = g.Noise3D(g.X, g.Y, g.Z, new NoiseSpec(7, 1f / 32f, Zn_FastNoiseLite.FractalTypeEnum.Fbm, 3));
        g.OutputSdf(g.Min(g.Abs(noise), g.Sqrt(g.Const(4f))));

        AssertThat(GraphBuilder.FindViolations(generator.GetMainFunction()).Count).IsEqual(0);
        g.Compile();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GraphBuilder_UnconnectedOutput_CompileThrows()
    {
        var generator = new VoxelGeneratorGraph();
        var function = generator.GetMainFunction();
        function.CreateNode(VoxelGraphFunction.NodeTypeId.Noise3D, new Vector2(0, 0));
        var g = new GraphBuilder(generator);
        g.OutputSdf(g.Noise3D(g.X, g.Y, g.Z, new NoiseSpec(1, 0.1f, Zn_FastNoiseLite.FractalTypeEnum.None, 1)));

        // GraphBuilder clears the generator in its constructor, so this graph is valid; an empty one is not.
        g.Compile();
        var empty = new VoxelGeneratorGraph();

        AssertThrown(() => new GraphBuilder(empty).Compile()).IsInstanceOf<InvalidOperationException>();
    }

    [TestCase]
    public void NoiseSeeds_SameInputs_SameSeedsAndDifferentSlots()
    {
        AssertThat(NoiseSeeds.For(123UL, NoiseSeeds.ContinentSlot)).IsEqual(NoiseSeeds.For(123UL, NoiseSeeds.ContinentSlot));
        AssertThat(NoiseSeeds.For(123UL, NoiseSeeds.ContinentSlot)).IsNotEqual(NoiseSeeds.For(123UL, NoiseSeeds.DetailSlot));
        AssertThat(NoiseSeeds.For(123UL, NoiseSeeds.ContinentSlot)).IsNotEqual(NoiseSeeds.For(124UL, NoiseSeeds.ContinentSlot));
    }
}
