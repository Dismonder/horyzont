using Horyzont.Core.Hashing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-35, D22 and A0: a world without the new state hashes as it did before the package.</summary>
public sealed class DeathHashTests
{
    [Fact]
    public void NoNewState_PreservesPreviousHash()
    {
        var state = DeathHashFixture.Create();

        Assert.Equal(DeathHashFixture.Pin, StateHasher.Hash(state));
    }
}
