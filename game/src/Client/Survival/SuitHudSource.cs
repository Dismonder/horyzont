using Horyzont.Client.Survival;

namespace Horyzont.Game.Client.Survival;

/// <summary>Builds the data of the suit HUD from the vitals the server told about (K6-34); static, because it holds no state.</summary>
public static class SuitHudSource
{
    /// <summary>Builds the data of the current frame.</summary>
    /// <param name="vitals">The vitals of the local player.</param>
    /// <returns>The data; not visible until the server has sent the vitals.</returns>
    public static SuitHudData Build(ClientVitals vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        var env = vitals.Environment;
        return new SuitHudData(
            vitals.HasState, vitals.Suit, vitals.SuitCapacity, env.Present, env.Active, env.Exposed, env.Sheltered, env.ShelterHits, env.Generator, env.LifeSupport);
    }
}
