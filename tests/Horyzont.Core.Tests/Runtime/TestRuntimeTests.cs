namespace Horyzont.Core.Tests.Runtime;

public sealed class TestRuntimeTests
{
    [Fact]
    public void TestHost_TieredCompilation_IsDisabledForDeterministicAllocationCounts()
    {
        Assert.Equal("false", AppContext.GetData("System.Runtime.TieredCompilation")?.ToString(), ignoreCase: true);
    }
}
