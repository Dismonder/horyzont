using System.Diagnostics;
using System.Reflection;

using Godot;

namespace Horyzont.Game.Testing;

/// <summary>
/// Runs engine tests inside the real engine: <c>godot --headless --path game -- --engine-tests [--filter=text] [--slow] [--manual]</c>.
/// Public methods marked with <see cref="EngineTestAttribute"/> on subclasses of <see cref="EngineTestBase"/> are run one after
/// another on the main thread; the process exit code is 0 when all of them pass. This is the runner K2-12 brings until a shared
/// harness (K2-09) is on <c>main</c>.
/// </summary>
public sealed partial class EngineTestRunner : Node
{
    private static readonly TimeSpan _testTimeout = TimeSpan.FromMinutes(3);

    private readonly string? _filter;
    private readonly bool _includeSlow;
    private readonly bool _includeManual;

    /// <summary>Creates a runner.</summary>
    /// <param name="filter">Only tests whose "Class.Method" contains this text; null = all.</param>
    /// <param name="includeSlow">Include tests marked slow.</param>
    /// <param name="includeManual">Include tests marked manual.</param>
    public EngineTestRunner(string? filter, bool includeSlow, bool includeManual)
    {
        Name = "EngineTestRunner";
        _filter = filter;
        _includeSlow = includeSlow;
        _includeManual = includeManual;
    }

    /// <summary>Godot requires a parameterless constructor for scene instantiation.</summary>
    public EngineTestRunner()
        : this(null, false, false)
    {
    }

    /// <summary>Creates a runner from the user arguments after <c>--</c>.</summary>
    /// <param name="args">The user arguments.</param>
    /// <returns>The runner.</returns>
    public static EngineTestRunner FromArgs(IReadOnlyCollection<string> args)
    {
        string? filter = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--filter=", StringComparison.Ordinal))
            {
                filter = arg["--filter=".Length..];
            }
        }

        return new EngineTestRunner(filter, args.Contains("--slow"), args.Contains("--manual"));
    }

    /// <inheritdoc />
    public override void _Ready() => _ = RunAsync();

    private static IEnumerable<(Type Type, MethodInfo Method, EngineTestAttribute Attribute)> Discover()
    {
        foreach (var type in typeof(EngineTestRunner).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(EngineTestBase))).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var attribute = method.GetCustomAttribute<EngineTestAttribute>();
                if (attribute is not null)
                {
                    yield return (type, method, attribute);
                }
            }
        }
    }

    private async Task RunAsync()
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        try
        {
            GD.Print($"ENGINE TESTS physics_engine={ProjectSettings.GetSetting("physics/3d/physics_engine")} " +
                     $"rendering_driver={RenderingServer.GetCurrentRenderingDriverName()}");
            foreach (var (type, method, attribute) in Discover())
            {
                var name = $"{type.Name}.{method.Name}";
                if ((_filter is not null && !name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    || (attribute.Slow && !_includeSlow)
                    || (attribute.Manual && !_includeManual))
                {
                    skipped++;
                    continue;
                }

                var started = Stopwatch.GetTimestamp();
                var error = await RunOneAsync(type, method);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (error is null)
                {
                    passed++;
                    GD.Print($"[PASS] {name} ({elapsed:F0} ms)");
                }
                else
                {
                    failed++;
                    GD.PrintErr($"[FAIL] {name} ({elapsed:F0} ms): {error}");
                }
            }
        }
        catch (Exception e)
        {
            // The runner itself broke: report it and fail the run instead of hanging the process.
            GD.PrintErr($"ENGINE TESTS runner error: {e}");
            failed++;
        }

        GD.Print($"ENGINE TESTS passed={passed} failed={failed} skipped={skipped}");
        GetTree().Quit(failed == 0 && passed > 0 ? 0 : 1);
    }

    private async Task<string?> RunOneAsync(Type type, MethodInfo method)
    {
        var host = new Node3D { Name = "TestHost" };
        AddChild(host);
        try
        {
            var instance = (EngineTestBase)Activator.CreateInstance(type)!;
            instance.Tree = GetTree();
            instance.Host = host;
            var result = method.Invoke(instance, null);
            if (result is Task task)
            {
                var finished = await Task.WhenAny(task, Task.Delay(_testTimeout));
                if (finished != task)
                {
                    return $"timed out after {_testTimeout.TotalSeconds:F0} s";
                }

                await task;
            }

            return null;
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            return Describe(e.InnerException);
        }
        catch (Exception e)
        {
            return Describe(e);
        }
        finally
        {
            RemoveChild(host);
            host.Free();
        }
    }

    private static string Describe(Exception e) => e is EngineTestFailedException ? e.Message : e.ToString();
}
