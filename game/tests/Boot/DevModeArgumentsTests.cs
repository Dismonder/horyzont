using GdUnit4;

using Horyzont.Game.Boot;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class DevModeArgumentsTests
{
    [TestCase]
    public void GalaxySeed_NotGiven_IsDefault()
    {
        AssertThat(new DevModeArguments(["--planet-preview"]).GalaxySeed()).IsEqual(DevModeArguments.DefaultGalaxySeed);
    }

    [TestCase]
    public void GalaxySeed_HexWithAndWithoutPrefix_Parses()
    {
        AssertThat(new DevModeArguments(["--seed", "ff"]).GalaxySeed()).IsEqual(255UL);
        AssertThat(new DevModeArguments(["--seed", "0xFFFFFFFFFFFFFFFF"]).GalaxySeed()).IsEqual(ulong.MaxValue);
    }

    [TestCase]
    public void GalaxySeed_Invalid_Throws()
    {
        foreach (var bad in new[] { "xyz", "", "0x", "1FFFFFFFFFFFFFFFF", "-1", "12 34" })
        {
            var args = new DevModeArguments(["--seed", bad]);
            AssertThrown(() => args.GalaxySeed()).IsInstanceOf<ArgumentException>();
        }
    }

    [TestCase]
    public void ValueOf_MissingValue_Throws()
    {
        AssertThrown(() => new DevModeArguments(["--seed"]).ValueOf("--seed")).IsInstanceOf<ArgumentException>();
        AssertThrown(() => new DevModeArguments(["--seed", "--screenshots", "x"]).ValueOf("--seed")).IsInstanceOf<ArgumentException>();
        AssertThat(new DevModeArguments(["--other"]).ValueOf("--seed") is null).IsTrue();
    }

    [TestCase]
    public void Directory_MustExist_ValidatesAndCreateMode_Creates()
    {
        var existing = Path.GetTempPath();
        var missing = Path.Combine(Path.GetTempPath(), $"horyzont-devmode-{Guid.NewGuid():N}");

        AssertThat(new DevModeArguments(["--out", existing]).Directory("--out", mustExist: true)).IsEqual(Path.GetFullPath(existing));
        AssertThrown(() => new DevModeArguments(["--out", missing]).Directory("--out", mustExist: true)).IsInstanceOf<ArgumentException>();

        try
        {
            AssertThat(new DevModeArguments(["--out", missing]).Directory("--out", mustExist: false)).IsEqual(Path.GetFullPath(missing));
            AssertThat(Directory.Exists(missing)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(missing))
            {
                Directory.Delete(missing);
            }
        }
    }

    [TestCase]
    public void Number_OutOfRangeOrNotFinite_Throws()
    {
        AssertThat(new DevModeArguments(["--lod-distance", "96.5"]).Number("--lod-distance", 16f, 4096f)!.Value).IsEqual(96.5f);
        foreach (var bad in new[] { "abc", "NaN", "Infinity", "1", "99999" })
        {
            var args = new DevModeArguments(["--lod-distance", bad]);
            AssertThrown(() => args.Number("--lod-distance", 16f, 4096f)).IsInstanceOf<ArgumentException>();
        }
    }
}
