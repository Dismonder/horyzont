using System.Globalization;

using Horyzont.Core.Frames;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>A body for the altitude of the flight HUD: centre in the frame of the ship positions and radius R.</summary>
/// <param name="Center">Centre of the body.</param>
/// <param name="Radius">Surface radius.</param>
public readonly record struct HudBody(Vector3D Center, double Radius);

/// <summary>Values shown by the flight HUD (K5-24 decision 20).</summary>
/// <param name="Altitude">Height over the surface of the nearest body (metres); NaN when no body is known.</param>
/// <param name="Speed">Speed (m/s).</param>
/// <param name="SpeedLimit">Speed limit (m/s) of <see cref="FlightRules.SpeedLimit"/>; NaN without altitude.</param>
/// <param name="HasStatus">True when the server status of the ship is known (K5-26).</param>
/// <param name="FuelPercent">Fuel in percent of the tanks (with a status).</param>
/// <param name="Status">The status (with <paramref name="HasStatus"/>).</param>
public readonly record struct FlightHudValues(double Altitude, double Speed, double SpeedLimit, bool HasStatus, double FuelPercent, ShipFlightStatus Status)
{
    /// <summary>Fuel percentage under which the HUD warns.</summary>
    public const double LowFuelPercent = 25;

    /// <summary>Computes the values from two poses one tick apart, the nearby bodies and the status.</summary>
    /// <param name="now">Centre of the ship now (any frame shared with the bodies).</param>
    /// <param name="before">Centre of the ship one tick earlier.</param>
    /// <param name="tickSeconds">Seconds between the poses.</param>
    /// <param name="bodies">Bodies in the same frame.</param>
    /// <param name="hasStatus">Whether <paramref name="status"/> is known.</param>
    /// <param name="status">The status.</param>
    /// <param name="rules">Flight rules (limit and clearance).</param>
    /// <returns>The values.</returns>
    public static FlightHudValues Compute(
        in Vector3D now, in Vector3D before, double tickSeconds, ReadOnlySpan<HudBody> bodies, bool hasStatus, in ShipFlightStatus status, FlightRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var altitude = double.NaN;
        foreach (var body in bodies)
        {
            var height = Vector3D.Distance(now, body.Center) - body.Radius;
            if (double.IsNaN(altitude) || height < altitude)
            {
                altitude = height;
            }
        }

        var speed = tickSeconds > 0 ? Vector3D.Distance(now, before) / tickSeconds : 0;
        var pulse = hasStatus && (status.Flags & ShipFlightFlags.PulseEngaged) != 0;
        var limit = hasStatus && status.SpeedLimit > 0 ? status.SpeedLimit
            : double.IsNaN(altitude) ? double.NaN
            : rules.SpeedLimit(Math.Max(0, altitude - rules.SurfaceClearanceMeters), pulse);
        var fuel = hasStatus && status.FuelCapacity > 0 ? 100.0 * status.Fuel / status.FuelCapacity : 0;
        return new FlightHudValues(altitude, speed, limit, hasStatus, fuel, status);
    }
}

/// <summary>Translated labels of the flight HUD (resolved once by the game).</summary>
/// <param name="Altitude">Altitude label.</param>
/// <param name="Speed">Speed label.</param>
/// <param name="Limit">Limit label.</param>
/// <param name="Fuel">Fuel label.</param>
/// <param name="LowFuel">Low fuel warning.</param>
/// <param name="Reserve">Reserve warning (landing only).</param>
/// <param name="BlockedReserve">Launch blocked by the reserve.</param>
/// <param name="BlockedPerson">Launch blocked for another reason (K5-25: a person in the hull).</param>
/// <param name="TerrainUnavailable">Terrain unavailable.</param>
/// <param name="TerrainHold">Waiting for the terrain.</param>
public sealed record FlightHudLabels(
    string Altitude, string Speed, string Limit, string Fuel, string LowFuel, string Reserve, string BlockedReserve, string BlockedPerson,
    string TerrainUnavailable, string TerrainHold);

/// <summary>Writes the flight HUD text into a character buffer without allocating (K5-24 decision 20).</summary>
public static class FlightHudText
{
    /// <summary>Text shown for an unknown value.</summary>
    public const string Unknown = "—";

    /// <summary>Formats the HUD.</summary>
    /// <param name="destination">Buffer (512 characters are enough for the default labels).</param>
    /// <param name="values">The values.</param>
    /// <param name="labels">The labels.</param>
    /// <returns>Characters written; the text is cut at the end of the buffer.</returns>
    public static int Format(Span<char> destination, in FlightHudValues values, FlightHudLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var w = new Writer(destination);
        w.Text(labels.Altitude);
        w.Text(": ");
        w.Number(values.Altitude, " m");
        w.Line();
        w.Text(labels.Speed);
        w.Text(": ");
        w.Number(values.Speed, " m/s");
        w.Text("  ");
        w.Text(labels.Limit);
        w.Text(": ");
        w.Number(values.SpeedLimit, " m/s");
        w.Line();
        w.Text(labels.Fuel);
        w.Text(": ");
        if (values.HasStatus)
        {
            w.Number(values.FuelPercent, " %");
            var flags = values.Status.Flags;
            if ((flags & ShipFlightFlags.Reserve) != 0)
            {
                w.Text("  ");
                w.Text(labels.Reserve);
            }
            else if (values.FuelPercent <= FlightHudValues.LowFuelPercent)
            {
                w.Text("  ");
                w.Text(labels.LowFuel);
            }

            if (values.Status.LaunchBlock == ShipLaunchBlock.FuelReserve)
            {
                w.Line();
                w.Text(labels.BlockedReserve);
            }
            else if (values.Status.LaunchBlock != ShipLaunchBlock.None)
            {
                w.Line();
                w.Text(labels.BlockedPerson);
            }

            if ((flags & ShipFlightFlags.TerrainUnavailable) != 0)
            {
                w.Line();
                w.Text(labels.TerrainUnavailable);
            }
            else if ((flags & ShipFlightFlags.TerrainHold) != 0)
            {
                w.Line();
                w.Text(labels.TerrainHold);
            }
        }
        else
        {
            w.Text(Unknown);
        }

        return w.Length;
    }

    private ref struct Writer(Span<char> buffer)
    {
        private readonly Span<char> _buffer = buffer;

        public int Length { get; private set; }

        public void Text(ReadOnlySpan<char> text)
        {
            var count = Math.Min(text.Length, _buffer.Length - Length);
            text[..count].CopyTo(_buffer[Length..]);
            Length += count;
        }

        /// <summary>A rounded number and its unit; an unknown value is a dash without the unit.</summary>
        public void Number(double value, ReadOnlySpan<char> unit)
        {
            if (!double.IsFinite(value))
            {
                Text(Unknown);
                return;
            }

            if (Math.Round(value).TryFormat(_buffer[Length..], out var written, "0", CultureInfo.InvariantCulture))
            {
                Length += written;
            }

            Text(unit);
        }

        public void Line() => Text("\n");
    }
}
