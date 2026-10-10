using Horyzont.Core.Simulation;

namespace Horyzont.Client.Tools;

/// <summary>Current tool and its parameters.</summary>
/// <param name="Tool">The tool.</param>
/// <param name="Radius">Radius of the terrain tool in [rules.MinToolRadius, rules.MaxToolRadius], step <see cref="RadiusStep"/>.</param>
/// <param name="FillMaterial">Material of filling.</param>
/// <param name="BlockType">Block type to place.</param>
/// <param name="Orientation">Orientation of the block, 0..23.</param>
public readonly record struct ToolSettings(ToolKind Tool, float Radius, byte FillMaterial, ushort BlockType, byte Orientation)
{
    /// <summary>Radius change of one step in metres.</summary>
    public const float RadiusStep = 0.25f;

    /// <summary>The radius after a number of steps, clamped to the limits of the rules.</summary>
    /// <param name="steps">Signed number of steps.</param>
    /// <param name="rules">Game rules.</param>
    /// <returns>The changed settings.</returns>
    public ToolSettings WithRadiusSteps(int steps, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var radius = Math.Clamp(Radius + (steps * RadiusStep), rules.MinToolRadius, rules.MaxToolRadius);
        return this with { Radius = radius };
    }
}
