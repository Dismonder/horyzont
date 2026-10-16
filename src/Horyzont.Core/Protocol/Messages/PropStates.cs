using Horyzont.Core.Ids;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// Harvested props of some regions of a planet (message 45, K6-31 D17): the full state of every listed region. At most
/// <see cref="ProtocolConstants.MaxPropRegionsPerMessage"/> regions and <see cref="ProtocolConstants.MaxPropEntriesPerMessage"/> entries.
/// </summary>
/// <param name="Planet">The planet.</param>
/// <param name="Regions">The regions, ascending and unique.</param>
public sealed record PropStates(PlanetId Planet, IReadOnlyList<PropRegionState> Regions) : Message
{
    /// <inheritdoc />
    public override MessageType Type => (MessageType)45; // K6-31 B2: MessageType.PropStates.
}
