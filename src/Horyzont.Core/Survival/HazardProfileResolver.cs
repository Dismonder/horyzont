using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Survival;

/// <summary>The hazard profile of a body (K6-34, D9): a pure function of the world and the content, shared by the server and the client.</summary>
public static class HazardProfileResolver
{
    /// <summary>
    /// The profile at a point: the profile of space when <paramref name="planet"/> is null (the frame of the star system) or <paramref name="heightAboveRadius"/> is above the atmosphere of the planet, otherwise the
    /// profile assigned to the body of the planet (its index in <c>WorldState.StarSystem</c>; a world without a star system has the home planet as body 0), else the default profile.
    /// </summary>
    /// <remarks>
    /// The atmosphere is inclusive: at its top the profile of the body applies. A height that is not a number is not above the atmosphere (the rates are zero for it anyway, <see cref="HazardField"/>).
    /// A planet the world does not know has no body and no atmosphere, so it gets the default profile.
    /// </remarks>
    /// <param name="state">The world (the star system and the planets).</param>
    /// <param name="hazards">The hazard content.</param>
    /// <param name="planet">The planet the point is in the frame of; null outside any planet.</param>
    /// <param name="heightAboveRadius">Height of the point above the radius of the planet in metres.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidOperationException">The content names a profile it does not have (the loader refuses such content).</exception>
    public static HazardProfileDef Resolve(WorldState state, HazardContent hazards, PlanetId? planet, float heightAboveRadius)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hazards);
        if (planet is not { } id)
        {
            return Profile(hazards, hazards.SpaceProfile);
        }

        if (state.Planets.TryGet(id, out var planetParams) && heightAboveRadius > planetParams.AtmosphereHeightMeters)
        {
            return Profile(hazards, hazards.SpaceProfile);
        }

        if (TryGetBody(state, id, out var body))
        {
            for (var i = 0; i < hazards.Bodies.Count; i++)
            {
                if (hazards.Bodies[i].Body == body)
                {
                    return Profile(hazards, hazards.Bodies[i].Profile);
                }
            }
        }

        return Profile(hazards, hazards.DefaultProfile);
    }

    private static bool TryGetBody(WorldState state, PlanetId planet, out byte body)
    {
        body = 0;
        if (state.StarSystem is { } system)
        {
            for (var i = 0; i < system.Bodies.Count; i++)
            {
                if (system.Bodies[i].Planet == planet)
                {
                    body = system.Bodies[i].BodyIndex;
                    return true;
                }
            }

            return false;
        }

        // A world without a star system: the home planet is body 0.
        return planet == state.HomePlanet;
    }

    private static HazardProfileDef Profile(HazardContent hazards, string key) =>
        hazards.TryGetProfile(key, out var profile) ? profile : throw new InvalidOperationException($"The hazard content has no profile '{key}'.");
}
