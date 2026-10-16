using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>
/// Use the suit refill (a canister, a cell) in a slot of the player's own bag or hotbar (intent 26, spec 03, K6-34 D7). The resolution is <c>SuitRules</c> (step B4); the codec comes in step B2.
/// </summary>
/// <param name="Container">The bag or hotbar of the player.</param>
/// <param name="Slot">Slot in the container.</param>
public sealed record SuitRefillIntent(ContainerId Container, ushort Slot) : Intent;
