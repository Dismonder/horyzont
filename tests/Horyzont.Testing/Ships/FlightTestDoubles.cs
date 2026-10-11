using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;
using Horyzont.Server.Ships;
using Horyzont.Server.Space;

namespace Horyzont.Testing.Ships;

/// <summary><see cref="IShipCollider"/> over <see cref="SphereGroundCollider"/> with the stats of the ship (K5-24 tests).</summary>
/// <param name="ground">The sphere grounds in the parent frame of the ships.</param>
/// <param name="stats">Ship stats.</param>
public sealed class SphereShipCollider(SphereGroundCollider ground, ShipStatsCache stats) : IShipCollider
{
    /// <summary>Gets the number of sweeps.</summary>
    public int Sweeps { get; private set; }

    /// <summary>Gets the number of forgotten ships.</summary>
    public int Forgotten { get; private set; }

    /// <inheritdoc />
    public ShipSweepResult Sweep(EntityId ship, in PoseD startPose, in PoseD endPose)
    {
        Sweeps++;
        return stats.TryGet(ship, out var s) ? ground.Sweep(startPose, endPose, s) : ShipSweepResult.Free;
    }

    /// <inheritdoc />
    public void Forget(EntityId ship) => Forgotten++;
}

/// <summary>A control source driven by a function of the tick count of the source (null = no pilot).</summary>
/// <param name="script">Control for the n-th call of a ship; null = no pilot.</param>
public sealed class ScriptedControlSource(Func<EntityId, int, ShipControl?> script) : IShipControlSource
{
    private readonly Dictionary<EntityId, int> _calls = [];

    /// <inheritdoc />
    public bool TryGetControl(EntityId ship, Tick tick, out ShipControl control)
    {
        _calls.TryGetValue(ship, out var n);
        _calls[ship] = n + 1;
        var value = script(ship, n);
        control = value ?? default;
        return value.HasValue;
    }
}

/// <summary>Terrain readiness with a settable answer; records the spheres asked.</summary>
public sealed class FakeTerrainReadiness : IPlanetTerrainReadiness
{
    /// <summary>Gets or sets the answer.</summary>
    public PlanetTerrainReadiness Answer { get; set; } = PlanetTerrainReadiness.Ready;

    /// <summary>Gets or sets an answer by sphere (centre, radius); null = <see cref="Answer"/>.</summary>
    public Func<Vector3, float, PlanetTerrainReadiness>? By { get; set; }

    /// <summary>Gets the number of calls.</summary>
    public int Calls { get; private set; }

    /// <summary>Gets the centre of the first sphere of the last query (the column).</summary>
    public Vector3 LastCenter { get; private set; }

    /// <inheritdoc />
    public PlanetTerrainReadiness GetReadiness(PlanetId planet, Vector3 localCenter, float radius)
    {
        Calls++;
        LastCenter = localCenter;
        return By?.Invoke(localCenter, radius) ?? Answer;
    }
}
