using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Testing.Generators;

/// <summary>
/// FsCheck <see cref="Arbitrary{T}"/> registrations for the K1-04 codec tests. Use with
/// <c>[Property(Arbitrary = [typeof(CodecArbitraries)])]</c>.
/// </summary>
public static class CodecArbitraries
{
    /// <summary>Operations of every kind.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<Operation> Operations() => Arb.From(CodecGen.Operation);

    /// <summary>Intents of every kind.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<Intent> Intents() => Arb.From(CodecGen.Intent);

    /// <summary>Messages of every type.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<Message> Messages() => Arb.From(CodecGen.Message);

    /// <summary>Construct headers.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<ConstructHeaderRecord> ConstructHeaders() => Arb.From(CodecGen.ConstructHeader);

    /// <summary>Player records.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<PlayerRecord> Players() => Arb.From(CodecGen.PlayerRecord);

    /// <summary>Planet parameters.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<PlanetParams> Planets() => Arb.From(CodecGen.Planet);

    /// <summary>Chunk blocks.</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<ChunkBlocksCase> Chunks() => Arb.From(CodecGen.ChunkBlocks.Select(b => new ChunkBlocksCase(b)));

    /// <summary>Chunk records of construct 77 (chunk position varies).</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<ConstructChunkRecord> ChunkRecords() => Arb.From(CodecGen.ChunkRecord(new Horyzont.Core.Ids.EntityId(77)));

    /// <summary>Block-type registry entries (ids 1 to 2048).</summary>
    /// <returns>The arbitrary.</returns>
    public static Arbitrary<RegistryEntry> RegistryEntries() => Arb.From(CodecGen.RegistryEntry(ProtocolConstants.MaxBlockTypes));
}
