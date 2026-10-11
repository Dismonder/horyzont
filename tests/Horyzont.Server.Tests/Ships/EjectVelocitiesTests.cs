using System.Numerics;

using Horyzont.Core.Operations;
using Horyzont.Server.Frames;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-25 decision 19 (R4): the ejection velocity reaches the server body through <see cref="EjectVelocities"/>.</summary>
public sealed class EjectVelocitiesTests
{
    [Fact]
    public void EjectVelocities_ReparentToPlanetWithVelocity_TakenOnceBySeq()
    {
        var rig = new SeatRig();
        using var ejects = new EjectVelocities(rig.Flight.Sim);
        var player = rig.Join(1);
        rig.Sit(player, SeatRig.SeatA);
        var seated = rig.Player(player);
        var entity = seated.Entity;
        var seq = seated.ParentSeq;
        var velocity = new Vector3(3, 1, -4);

        rig.Flight.Sim.ApplyServerOperation(new EntityReparentOp(
            entity, rig.Flight.Construct().Parent, seq + 1, new Vector3(0, 4100, 0), Quaternion.Identity, velocity, null));

        Assert.False(ejects.TryTake(entity, seq, out _));
        rig.Sit(player, SeatRig.SeatA);
        rig.Flight.Sim.ApplyServerOperation(new EntityReparentOp(
            entity, rig.Flight.Construct().Parent, seq + 3, new Vector3(0, 4100, 0), Quaternion.Identity, velocity, null));
        Assert.True(ejects.TryTake(entity, seq + 3, out var taken));
        Assert.Equal(velocity, taken);
        Assert.False(ejects.TryTake(entity, seq + 3, out _));
    }

    [Fact]
    public void EjectVelocities_ExitWithoutVelocity_NotRemembered_AndUntakenExpires()
    {
        var rig = new SeatRig();
        using var ejects = new EjectVelocities(rig.Flight.Sim);
        var player = rig.Join(1);
        rig.Sit(player, SeatRig.SeatA);
        var p = rig.Player(player);
        rig.Flight.Sim.ApplyServerOperation(new EntityReparentOp(
            p.Entity, rig.Flight.Construct().Parent, p.ParentSeq + 1, new Vector3(0, 4100, 0), Quaternion.Identity, new Vector3(0.2f, 0, 0), null));
        Assert.Equal(0, ejects.Count);

        rig.Sit(player, SeatRig.SeatA);
        p = rig.Player(player);
        rig.Flight.Sim.ApplyServerOperation(new EntityReparentOp(
            p.Entity, rig.Flight.Construct().Parent, p.ParentSeq + 1, new Vector3(0, 4100, 0), Quaternion.Identity, new Vector3(9, 0, 0), null));
        var last = p.ParentSeq;
        Assert.Equal(1, ejects.Count);
        for (var i = 0; i <= EjectVelocities.ExpireTicks; i++)
        {
            rig.Flight.Sim.AdvanceTick();
        }

        Assert.False(ejects.TryTake(p.Entity, last, out _));
        Assert.Equal(0, ejects.Count);
    }
}
