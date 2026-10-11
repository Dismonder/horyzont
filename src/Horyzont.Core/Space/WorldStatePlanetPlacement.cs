using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Core.Space;

/// <summary>
/// Planet placement of the server (K5-21 decision 15): the body poses of <see cref="WorldState.StarSystem"/>; a world without a
/// star system places only its home planet, at the origin (like <see cref="HomeOnlyPlacement"/>). Allocation free.
/// </summary>
/// <param name="state">The world.</param>
public sealed class WorldStatePlanetPlacement(WorldState state) : IPlanetPlacement
{
    private readonly WorldState _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <inheritdoc />
    public bool TryGetPose(PlanetId planet, out PoseD pose)
    {
        if (_state.StarSystem is { } system)
        {
            var bodies = system.Bodies;
            for (var i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Planet == planet)
                {
                    pose = bodies[i].Pose;
                    return true;
                }
            }

            pose = default;
            return false;
        }

        pose = PoseD.Identity;
        return planet.Value != 0 && planet == _state.HomePlanet;
    }
}
