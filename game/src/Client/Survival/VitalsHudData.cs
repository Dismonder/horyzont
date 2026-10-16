using Horyzont.Game.Client.Inventory;

namespace Horyzont.Game.Client.Survival;

/// <summary>One food slot of the vitals HUD (K6-33): what sits in it, how much of its time is left and the text of that time.</summary>
/// <param name="Filled">Whether the slot holds food that still lasts.</param>
/// <param name="Visual">The icon of the item.</param>
/// <param name="Name">The name of the item (the translated one).</param>
/// <param name="Fraction">The fraction of the time that remains, 0 to 1 (the curve of the server).</param>
/// <param name="Time">The time left as text, for example <c>6 min</c>.</param>
/// <param name="Blinking">Whether so little is left that the same food may be eaten again (20 % or less).</param>
public readonly record struct FoodSlotData(bool Filled, ItemVisual Visual, string Name, double Fraction, string Time, bool Blinking);

/// <summary>Everything the vitals HUD shows in one frame, already reduced to numbers and short texts (K6-33), so the drawing does not depend on the network.</summary>
/// <param name="Visible">Whether the HUD is shown at all (the server sent the vitals).</param>
/// <param name="Health">The health.</param>
/// <param name="HealthMax">The maximum health.</param>
/// <param name="Stamina">The stamina.</param>
/// <param name="StaminaMax">The maximum stamina.</param>
/// <param name="Exhausted">Whether the player is exhausted.</param>
/// <param name="Resting">Whether the conditions of rest hold and the warm-up runs.</param>
/// <param name="WarmupSeconds">Seconds of the warm-up so far.</param>
/// <param name="WarmupTotalSeconds">Seconds of the whole warm-up.</param>
/// <param name="RestedTime">The time of the rest left as text (empty when the player is not rested).</param>
/// <param name="Comfort">The comfort level, 0 to 10.</param>
/// <param name="Food">The three food slots.</param>
public readonly record struct VitalsHudData(
    bool Visible, float Health, float HealthMax, float Stamina, float StaminaMax, bool Exhausted, bool Resting, int WarmupSeconds, int WarmupTotalSeconds, string RestedTime,
    int Comfort, IReadOnlyList<FoodSlotData> Food);
