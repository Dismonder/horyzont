namespace Horyzont.Core.Content;

/// <summary>The food statistics of an item type (K6-33, D8): the bonuses at full strength and how long the food lasts.</summary>
/// <param name="Health">Extra maximum health, 0 to 500.</param>
/// <param name="Stamina">Extra maximum stamina, 0 to 500.</param>
/// <param name="HealthRegenPerSecond">Extra health regeneration per second, 0 to 50.</param>
/// <param name="DurationSeconds">Seconds the food lasts, 30 to 7200.</param>
public sealed record FoodDef(ushort Health, ushort Stamina, float HealthRegenPerSecond, uint DurationSeconds);
