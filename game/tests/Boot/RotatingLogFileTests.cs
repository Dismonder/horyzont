using GdUnit4;

using Horyzont.Game.Logging;

using Microsoft.Extensions.Logging;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class RotatingLogFileTests
{
    private static readonly string[] _expectedLogFiles = ["game.1.log", "game.2.log", "game.log"];

    [TestCase]
    public void WriteLine_PastLimit_RotatesAndKeepsOnlyTheNewestFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"));
        using (var file = new RotatingLogFile(dir, "game", 1024, 3))
        {
            var line = new string('x', 99);
            for (var i = 0; i < 100; i++)
            {
                file.WriteLine(line);
            }
        }

        var names = Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        AssertThat(names.SequenceEqual(_expectedLogFiles)).IsTrue();
        foreach (var name in names)
        {
            AssertThat(new FileInfo(Path.Combine(dir, name!)).Length <= 1124).IsTrue();
        }
    }

    [TestCase]
    public void Provider_WritesFileAndConsole_WarningsAsErrors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"));
        var console = new List<(string Text, bool IsError)>();
        var file = new RotatingLogFile(dir, "game", 1024 * 1024, 2);
        using (var provider = new GodotLoggerProvider(file, LogLevel.Information, TimeProvider.System, (text, isError) => console.Add((text, isError))))
        {
            var logger = provider.CreateLogger("Test.Category");
            logger.Log(LogLevel.Information, new EventId(7), "state", null, (s, _) => $"hello {s}");
            logger.Log(LogLevel.Debug, default, "state", null, (s, _) => "filtered out");
            logger.Log(LogLevel.Error, default, "state", new InvalidOperationException("boom"), (s, _) => "failed");
        }

        AssertThat(console.Count).IsEqual(2);
        AssertThat(console[0].IsError).IsFalse();
        AssertThat(console[0].Text.Contains("[Info] Test.Category #7: hello state", StringComparison.Ordinal)).IsTrue();
        AssertThat(console[1].IsError).IsTrue();
        var text = File.ReadAllText(Path.Combine(dir, "game.log"));
        AssertThat(text.Contains("hello state", StringComparison.Ordinal) && text.Contains("boom", StringComparison.Ordinal)).IsTrue();
        AssertThat(text.Contains("filtered out", StringComparison.Ordinal)).IsFalse();
    }
}
