namespace Horyzont.Client.Diagnostics;

/// <summary>Sampled metrics with explicit availability.</summary>
/// <param name="Present">Available fields.</param>
/// <param name="Fps">Frame rate.</param>
/// <param name="ServerTickMilliseconds">Local server tick duration.</param>
/// <param name="Tick">Authoritative tick.</param>
/// <param name="RttMilliseconds">Remote RTT.</param>
/// <param name="SentBytesPerSecond">Transport send rate.</param>
/// <param name="ReceivedBytesPerSecond">Transport receive rate.</param>
/// <param name="PendingIntents">Pending intentions.</param>
/// <param name="Rejections">Rejected intentions.</param>
/// <param name="ReadOnly">Confirmed server availability.</param>
public readonly record struct HudSample(HudFields Present, double Fps, double ServerTickMilliseconds, ulong Tick,
    double RttMilliseconds, double SentBytesPerSecond, double ReceivedBytesPerSecond, int PendingIntents, int Rejections, bool ReadOnly);
