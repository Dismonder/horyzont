using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-35, D22: the new state (settings, persistent entities) enters the hash only when it exists, and every field of it counts.</summary>
public sealed class DeathHashStateTests
{
    [Fact]
    public void NewWorld_HasNoEntitiesAndDefaultSettings()
    {
        var state = DeathHashFixture.Create();

        Assert.Equal(0, state.PersistentEntities.Count);
        Assert.True(state.Settings.IsDefault);
        Assert.Equal(WorldSettings.Default, state.Settings);
    }

    [Fact]
    public void DefaultSettings_DoNotChangeTheHash()
    {
        var state = DeathHashFixture.Create();
        var sim = Simulation(state);
        var hash = StateHasher.Hash(state);

        sim.ApplyServerOperation(new WorldSettingsSetOp(WorldSettings.Default));

        Assert.Equal(hash, StateHasher.Hash(state));
    }

    [Fact]
    public void NonDefaultSettings_ChangeTheHash_EachField()
    {
        var baseHash = StateHasher.Hash(DeathHashFixture.Create());
        WorldSettings[] changes =
        [
            new() { DeathPenalty = DeathPenalty.Mild },
            new() { DeathPenalty = DeathPenalty.Hard },
            new() { TeleportAll = true },
            new() { FreeFuel = true },
            new() { FreeBuilding = true },
            new() { ResourceMultiplierPercent = 150 },
            new() { StackMultiplierPercent = 200 },
            new() { HazardIntensityPercent = 75 },
            new() { BuildingIntegrity = BuildingIntegrityMode.Relaxed },
        ];
        var seen = new HashSet<ulong> { baseHash };

        foreach (var settings in changes)
        {
            var state = DeathHashFixture.Create();
            Simulation(state).ApplyServerOperation(new WorldSettingsSetOp(settings));

            Assert.Equal(settings, state.Settings);
            Assert.True(seen.Add(StateHasher.Hash(state)), $"{settings} hashes like another world");
        }

        Assert.Equal(changes.Length + 1, seen.Count);
    }

    [Fact]
    public void SettingsOutOfRange_AreRefused()
    {
        var state = DeathHashFixture.Create();
        var sim = Simulation(state);
        var hash = StateHasher.Hash(state);
        WorldSettings[] bad =
        [
            new() { ResourceMultiplierPercent = 49 },
            new() { ResourceMultiplierPercent = 301 },
            new() { StackMultiplierPercent = 99 },
            new() { StackMultiplierPercent = 301 },
            new() { HazardIntensityPercent = 49 },
            new() { HazardIntensityPercent = 201 },
            new() { BuildingIntegrity = (BuildingIntegrityMode)0 },
            new() { DeathPenalty = (DeathPenalty)4 },
        ];

        foreach (var settings in bad)
        {
            Assert.Throws<InvalidOperationException>(() => sim.ApplyServerOperation(new WorldSettingsSetOp(settings)));
        }

        Assert.Equal(hash, StateHasher.Hash(state));
        Assert.True(state.Settings.IsDefault);
    }

    [Fact]
    public void Tombstone_ChangesTheHash_AndTheEntityIsPartOfIt()
    {
        var state = DeathHashFixture.Create();
        var hash = StateHasher.Hash(state);
        var entity = state.EntityIds.PeekNext();
        var container = state.ContainerIds.PeekNext();

        Simulation(state).ApplyServerOperation(Tombstone(entity, container, new Vector3(1f, 4000f, 0f)));
        var withTombstone = StateHasher.Hash(state);

        Assert.NotEqual(hash, withTombstone);

        // The pose of the entity is hashed: the same world with the tombstone one metre further has another hash.
        var other = DeathHashFixture.Create();
        Simulation(other).ApplyServerOperation(Tombstone(entity, container, new Vector3(2f, 4000f, 0f)));
        Assert.NotEqual(withTombstone, StateHasher.Hash(other));
    }

    private static CompoundOp Tombstone(EntityId entity, ContainerId container, Vector3 position) => new(
    [
        new PersistentEntityCreateOp(entity, PersistentEntityKind.Tombstone, FrameRef.Of(new PlanetId(1)), 0, position, Quaternion.Identity, new PlayerId(1)),
        new ContainerCreateOp(container, ContainerKind.Tombstone, ContainerHost.PersistentEntity(entity), new PlayerId(1), ContainerAccess.Private, 1),
        new ItemTransferOp(new SlotRef(new ContainerId(1), 2), new SlotRef(container, 0), new ItemTypeId(1), 10, 100),
    ]);

    private static ServerSimulation Simulation(WorldState state) =>
        new(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
}
