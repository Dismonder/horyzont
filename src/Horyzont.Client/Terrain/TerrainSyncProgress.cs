using Horyzont.Core.Ids;

namespace Horyzont.Client.Terrain;

/// <summary>The effect of one manifest page on the transfer it belongs to.</summary>
/// <param name="Violation">Null when the page was valid; otherwise why it broke the protocol (the peer is to be left).</param>
/// <param name="Completed">Whether this page ended its transfer.</param>
/// <param name="Purpose">Purpose of the transfer the page belongs to.</param>
/// <param name="Barrier">Operation of an edit barrier; none for other purposes.</param>
public readonly record struct TerrainSyncProgress(string? Violation, bool Completed, byte Purpose, OpId Barrier)
{
    /// <summary>Gets a value indicating whether the page was valid.</summary>
    public bool IsValid => Violation is null;
}
