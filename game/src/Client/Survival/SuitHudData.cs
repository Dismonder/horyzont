using Horyzont.Core.Survival;

namespace Horyzont.Game.Client.Survival;

/// <summary>
/// What the suit HUD shows in one frame (K6-34): the five resources with their capacity and the hazard masks of message 44 (bit 0 heating/cold to bit 4 oxygen/vacuum), so the drawing does not depend on the network.
/// </summary>
/// <param name="Visible">Whether the suit HUD is shown (the world has hazards and the server sent the vitals).</param>
/// <param name="Values">The value of each resource.</param>
/// <param name="Capacity">The capacity of each resource.</param>
/// <param name="Present">Hazards present around the player (grey when only present).</param>
/// <param name="Active">Hazards that act on the suit (colour).</param>
/// <param name="Exposed">Hazards that reach the player because the resource is empty (pulsing red).</param>
/// <param name="Sheltered">Whether the player stands in a shelter.</param>
/// <param name="ShelterHits">Rays of the shelter that hit, 0 to 26.</param>
/// <param name="Generator">Whether an oxygen generator covers the player.</param>
/// <param name="LifeSupport">Whether the player sits in a seat of a ship with life support.</param>
public readonly record struct SuitHudData(
    bool Visible, SuitResources Values, SuitResources Capacity, byte Present, byte Active, byte Exposed, bool Sheltered, int ShelterHits, bool Generator, bool LifeSupport);
