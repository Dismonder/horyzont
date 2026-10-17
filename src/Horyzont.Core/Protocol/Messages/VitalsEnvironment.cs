namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The surroundings of the player in the tail of message 44 (K6-34 D18): the three protections and what the hazards are. The masks have one bit per hazard (bit 0 cold, 1 heat, 2 toxins, 3 radiation, 4 vacuum;
/// bits 5 to 7 are an error). Invariants in both directions: <see cref="Active"/> is a subset of <see cref="Present"/> and <see cref="Exposed"/> a subset of <see cref="Active"/>.
/// </summary>
/// <param name="Sheltered">The player is in a shelter (bit 0 of the flag byte).</param>
/// <param name="Generator">An oxygen generator that runs is within range (bit 1).</param>
/// <param name="LifeSupport">The player sits in the seat of a ship (bit 2).</param>
/// <param name="Present">Hazards with a rate above 0 before the protection.</param>
/// <param name="Active">Hazards with a rate above 0 after the protection.</param>
/// <param name="Exposed">Hazards whose resource is at zero (the player takes damage).</param>
/// <param name="ShelterHits">Rays of the 26 that hit a block or the terrain, 0 to 26.</param>
public readonly record struct VitalsEnvironment(bool Sheltered, bool Generator, bool LifeSupport, byte Present, byte Active, byte Exposed, byte ShelterHits)
{
    /// <summary>Gets the surroundings of a server without hazard content: no protection, no hazard, no rays.</summary>
    public static VitalsEnvironment None => default;
}
