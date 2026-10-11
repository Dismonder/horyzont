using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Bots.Client;

/// <summary>
/// What a bot without an engine knows of the world: the planets of the hello and the terrain as a sphere through the feet of the bot
/// (the server does the real hit test, so a bot at ground level aims at the ground it stands on). Constructs are known only when a
/// replica is attached (K3-15); without one the bot builds on terrain only.
/// </summary>
public sealed class BotToolWorldView : IToolWorldView, IRemoteTerrainRaycast
{
    private PlanetParams[] _planets = [];
    private float _surfaceRadius;
    private IToolWorldView? _constructs;

    /// <summary>Gets the radius of the ground sphere in metres; 0 before the first own state.</summary>
    public float SurfaceRadius => _surfaceRadius;

    /// <summary>Remembers the planets of the hello.</summary>
    /// <param name="planets">The planets.</param>
    public void SetPlanets(IReadOnlyList<PlanetParams> planets)
    {
        ArgumentNullException.ThrowIfNull(planets);
        _planets = [.. planets];
    }

    /// <summary>Updates the ground sphere from the latest feet of the bot.</summary>
    /// <param name="feet">Feet in the planet frame.</param>
    public void SetFeet(Vector3 feet) => _surfaceRadius = feet.Length();

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters)
    {
        foreach (var candidate in _planets)
        {
            if (candidate.Id == planet)
            {
                parameters = candidate;
                return true;
            }
        }

        parameters = null;
        return false;
    }

    /// <inheritdoc />
    public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        hit = default;
        var r = _surfaceRadius;
        if (!(r > 0f))
        {
            return false;
        }

        var c = origin.LengthSquared() - (r * r);
        if (c <= 0f)
        {
            hit = new TerrainHit(origin, TerrainMath.UpAt(origin), 0f);
            return true;
        }

        var b = Vector3.Dot(origin, direction);
        var discriminant = (b * b) - c;
        if (discriminant < 0f)
        {
            return false;
        }

        var t = -b - MathF.Sqrt(discriminant);
        if (t < 0f || t > maxDistance)
        {
            return false;
        }

        var position = origin + (direction * t);
        hit = new TerrainHit(position, TerrainMath.UpAt(position), t);
        return true;
    }

    /// <summary>
    /// Lets the construct queries go to a view over the mirrors of the bot (K3-15: <c>RemoteToolWorldView</c> with this view as its
    /// terrain); null keeps a world without constructs.
    /// </summary>
    /// <param name="constructs">The view that answers construct and player queries.</param>
    public void UseConstructs(IToolWorldView? constructs) => _constructs = constructs;

    /// <inheritdoc />
    public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit)
    {
        hit = default;
        return _constructs is not null && _constructs.RaycastConstructs(planet, origin, direction, maxDistance, out hit);
    }

    /// <inheritdoc />
    public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value)
    {
        value = null;
        return _constructs is not null && _constructs.TryGetConstruct(construct, out value);
    }

    /// <inheritdoc />
    public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = Quaternion.Identity;
        return _constructs is not null && _constructs.TryGetConstructPose(construct, out origin, out rotation);
    }

    /// <inheritdoc />
    public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) =>
        _constructs is not null && _constructs.OverlapsConstruct(planet, center, rotation, skip);

    /// <inheritdoc />
    public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination) =>
        _constructs?.CopyOtherPlayerFeet(planet, destination) ?? 0;
}
