using Horyzont.Client.Ships;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Tests.Ships;

/// <summary>K5-24 decision 20: values and text of the flight HUD.</summary>
public sealed class FlightHudTextTests
{
    private static readonly FlightHudLabels _labels = new("ALT", "SPD", "LIM", "FUEL", "LOW", "RESERVE", "BLOCKED_RESERVE", "BLOCKED_PERSON", "NO_TERRAIN", "WAIT_TERRAIN");
    private static readonly HudBody[] _home = [new(Vector3D.Zero, 4000)];

    private static string Text(in FlightHudValues values)
    {
        Span<char> buffer = stackalloc char[512];
        return new string(buffer[..FlightHudText.Format(buffer, values, _labels)]);
    }

    [Fact]
    public void FlightHud_UnknownStatus_ShowsDashesAndLimitFromAltitude()
    {
        // 1300 m over R: 1000 m over the clearance, limit clamp(1000 / 8, 100, 250) = 125 m/s; 12 m in 1/60 s → 720 m/s.
        var now = new Vector3D(0, 5300, 0);
        var before = new Vector3D(0, 5312, 0);

        var values = FlightHudValues.Compute(now, before, 1.0 / 60, _home, false, default, FlightRules.Default);

        Assert.Equal(1300, values.Altitude, 6);
        Assert.Equal(720, values.Speed, 3);
        Assert.Equal(125, values.SpeedLimit, 6);
        Assert.False(new UnknownShipStatus().TryGet(new EntityId(1), out _));
        Assert.Equal("ALT: 1300 m\nSPD: 720 m/s  LIM: 125 m/s\nFUEL: —", Text(values));
    }

    [Fact]
    public void FlightHud_Status_FuelWarningsAndLaunchBlock()
    {
        var status = new ShipFlightStatus(150f, 2000f, 100f, ShipFlightFlags.Parked | ShipFlightFlags.Reserve, ShipLaunchBlock.FuelReserve);

        var values = FlightHudValues.Compute(new Vector3D(0, 4002, 0), new Vector3D(0, 4002, 0), 1.0 / 60, _home, true, status, FlightRules.Default);

        Assert.Equal("ALT: 2 m\nSPD: 0 m/s  LIM: 100 m/s\nFUEL: 8 %  RESERVE\nBLOCKED_RESERVE", Text(values));

        var low = status with { Fuel = 400f, Flags = ShipFlightFlags.TerrainUnavailable, LaunchBlock = ShipLaunchBlock.None };
        var lowValues = FlightHudValues.Compute(new Vector3D(0, 4002, 0), new Vector3D(0, 4002, 0), 1.0 / 60, [], true, low, FlightRules.Default);
        Assert.Equal("ALT: —\nSPD: 0 m/s  LIM: 100 m/s\nFUEL: 20 %  LOW\nNO_TERRAIN", Text(lowValues));
    }

    [Fact]
    public void FlightHud_Format_AllocatesNothingAndCutsAtBufferEnd()
    {
        var values = FlightHudValues.Compute(new Vector3D(0, 5300, 0), new Vector3D(0, 5300, 0), 1.0 / 60, _home, false, default, FlightRules.Default);
        var buffer = new char[512];
        FlightHudText.Format(buffer, values, _labels);

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                FlightHudText.Format(buffer, values, _labels);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
        Assert.Equal(5, FlightHudText.Format(new char[5], values, _labels));
    }
}
