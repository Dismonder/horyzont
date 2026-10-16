namespace Horyzont.Core.Survival;

/// <summary>The last evaluation of the surroundings of a player, kept by the hazard tracker of the server between two evaluations (K6-34, D17).</summary>
/// <param name="Present">The rates before protection, per second (zero when the hazard is absent).</param>
/// <param name="Sheltered">Whether the player stands in a shelter (<see cref="ShelterResult.Sheltered"/>).</param>
/// <param name="ShelterHits">How many of the 26 rays hit (0 to 26).</param>
/// <param name="Generator">Whether a running oxygen generator covers the player (<see cref="OxygenCoverage"/>).</param>
/// <param name="LifeSupport">Whether the player sits in a seat of a ship, which supports life fully (D14).</param>
public readonly record struct HazardSample(HazardRates Present, bool Sheltered, byte ShelterHits, bool Generator, bool LifeSupport);
