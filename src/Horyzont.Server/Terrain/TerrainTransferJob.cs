using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Server.Terrain;

/// <summary>One manifest or barrier transfer of a client and planet; at most one is active per client and planet.</summary>
internal sealed class TerrainTransferJob
{
    /// <summary>Gets the manifest purpose: 0 startup answer, 1 interest update, 2 edit barrier.</summary>
    public byte Purpose { get; init; }

    /// <summary>Gets the planet of every entry.</summary>
    public PlanetId Planet { get; init; }

    /// <summary>Gets the operation of an edit barrier; none otherwise.</summary>
    public OpId Barrier { get; init; }

    /// <summary>Gets the candidates to examine, with the version the client reported (0 when unknown); sorted by key.</summary>
    public List<KeyValuePair<TerrainBlockKey, uint>> Candidates { get; init; } = [];

    /// <summary>Gets the entries of the manifest, sorted by key.</summary>
    public List<TerrainSyncEntry> Entries { get; } = [];

    /// <summary>Gets or sets the index of the next candidate to examine.</summary>
    public int NextCandidate { get; set; }

    /// <summary>Gets or sets the id of the transfer, assigned when it becomes active.</summary>
    public uint TransferId { get; set; }

    /// <summary>Gets or sets the index of the first entry of the next page.</summary>
    public int NextEntry { get; set; }

    /// <summary>Gets or sets the index of the next page.</summary>
    public ushort NextPage { get; set; }

    /// <summary>Gets or sets a value indicating whether the empty final page was sent.</summary>
    public bool FinalSent { get; set; }

    /// <summary>Gets a value indicating whether every candidate was examined.</summary>
    public bool IsBuilt => NextCandidate >= Candidates.Count;
}
