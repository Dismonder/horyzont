using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Integration.Tests.Tools;

/// <summary>
/// The tool view of the local client of a solo or host process: it reads the authoritative state and terrain of the server in the
/// same process (K2-13 decision 7: no second copy), except the player of the client itself.
/// </summary>
internal sealed class StateToolView(WorldState state, ITerrainService terrain, PlayerId self) : IToolWorldView
{
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters) => state.Planets.TryGet(planet, out parameters);

    public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit) =>
        terrain.Raycast(planet, origin, direction, maxDistance, out hit);

    public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit) =>
        ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(planet), origin, direction, maxDistance, out hit);

    public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value) => state.Constructs.TryGet(construct, out value);

    public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = Quaternion.Identity;
        return state.Constructs.TryGet(construct, out var value)
            && state.TryResolvePlanet(value.Parent, out var planet)
            && BlockOverlap.TryGetConstructPose(state.Constructs, value, FrameRef.Of(planet), out origin, out rotation);
    }

    public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) =>
        BlockOverlap.OverlapsConstruct(state.Constructs, FrameRef.Of(planet), center, rotation, skip);

    public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination)
    {
        var count = 0;
        var players = state.Players.All;
        for (var i = 0; i < players.Count && count < destination.Length; i++)
        {
            var other = players[i];
            if (other.Player != self && other.IsOnline && state.TryToPlanetFrame(other.Frame, other.Position, out var otherPlanet, out var feet) && otherPlanet == planet)
            {
                destination[count++] = feet;
            }
        }

        return count;
    }
}
