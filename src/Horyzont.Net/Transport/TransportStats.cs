namespace Horyzont.Net.Transport;

/// <summary>Per-connection link statistics.</summary>
/// <param name="RttMs">Round-trip time in milliseconds.</param>
/// <param name="PacketLoss">Fraction of packets lost, from 0 to 1.</param>
/// <param name="BytesSent">Total bytes sent.</param>
/// <param name="BytesReceived">Total bytes received.</param>
public readonly record struct TransportStats(float RttMs, float PacketLoss, long BytesSent, long BytesReceived);
