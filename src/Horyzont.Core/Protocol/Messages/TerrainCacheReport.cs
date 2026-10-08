using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Versions of terrain blocks cached by the client.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Entries">Cached versions.</param>
public sealed record TerrainCacheReport(PlanetId Planet, IReadOnlyList<TerrainBlockVersion> Entries) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.TerrainCacheReport;
}
