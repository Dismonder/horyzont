using Horyzont.Core.Progress;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The discoveries of the receiving player and the progress keys of the world (message 48, K6-32 D11); reliable ordered, server to the owner only.
/// Format 1: the bitset (varuint length 0 to 513, canonical bytes), a varuint count of keys (0 to 256) and the keys in ascending ordinal order; a later
/// package extends it by appending fields and raising the protocol version (K6-32 D27). K6-33 (D6) appends the food of the player: three slots, each a
/// varuint type and, with a type other than 0, a varuint tick of eating; the version byte stays 01 and the protocol version rises.
/// </summary>
/// <param name="Discovered">Item types the player discovered.</param>
/// <param name="WorldProgressKeys">Progress keys of the world, ascending by <see cref="string.CompareOrdinal(string, string)"/>, at most <c>ProtocolConstants.MaxProgressKeys</c>.</param>
/// <param name="Food">The three food slots of the player (K6-33); required: a state without food cannot be sent by accident, use <see cref="FoodSlots.Empty"/>.</param>
/// <param name="Respawn">The respawn point, the tombstones and the transition of the player (K6-35 tail, see <see cref="PlayerRespawnState"/>).</param>
public sealed record PlayerProgressState(ItemTypeBitSet Discovered, IReadOnlyList<string> WorldProgressKeys, FoodSlots Food, PlayerRespawnState Respawn) : Message
{
    /// <summary>Creates the state of a player with no bed, no tombstones and no transition: <see cref="PlayerRespawnState.None"/>.</summary>
    /// <param name="discovered">Item types the player discovered.</param>
    /// <param name="worldProgressKeys">Progress keys of the world.</param>
    /// <param name="food">The three food slots.</param>
    public PlayerProgressState(ItemTypeBitSet discovered, IReadOnlyList<string> worldProgressKeys, FoodSlots food)
        : this(discovered, worldProgressKeys, food, PlayerRespawnState.None)
    {
    }

    /// <inheritdoc />
    public override MessageType Type => MessageType.PlayerProgressState;
}
