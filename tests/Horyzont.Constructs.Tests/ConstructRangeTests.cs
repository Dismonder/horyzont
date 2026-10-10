using Horyzont.Game.Constructs;

namespace Horyzont.Constructs.Tests;

public sealed class ConstructRangeTests
{
    [Theory]
    [InlineData(0, false, 0, 128, false)]
    [InlineData(0, false, 0, 192, false)]
    [InlineData(0, true, 10000, 128, true)]
    [InlineData(1, false, 120, 128, true)]
    [InlineData(1, false, 140, 128, false)]
    [InlineData(1, false, 140, 192, true)]
    [InlineData(1, true, 10000, 128, false)]
    public void Includes_ViewerInterest_RespectsExplicitToolsMode(int viewers, bool tools, double distance, double radius, bool expected) =>
        Assert.Equal(expected, ConstructRange.Includes(viewers, tools, distance, radius));
}
