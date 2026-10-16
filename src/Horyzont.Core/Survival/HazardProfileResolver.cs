using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>The hazard profile of a body (K6-34, D9); skeleton of step B1, the logic comes in B4.</summary>
public static class HazardProfileResolver
{
    /// <summary>
    /// The profile at a point: the profile of space when <paramref name="planet"/> is null (the frame of the star system) or <paramref name="heightAboveRadius"/> is above the atmosphere of the planet, otherwise the
    /// profile assigned to the body of the planet (its index in <c>WorldState.StarSystem</c>; a world without a star system has the home planet as body 0), else the default profile.
    /// </summary>
    /// <param name="state">The world (the star system and the planets).</param>
    /// <param name="hazards">The hazard content.</param>
    /// <param name="planet">The planet the point is in the frame of; null outside any planet.</param>
    /// <param name="heightAboveRadius">Height of the point above the radius of the planet in metres.</param>
    /// <returns>The profile.</returns>
    public static HazardProfileDef Resolve(WorldState state, HazardContent hazards, PlanetId? planet, float heightAboveRadius) =>
        throw new NotImplementedException("K6-34 B4");
}
