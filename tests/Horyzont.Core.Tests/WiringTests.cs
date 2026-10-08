using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Core.Tests;

public sealed class WiringTests
{
    [Fact]
    public void LibraryAssembly_Loads_HasExpectedName()
    {
        var assembly = typeof(Horyzont.Core.AssemblyMarker).Assembly;

        Assert.Equal("Horyzont.Core", assembly.GetName().Name);
    }

    [Fact]
    public void FakeTimeProvider_AdvanceOneSecond_ElapsedIsOneSecond()
    {
        var time = new FakeTimeProvider();
        var start = time.GetTimestamp();

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), time.GetElapsedTime(start));
    }
}
