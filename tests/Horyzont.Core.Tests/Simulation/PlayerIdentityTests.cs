using Horyzont.Core.Ids;

namespace Horyzont.Core.Tests.Simulation;

public sealed class PlayerIdentityTests
{
    private static readonly PlatformIdentity _alice = new(IdentityPlatform.Steam, 76561198000000001);
    private static readonly PlatformIdentity _bob = new(IdentityPlatform.Steam, 76561198000000002);

    [Fact]
    public void PreparePlayerJoin_NewIdentity_GetsPeekNext()
    {
        var f = new SimFixture();
        var expectedPlayer = f.State.PlayerIds.PeekNext();
        var expectedEntity = f.State.EntityIds.PeekNext();

        var op = f.Sim.PreparePlayerJoin(_alice, "alice");

        Assert.Equal(expectedPlayer, op.Player);
        Assert.Equal(expectedEntity, op.Entity);
        Assert.Equal(FrameRef.Of(SimFixture.Planet), op.Frame);
        Assert.Equal(0u, op.ParentSeq);
        Assert.Equal(f.Radius + 0.1f, op.Position.Y, 3);
        Assert.Equal(1UL, f.State.PlayerIds.NextValue);
    }

    [Fact]
    public void PreparePlayerJoin_KnownIdentity_ReturnsItsPlayerAndEntity()
    {
        var f = new SimFixture();
        var first = f.Sim.PreparePlayerJoin(_alice, "alice");
        f.Sim.ApplyServerOperation(first);
        var moved = f.PlayerOf(first.Player).Position + System.Numerics.Vector3.UnitX;
        f.MovePlayer(first.Player, moved);
        f.Sim.LeaveAllOnlinePlayers();

        var again = f.Sim.PreparePlayerJoin(_alice, "alice2");

        Assert.Equal(first.Player, again.Player);
        Assert.Equal(first.Entity, again.Entity);
        Assert.Equal(moved, again.Position);
        f.Sim.ApplyServerOperation(again);
        Assert.Equal("alice2", f.PlayerOf(first.Player).DisplayName);
    }

    [Fact]
    public void Join_TwoIdentities_NeverSharePlayerId()
    {
        var f = new SimFixture();
        var a = f.Sim.PreparePlayerJoin(_alice, "alice");
        f.Sim.ApplyServerOperation(a);
        var b = f.Sim.PreparePlayerJoin(_bob, "bob");
        f.Sim.ApplyServerOperation(b);

        Assert.NotEqual(a.Player, b.Player);
        Assert.NotEqual(a.Entity, b.Entity);
        Assert.True(f.State.Players.TryGetByIdentity(_bob, out var bob));
        Assert.Equal(b.Player, bob);
    }

    [Fact]
    public void PlayerJoin_KnownPlayerWithForeignIdentity_BreaksPrecondition()
    {
        var f = new SimFixture();
        var a = f.Sim.PreparePlayerJoin(_alice, "alice");
        f.Sim.ApplyServerOperation(a);
        f.Sim.LeaveAllOnlinePlayers();

        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(a with { Identity = _bob }));
    }

    [Fact]
    public void PlayerJoin_AlreadyOnlineOrReusedIds_BreaksPrecondition()
    {
        var f = new SimFixture();
        var a = f.Sim.PreparePlayerJoin(_alice, "alice");
        f.Sim.ApplyServerOperation(a);
        var b = f.Sim.PreparePlayerJoin(_bob, "bob");

        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(a));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(b with { Entity = a.Entity }));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(b with { Identity = _alice, Player = new PlayerId(9) }));
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(b with { ParentSeq = 1 }));
        Assert.Throws<ArgumentException>(() => f.Sim.PreparePlayerJoin(_bob, new string('x', 33)));
    }
}
