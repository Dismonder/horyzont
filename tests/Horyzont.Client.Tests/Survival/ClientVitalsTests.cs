using Horyzont.Client.Replication;
using Horyzont.Client.Survival;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Testing;

namespace Horyzont.Client.Tests.Survival;

/// <summary>The vitals the server told the local player (K6-33 D20, message 44): the last message wins, the exhaustion and the rest are exposed.</summary>
public sealed class ClientVitalsTests
{
    private static PlayerVitalsMessage Message(
        float health = 80f, float healthMax = 110f, float stamina = 50f, float staminaMax = 120f, bool exhausted = false, bool resting = false, bool rested = false,
        ushort warmup = 0, uint remaining = 0, byte comfort = 0) =>
        TestMessages.Vitals(health, healthMax, stamina, staminaMax, exhausted, resting, rested, warmup, remaining, comfort);

    [Fact]
    public void Receive_LatestWins()
    {
        var vitals = new ClientVitals();
        var changed = 0;
        vitals.Changed += () => changed++;
        Assert.False(vitals.HasState);
        Assert.Equal(0f, vitals.Health);

        vitals.Apply(Message(health: 80f, healthMax: 110f), 500);
        vitals.Apply(Message(health: 75.5f, healthMax: 112f, stamina: 20f, staminaMax: 121f), 506);

        Assert.True(vitals.HasState);
        Assert.Equal(75.5f, vitals.Health);
        Assert.Equal(112f, vitals.HealthMax);
        Assert.Equal(20f, vitals.Stamina);
        Assert.Equal(121f, vitals.StaminaMax);
        Assert.Equal(506UL, vitals.ReceivedAtTick);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Exhausted_Exposed()
    {
        var vitals = new ClientVitals();
        vitals.Apply(Message(stamina: 0f, exhausted: true), 1);
        Assert.True(vitals.Exhausted);

        vitals.Apply(Message(stamina: 30f, exhausted: false), 7);
        Assert.False(vitals.Exhausted);
    }

    [Fact]
    public void RestedRemaining_Exposed()
    {
        var vitals = new ClientVitals();
        Assert.Equal(0UL, vitals.RestRemainingTicks(100));

        vitals.Apply(Message(rested: true, remaining: 39_600, comfort: 3), 1_000);

        Assert.Equal(39_600UL, vitals.RestRemainingTicks(1_000));
        Assert.Equal(39_600UL - 600UL, vitals.RestRemainingTicks(1_600));
        Assert.Equal(0UL, vitals.RestRemainingTicks(1_000 + 39_600));
        Assert.Equal(0UL, vitals.RestRemainingTicks(1_000 + 99_999));
        Assert.Equal(39_600UL, vitals.RestRemainingTicks(900));
        Assert.Equal(3, vitals.Comfort);
    }

    [Fact]
    public void Warmup_Exposed()
    {
        var vitals = new ClientVitals();
        vitals.Apply(Message(resting: true, warmup: 600, comfort: 2), 1_000);

        Assert.True(vitals.Resting);
        Assert.Equal(600, vitals.RestWarmupTicks);
        Assert.Equal(0UL, vitals.RestRemainingTicks(1_000));
    }

    [Fact]
    public void Replication_Message44_ReachesVitals_WithTheServerTick_DisconnectClearsIt()
    {
        var vitals = new ClientVitals();
        var tick = 4_000UL;
        var replication = new ClientReplication(_ => { }, null, null, null, vitals: vitals, serverTick: (out ulong t) =>
        {
            t = tick;
            return true;
        });
        Assert.Same(vitals, replication.Vitals);

        replication.OnMessage(Message(health: 61f));

        Assert.Equal(61f, vitals.Health);
        Assert.Equal(4_000UL, vitals.ReceivedAtTick);

        replication.OnDisconnected(DisconnectCode.ServerShutdown);

        Assert.False(vitals.HasState);
    }

    [Fact]
    public void Replication_WithoutVitals_IgnoresTheMessage()
    {
        var replication = new ClientReplication(_ => { }, null);

        replication.OnMessage(Message());

        Assert.Null(replication.Vitals);
    }
}
