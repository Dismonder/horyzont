namespace Horyzont.Core.Tests.Build;

public sealed class BuildInfoTests
{
    [Fact]
    public void Commit_Generated_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Commit));
    }

    [Fact]
    public void ComputeBuildHash_SameProfile_IsStable()
    {
        Assert.Equal(BuildInfo.ComputeBuildHash("planet-v1"), BuildInfo.ComputeBuildHash("planet-v1"));
    }

    [Fact]
    public void ComputeBuildHash_OtherGeneratorProfile_Differs()
    {
        Assert.NotEqual(BuildInfo.ComputeBuildHash("planet-v1"), BuildInfo.ComputeBuildHash("planet-v2"));
    }

}
