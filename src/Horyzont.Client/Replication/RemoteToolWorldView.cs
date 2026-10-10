using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Replication;

/// <summary>Neutral remote tool view: hello planets and delegated terrain; entity mirrors are supplied by K3-15.</summary>
public sealed class RemoteToolWorldView : IToolWorldView
{
    private readonly IReadOnlyList<PlanetParams> _planets;
    private readonly IRemoteTerrainRaycast _terrain;

    /// <summary>Initializes a remote view from the accepted handshake.</summary>
    /// <param name="hello">Accepted handshake.</param>
    /// <param name="terrain">Remote terrain raycaster.</param>
    public RemoteToolWorldView(ServerHello hello, IRemoteTerrainRaycast terrain)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(terrain);
        _planets = hello.Planets;
        _terrain = terrain;
    }

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters)
    {
        for (var i = 0; i < _planets.Count; i++)
        {
            if (_planets[i].Id == planet)
            {
                parameters = _planets[i];
                return true;
            }
        }

        parameters = null;
        return false;
    }

    /// <inheritdoc />
    public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit) =>
        _terrain.RaycastTerrain(planet, origin, direction, maxDistance, out hit);

    /// <inheritdoc />
    public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit)
    {
        hit = default;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value)
    {
        value = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = default;
        return false;
    }

    /// <inheritdoc />
    public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) => false;

    /// <inheritdoc />
    public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination) => 0;
}
