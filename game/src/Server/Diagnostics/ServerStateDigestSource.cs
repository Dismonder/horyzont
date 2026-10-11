using Godot;

using Horyzont.Bots.Digests;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Terrain;
using Horyzont.Server;
using Horyzont.Server.Diagnostics;

namespace Horyzont.Game.Server.Diagnostics;

/// <summary>
/// The <c>digest</c> command of the game server (K4-19): constructs through <see cref="ConstructDigest"/>, the versions of the edited
/// terrain blocks through <see cref="TerrainVersionDigest"/> with the hash of the envelope the replication would send, and the voxels of
/// the same blocks through <see cref="TerrainDigest"/> (each exported envelope decoded and digested, loaded or not). Main thread only,
/// on demand: it exports every edited block, so it belongs in check breaks, never in a tick.
/// </summary>
/// <param name="runtime">The server.</param>
/// <param name="terrain">The terrain factory of the server (its terrain exists once the world has started).</param>
public sealed class ServerStateDigestSource(ServerRuntime runtime, VoxelServerTerrainFactory terrain) : IStateDigestSource
{
    /// <inheritdoc />
    public StateDigest Compute()
    {
        var blocks = Blocks();
        var constructs = ConstructDigest.Compute(runtime.State.Constructs.All);
        var versions = TerrainVersionDigest.Compute(blocks.Select(b => new TerrainBlockVersion(b.Key, b.Version)));
        ulong? voxels = blocks.All(b => b.Voxels.HasValue)
            ? TerrainDigest.Compute(blocks.Select(b => (b.Key, b.Version, b.Voxels!.Value)))
            : null;
        return new StateDigest(constructs, versions, voxels);
    }

    /// <inheritdoc />
    public void WriteDump(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var entries = runtime.State.Constructs.All.Select(ConstructDigest.Entry).ToList();
        entries.AddRange(Blocks().Select(b => TerrainVersionDigest.Entry(b.Key, b.Version, b.EnvelopeHash, b.Voxels)));
        DigestDump.Write(writer, entries);
    }

    private List<Block> Blocks()
    {
        var result = new List<Block>();
        var source = terrain.Terrain;
        using var codec = new TerrainBlockCodec();
        using var buffer = new VoxelBuffer();
        foreach (var (key, version) in runtime.State.TerrainVersions.All)
        {
            if (source is null || !source.TryExportBlock(key, out _, out var blob))
            {
                result.Add(new Block(key, version, 0, null));
                continue;
            }

            ulong? voxels;
            try
            {
                codec.Decode(blob, buffer);
                voxels = codec.Digest(buffer);
            }
            catch (Horyzont.Core.Serialization.ProtocolViolationException)
            {
                voxels = null;
            }

            result.Add(new Block(key, version, TerrainBlockHash.Compute(blob), voxels));
        }

        return result;
    }

    private readonly record struct Block(TerrainBlockKey Key, uint Version, ulong EnvelopeHash, ulong? Voxels);
}
