namespace Horyzont.Core.Protocol;

/// <summary>
/// Message codes. Ranges: 1–20 K1, 21–29 K2–K4, 30–39 Space, 40–49 Survival, 50–59 Combat, 60–69 EA content, 70–79 Release.
/// </summary>
public enum MessageType : byte
{
    /// <summary>Client → server, reliable.</summary>
    ClientHello = 1,

    /// <summary>Server → client, reliable.</summary>
    ServerHello = 2,

    /// <summary>Server → client, reliable.</summary>
    HandshakeReject = 3,

    /// <summary>Client → server, reliable.</summary>
    ClientReady = 4,

    /// <summary>Client → server, reliable.</summary>
    TerrainCacheReport = 5,

    /// <summary>Server → client, reliable.</summary>
    TerrainBlockData = 6,

    /// <summary>Client → server, reliable.</summary>
    TerrainChecksums = 7,

    /// <summary>Server → client, reliable.</summary>
    ConstructState = 8,

    /// <summary>Server → client, reliable.</summary>
    ConstructRemoved = 9,

    /// <summary>Server → client, reliable.</summary>
    OperationApplied = 10,

    /// <summary>Client → server, reliable.</summary>
    IntentSubmit = 11,

    /// <summary>Server → client, reliable.</summary>
    IntentReply = 12,

    /// <summary>Client → server, unreliable.</summary>
    InputBatch = 13,

    /// <summary>Server → client, unreliable.</summary>
    Snapshot = 14,

    /// <summary>Client → server, unreliable.</summary>
    SnapshotAck = 15,

    /// <summary>Server → client, reliable.</summary>
    EntitySpawn = 16,

    /// <summary>Server → client, reliable.</summary>
    EntityDespawn = 17,

    /// <summary>Client → server, unreliable.</summary>
    Ping = 18,

    /// <summary>Server → client, unreliable.</summary>
    Pong = 19,

    /// <summary>Both directions, reliable.</summary>
    Disconnect = 20,

    /// <summary>Client to server, reliable; visible entity state repair.</summary>
    ResyncRequest = 21,

    /// <summary>Server availability, reliable.</summary>
    ServerStatus = 22,

    /// <summary>Pre-hello availability refusal, reliable.</summary>
    JoinUnavailable = 23,

    /// <summary>Server to client, reliable terrain manifest or edit barrier.</summary>
    TerrainSync = 24,

    /// <summary>Client to server, reliable terrain viewer update.</summary>
    TerrainView = 25,
}
