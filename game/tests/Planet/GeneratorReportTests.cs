using System.Diagnostics;

using GdUnit4;

using Godot;

using Horyzont.Game.Planet.Diagnostics;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

[TestSuite]
public class GeneratorReportTests
{
    private static string? FindPython()
    {
        foreach (var name in new[] { "python", "python3" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(name, "-I -c \"import sys\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                probe?.WaitForExit(10000);
                if (probe is { HasExited: true, ExitCode: 0 })
                {
                    return name;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not on PATH under this name; try the next one.
            }
        }

        return null;
    }

    [TestCase]
    public void ReadKeys_RejectsOriginsThatWouldOverflow()
    {
        var file = Path.Combine(Path.GetTempPath(), $"horyzont-keys-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(file, "# lod x y z\n0 249 -3 0\n15 1 0 0\n");
            AssertThat(GeneratorReport.ReadKeys(file).Count).IsEqual(2);

            File.WriteAllText(file, "15 70000 0 0\n");
            AssertThrown(() => GeneratorReport.ReadKeys(file)).IsInstanceOf<ArgumentException>();

            File.WriteAllText(file, "0 0 0 1048577\n");
            AssertThrown(() => GeneratorReport.ReadKeys(file)).IsInstanceOf<ArgumentException>();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void GeneratorReport_WritesValidFile()
    {
        var python = FindPython();
        if (python is null)
        {
            // gdUnit4Net has no runtime skip; CI has Python, so the check runs there.
            GD.PushWarning("GeneratorReport_WritesValidFile skipped: no Python on PATH.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"horyzont-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var file = GeneratorReport.Run(directory, 0x484F52595A4F4E54UL, null, maxBlocks: 40);
            var script = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "generator", "compare_reports.py"));
            var output = Path.Combine(directory, "out.md");
            var start = new ProcessStartInfo(python) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { "-I", script, file, file, "--out", output })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            AssertThat(process.ExitCode).IsEqual(0);
            AssertThat(stdout.Contains("IDENTICAL")).IsTrue();
            AssertThat(stderr).IsEmpty();
            AssertThat(File.Exists(output)).IsTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
