using Horyzont.Core.BlockEntities;

namespace Horyzont.Core.Intents;

/// <summary>
/// Craft a recipe, or choose the recipe of a station with time (intent 24, spec 03, K6-32 D12, D18). The codec arrives with step B2, the
/// resolution with step B4.
/// </summary>
/// <param name="Recipe">Key of the recipe, 1 to 64 bytes of UTF-8.</param>
/// <param name="Count">Number of pieces: 1 to <c>GameRules.MaxCraftCount</c> (the validator checks the range; the codec only requires at least 1).</param>
/// <param name="Station">The station block, null for the recipes of the hands.</param>
public sealed record CraftIntent(string Recipe, ushort Count, BlockRef? Station) : Intent;
