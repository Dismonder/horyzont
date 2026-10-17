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
        // Every operation raises LastOpId, which is hashed, so compare two worlds with the same number of operations: one that never left the defaults and one that
        // went to other settings and back.
        var control = DeathHashFixture.Create();
        Simulation(control).ApplyServerOperation(new WorldSettingsSetOp(WorldSettings.Default));
        Simulation(control).ApplyServerOperation(new WorldSettingsSetOp(WorldSettings.Default));

        var back = DeathHashFixture.Create();
        Simulation(back).ApplyServerOperation(new WorldSettingsSetOp(new WorldSettings { DeathPenalty = DeathPenalty.Hard, FreeFuel = true }));
        Simulation(back).ApplyServerOperation(new WorldSettingsSetOp(WorldSettings.Default));

        Assert.True(back.Settings.IsDefault);
        Assert.Equal(StateHasher.Hash(control), StateHasher.Hash(back));
    }

    [Fact]
    public void NonDefaultSettings_ChangeTheHash_EachField()
    {
        // The control has the same number of operations as the others, so only the settings can make a difference.
        var control = DeathHashFixture.Create();
        Simulation(control).ApplyServerOperation(new WorldSettingsSetOp(WorldSettings.Default));
        var baseHash = StateHasher.Hash(control);
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

    [Fact]
    public void Tombstone_ChangesTheHash_EveryFieldOfTheEntity()
    {
        // K6-35 review A, P3-5: each field of the record is hashed, not only the position.
        var reference = DeathHashFixture.Create();
        var entity = reference.EntityIds.PeekNext();
        var container = reference.ContainerIds.PeekNext();
        var baseCreate = new PersistentEntityCreateOp(
            entity, PersistentEntityKind.Tombstone, FrameRef.Of(new PlanetId(1)), 0, new Vector3(1f, 4000f, 0f), Quaternion.Identity, new PlayerId(1));
        PersistentEntityCreateOp[] variants =
        [
            baseCreate,
            baseCreate with { Owner = new PlayerId(2) },
            baseCreate with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f) },
            baseCreate with { ParentSeq = 1 },
            baseCreate with { Position = new Vector3(1f, 4000f, 0.5f) },
            baseCreate with { EntityKind = PersistentEntityKind.DroppedItem },
        ];

        var seen = new HashSet<ulong>();
        foreach (var create in variants)
        {
            var state = DeathHashFixture.Create();
            var kind = create.EntityKind == PersistentEntityKind.Tombstone ? ContainerKind.Tombstone : ContainerKind.DroppedItem;
            Simulation(state).ApplyServerOperation(new CompoundOp(
            [
                create,
                new ContainerCreateOp(container, kind, ContainerHost.PersistentEntity(entity), create.Owner, ContainerAccess.Private, 1),
            ]));
            Assert.True(seen.Add(StateHasher.Hash(state)), $"{create} hashes like another variant");
        }

        Assert.Equal(variants.Length, seen.Count);
    }

    [Fact]
    public void Tombstone_ChangesTheHash_TheFrameOfTheEntity()
    {
        // The frozen fixture has one planet; the frame needs two.
        var seen = new HashSet<ulong>();
        foreach (var planet in new[] { 1u, 2u })
        {
            var state = WorldState.CreateNew(
                DeathHashFixture.Id, 77,
                [new PlanetParams(new PlanetId(1), new SystemId(1), 11, "core:test/1"), new PlanetParams(new PlanetId(2), new SystemId(1), 12, "core:test/1")],
                ["hash:stone"], ["hash_stone"], ["hash:a"]);
            var sim = Simulation(state);
            var join = sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 901), "hash1");
            sim.ApplyServerOperation(join);
            var entity = state.EntityIds.PeekNext();
            sim.ApplyServerOperation(new CompoundOp(
            [
                new PersistentEntityCreateOp(entity, PersistentEntityKind.Tombstone, FrameRef.Of(new PlanetId(planet)), 0, new Vector3(1f, 4000f, 0f), Quaternion.Identity, join.Player),
                new ContainerCreateOp(state.ContainerIds.PeekNext(), ContainerKind.Tombstone, ContainerHost.PersistentEntity(entity), join.Player, ContainerAccess.Private, 1),
            ]));
            seen.Add(StateHasher.Hash(state));
        }

        Assert.Equal(2, seen.Count);
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
