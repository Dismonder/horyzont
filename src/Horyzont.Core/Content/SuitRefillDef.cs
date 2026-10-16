using Horyzont.Core.Survival;

namespace Horyzont.Core.Content;

/// <summary>What an item does when used on the suit (K6-34, D8): it refills one resource.</summary>
/// <param name="Resource">The resource.</param>
/// <param name="Amount">Points added per item, 1 to 1000.</param>
public sealed record SuitRefillDef(SuitResource Resource, ushort Amount);
