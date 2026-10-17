using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// The frozen world of the hash pin of K6-35 (D22). It does not use <see cref="WorldFactory"/> or any other shared fixture, so a change of a neighbour's fixture cannot move the pin
/// (the lesson of K6-31). Every number below is part of the pin: change one and the pin has to be taken again, with a description in the pull request.
/// </summary>
internal static class DeathHashFixture
{
    public static readonly WorldId Id = new(new Guid("c0de0000-0000-4000-8000-000000000035"));

    /// <summary>The hash of this world with the state of K6-34, taken at the commit A0 of K6-35 before any change of Core.</summary>
    public const ulong Pin = 1459890191863420218UL;

    public static WorldState Create()
    {
        var state = WorldState.CreateNew(
            Id, 77, [new PlanetParams(new PlanetId(1), new SystemId(1), 11, "core:test/1")], ["hash:stone", "hash:bed"], ["hash_stone", "hash_dirt"],
            ["hash:a", "hash:b", "hash:c", "hash:d"]);
        var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        for (var n = 1; n <= 2; n++)
        {
            var join = sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, (ulong)(900 + n)), $"hash{n}");
            sim.ApplyServerOperation(join);
            var player = join.Player;
            sim.ApplyServerOperation(new CompoundOp(
            [
                new ContainerCreateOp(new ContainerId((ulong)((2 * n) - 1)), ContainerKind.PlayerBag, ContainerHost.Player(player), player, ContainerAccess.Private, 12),
                new ContainerCreateOp(new ContainerId((ulong)(2 * n)), ContainerKind.Hotbar, ContainerHost.Player(player), player, ContainerAccess.Private, 4),
            ]));
            sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(new ContainerId((ulong)((2 * n) - 1)), 2), new ItemTypeId(1), (ushort)(10 * n), 100, ItemSource.Admin));
            sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(new ContainerId((ulong)(2 * n)), 0), new ItemTypeId(2), 3, 100, ItemSource.Admin));
        }

        var construct = new EntityId(50);
        sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(new PlanetId(1)), new Vector3(0f, 4000f, 0f), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
        sim.ApplyServerOperation(new BlockSetOp(construct, new CellPos(1, 0, 0), BlockState.Create(2, 0)));
        sim.ApplyServerOperation(new BlockEntitySetOp(construct, new CellPos(1, 0, 0), 2, new BedBlockState(new PlayerId(1))));
        sim.ApplyServerOperation(new RespawnPointSetOp(new PlayerId(1), new BedRef(construct, new CellPos(1, 0, 0), 2)));
        sim.ApplyServerOperation(new FoodSetOp(new PlayerId(1), new FoodSlots(new FoodSlot(new ItemTypeId(3), new Tick(40)), default, default)));
        sim.ApplyServerOperation(new ItemDiscoverOp(new PlayerId(2), new ItemTypeId(4)));
        sim.ApplyServerOperation(new ProgressKeyGrantOp("hash:key"));
        return state;
    }
}
