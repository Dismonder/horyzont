using System.Runtime.InteropServices;

using GdUnit4;

using Horyzont.Game.Boot;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class QuitCoordinatorTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void ThirdSigint_ForcesExit130()
    {
        var (quit, exits) = Create();
        quit.HandleSignal(PosixSignal.SIGINT);
        quit.HandleSignal(PosixSignal.SIGINT);
        AssertThat(exits.Count).IsEqual(0);
        quit.HandleSignal(PosixSignal.SIGINT);
        AssertThat(exits.Count).IsEqual(1);
        AssertThat(exits[0]).IsEqual(130);
        quit.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ManySigtermAndSigquit_NeverForceExit()
    {
        var (quit, exits) = Create();
        for (var i = 0; i < 6; i++)
        {
            quit.HandleSignal(i % 2 == 0 ? PosixSignal.SIGTERM : PosixSignal.SIGQUIT);
        }

        AssertThat(exits.Count).IsEqual(0);
        quit.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void MixedSignals_OnlyThirdSigintForcesExit()
    {
        var (quit, exits) = Create();
        quit.HandleSignal(PosixSignal.SIGTERM);
        quit.HandleSignal(PosixSignal.SIGINT);
        quit.HandleSignal(PosixSignal.SIGQUIT);
        quit.HandleSignal(PosixSignal.SIGINT);
        AssertThat(exits.Count).IsEqual(0);
        quit.HandleSignal(PosixSignal.SIGINT);
        AssertThat(exits.Count).IsEqual(1);
        quit.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SlowCleanup_IsWaitedForPerFrameUntilTheDeadline_ThenQuits()
    {
        var time = new FakeTimeProvider();
        var quit = new QuitCoordinator();
        var done = false;
        var calls = 0;
        quit.Initialize(() => { }, () => true, () => 0, () => { calls++; return done; }, false, NullLogger<QuitCoordinator>.Instance, time, TimeSpan.FromSeconds(5));

        AssertThat(quit.Advance()).IsFalse();
        time.Advance(TimeSpan.FromSeconds(4));
        AssertThat(quit.Advance()).IsFalse();
        AssertThat(calls).IsEqual(2);

        // The cleanup finishes in time: quit at once.
        done = true;
        AssertThat(quit.Advance()).IsTrue();
        quit.Free();

        // A cleanup that never finishes (a hung transport) does not hold the process after the deadline.
        var stuck = new QuitCoordinator();
        stuck.Initialize(() => { }, () => true, () => 0, () => false, false, NullLogger<QuitCoordinator>.Instance, time, TimeSpan.FromSeconds(5));
        AssertThat(stuck.Advance()).IsFalse();
        time.Advance(TimeSpan.FromSeconds(6));
        AssertThat(stuck.Advance()).IsTrue();
        stuck.Free();
    }

    private static (QuitCoordinator Quit, List<int> Exits) Create()
    {
        var quit = new QuitCoordinator();
        quit.Initialize(() => { }, () => false, () => 0, null, false, NullLogger<QuitCoordinator>.Instance);
        var exits = new List<int>();
        quit.UseForceExit(exits.Add);
        return (quit, exits);
    }
}
