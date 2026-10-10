using Horyzont.Game.Server;

namespace Horyzont.Server.Tests;

public sealed class TerrainFailureCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_PendingRead_ClosesOnlyAfterSuccessfulDrain(bool drained)
    {
        var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var disposed = false;
        var cleanup = TerrainFailureCleanup.RunAsync(() => read.Task, () => closed = true, () => disposed = true);
        Assert.False(cleanup.IsCompleted);
        Assert.False(closed);
        Assert.False(disposed);
        read.SetResult(drained);
        await cleanup;
        Assert.Equal(drained, closed);
        Assert.Equal(drained, disposed);
    }

    [Fact]
    public async Task RunAsync_DrainThrows_LeavesResourcesOpen()
    {
        var closed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => TerrainFailureCleanup.RunAsync(
            () => Task.FromException<bool>(new InvalidOperationException()), () => closed = true, () => closed = true));
        Assert.False(closed);
    }
}
