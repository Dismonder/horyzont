using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-26 criterion 5: a ship crossing planet → system next to an observer riding another ship rebases without a jump.</summary>
public sealed class ShipZoneTransitionReplicationTests(ITestOutputHelper output)
{
    private static int StatesOf(ShipCoopClient client, EntityId ship) =>
        client.Client.Received.Count(m => m is ConstructState s && s.Header.Id == ship);

    [Fact]
    public async Task ShipReplication_ZoneTransition_ObserverRebasesWithoutJump()
    {
        await using var rig = await ShipCoopRig.StartAsync(4, lossy: false);
        var up = rig.UpToward(1);
        var side = Vector3D.Normalize(Vector3D.Cross(up, Math.Abs(up.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)));
        var a = rig.AddShip(up);
        var b = rig.AddShip(Vector3D.Normalize(up + (side * (40 / rig.Model.GetBody(0).RadiusMeters))));
        var observer = await rig.JoinAsync("observer", 1);
        rig.Seat(observer.Player, a, new CellPos(1, 2, 1));
        rig.Settle(a, b);
        var statesA = StatesOf(observer, a);
        var statesB = StatesOf(observer, b);

        rig.Fly(a, 1);
        rig.Fly(b, 1);
        var measure = new ShipPoseMeasure(rig);
        var inSystemFrames = 0;
        Assert.True(rig.StepUntil(() => !rig.IsFlying(a) && !rig.IsFlying(b), 60 * 240, () =>
        {
            if (rig.Ship(b).Parent.Kind == FrameKind.System)
            {
                inSystemFrames++;
            }

            measure.Sample(observer, b);
        }), "flight to the moon");

        output.WriteLine(measure.Report());
        Assert.True(inSystemFrames > 60, "frames in the system frame: " + inSystemFrames);
        Assert.True(rig.Ship(b).ParentSeq >= 2);
        Assert.True(measure.Samples > 600, measure.Report());
        Assert.True(measure.MaxError <= 0.5, measure.Report());
        Assert.True(measure.MaxJump <= 0.5, measure.Report());
        Assert.Equal(0, observer.Entities.WrongFrameApplied);

        // The reparents reached the live copies as operations: no complete state of either ship was sent again.
        Assert.Equal(statesA, StatesOf(observer, a));
        Assert.Equal(statesB, StatesOf(observer, b));
    }
}
