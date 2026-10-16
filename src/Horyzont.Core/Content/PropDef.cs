namespace Horyzont.Core.Content;

/// <summary>A harvestable prop kind: hit points, required tool tier, yield and regrow time (K6-31 D8, <c>props/props.json</c>).</summary>
/// <param name="Key">Prop key, for example <c>core:pine</c>.</param>
/// <param name="HitPoints">Hit points (1-1000).</param>
/// <param name="ToolTier">Tool tier needed to hit it (0 is the bare hand).</param>
/// <param name="Yield">Items it yields, 1 to 8 entries.</param>
/// <param name="RegrowSeconds">Seconds after the harvest at which it regrows; 0 means it never does.</param>
public sealed record PropDef(string Key, ushort HitPoints, byte ToolTier, IReadOnlyList<PropYield> Yield, uint RegrowSeconds);
