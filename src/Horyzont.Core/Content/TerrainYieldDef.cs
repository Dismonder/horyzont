namespace Horyzont.Core.Content;

/// <summary>What digging a terrain material yields (<c>content/terrain/yields.json</c>).</summary>
/// <param name="MaterialKey">Terrain material key of the world registry, e.g. <c>core:stone</c>.</param>
/// <param name="ItemKey">Item key.</param>
/// <param name="ItemsPerCubicMeter">Items per cubic metre of solid material, finite and positive.</param>
/// <param name="ToolTier">Tool tier needed (checked from K6-31).</param>
public sealed record TerrainYieldDef(string MaterialKey, string ItemKey, double ItemsPerCubicMeter, byte ToolTier);
