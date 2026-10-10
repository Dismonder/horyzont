using System.Diagnostics.CodeAnalysis;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// The tool view of the local client of a solo or host process: it reads the authoritative terrain (the Voxel Tools service of the
/// server, whose <c>Raycast</c> does not allocate, D16) and the state of the server in the same process, with no second copy
/// (K2-13 decision 7). Everything runs on the main thread, where the simulation runs too.
/// </summary>
public sealed class GodotToolWorldView : IToolWorldView
{
    private readonly WorldState _state;
    private readonly ITerrainService _terrain;
    private readonly PlayerId _self;

    /// <summary>Initializes a new instance of the <see cref="GodotToolWorldView"/> class.</summary>
    /// <param name="state">The state of the server.</param>
    /// <param name="terrain">The terrain service of the server.</param>
    /// <param name="self">The local player, left out of the other players.</param>
    public GodotToolWorldView(WorldState state, ITerrainService terrain, PlayerId self)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
        _state = state;
        _terrain = terrain;
        _self = self;
    }

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters) => _state.Planets.TryGet(planet, out parameters);

    /// <inheritdoc />
    public bool RaycastTerrain(PlanetId planet, NumVector3 origin, NumVector3 direction, float maxDistance, out TerrainHit hit) =>
        _terrain.Raycast(planet, origin, direction, maxDistance, out hit);

    /// <inheritdoc />
    public bool RaycastConstructs(PlanetId planet, NumVector3 origin, NumVector3 direction, float maxDistance, out ConstructHit hit) =>
        ConstructRaycast.Raycast(_state.Constructs, FrameRef.Of(planet), origin, direction, maxDistance, out hit);

    /// <inheritdoc />
    public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value) => _state.Constructs.TryGet(construct, out value);

    /// <inheritdoc />
    public bool TryGetConstructPose(EntityId construct, out NumVector3 origin, out NumQuaternion rotation)
    {
        origin = default;
        rotation = NumQuaternion.Identity;
        return _state.Constructs.TryGet(construct, out var value)
            && _state.TryResolvePlanet(value.Parent, out var planet)
            && BlockOverlap.TryGetConstructPose(_state.Constructs, value, FrameRef.Of(planet), out origin, out rotation);
    }

    /// <inheritdoc />
    public bool OverlapsConstruct(PlanetId planet, NumVector3 center, NumQuaternion rotation, EntityId? skip) =>
        BlockOverlap.OverlapsConstruct(_state.Constructs, FrameRef.Of(planet), center, rotation, skip);

    /// <inheritdoc />
    public int CopyOtherPlayerFeet(PlanetId planet, Span<NumVector3> destination)
    {
        var count = 0;
        var players = _state.Players.All;
        for (var i = 0; i < players.Count && count < destination.Length; i++)
        {
            var other = players[i];
            if (other.Player != _self && other.IsOnline && _state.TryToPlanetFrame(other.Frame, other.Position, out var otherPlanet, out var feet)
                && otherPlanet == planet)
            {
                destination[count++] = feet;
            }
        }

        return count;
    }
}
