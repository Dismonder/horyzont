using Godot;

using Horyzont.Client.Terrain;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client.Terrain;

/// <summary>
/// The local, provisional effect of the terrain tool on the own terrain of a remote client (K3-16). Before an edit changes a block,
/// the authoritative block is kept as its base; a refusal, a timeout or a disconnect puts the base back and applies the edits that
/// remain again in the order they were sent, instead of undoing a sphere. An accepted edit stays until the barrier of its operation
/// says that the final blocks arrived: only then the base (now the data of the server) is the truth. Nothing here reaches the cache,
/// the journal or the versions. Main thread only; used by the tool node as <see cref="IEditPrediction"/> and reply observer.
/// </summary>
public sealed class RemoteTerrainPrediction : IEditPrediction, IIntentReplyObserver
{
    /// <summary>How long an accepted edit waits for its barrier before the server is asked for the blocks again.</summary>
    public static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(5);

    private readonly OwnTerrainProvider _terrain;
    private readonly ILogger _logger;
    private readonly EditPredictionLedger _ledger;
    private readonly Dictionary<TerrainBlockKey, BaseBlock> _bases = [];
    private readonly Dictionary<PlanetId, PlanetParams> _planets = [];
    private readonly List<TerrainBlockKey> _keys = [];
    private readonly List<PredictedEdit> _scratchEdits = [];
    private readonly List<PredictedEdit> _expired = [];
    private ClientTerrainSyncState? _sync;
    private TerrainMaterialMap? _materials;

