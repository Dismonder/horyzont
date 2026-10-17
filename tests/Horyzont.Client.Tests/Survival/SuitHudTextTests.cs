using Horyzont.Client.Survival;
using Horyzont.Core.Survival;

namespace Horyzont.Client.Tests.Survival;

/// <summary>The numbers of the suit HUD as text (K6-34): whole numbers rounded down, the drain multiplier, the time of a generator as mm:ss and the rays of the shelter; the capacity and the drain come from the same loadout the server uses.</summary>
public sealed class SuitHudTextTests
{
    private const int TickRate = 60;

    [Fact]
    public void ResourceLine_ValueCapacityDrain()
    {
        Assert.Equal("75 / 200  x0.6", SuitHudText.ResourceLine(75.9f, 200f, 0.6f));
        Assert.Equal("100 / 100  x1", SuitHudText.ResourceLine(100f, 100f, 1f));
        Assert.Equal("0 / 150  x0.25", SuitHudText.ResourceLine(0.4f, 150f, 0.25f));

        // A value that is not a number counts as 0, like the whole numbers of the vitals HUD.
        Assert.Equal("0 / 100  x1", SuitHudText.ResourceLine(float.NaN, 100f, 1f));
    }

    [Fact]
    public void GeneratorRemaining_MinutesSeconds()
    {
        Assert.Equal("00:10", SuitHudText.GeneratorRemaining(600, TickRate));
        Assert.Equal("20:20", SuitHudText.GeneratorRemaining(1220UL * TickRate, TickRate));
        Assert.Equal("00:00", SuitHudText.GeneratorRemaining(0, TickRate));

        // A started second counts as a whole one: a single tick is still 00:01.
        Assert.Equal("00:01", SuitHudText.GeneratorRemaining(1, TickRate));
        Assert.Equal("00:11", SuitHudText.GeneratorRemaining(601, TickRate));
    }

    [Fact]
    public void ShelterHits_Format()
    {
        Assert.Equal("21/26", SuitHudText.ShelterHits(21));
        Assert.Equal("0/26", SuitHudText.ShelterHits(0));
        Assert.Equal("26/26", SuitHudText.ShelterHits(26));
    }

    [Fact]
    public void FirstExposed_LowestBit()
    {
        Assert.Null(SuitHudText.FirstExposed(0));
        Assert.Equal(SuitResource.Heating, SuitHudText.FirstExposed(0b00001));
        Assert.Equal(SuitResource.Filter, SuitHudText.FirstExposed(0b10100));
        Assert.Equal(SuitResource.Oxygen, SuitHudText.FirstExposed(0b10000));
    }

    [Fact]
    public void Loadout_ModuleInTheEquipment_CapacityAndDrain()
    {
        var fx = new HazardClientFixture(withTank: true);
        var loadout = new ClientSuitLoadout(fx.Inventory);

        // The tank in module slot 4 raises the oxygen to 200; nothing else changes.
        var stats = loadout.Compute();
        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 200f), stats.Capacity);
        Assert.Equal(new SuitResources(1f, 1f, 1f, 1f, 1f), stats.Drain);

        // A thermal module in the next slot slows the heating and the cooling to 0.6; the loadout follows the equipment without being told.
        fx.Put(HazardClientFixture.EquipmentId, 5, HazardClientFixture.Thermal, 1);
        var both = loadout.Compute();
        Assert.Equal(200f, both.Capacity.Oxygen);
        Assert.Equal(0.6f, both.Drain.Heating, 0.0001f);
        Assert.Equal(0.6f, both.Drain.Cooling, 0.0001f);
        Assert.Equal(1f, both.Drain.Oxygen);

        // Taking the tank off brings the base capacity back.
        fx.Put(HazardClientFixture.EquipmentId, 4, default, 0);
        Assert.Equal(100f, loadout.Compute().Capacity.Oxygen);
    }

    [Fact]
    public void Loadout_NoEquipmentContainer_BaseValues()
    {
        var fx = new HazardClientFixture(withTank: true);
        fx.Inventory.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.Normal);

        var stats = new ClientSuitLoadout(fx.Inventory).Compute();

        Assert.Equal(new SuitResources(100f, 100f, 100f, 100f, 100f), stats.Capacity);
        Assert.Equal(new SuitResources(1f, 1f, 1f, 1f, 1f), stats.Drain);
    }
}
