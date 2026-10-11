using Horyzont.Client.Ships;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Ships;

/// <summary>K5-26 decision 10: the newest status by tick, stale after 2 s, cleared when the ship despawns.</summary>
public sealed class ClientShipStatusTests
{
    private static readonly EntityId _ship = new(20);

    private static ShipStatus Status(ulong tick, float fuel) =>
        new(_ship, new Tick(tick), fuel, 100f, 250f, ShipFlightFlags.Assist, ShipLaunchBlock.None);

    [Fact]
    public void ClientShipStatus_NewestByTick_StaleAfter2s_ClearedOnDespawn()
    {
        var time = new FakeTimeProvider();
        var statuses = new ClientShipStatus(time);
        Assert.False(statuses.TryGet(_ship, out _));

        statuses.OnMessage(Status(60, 50f));
        Assert.True(statuses.TryGet(_ship, out var status));
        Assert.Equal(new ShipFlightStatus(50f, 100f, 250f, ShipFlightFlags.Assist, ShipLaunchBlock.None), status);

        // An older status (reordered on the unreliable channel) does not replace a newer one, nor refresh its age.
        time.Advance(TimeSpan.FromSeconds(1.5));
        statuses.OnMessage(Status(30, 70f));
        Assert.True(statuses.TryGet(_ship, out status));
        Assert.Equal(50f, status.Fuel);
        time.Advance(TimeSpan.FromSeconds(0.6));
        Assert.False(statuses.TryGet(_ship, out _));

        statuses.OnMessage(Status(90, 40f));
        Assert.True(statuses.TryGet(_ship, out status));
        Assert.Equal(40f, status.Fuel);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(statuses.TryGet(_ship, out _));

        statuses.OnMessage(new EntityDespawn(_ship));
        Assert.False(statuses.TryGet(_ship, out _));

        // After a despawn any tick is new again (the ship may come back); a disconnect forgets everything.
        statuses.OnMessage(Status(10, 30f));
        Assert.True(statuses.TryGet(_ship, out _));
        statuses.OnDisconnected(DisconnectCode.Normal);
        Assert.False(statuses.TryGet(_ship, out _));
    }
}
