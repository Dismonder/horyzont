using Horyzont.Core.Ids;
namespace Horyzont.Core.Protocol.Messages;
/// <summary>Ordered manifest or edit barrier; a transfer ends with an empty final page.</summary>
/// <param name="TransferId">Session-monotonic transfer id.</param>
/// <param name="Purpose">Startup (0), interest (1), or edit barrier (2).</param>
/// <param name="Planet">Planet of every entry.</param>
/// <param name="BarrierOpId">Nonzero only for an edit barrier.</param>
/// <param name="PageIndex">Zero-based page index.</param>
/// <param name="IsLastPage">Empty terminal page.</param>
/// <param name="Entries">Sorted distinct LOD zero entries.</param>
public sealed record TerrainSync(uint TransferId, byte Purpose, PlanetId Planet, OpId BarrierOpId, ushort PageIndex,
    bool IsLastPage, IReadOnlyList<TerrainSyncEntry> Entries) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.TerrainSync;
}
