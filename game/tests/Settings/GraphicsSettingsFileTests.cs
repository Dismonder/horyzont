using GdUnit4;

using Horyzont.Client.Settings;
using Horyzont.Game.Settings;

using Microsoft.Extensions.Logging.Abstractions;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Settings;

[TestSuite]
public class GraphicsSettingsFileTests
{
    private static string NewPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-graphics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "graphics.cfg");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Load_MissingFile_ReturnsDefaults()
    {
        AssertThat(GraphicsSettingsFile.Load(NewPath(), NullLogger.Instance)).IsEqual(GraphicsSettings.Default);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SaveThenLoad_RoundTrips()
    {
        var path = NewPath();
        var settings = new GraphicsSettings { Mode = UpscaleMode.Fsr2, RenderScale = 0.67f, Sharpness = 1.25f };

        AssertThat(GraphicsSettingsFile.Save(path, settings, NullLogger.Instance)).IsTrue();
        var loaded = GraphicsSettingsFile.Load(path, NullLogger.Instance);

        AssertThat(loaded.Mode).IsEqual(UpscaleMode.Fsr2);
        AssertThat(loaded.RenderScale).IsEqual(0.67f);
        AssertThat(loaded.Sharpness).IsEqual(1.25f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Load_GarbageFile_ReturnsDefaults()
    {
        var path = NewPath();
        File.WriteAllText(path, "this is [[ not = a { config file\n\0\0");

        AssertThat(GraphicsSettingsFile.Load(path, NullLogger.Instance)).IsEqual(GraphicsSettings.Default);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Load_BadValues_FallBackPerKey()
    {
        var path = NewPath();
        File.WriteAllText(path, "[graphics]\nupscale=\"fsr9\"\nrender_scale=7.0\nfsr_sharpness=\"loud\"\n");

        var loaded = GraphicsSettingsFile.Load(path, NullLogger.Instance);

        AssertThat(loaded.Mode).IsEqual(UpscaleMode.Off);
        AssertThat(loaded.RenderScale).IsEqual(1f);
        AssertThat(loaded.Sharpness).IsEqual(0.2f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Load_GoodAndBadKeys_KeepsTheGoodOnes()
    {
        var path = NewPath();
        File.WriteAllText(path, "[graphics]\nupscale=\"fsr1\"\nrender_scale=0.1\n");

        var loaded = GraphicsSettingsFile.Load(path, NullLogger.Instance);

        AssertThat(loaded.Mode).IsEqual(UpscaleMode.Fsr1);
        AssertThat(loaded.RenderScale).IsEqual(1f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Save_UnwritablePath_ReturnsFalse()
    {
        var blocker = NewPath();
        File.WriteAllText(blocker, "x");

        AssertThat(GraphicsSettingsFile.Save(Path.Combine(blocker, "sub", "graphics.cfg"), GraphicsSettings.Default, NullLogger.Instance)).IsFalse();
    }
}
