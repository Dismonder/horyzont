using System.Text.Json;

using Horyzont.Bots.Client;

namespace Horyzont.Bots.Runner;

/// <summary>
/// Writes the result of a run as <c>bots.json</c>: counters and every answered intent of every bot with raw timestamps of the clock
/// (<c>frequency</c> ticks per second; on Windows the performance counter, shared by the processes of one machine), so the orchestrator
/// can line the replies up with the log of the server.
/// </summary>
public static class BotReport
{
    /// <summary>Writes the report atomically (a temporary file, then a move).</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="bots">The bots.</param>
    /// <param name="time">The clock the timestamps of the bots come from.</param>
    /// <param name="frames">Frames run by the crowd.</param>
    /// <param name="skippedFrames">Frames skipped after stalls.</param>
    /// <param name="latency">Edit latency measurement; null when none was taken.</param>
    /// <param name="breaks">Check breaks dumped so far.</param>
    public static void Write(
        string path, IReadOnlyList<BotClient> bots, TimeProvider time, long frames, long skippedFrames, EditLatencyTracker? latency = null, int breaks = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(time);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            json.WriteStartObject();
            json.WriteNumber("frequency", time.TimestampFrequency);
            json.WriteNumber("written_at", time.GetTimestamp());
            json.WriteString("written_utc", time.GetUtcNow());
            json.WriteNumber("frames", frames);
            json.WriteNumber("skipped_frames", skippedFrames);
            json.WriteNumber("working_set_bytes", Environment.WorkingSet);
            json.WriteNumber("breaks", breaks);
            if (latency is not null)
            {
                // From the submit of the author to the arrival at another bot (P5, network part), milliseconds.
                var (count, p50, p99, max) = latency.Summary();
                json.WriteStartObject("edit_latency_ms");
                json.WriteNumber("count", count);
                json.WriteNumber("p50", Math.Round(p50, 3));
                json.WriteNumber("p99", Math.Round(p99, 3));
                json.WriteNumber("max", Math.Round(max, 3));
                json.WriteEndObject();
            }
            json.WriteStartArray("bots");
            foreach (var bot in bots)
            {
                WriteBot(json, bot);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteBot(Utf8JsonWriter json, BotClient bot)
    {
        var stats = bot.Stats;
        json.WriteStartObject();
        json.WriteString("name", bot.Options.Name);
        json.WriteString("role", bot.Options.Role.ToString());
        json.WriteNumber("seed", bot.Options.Seed);
        json.WriteNumber("player", bot.Player.Value);
        json.WriteBoolean("in_game", bot.IsInGame);
        json.WriteNumber("joins", stats.Joins);
        json.WriteNumber("disconnects", stats.Disconnects);
        json.WriteNumber("reconnects", stats.Reconnects);
        json.WriteNumber("inputs", stats.Inputs);
        json.WriteNumber("snapshots", stats.Snapshots);
        json.WriteNumber("intents_sent", stats.IntentsSent);
        json.WriteNumber("accepted", stats.Accepted);
        json.WriteNumber("rejected", stats.Rejected);
        json.WriteNumber("refused_locally", stats.RefusedLocally);
        json.WriteNumber("unknown_replies", stats.UnknownReplies);
        json.WriteNumber("replica_digest", bot.Replica.Digest);

        // The transport counters of the session (K3-18): the bandwidth per client of X3 is reported from them (P10, not gated).
        var transport = bot.Runtime.Session.GetTransportStats();
        json.WriteStartObject("transport");
        json.WriteNumber("bytes_sent", transport.BytesSent);
        json.WriteNumber("bytes_received", transport.BytesReceived);
        json.WriteNumber("rtt_ms", Math.Round(transport.RttMs, 2));
        json.WriteNumber("packet_loss", Math.Round(transport.PacketLoss, 4));
        json.WriteNumber("in_game_seconds", Math.Round(bot.InGameSeconds, 1));
        json.WriteEndObject();
        if (bot.Replica is Replicas.BotReplicaSet set)
        {
            json.WriteStartObject("terrain");
            json.WriteBoolean("ready", set.Terrain.IsReady);
            json.WriteNumber("reports", set.Terrain.ReportsSent);
            json.WriteNumber("views", set.Terrain.ViewsSent);
            json.WriteNumber("pages", set.Terrain.SyncPages);
            json.WriteNumber("blocks_received", set.Terrain.BlocksReceived);
            json.WriteNumber("blocks_stored", set.Terrain.BlocksStored);
            json.WriteNumber("stale", set.Terrain.StaleBlocks);
            json.WriteNumber("violations", set.Terrain.Violations);
            json.WriteEndObject();
        }
        json.WriteStartObject("rejections");
        foreach (var (reason, count) in stats.Rejections.OrderBy(r => r.Key))
        {
            json.WriteNumber(reason.ToString(), count);
        }

        json.WriteEndObject();

        // One array per answered intent: [client_seq, kind, sent_at, replied_at, reason, applied_op_id].
        json.WriteStartArray("intents");
        foreach (var record in stats.Records)
        {
            json.WriteStartArray();
            json.WriteNumberValue(record.ClientSeq);
            json.WriteStringValue(record.Kind.ToString());
            json.WriteNumberValue(record.SentAt);
            json.WriteNumberValue(record.RepliedAt);
            json.WriteStringValue(record.Reason.ToString());
            json.WriteNumberValue(record.AppliedOpId.Value);
            json.WriteEndArray();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}
