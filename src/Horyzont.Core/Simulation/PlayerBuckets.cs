namespace Horyzont.Core.Simulation;

/// <summary>Rate limits of one player: a token bucket per intent class plus a sliding window for terrain edits (K2-13 W3).</summary>
internal sealed class PlayerBuckets(GameRules rules)
{
    internal TokenBucket Terrain { get; } = new(rules.TerrainEditsPerSecond, rules.TerrainEditBurst, rules.TickRate);

    /// <summary>At most <see cref="GameRules.TerrainEditsPerSecond"/> accepted terrain edits in every window of one second.</summary>
    internal SlidingWindowLimiter TerrainWindow { get; } = new(Math.Max(1, rules.TerrainEditsPerSecond), rules.TickRate);

    internal TokenBucket Blocks { get; } = new(rules.BlockEditsPerSecond, rules.BlockEditBurst, rules.TickRate);

    /// <summary>Marker intents (K5-27).</summary>
    internal TokenBucket Markers { get; } = new(rules.MarkerIntentsPerSecond, rules.MarkerIntentBurst, rules.TickRate);
    /// <summary>Item intents (move, drop), K6-29W D19.</summary>
    internal TokenBucket Items { get; } = new(rules.ItemIntentsPerSecond, rules.ItemIntentBurst, rules.TickRate);

    /// <summary>Prop hits, the wearing ones as well as the last one (K6-31 D22, D24).</summary>
    internal TokenBucket Props { get; } = new(rules.PropHitsPerSecond, rules.PropHitBurst, rules.TickRate);
}
