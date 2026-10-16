using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.World;

/// <summary>
/// Health and stamina of a player (K6-33, D5): changed every tick outside the journal like movement, written only by
/// <c>ServerSimulation.UpdatePlayerVitals</c>, saved by the checkpoint (blob 2 version 2) and not part of the state hash.
/// </summary>
/// <param name="Health">Current health, 0 to <see cref="ProtocolConstants.MaxVitalValue"/>.</param>
/// <param name="Stamina">Current stamina, 0 to <see cref="ProtocolConstants.MaxVitalValue"/>.</param>
/// <param name="RestedUntil">The tick the rested bonus ends at; <c>Tick(0)</c> for none.</param>
/// <param name="Exhausted">True from stamina 0 until it recovers to 20% of the maximum.</param>
public readonly record struct PlayerVitals(float Health, float Stamina, Tick RestedUntil, bool Exhausted)
{
    /// <summary>Gets a value indicating whether both values are finite and within 0 to <see cref="ProtocolConstants.MaxVitalValue"/>.</summary>
    public bool IsValid =>
        float.IsFinite(Health) && float.IsFinite(Stamina)
        && Health >= 0f && Health <= ProtocolConstants.MaxVitalValue
        && Stamina >= 0f && Stamina <= ProtocolConstants.MaxVitalValue;
}
