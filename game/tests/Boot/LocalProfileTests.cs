using GdUnit4;

using Horyzont.Game.Boot;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class LocalProfileTests
{
    [TestCase]
    public void LoadOrCreate_SecondCall_ReturnsTheSameIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"), "profile.json");

        var first = LocalProfile.LoadOrCreate(path);
        var second = LocalProfile.LoadOrCreate(path);

        AssertThat(first.Identity).IsEqual(second.Identity);
        AssertThat(first.Identity.Value).IsNotEqual(0UL);
        AssertThat(second.DisplayName).IsEqual(first.DisplayName);
    }

    [TestCase]
    public void LoadOrCreate_DamagedFile_IsReplaced()
    {
        var path = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"), "profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var profile = LocalProfile.LoadOrCreate(path);

        AssertThat(profile.Identity.Value).IsNotEqual(0UL);
        AssertThat(LocalProfile.LoadOrCreate(path).Identity).IsEqual(profile.Identity);
    }
}
