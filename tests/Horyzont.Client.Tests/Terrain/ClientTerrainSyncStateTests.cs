using Horyzont.Client.Terrain;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tests.Terrain;

public sealed class ClientTerrainSyncStateTests
{
    private static readonly PlanetId _planet = new(1);
    private static readonly TerrainBlockKey _a = new(_planet, 0, 1, 0, 0);
    private static readonly TerrainBlockKey _b = new(_planet, 0, 2, 0, 0);

    private static TerrainSync Page(uint id, ushort page, bool last, byte purpose = 0, ulong barrier = 0, params TerrainSyncEntry[] entries) =>
        new(id, purpose, _planet, new OpId(barrier), page, last, entries);

    [Fact]
    public void IsReady_AfterTheStartupFinalPageOfEveryPlanet_AndNotBefore()
    {
        var other = new PlanetId(2);
        var state = new ClientTerrainSyncState([_planet, other]);
        Assert.False(state.IsReady);

        Assert.True(state.OnSync(Page(1, 0, true)).Completed);
        Assert.False(state.IsReady);

        Assert.True(state.OnSync(new TerrainSync(2, 0, other, default, 0, true, [])).IsValid);
        Assert.True(state.IsReady);
    }

    [Fact]
    public void IsReady_WaitsForTheBlocksTheManifestAskedFor_AndStaysReadyAfterwards()
    {
        var state = new ClientTerrainSyncState([_planet]);
        var entry = new TerrainSyncEntry(_a, 3, 99);
        state.OnSync(Page(1, 0, false, 0, 0, entry));
        state.MarkPending(entry);
        state.OnSync(Page(1, 1, true));
        Assert.False(state.IsReady);

        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(_a, 3, 99));
        Assert.True(state.IsReady);

        state.MarkPending(new TerrainSyncEntry(_b, 1, 5));
        Assert.True(state.IsReady);
    }

    [Fact]
    public void MarkPending_BlockAlreadySeenAtANewerVersion_IsNotAwaited()
    {
        var state = new ClientTerrainSyncState([_planet]);
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(_a, 2, 7));

        // The live block v2 overtook the manifest entry v1: nothing will be sent for v1.
        Assert.False(state.MarkPending(new TerrainSyncEntry(_a, 1, 5)));
        Assert.False(state.MarkPending(new TerrainSyncEntry(_a, 0, 5)));
        Assert.False(state.MarkPending(new TerrainSyncEntry(_a, 2, 7)));
        Assert.False(state.IsPending(_a));
        Assert.True(state.MarkPending(new TerrainSyncEntry(_a, 3, 9)));
        Assert.True(state.IsPending(_a));
    }

    [Fact]
    public void IsReady_LiveBlockOvertookItsManifestEntry_DoesNotWaitForever()
    {
        var state = new ClientTerrainSyncState([_planet]);
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(_a, 2, 7));
        var entry = new TerrainSyncEntry(_a, 1, 5);
        state.OnSync(Page(1, 0, true, 0, 0, entry));
        state.MarkPending(entry);

        Assert.True(state.IsReady);
    }

    [Fact]
    public void OnSync_ProtocolBreaks_AreReported()
    {
        var state = new ClientTerrainSyncState([_planet]);
        Assert.NotNull(state.OnSync(Page(1, 1, false)).Violation);
        Assert.NotNull(state.OnSync(new TerrainSync(1, 0, new PlanetId(9), default, 0, true, [])).Violation);

        Assert.True(state.OnSync(Page(5, 0, false, 0, 0, new TerrainSyncEntry(_a, 1, 1))).IsValid);
        Assert.NotNull(state.OnSync(Page(6, 0, false)).Violation);
        Assert.NotNull(state.OnSync(Page(5, 2, false)).Violation);
        Assert.NotNull(state.OnSync(Page(5, 1, false, 0, 0, new TerrainSyncEntry(_a, 1, 1))).Violation);
        Assert.True(state.OnSync(Page(5, 1, true)).Completed);
        Assert.NotNull(state.OnSync(Page(6, 0, true)).Violation);
        Assert.NotNull(state.OnSync(Page(5, 0, false, 1)).Violation);
    }

    [Fact]
    public void OnSync_TransferIdsMustIncrease()
    {
        var state = new ClientTerrainSyncState([_planet]);
        Assert.True(state.OnSync(Page(4, 0, true)).IsValid);
        Assert.NotNull(state.OnSync(Page(4, 0, true, 1)).Violation);
        Assert.True(state.OnSync(Page(5, 0, true, 1)).IsValid);
    }

    [Fact]
    public void OnSync_BarrierCompletesAcrossPages_AndIsRemembered()
    {
        var state = new ClientTerrainSyncState([_planet]);
        state.OnSync(Page(1, 0, true));
        OpId? seen = null;
        state.BarrierCompleted += (op, _) => seen = op;

        state.OnSync(Page(2, 0, false, 2, 7, new TerrainSyncEntry(_a, 2, 1)));
        Assert.False(state.TryGetBarrier(new OpId(7), out _));
        state.OnSync(Page(2, 1, false, 2, 7, new TerrainSyncEntry(_b, 4, 2)));
        state.OnSync(Page(2, 2, true, 2, 7));

        Assert.Equal(new OpId(7), seen);
        Assert.True(state.TryGetBarrier(new OpId(7), out var entries));
        Assert.Equal([_a, _b], entries.Select(e => e.Key));
        state.ForgetBarrier(new OpId(7));
        Assert.False(state.TryGetBarrier(new OpId(7), out _));
    }

    [Fact]
    public void OnData_PendingManifest_ChecksVersionAndHash()
    {
        var state = new ClientTerrainSyncState([_planet]);
        state.MarkPending(new TerrainSyncEntry(_a, 3, 99));

        Assert.Equal(TerrainDataVerdict.Stale, state.OnData(_a, 2, 1));
        Assert.True(state.IsPending(_a));
        Assert.Equal(TerrainDataVerdict.Violation, state.OnData(_a, 3, 100));
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(_a, 4, 1));
        Assert.False(state.IsPending(_a));
    }

    [Fact]
    public void OnData_ManifestMayDowngrade_OnlyBeforeTheSessionSawTheBlock()
    {
        var state = new ClientTerrainSyncState([_planet]);
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(_a, 5, 1));
        Assert.Equal(TerrainDataVerdict.Stale, state.OnData(_a, 4, 1));

        // A manifest entry older than what the session already saw is a snapshot taken before a live edit: it is not awaited.
        Assert.False(state.MarkPending(new TerrainSyncEntry(_a, 0, 7)));
        Assert.Equal(TerrainDataVerdict.Stale, state.OnData(_a, 0, 7));

        // Before any block of the session a manifest may name a lower version than the cache holds: the server state wins.
        Assert.True(state.MarkPending(new TerrainSyncEntry(_b, 0, 7)));
        Assert.Equal(TerrainDataVerdict.Accept, state.OnData(_b, 0, 7));
    }
}
