using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Replication;

/// <summary>
/// Tool view of a remote client: planets from the handshake, terrain delegated to the terrain replication (K3-16), constructs and other
/// players from the mirrors. Queries do not allocate. Without mirrors it stays the neutral view of the seam.
/// </summary>
public sealed class RemoteToolWorldView : IToolWorldView
{
    private readonly IReadOnlyList<PlanetParams> _planets;
    private readonly IRemoteTerrainRaycast _terrain;
    private readonly ConstructMirror? _constructs;
    private readonly EntityMirror? _entities;
    private readonly PlayerId _self;

    /// <summary>Initializes a remote view from the accepted handshake.</summary>
    /// <param name="hello">Accepted handshake.</param>
    /// <param name="terrain">Remote terrain raycaster.</param>
    public RemoteToolWorldView(ServerHello hello, IRemoteTerrainRaycast terrain)
        : this(hello, terrain, null, null)
    {
    }

    /// <summary>Initializes a remote view with the replicated constructs and players.</summary>
    /// <param name="hello">Accepted handshake.</param>
    /// <param name="terrain">Remote terrain raycaster.</param>
    /// <param name="constructs">Mirror of the constructs; none when null.</param>
    /// <param name="entities">Mirror of the other players; none when null.</param>
    public RemoteToolWorldView(ServerHello hello, IRemoteTerrainRaycast terrain, ConstructMirror? constructs, EntityMirror? entities)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(terrain);
        _planets = hello.Planets;
        _terrain = terrain;
        _constructs = constructs;
        _entities = entities;
        _self = hello.YourPlayer;
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
        if (_constructs is null)
        {
            hit = default;
            return false;
        }

        return ConstructRaycast.Raycast(_constructs.Store, FrameRef.Of(planet), origin, direction, maxDistance, out hit);
    }

    /// <inheritdoc />
    public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value)
    {
        if (_constructs is not null)
        {
            return _constructs.TryGetConstruct(construct, out value);
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = Quaternion.Identity;
        if (_constructs is null || !_constructs.TryGetConstruct(construct, out var value))
        {
            return false;
        }

        if (value.Parent.Kind == FrameKind.Planet)
        {
            return BlockOverlap.TryGetConstructPose(_constructs.Store, value, value.Parent, out origin, out rotation);
        }

        for (var i = 0; i < _planets.Count; i++)
        {
            if (BlockOverlap.TryGetConstructPose(_constructs.Store, value, FrameRef.Of(_planets[i].Id), out origin, out rotation))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) =>
        _constructs is not null && BlockOverlap.OverlapsConstruct(_constructs.Store, FrameRef.Of(planet), center, rotation, skip);

    /// <inheritdoc />
    public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination) =>
        _entities?.CopyOtherPlayerFeet(planet, _self, destination) ?? 0;
}
