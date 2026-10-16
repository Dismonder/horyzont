using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>
/// Eat the item in a slot of the player's own bag or hotbar (intent 25, spec 03, K6-33 D10). The resolution is <c>FoodRules</c> (step B4).
/// </summary>
/// <param name="Container">The bag or hotbar of the player.</param>
/// <param name="Slot">Slot in the container.</param>
public sealed record EatIntent(ContainerId Container, ushort Slot) : Intent;
