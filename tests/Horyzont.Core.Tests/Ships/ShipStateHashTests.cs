using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.World.Ships;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships;

/// <summary>K5-24 decision 11, spec 02:194: the state hash skips the ship state.</summary>
public sealed class ShipStateHashTests
{
    [Fact]
    public void StateHasher_ShipStateIgnored()
    {
        var a = ShipFuelTests.Ship();
        var b = ShipFuelTests.Ship();
        var id = ShipFixtures.ConstructId;

        a.Sim.UpdateShipState(id, new ShipStateRecord(id, 1f, Vector3.Zero, Vector3.Zero, ShipStateFlags.Parked));
        b.Sim.UpdateShipState(id, new ShipStateRecord(id, 1999f, new Vector3(30, -4, 2), new Vector3(0, 1, 0), ShipStateFlags.Assist | ShipStateFlags.GearDown));

        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
        Assert.Equal(StateHasher.Hash(ShipFuelTests.Ship().State), StateHasher.Hash(a.State));
    }
}