    /// <summary>Creates the prediction.</summary>
    /// <param name="terrain">The own terrain of the client.</param>
    /// <param name="time">Clock of the barrier timeout.</param>
    /// <param name="logger">Logger.</param>
    public RemoteTerrainPrediction(OwnTerrainProvider terrain, TimeProvider time, ILogger<RemoteTerrainPrediction> logger)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _terrain = terrain;
        _logger = logger;
        _ledger = new EditPredictionLedger(time);
    }

    /// <summary>Gets the number of edits that wait for the server.</summary>
    public int Count => _ledger.Count;

    /// <summary>Starts a session: the planets and materials of the hello, the sync state that tells which blocks are awaited.</summary>
    /// <param name="sync">The sync state of the session.</param>
    /// <param name="hello">The server hello.</param>
    public void StartSession(ClientTerrainSyncState sync, ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(hello);
        RevertAll();
        _sync = sync;
        _planets.Clear();
        foreach (var planet in hello.Planets)
        {
            _planets[planet.Id] = planet;
        }

        _materials = TerrainMaterialMap.Build(hello.Materials);
    }

    /// <summary>Whether a pending edit touches the block (a predicted block is not compared with the generator).</summary>
    /// <param name="key">LOD zero key.</param>
    /// <returns>True while an edit that touches it waits.</returns>
    public bool Touches(TerrainBlockKey key) => _bases.ContainsKey(key);

    /// <inheritdoc />
    public void Begin(uint clientSeq, Intent intent, in ToolPreview preview)
    {
        if (intent is not TerrainToolIntent tool || preview.Kind != ToolPreviewKind.Sphere || _sync is not { IsReady: true } || _materials is null
            || !_planets.TryGetValue(tool.Planet, out var planet) || !_terrain.TryGetNode(tool.Planet, out var node))
        {
            return;
        }

        TerrainMath.AffectedBlocks(tool.Planet, preview.Center, tool.Radius, planet.VoxelSizeMeters, TerrainRules.EditMarginVoxels, _keys);
        if (_keys.Count == 0 || _ledger.Count >= EditPredictionLedger.MaxEdits)
        {
            return;
        }

        // A block that is not loaded, or whose data the manifest still awaits, is not shaped locally: the server alone decides.
        var first = BlockOrigin(_keys[0]);
        var last = BlockOrigin(_keys[^1]) + new Vector3I(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        if (!node.Tool.IsAreaEditable(new Aabb(first, last - first)))
        {
            return;
        }

        foreach (var key in _keys)
        {
            if (_sync.IsPending(key))
            {
                return;
            }
        }

        byte texture;
        try
        {
            texture = tool.Mode == TerrainEditMode.Add ? _materials.ToTextureIndex(tool.MaterialId) : (byte)0;
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        var edit = new PredictedEdit(clientSeq, tool.Planet, preview.Center, tool.Radius, tool.Mode, tool.MaterialId, [.. _keys]);
        if (!_ledger.TryBegin(edit))
        {
            return;
        }

        foreach (var key in edit.Keys)
        {
            if (!_bases.TryGetValue(key, out var held))
            {
                held = new BaseBlock();
                _bases.Add(key, held);
                var buffer = new VoxelBuffer();
                buffer.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
                node.Tool.Copy(BlockOrigin(key), buffer, TerrainChannels.UsedChannelsMask);
                held.Voxels = buffer;
                _terrain.Cache?.Pin(key);
            }

            held.Users++;
        }

        TerrainSphereEdit.Apply(node.Tool, edit.Mode, TerrainVectors.ToGodot(edit.Center), edit.Radius, texture);
    }

    /// <inheritdoc />
    /// <remarks>Acceptance is noted by <see cref="NoteReply"/>; the edit stays until its barrier arrives.</remarks>
    public void Commit(uint clientSeq)
    {
    }

    /// <inheritdoc />
    public void Revert(uint clientSeq)
    {
        if (_ledger.Remove(clientSeq) is { } edit)
        {
            Settle(edit);
        }
    }

    /// <inheritdoc />
    public void NoteReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Reason == RejectReason.None && reply.AppliedOpId.Value != 0
            && _ledger.NoteAccepted(reply.ClientSeq, reply.AppliedOpId, out var edit) && edit is not null)
        {
            // The blocks of the edit arrived before the reply (the barrier was seen first).
            Settle(edit);
        }
    }

    /// <summary>The barrier of an operation arrived: the final blocks of the edit were stored, the overlay is no longer needed.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="entries">The final blocks of the edit.</param>
    public void OnBarrier(OpId operation, IReadOnlyList<TerrainSyncEntry> entries)
    {
        if (_ledger.Complete(operation) is { } edit)
        {
            Settle(edit);
        }
    }

    /// <summary>Takes back every edit, newest first (the session ended).</summary>
    public void RevertAll()
    {
        _ledger.TakeAll(_expired);
        foreach (var edit in _expired)
        {
            Settle(edit);
        }

        _expired.Clear();
    }

    /// <summary>Takes back the accepted edits whose barrier did not come in time and asks the server for their blocks.</summary>
    /// <param name="scratch">Reusable list for the keys.</param>
    /// <param name="request">Asks the server to send the blocks again.</param>
    public void Expire(List<TerrainBlockKey> scratch, Action<IReadOnlyCollection<TerrainBlockKey>> request)
    {
        _ledger.TakeExpired(BarrierTimeout, _expired);
        if (_expired.Count == 0)
        {
            return;
        }

        scratch.Clear();
        foreach (var edit in _expired)
        {
            Settle(edit);
            scratch.AddRange(edit.Keys);
        }

        _expired.Clear();
        request(scratch);
    }

    /// <summary>
    /// A block of the server arrived. When an edit that touches it waits, the block becomes the new base and the edits are applied over
    /// the bases again; the caller then does not paste it.
    /// </summary>
    /// <param name="node">Terrain of the planet.</param>
    /// <param name="key">The block.</param>
    /// <param name="authoritative">The decoded block.</param>
    /// <returns>True when the prediction put the block into the scene.</returns>
    public bool TryApplyAuthoritative(PlanetTerrain node, TerrainBlockKey key, VoxelBuffer authoritative)
    {
        if (!_bases.TryGetValue(key, out var held))
        {
            return false;
        }

        held.Voxels!.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        for (var channel = 0u; channel < (uint)VoxelBuffer.ChannelId.MaxChannels; channel++)
        {
            held.Voxels.SetChannelDepth(channel, authoritative.GetChannelDepth(channel));
            held.Voxels.CopyChannelFrom(authoritative, channel);
        }

        Rebuild([key], node);
        return true;
    }

    /// <summary>An edit left the ledger: put the bases of its blocks back and apply the edits that remain over them.</summary>
    private void Settle(PredictedEdit edit)
    {
        if (!_terrain.TryGetNode(edit.Planet, out var node))
        {
            ReleaseBases(edit);
            return;
        }

        Rebuild(edit.Keys, node);
        ReleaseBases(edit);
    }

    private void Rebuild(IReadOnlyList<TerrainBlockKey> start, PlanetTerrain node)
    {
        // The edits that remain and the blocks they share form one group: a sphere cannot be applied to a single block of it.
        var group = new HashSet<TerrainBlockKey>(start);
        var edits = new List<PredictedEdit>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var key in group.ToArray())
            {
                _ledger.CollectTouching(key, _scratchEdits);
                foreach (var other in _scratchEdits)
                {
                    if (!edits.Contains(other))
                    {
                        edits.Add(other);
                        foreach (var shared in other.Keys)
                        {
                            changed |= group.Add(shared);
                        }
                    }
                }
            }
        }

        edits.Sort(static (a, b) => a.ClientSeq.CompareTo(b.ClientSeq));
        foreach (var key in group)
        {
            if (_bases.TryGetValue(key, out var held) && held.Voxels is not null)
            {
                ClientTerrainReplicator.Paste(node.Tool, key, held.Voxels);
            }
        }

        foreach (var edit in edits)
        {
            byte texture;
            try
            {
                texture = edit.Mode == TerrainEditMode.Add ? _materials!.ToTextureIndex(edit.MaterialId) : (byte)0;
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            TerrainSphereEdit.Apply(node.Tool, edit.Mode, TerrainVectors.ToGodot(edit.Center), edit.Radius, texture);
        }

    }

    private void ReleaseBases(PredictedEdit edit)
    {
        foreach (var key in edit.Keys)
        {
            if (!_bases.TryGetValue(key, out var held))
            {
                continue;
            }

            if (--held.Users > 0)
            {
                continue;
            }

            held.Voxels?.Dispose();
            _bases.Remove(key);
            _terrain.Cache?.Unpin(key);
        }
    }

    private static Vector3I BlockOrigin(TerrainBlockKey key) =>
        new(key.X * TerrainMath.BlockSize, key.Y * TerrainMath.BlockSize, key.Z * TerrainMath.BlockSize);

    private sealed class BaseBlock
    {
        public VoxelBuffer? Voxels { get; set; }

        public int Users { get; set; }
    }
}
