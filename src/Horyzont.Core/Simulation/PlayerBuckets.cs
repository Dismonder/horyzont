namespace Horyzont.Core.Simulation;

/// <summary>Rate limits of one player.</summary>
internal sealed class PlayerBuckets(GameRules rules)
{
    internal TokenBucket Terrain { get; } = new(rules.TerrainEditsPerSecond, rules.TerrainEditBurst, rules.TickRate);

    internal TokenBucket Blocks { get; } = new(rules.BlockEditsPerSecond, rules.BlockEditBurst, rules.TickRate);
}
