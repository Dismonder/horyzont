using Horyzont.Constructs.Meshing;
using Horyzont.Game.Constructs;
using Horyzont.Game.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Game.Tests.Constructs;

public sealed class MeshWorkerPoolTests : EngineTestBase
{
    [EngineTest]
    public async Task ShutdownTimeout_LastWorkerExit_CleansUpExactlyOnce()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var catalog = BlockContentLoader.Load();
        var state = ConstructTestWorld.NewState(catalog);
        var table = BlockRenderTable.Build(state.BlockTypes.Entries, catalog);
        using var pool = new MeshWorkerPool(table, 2, 3, NullLogger.Instance, _ =>
        {
            entered.Set();
            release.Wait();
        });
        pool.TryRentSlot(out var slot);
        pool.Submit(slot!);
        try
        {
            await WaitUntil(() => entered.IsSet, 600, "worker to enter stalled job");
            EngineAssert.True(!pool.Shutdown(TimeSpan.Zero), "zero timeout returns while the worker is stalled");
            pool.Dispose();
            EngineAssert.Equal(0, pool.CleanupCount, "active worker buffers cannot be disposed");
            EngineAssert.True(!pool.TryTakeResult(out _), "results detached immediately");
        }
        finally
        {
            release.Set();
        }

        await WaitUntil(() => pool.AliveThreads == 0, 600, "last worker to exit");
        EngineAssert.Equal(1, pool.CleanupCount, "cleanup after the last worker exits");
        EngineAssert.True(slot!.Prepared is null, "prepared arrays released");
        pool.Dispose();
        EngineAssert.Equal(1, pool.CleanupCount, "repeated disposal does not repeat cleanup");
    }
}
