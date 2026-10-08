using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Server accepts a client.</summary>
/// <param name="World">World id.</param>
/// <param name="ServerTick">Current server tick.</param>
/// <param name="TickRate">Ticks per second.</param>
/// <param name="YourPlayer">Player id of the client.</param>
/// <param name="YourEntity">Body entity of the client.</param>
/// <param name="Planets">Planets.</param>
/// <param name="BlockTypes">Block type registry.</param>
/// <param name="Materials">Material registry.</param>
public sealed record ServerHello(
    WorldId World, Tick ServerTick, byte TickRate, PlayerId YourPlayer, EntityId YourEntity, IReadOnlyList<PlanetParams> Planets,
    IReadOnlyList<RegistryEntry> BlockTypes, IReadOnlyList<RegistryEntry> Materials) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.ServerHello;
}
