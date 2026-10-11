using System.Globalization;
using System.Text;
using System.Text.Json;

using Godot;

using Horyzont.Bots.Digests;
using Horyzont.Client.Replication;
using Horyzont.Client.Terrain;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Planet;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Client.Bots;

/// <summary>
/// What a bot in the engine writes (K4-19): the dump of a check break in the format of <see cref="DigestDump"/> (a header, the constructs
/// of the mirror, and the terrain blocks it holds near its feet with the envelope hash from the cache and the digest of the voxels it
/// shows), and <c>engine-bot.json</c> with the motion statistics of K3-14 and the counters of the terrain replication of K3-16. Main
/// thread; only in check breaks and every half minute.
/// </summary>
public static class EngineBotReport
{
    /// <summary>Radius around the feet in which the held blocks are dumped, in metres (the bots without an engine use their view less one block diagonal).</summary>
    public const float DumpRadius = 228f;

    /// <summary>Writes the dump of a check break.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="name">Name of the bot.</param>
    /// <param name="role">Role in lower case.</param>
    /// <param name="player">The local player.</param>
    /// <param name="constructs">The construct mirror; null when there is none.</param>
    /// <param name="terrain">The own terrain of the client (cache and voxels); null when there is none.</param>
    public static void WriteDump(
        TextWriter writer, string name, string role, LocalPlayer player, ConstructMirror? constructs, OwnTerrainProvider? terrain)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(player);
        var feet = player.PredictedFeet;
        DigestDump.WriteBotHeader(writer, name, role, player.IsActive, feet);
        var entries = new List<DigestEntry>();
        if (constructs is not null)
        {
            entries.AddRange(constructs.Store.All.Select(ConstructDigest.Entry));
        }

        if (terrain?.Cache is { } cache && player.Planet.Value != 0)
        {
            var buffer = new TerrainBlockVersion[1024];
            var all = new List<TerrainBlockVersion>();
            int read;
            do
            {
                read = cache.CopyVersions(player.Planet, buffer, all.Count);
                all.AddRange(buffer.AsSpan(0, read));
            }
            while (read == buffer.Length);

            using var voxels = new VoxelBuffer();
            var hasNode = terrain.TryGetNode(player.Planet, out var node);
            foreach (var (key, _) in all)
            {
                var center = new NumVector3((key.X + 0.5f) * TerrainMath.BlockSize, (key.Y + 0.5f) * TerrainMath.BlockSize, (key.Z + 0.5f) * TerrainMath.BlockSize);
                if (key.Lod != 0 || NumVector3.Distance(center, feet) > DumpRadius || !cache.TryGetHeld(key, out var version, out var hash))
                {
                    continue;
                }

                ulong? digest = null;
                var origin = new Vector3I(key.X * TerrainMath.BlockSize, key.Y * TerrainMath.BlockSize, key.Z * TerrainMath.BlockSize);
                var size = new Vector3I(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
                if (hasNode && node!.Tool.IsAreaEditable(new Aabb(origin, size)))
                {
                    node.Tool.Copy(origin, voxels, TerrainChannels.UsedChannelsMask);
                    digest = terrain.Codec.Digest(voxels);
                }

                entries.Add(TerrainVersionDigest.Entry(key, version, hash, digest));
            }
        }

        DigestDump.Write(writer, entries);
    }

    /// <summary>Writes <c>engine-bot.json</c> atomically.</summary>
    /// <param name="path">Destination.</param>
    /// <param name="name">Name of the bot.</param>
    /// <param name="player">The local player.</param>
    /// <param name="terrain">Statistics of the terrain replication; null when there is none.</param>
    /// <param name="frames">Physics frames run in game.</param>
    /// <param name="toolActions">Tool actions pressed.</param>
    /// <param name="breaks">Check breaks dumped.</param>
    public static void Write(
        string path, string name, LocalPlayer player, TerrainReplicationStatistics? terrain, long frames, long toolActions, int breaks)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(player);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteString("written_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            json.WriteBoolean("in_game", player.IsActive);
            json.WriteNumber("frames", frames);
            json.WriteNumber("tool_actions", toolActions);
            json.WriteNumber("breaks", breaks);
            json.WriteNumber("corrections", player.Corrections);
            json.WriteNumber("compared", player.Compared);
            json.WriteString("motion", player.Motion.ToString());
            if (terrain is not null)
            {
                json.WriteStartObject("terrain");
                json.WriteBoolean("ready", terrain.Ready);
                json.WriteNumber("pending", terrain.PendingBlocks);
                json.WriteNumber("blocks_received", terrain.BlocksReceived);
                json.WriteNumber("bytes_received", terrain.BytesReceived);
                json.WriteNumber("pages", terrain.ManifestPages);
                json.WriteNumber("stale", terrain.StaleBlocks);
                json.WriteNumber("rejected", terrain.RejectedBlocks);
                json.WriteNumber("repairs", terrain.GeneratorRepairs);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Writes a dump file of a check break.</summary>
    /// <param name="directory">The <c>dumps</c> directory.</param>
    /// <param name="index">Break index (from 1).</param>
    /// <param name="name">Name of the bot (file name).</param>
    /// <param name="write">Writes the content.</param>
    public static void WriteBreak(string directory, long index, string name, Action<TextWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var folder = Path.Combine(directory, "break" + index.ToString("000", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        using var writer = new StreamWriter(Path.Combine(folder, name + ".jsonl"), append: false, new UTF8Encoding(false));
        write(writer);
    }
}
