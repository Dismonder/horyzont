using Horyzont.Client.Navigation;

namespace Horyzont.Client.Tests.Navigation;

public sealed class DistanceFormatterTests
{
    private static string Format(double meters, char separator = '.')
    {
        Span<char> buffer = stackalloc char[16];
        Assert.True(DistanceFormatter.TryFormat(meters, buffer, out var written, separator));
        return new string(buffer[..written]);
    }

    [Theory]
    [InlineData(0, "0 m")]
    [InlineData(850.2, "850 m")]
    [InlineData(999.4, "999 m")]
    [InlineData(999.6, "1.0 km")]
    [InlineData(1000, "1.0 km")]
    [InlineData(12_345, "12.3 km")]
    [InlineData(99_949, "99.9 km")]
    [InlineData(99_960, "100 km")]
    [InlineData(123_456, "123 km")]
    [InlineData(480_000, "480 km")]
    public void DistanceFormatter_Units(double meters, string expected) => Assert.Equal(expected, Format(meters));

    [Fact]
    public void DistanceFormatter_DecimalSeparatorOfTheLanguage()
    {
        Assert.Equal("12,3 km", Format(12_345, ','));
        Assert.Equal("850 m", Format(850, ','));
    }

    [Fact]
    public void DistanceFormatter_InvalidOrTooSmallBuffer_Fails()
    {
        Span<char> small = stackalloc char[4];
        Assert.False(DistanceFormatter.TryFormat(double.NaN, small, out var written));
        Assert.Equal(0, written);
        Assert.False(DistanceFormatter.TryFormat(-1, small, out _));
        Assert.False(DistanceFormatter.TryFormat(double.PositiveInfinity, small, out _));
        Assert.False(DistanceFormatter.TryFormat(12_345, small, out _));
    }

    [Fact]
    public void DistanceFormatter_AllocatesZeroBytes()
    {
        Span<char> buffer = stackalloc char[16];
        Assert.True(DistanceFormatter.TryFormat(12_345, buffer, out _, ','));

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var ok = true;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++)
            {
                ok &= DistanceFormatter.TryFormat(i * 37.5, buffer, out _, ',');
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(ok);
        }

        var allocated = smallest;
        Assert.Equal(0, allocated);
    }
}
