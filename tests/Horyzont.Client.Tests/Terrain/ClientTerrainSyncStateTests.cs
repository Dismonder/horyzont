using Horyzont.Client.Terrain;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tests.Terrain;

public sealed class ClientTerrainSyncStateTests
{
    private static readonly PlanetId Planet = new(1);
    private static readonly TerrainBlockKey A = new(Planet, 0, 1, 0, 0);
    private static readonly TerrainBlockKey B = new(Planet, 0, 2, 0, 0);

    private static TerrainSync Page(uint id, ushort page, bool last, byte purpose = 0, ulong barrier = 0, params TerrainSyncEntry[] entries) =>
        new(id, purpose, Planet, new OpId(barrier), page, last, entries);

    [Fact]
    public void IsReady_AfterTheStartupFinalPageOfEveryPlanet_AndNotBefore()
    {
        var other = new PlanetId(2);
        var state = new ClientTerrainSyncState([Planet, other]);
        Assert.False(state.IsReady);

        Assert.True(state.OnSync(Page(1, 0, true)).Completed);
        Assert.False(state.IsReady);

        Assert.True(state.OnSync(new TerrainSync(2, 0, other, default, 0, true, [])).IsValid);
        Assert.True(state.IsReady);
    }

    [Fact]
    public void IsReady_WaitsForTheBlocksTheManifestAskedFor_AndStaysReadyAfterwards()
    {
        var state = new ClientTerrainSyncState([Planet]);
        var entry = new TerrainSyncEntry(A, 3, 99);
        state.OnSync(Page(1, 0, false, 0, 0, entry));
        state.MarkPending(entry);
        state.OnSync(Page(1, 1, true));
        Assert.False(state.IsReady);

        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(A, 3, 99));
        Assert.True(state.IsReady);

        state.MarkPending(new TerrainSyncEntry(B, 1, 5));
        Assert.True(state.IsReady);
    }

    [Fact]
    public void OnSync_ProtocolBreaks_AreReported()
    {
        var state = new ClientTerrainSyncState([Planet]);
        Assert.NotNull(state.OnSync(Page(1, 1, false)).Violation);
        Assert.NotNull(state.OnSync(new TerrainSync(1, 0, new PlanetId(9), default, 0, true, [])).Violation);

        Assert.True(state.OnSync(Page(5, 0, false, 0, 0, new TerrainSyncEntry(A, 1, 1))).IsValid);
        Assert.NotNull(state.OnSync(Page(6, 0, false)).Violation);
        Assert.NotNull(state.OnSync(Page(5, 2, false)).Violation);
        Assert.NotNull(state.OnSync(Page(5, 1, false, 0, 0, new TerrainSyncEntry(A, 1, 1))).Violation);
        Assert.True(state.OnSync(Page(5, 1, true)).Completed);
        Assert.NotNull(state.OnSync(Page(6, 0, true)).Violation);
        Assert.NotNull(state.OnSync(Page(5, 0, false, 1)).Violation);
    }

    [Fact]
    public void OnSync_TransferIdsMustIncrease()
    {
        var state = new ClientTerrainSyncState([Planet]);
        Assert.True(state.OnSync(Page(4, 0, true)).IsValid);
        Assert.NotNull(state.OnSync(Page(4, 0, true, 1)).Violation);
        Assert.True(state.OnSync(Page(5, 0, true, 1)).IsValid);
    }

    [Fact]
    public void OnSync_BarrierCompletesAcrossPages_AndIsRemembered()
    {
        var state = new ClientTerrainSyncState([Planet]);
        state.OnSync(Page(1, 0, true));
        OpId? seen = null;
        state.BarrierCompleted += (op, _) => seen = op;

        state.OnSync(Page(2, 0, false, 2, 7, new TerrainSyncEntry(A, 2, 1)));
        Assert.False(state.TryGetBarrier(new OpId(7), out _));
        state.OnSync(Page(2, 1, false, 2, 7, new TerrainSyncEntry(B, 4, 2)));
        state.OnSync(Page(2, 2, true, 2, 7));

        Assert.Equal(new OpId(7), seen);
        Assert.True(state.TryGetBarrier(new OpId(7), out var entries));
        Assert.Equal([A, B], entries.Select(e => e.Key));
        state.ForgetBarrier(new OpId(7));
        Assert.False(state.TryGetBarrier(new OpId(7), out _));
    }

    [Fact]
    public void OnData_PendingManifest_ChecksVersionAndHash()
    {
        var state = new ClientTerrainSyncState([Planet]);
        state.MarkPending(new TerrainSyncEntry(A, 3, 99));

        Assert.Equal(TerrainDataVerdict.Stale, state.OnData(A, 2, 1));
        Assert.True(state.IsPending(A));
        Assert.Equal(TerrainDataVerdict.Violation, state.OnData(A, 3, 100));
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(A, 4, 1));
        Assert.False(state.IsPending(A));
    }

    [Fact]
    public void OnData_ManifestMayDowngrade_ButTheSessionNeverGoesBackOutsideOne()
    {
        var state = new ClientTerrainSyncState([Planet]);
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(A, 5, 1));
        Assert.Equal(TerrainDataVerdict.Stale, state.OnData(A, 4, 1));

        state.MarkPending(new TerrainSyncEntry(A, 0, 7));
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(A, 0, 7));
    }
}
