namespace Horyzont.Constructs.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void AssemblyMarker_IsLoadable() => Assert.NotNull(typeof(AssemblyMarker).Assembly);
}
