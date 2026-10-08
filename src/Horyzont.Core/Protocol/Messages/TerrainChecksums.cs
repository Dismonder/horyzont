using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Hashes of terrain blocks computed by the client.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Entries">Hashes.</param>
public sealed record TerrainChecksums(PlanetId Planet, IReadOnlyList<TerrainBlockChecksum> Entries) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.TerrainChecksums;
}
