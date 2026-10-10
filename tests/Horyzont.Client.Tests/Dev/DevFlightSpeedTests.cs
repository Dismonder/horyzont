using Horyzont.Client.Dev;

namespace Horyzont.Client.Tests.Dev;

public sealed class DevFlightSpeedTests
{
    [Theory]
    [InlineData(0f, 5f)]
    [InlineData(-50f, 5f)]
    [InlineData(10f, 5f)]
    [InlineData(100f, 50f)]
    [InlineData(1000f, 500f)]
    [InlineData(4000f, 2000f)]
    [InlineData(100_000f, 2000f)]
    [InlineData(float.NaN, 5f)]
    [InlineData(float.PositiveInfinity, 5f)]
    public void DevFlightSpeed_At_IsHalfTheHeightClamped(float height, float expected) =>
        Assert.Equal(expected, DevFlightSpeed.At(height));
}
