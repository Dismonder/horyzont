using Godot;

using Horyzont.Game.Planet.Dev;
using Horyzont.Game.Planet.Diagnostics;

namespace Horyzont.Game.Boot;

/// <summary>Developer modes started from the command line (<c>godot --path game -- --planet-preview</c> and others).</summary>
public static class DevModes
{
    /// <summary>Exit code for invalid arguments.</summary>
    public const int ExitBadArguments = 2;

    /// <summary>
    /// Starts the developer mode named in the arguments, if any. Returns true when a mode took over (it quits the tree itself),
    /// false when Boot should continue with the normal start.
    /// </summary>
    public static bool TryRun(Node host, string[] userArgs)
    {
        ArgumentNullException.ThrowIfNull(host);
        var args = new DevModeArguments(userArgs);
        if (args.Has("--generator-report"))
        {
            RunGeneratorReport(host, args);
            return true;
        }

        if (args.Has("--benchmark-report"))
        {
            RunBenchmarkReport(host, args);
            return true;
        }

        if (!args.Has("--planet-preview"))
        {
            return false;
        }

        try
        {
            var seed = args.GalaxySeed();
            var screenshots = args.Directory("--screenshots", mustExist: false);
            var lodDistance = args.Number("--lod-distance", 16f, 4096f);
            host.AddChild(new PlanetPreview(seed, screenshots, lodDistance) { Name = "PlanetPreview" });
        }
        catch (ArgumentException ex)
        {
            GD.PushError(ex.Message);
            GD.Print($"ARGUMENT ERROR: {ex.Message}");
            host.GetTree().Quit(ExitBadArguments);
        }

        return true;
    }

    private static void RunGeneratorReport(Node host, DevModeArguments args)
    {
        try
        {
            var directory = args.Directory("--generator-report", mustExist: true)!;
            var seed = args.GalaxySeed();
            var dumpKeys = args.ValueOf("--dump-keys");
            if (dumpKeys is not null && !File.Exists(dumpKeys))
            {
                throw new ArgumentException($"--dump-keys: file '{dumpKeys}' does not exist.");
            }

            GeneratorReport.Run(directory, seed, dumpKeys);
            host.GetTree().Quit(0);
        }
        catch (ArgumentException ex)
        {
            GD.PushError(ex.Message);
            GD.Print($"ARGUMENT ERROR: {ex.Message}");
            host.GetTree().Quit(ExitBadArguments);
        }
        catch (Exception ex)
        {
            // Headless CI tool: any other failure must end the process with an error instead of leaving it running.
            GD.PushError($"Generator report failed: {ex}");
            GD.Print($"GENERATOR REPORT FAILED: {ex.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static void RunBenchmarkReport(Node host, DevModeArguments args)
    {
        try
        {
            var directory = args.Directory("--benchmark-report", mustExist: true)!;
            BenchmarkReport.Run(directory, args.GalaxySeed());
            host.GetTree().Quit(0);
        }
        catch (ArgumentException ex)
        {
            GD.PushError(ex.Message);
            GD.Print($"ARGUMENT ERROR: {ex.Message}");
            host.GetTree().Quit(ExitBadArguments);
        }
        catch (Exception ex)
        {
            GD.PushError($"Benchmark report failed: {ex}");
            GD.Print($"BENCHMARK REPORT FAILED: {ex.Message}");
            host.GetTree().Quit(1);
        }
    }
}
