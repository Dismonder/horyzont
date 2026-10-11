using Horyzont.Client.Replication;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Ships;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decision 16: the client steps with the same <see cref="ShipStats"/> as the server, computed from its mirror.</summary>
public sealed class ShipStatsMirrorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShipStats_ClientMirrorEqualsServer_Bitwise(bool reversedChunks)
    {
        var rig = new SeatRig();
        var construct = rig.Flight.Construct();
        var chunks = construct.Chunks.Select(p => new ConstructChunkRecord(new ConstructChunkKey(construct.Id, p.Key), p.Value.Blocks.ToArray())).ToList();
        if (reversedChunks)
        {
            chunks.Reverse();
        }

        var writer = new ByteWriter();
        MessageCodec.Write(writer, new ConstructState(ConstructHeaderRecord.From(construct), 0, 1, chunks.ToArray()));
        var mirror = new ConstructMirror(new FakeTimeProvider());
        Assert.True(mirror.Apply((ConstructState)MessageCodec.Read(writer.WrittenSpan)));
        Assert.True(mirror.TryGetConstruct(construct.Id, out var copy));
        Assert.True(rig.Flight.Stats.TryGet(construct.Id, out var server));

        var client = ShipStatsCalculator.Compute(copy, ShipFixtures.ContentFor(rig.Flight.Sim.State));

        Assert.Equal(Bits(server), Bits(client));
        Assert.Equal(server.Seats, client.Seats);
    }

    private static string Bits(ShipStats s) => string.Join(
        ',',
        BitConverter.SingleToInt32Bits(s.MassKg),
        BitConverter.SingleToInt32Bits(s.CenterOfMass.X),
        BitConverter.SingleToInt32Bits(s.CenterOfMass.Y),
        BitConverter.SingleToInt32Bits(s.CenterOfMass.Z),
        s.Inertia,
        s.Thrust,
        BitConverter.SingleToInt32Bits(s.TorqueNewtonMeters),
        BitConverter.SingleToInt32Bits(s.FuelCapacity),
        s.BlockCount,
        s.Cockpits,
        s.Engines,
        s.Stabilizers,
        s.FuelTanks,
        s.PulseDrives,
        s.LandingGears,
        s.ControlFrame,
        BitConverter.SingleToInt32Bits(s.BoundingRadius),
        s.ControlCell);
}
