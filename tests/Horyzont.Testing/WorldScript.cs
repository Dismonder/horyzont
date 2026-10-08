using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Testing;

/// <summary>
/// Deterministic player activity from a seed on a world from <see cref="WorldFactory.CreateDefault"/> with a default
/// <see cref="FakeTerrainService"/>: joins, leaves, terrain edits, construct placement and block removal. Every <see cref="Step"/>
/// produces at most one journal operation, so an oracle can stop exactly at any <see cref="OpId"/>. The choices depend only on the
/// seed and the world state, so the same seed on equal states gives equal operations.
/// </summary>
public sealed class WorldScript(ulong seed)
{
    private const float Radius = 4000f;

    private static readonly PlatformIdentity[] _identities =
    [
        new(IdentityPlatform.Dev, 11),
        new(IdentityPlatform.Dev, 22),
    ];

    private ulong _state = seed;

    /// <summary>Runs steps on a fresh simulation until its <see cref="WorldState.LastOpId"/> reaches <paramref name="target"/>.</summary>
    /// <param name="sim">Simulation of a world from <see cref="WorldFactory.CreateDefault"/>.</param>
    /// <param name="target">Operation to stop at.</param>
    /// <param name="maxSteps">Safety limit.</param>
    public void RunUntil(ServerSimulation sim, OpId target, int maxSteps = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(sim);
        for (var i = 0; i < maxSteps && sim.State.LastOpId < target; i++)
        {
            Step(sim);
        }

        if (sim.State.LastOpId != target)
        {
            throw new InvalidOperationException($"The script reached operation {sim.State.LastOpId.Value}, not {target.Value}.");
        }
    }

    /// <summary>One action (at most one operation), then 1–8 ticks.</summary>
    /// <param name="sim">The simulation.</param>
    public void Step(ServerSimulation sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var state = sim.State;
        var roll = Next(100);
        var a = Next(9) - 4;
        var b = Next(9) - 4;
        var online = new List<PlayerState>(state.Players.Online);
        var offline = Offline(state);
        if (online.Count == 0 || (roll < 6 && offline is not null))
        {
            var identity = offline ?? _identities[0];
            var join = sim.PreparePlayerJoin(identity, $"p{identity.Value}");
            sim.ApplyServerOperation(join);
        }
        else
        {
            var player = online[Next(online.Count)];
            Act(sim, player, roll, a, b);
        }

        var ticks = 1 + Next(8);
        for (var i = 0; i < ticks; i++)
        {
            sim.AdvanceTick();
        }
    }

    private static PlatformIdentity? Offline(WorldState state)
    {
        foreach (var identity in _identities)
        {
            if (!state.Players.TryGetByIdentity(identity, out var id) || !state.Players.TryGet(id, out var player) || !player.IsOnline)
            {
                return identity;
            }
        }

        return null;
    }

    private static Vector3 Ground(float x, float z) => new(x, MathF.Sqrt((Radius * Radius) - (x * x) - (z * z)), z);

    private void Act(ServerSimulation sim, PlayerState player, int roll, int a, int b)
    {
        var state = sim.State;
        var eye = player.EyePosition(sim.Rules);
        var planet = WorldFactory.HomePlanet;
        state.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        Intent intent;
        if (roll < 9)
        {
            sim.ApplyServerOperation(new PlayerLeaveOp(player.Player, player.Frame, player.ParentSeq, player.Position, player.BodyRotation, player.Pitch));
            return;
        }

        if (roll < 35)
        {
            var target = Ground(a + 0.5f, b + 0.5f);
            intent = new TerrainToolIntent(planet, eye, Vector3.Normalize(target - eye), 0.5f + (Next(4) * 0.5f), TerrainEditMode.Remove, 0);
        }
        else if (roll < 50)
        {
            var target = Ground(a - 0.5f, b - 0.5f);
            intent = new TerrainToolIntent(planet, eye, Vector3.Normalize(target - eye), 0.5f + (Next(4) * 0.5f), TerrainEditMode.Add, (byte)(1 + Next(2)));
        }
        else if (roll < 62 || state.Constructs.Count == 0)
        {
            var target = Ground(a, b);
            intent = new PlaceBlockIntent(new TerrainTarget(planet, eye, Vector3.Normalize(target - eye), -Vector3.UnitZ), stone, 0);
        }
        else if (roll < 85)
        {
            var construct = state.Constructs.All[Next(state.Constructs.Count)];
            var cell = Next(2) == 0 ? construct.BoundsMax : construct.BoundsMin;
            intent = new PlaceBlockIntent(new ConstructFaceTarget(construct.Id, cell, (Face)Next(6)), stone, (byte)Next(24));
        }
        else
        {
            var construct = state.Constructs.All[Next(state.Constructs.Count)];
            intent = new RemoveBlockIntent(construct.Id, Next(2) == 0 ? construct.BoundsMax : construct.BoundsMin);
        }

        sim.Submit(player.Player, intent);
    }

    /// <summary>SplitMix64; stable across runtimes, unlike <see cref="Random"/>.</summary>
    private int Next(int bound)
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)bound);
    }
}
