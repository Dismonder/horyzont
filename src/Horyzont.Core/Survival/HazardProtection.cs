namespace Horyzont.Core.Survival;

/// <summary>Which hazards the surroundings of a player cancel (K6-34, D15); allocates nothing.</summary>
public static class HazardProtection
{
    /// <summary>A shelter zeroes cold, heat and toxins; a generator zeroes vacuum; life support zeroes everything. Radiation passes any shelter.</summary>
    /// <param name="present">The rates before protection.</param>
    /// <param name="sheltered">Whether the player is in a shelter.</param>
    /// <param name="generator">Whether a running oxygen generator covers the player.</param>
    /// <param name="lifeSupport">Whether the player sits in the seat of a ship.</param>
    /// <returns>The effective rates.</returns>
    public static HazardRates Apply(in HazardRates present, bool sheltered, bool generator, bool lifeSupport) => throw new NotImplementedException("K6-34 A6");
}
