namespace Horyzont.Core.Intents;

/// <summary>
/// Move the matching items of the player's bag into the chests within <c>GameRules.QuickStackRangeMeters</c> (intent 22, spec 03,
/// K6-30 D16). The intent has no fields.
/// </summary>
public sealed record QuickStackIntent : Intent;
