using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Net.Tests.Session;

public sealed class SessionAllocationTests
{
    private static readonly TimeSpan _tickLength = TimeSpan.FromMilliseconds(33);

    [Fact]
    public void Poll_SteadyState_DoesNotAllocate()
    {
        using var h = new SessionHarness() { PollServer = false };
        var clients = new List<TestClient>();
        for (var i = 0; i < 8; i++)
        {
            clients.Add(JoinManually(h));
        }

        var players = clients.Select(c => c.Player(h)).ToArray();
        uint seq = 1;
        ulong tick = 1000;

        // Warm-up: buffers, pools and dictionaries reach their steady size.
        for (var i = 0; i < 200; i++)
        {
            RunTick(h, clients, players, ref seq, ref tick, measure: false, out _);
        }

        var decodedBefore = h.Server.FramesDecoded;
        long serverBytes = 0;
        for (var i = 0; i < 1000; i++)
        {
            RunTick(h, clients, players, ref seq, ref tick, measure: true, out var bytes);
            serverBytes += bytes;
        }

        var frames = h.Server.FramesDecoded - decodedBefore;
        var inputBatches = 8L * 1000;
        var pings = frames - inputBatches;
        Assert.InRange(pings, 8 * 30, 8 * 40); // one per second per client over 33 s

        var batchFrame = SessionHarness.Encode(Batch(100));
        var pingFrame = SessionHarness.Encode(new Ping(123456));
        // Runtime noise when tests run in parallel (one-off allocations of a few dozen bytes) is absorbed by a fixed
        // slack; anything allocated per tick (at least 24 B × 1000 ticks) still fails.
        const long slack = 1024;
        var allowance = (inputBatches * DecodeCost(batchFrame)) + (pings * (DecodeCost(pingFrame) + PongCost())) + slack;

        Assert.True(
            serverBytes <= allowance,
            $"server allocated {serverBytes} B, message decoding accounts for {allowance} B ({frames} frames)");
        Assert.All(clients, c => Assert.Empty(c.Hooks.Disconnections));
    }

    private static TestClient JoinManually(SessionHarness h)
    {
        h.PollServer = true;
        var client = h.JoinInGame();
        h.PollServer = false;
        return client;
    }

    private static InputBatch Batch(uint first) =>
        new(first, [new PlayerInput(first, 0.5f, 0, 0, 0, InputButtons.None), new PlayerInput(first + 1, 0.5f, 0, 0, 0, InputButtons.None), new PlayerInput(first + 2, 0.5f, 0, 0, 0, InputButtons.None)]);

    private static void RunTick(
        SessionHarness h, List<TestClient> clients, PlayerId[] players, ref uint seq, ref ulong tick, bool measure, out long serverBytes)
    {
        h.Time.Advance(_tickLength);
        foreach (var client in clients)
        {
            client.Session.SendInputs(seq, Batch(seq).Inputs);
            client.Session.Poll();
        }

        seq++;
        var before = GC.GetAllocatedBytesForCurrentThread();
        h.Server.Poll(new Tick(tick++));
        foreach (var player in players)
        {
            h.Server.TryTakeInput(player, out _, out _);
        }

        serverBytes = measure ? GC.GetAllocatedBytesForCurrentThread() - before : 0;
    }

    private static long DecodeCost(byte[] frame)
    {
        MessageCodec.Read(frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            MessageCodec.Read(frame);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before + 99) / 100;
    }

    private static long PongCost()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var pong = new Pong(1, new Tick(2));
        var cost = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(pong);
        return cost;
    }
}
