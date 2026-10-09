using Godot;

using Horyzont.Game.Planet.Generators;

namespace Horyzont.Game.Tests.Character.Fixtures;

/// <summary>
/// A planet without relief for the character tests: SDF = |p| - radius, one material, no noise (gate decision D11: the walking
/// tests must not depend on the K2-08 profile). Not a game profile.
/// </summary>
public static class SphereGenerator
{
    /// <summary>Builds the generator of a smooth sphere around the origin.</summary>
    /// <param name="radius">Radius in metres.</param>
    /// <returns>The compiled generator.</returns>
    public static VoxelGeneratorGraph Create(float radius)
    {
        var generator = new VoxelGeneratorGraph();
        var g = new GraphBuilder(generator);
        g.OutputSdf(g.Sub(g.Length(g.X, g.Y, g.Z), g.Const(radius)));
        g.OutputTexture(g.Const(0f));
        g.Compile();
        generator.SetTextureMode(VoxelGeneratorGraph.TextureModeEnum.Single);
        return generator;
    }
}
