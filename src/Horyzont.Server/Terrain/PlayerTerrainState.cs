using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Server.Terrain;

/// <summary>Terrain replication state of one remote client.</summary>
internal sealed class PlayerTerrainState(PlayerId player)
{
    /// <summary>Gets the player.</summary>
    public PlayerId Player { get; } = player;

    /// <summary>Gets or sets a value indicating whether the client said ready and receives data.</summary>
    public bool Ready { get; set; }

    /// <summary>Gets or sets the last accepted viewer report.</summary>
    public TerrainViewState? View { get; set; }

    /// <summary>Gets or sets the viewers for which the last interest update was computed.</summary>
    public TerrainViewState? InterestView { get; set; }

    /// <summary>Gets or sets the sequence of the last accepted viewer report.</summary>
    public uint LastViewSeq { get; set; }

    /// <summary>Gets or sets a value indicating whether the client may lack data (new viewers or a dropped task).</summary>
    public bool InterestDirty { get; set; }

    /// <summary>Gets the cached versions the client reported, per planet, until the startup transfer is built.</summary>
    public Dictionary<PlanetId, SortedDictionary<TerrainBlockKey, uint>> Reports { get; } = [];

    /// <summary>Gets what the client is known to hold: the version and hash that were sent or confirmed.</summary>
    public Dictionary<TerrainBlockKey, (uint Version, ulong Hash)> Known { get; } = [];

    /// <summary>Gets the transfers per planet.</summary>
    public Dictionary<PlanetId, PlanetTransfers> Transfers { get; } = [];

    /// <summary>Gets the open edit barriers.</summary>
    public List<TerrainBarrier> Barriers { get; } = [];

    /// <summary>Gets the generator checks waiting for the next ticks.</summary>
    public Queue<KeyValuePair<TerrainBlockKey, ulong>> ChecksumWork { get; } = new();

    /// <summary>Gets the blocks the client asked for again (a hash it could not accept); at most one batch of a report is kept.</summary>
    public Queue<TerrainBlockKey> ResendWork { get; } = new();

    /// <summary>Gets or sets the id of the last transfer.</summary>
    public uint LastTransferId { get; set; }

    /// <summary>Gets or sets the timestamp of the last accepted checksum message.</summary>
    public long LastChecksumAt { get; set; }

    /// <summary>Gets or sets a value indicating whether a checksum message was accepted before.</summary>
    public bool HadChecksum { get; set; }

    /// <summary>Gets or sets the timestamp of the last logged violation.</summary>
    public long LastViolationLogAt { get; set; }

    /// <summary>Gets or sets a value indicating whether a violation was logged before.</summary>
    public bool HadViolationLog { get; set; }

    /// <summary>Gets a value indicating whether a startup or interest manifest is still being sent; data waits for it.</summary>
    public bool ManifestPending
    {
        get
        {
            foreach (var transfers in Transfers.Values)
            {
                if (transfers.Active is { Purpose: < 2 } || transfers.Pending.Exists(static j => j.Purpose < 2))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>The transfers of one planet of one client.</summary>
internal sealed class PlanetTransfers
{
    /// <summary>Gets or sets the transfer being sent.</summary>
    public TerrainTransferJob? Active { get; set; }

    /// <summary>Gets the transfers waiting for the active one; barriers go first.</summary>
    public List<TerrainTransferJob> Pending { get; } = [];
}

/// <summary>An accepted edit of a client whose final block versions are confirmed once the data was sent.</summary>
internal sealed class TerrainBarrier(OpId operation, PlanetId planet, SortedDictionary<TerrainBlockKey, uint> required)
{
    /// <summary>Gets the operation.</summary>
    public OpId Operation { get; } = operation;

    /// <summary>Gets the planet.</summary>
    public PlanetId Planet { get; } = planet;

    /// <summary>Gets the lowest version of each block that completes the edit.</summary>
    public SortedDictionary<TerrainBlockKey, uint> Required { get; } = required;
}
