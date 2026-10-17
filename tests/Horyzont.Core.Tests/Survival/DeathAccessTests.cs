using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35 B4: who may touch a tombstone, and what a body at zero health may do. The subscription side is in <c>DeathAccessSubscriptionTests</c> of the server tests; here the
/// intents: nothing of another player's tombstone opens (no content, no change of the world) and a player waiting for the respawn is refused with <c>InTransition</c> before
/// anything else is looked at, at no cost in tokens.
/// </summary>
public sealed class DeathAccessTests
{
    private static readonly PlayerId _p1 = DeathWorld.Player1;
    private static readonly PlayerId _p2 = DeathWorld.Player2;

    private static (DeathWorld World, Container Tombstone) Dead()
    {
        var w = new DeathWorld(2, survival: true);
        w.GiveKit(_p1);
        w.Die(_p1, DeathPenalty.Normal);
        return (w, Assert.Single(w.Tombstones()));
    }

    [Fact]
    public void OtherPlayer_CannotSubscribeOrTakeItems()
    {
        var (w, tombstone) = Dead();
        var blocks = w.Hazard.Blocks;
        var before = tombstone.Slots.ToArray();
        Assert.Contains(before, s => !s.IsEmpty);

        // Next to the tombstone, so reach is no excuse: every way to take refuses with the lock, and the tombstone is as it was.
        blocks.AssertRejected(_p2, new TakeAllIntent(tombstone.Id), RejectReason.ContainerLocked);
        blocks.AssertRejected(
            _p2, new ItemMoveIntent(new SlotRef(tombstone.Id, 0), ItemMoveTarget.AnyIn(DeathWorld.BagOf(_p2)), 1, DeathWorld.Stone, 30), RejectReason.ContainerLocked);
        blocks.AssertRejected(
            _p2, new ItemMoveIntent(new SlotRef(DeathWorld.BagOf(_p2), 0), ItemMoveTarget.AnyIn(tombstone.Id), 1, DeathWorld.Stone, 1), RejectReason.ContainerLocked);
        Assert.Equal(before, tombstone.Slots.ToArray());
        Assert.Empty(w.Held(_p2));
    }

    [Fact]
    public void PendingRespawn_RejectsMutationsWithInTransition()
    {
        var w = new DeathWorld(1, survival: true);
        w.GiveKit(_p1);
        var blocks = w.Hazard.Blocks;
        var bag = DeathWorld.BagOf(_p1);
        Intent move = new ItemMoveIntent(new SlotRef(bag, 0), ItemMoveTarget.ToSlot(new SlotRef(bag, 9)), 30, DeathWorld.Stone, 30);
        Intent drop = new ItemDropIntent(new SlotRef(bag, 5), 3, DeathWorld.Gem, 3);

        // Alive: the move is accepted (the control for everything below).
        blocks.Accept(_p1, move);
        w.Apply(new ItemCreateOp(new SlotRef(bag, 0), DeathWorld.Stone, 30, 500, ItemSource.Admin));

        // Zero health: the body waits for the operation of the death. Every intent is refused with InTransition and changes nothing...
        Assert.True(w.Simulation.UpdatePlayerVitals(_p1, new PlayerVitals(0f, 50f, default, false)));
        Assert.True(DeathRules.IsDead(w.Hazard.VitalsOf(_p1)));
        blocks.AssertRejected(_p1, move, RejectReason.InTransition);
        blocks.AssertRejected(_p1, drop, RejectReason.InTransition);
        blocks.AssertRejected(_p1, new TakeAllIntent(bag), RejectReason.InTransition);
        blocks.AssertRejected(_p1, new QuickStackIntent(), RejectReason.InTransition);

        // ...and costs no token: far more refusals than a bucket holds still end in InTransition, never in RateLimited.
        for (var i = 0; i < 400; i++)
        {
            Assert.Equal(RejectReason.InTransition, blocks.SubmitAs(_p1, move).Reason);
        }

        // The fresh body (no vitals: the state the reset of a death leaves) acts again, with the tokens it had.
        var body = blocks.Sim.PlayerOf(_p1);
        w.Apply(new EntityReparentOp(body.Entity, FrameRef.Of(WorldFactory.HomePlanet), body.ParentSeq + 1, body.Position, body.BodyRotation, Vector3.Zero, null, true));
        Assert.Null(w.Hazard.VitalsOf(_p1));
        blocks.Accept(_p1, new ItemMoveIntent(new SlotRef(bag, 0), ItemMoveTarget.ToSlot(new SlotRef(bag, 12)), 30, DeathWorld.Stone, 30));
    }
}
