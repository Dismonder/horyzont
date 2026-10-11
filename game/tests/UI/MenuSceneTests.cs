using GdUnit4;

using Godot;

using Horyzont.Client.Diagnostics;
using Horyzont.Client.UI;
using Horyzont.Game.Boot;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.UI;

using Microsoft.Extensions.Logging.Abstractions;

using static GdUnit4.Assertions;

using ServerProcessMode = Horyzont.Server.ProcessMode;

namespace Horyzont.Game.Tests.UI;

/// <summary>Scenes of the K3-18 interface. Engine tests: run one at a time on the runner (never in parallel).</summary>
[TestSuite]
public class MenuSceneTests
{
    private static readonly string[] _screens = ["MainMenu", "WorldListPanel", "JoinPanel", "LoadingPanel", "ErrorDialog", "PauseMenu"];

    [TestCase]
    public void Boot_NoMode_ShowsMenuWithoutStartingWorld_ArgumentsKeepTheirPath()
    {
        AssertThat(LaunchOptions.IsModeRequested([])).IsFalse();
        AssertThat(LaunchOptions.IsModeRequested(["--solo"])).IsTrue();
        AssertThat(LaunchOptions.IsModeRequested(["--server", "--config", "x.json"])).IsTrue();
        AssertThat(LaunchOptions.IsModeRequested(["--connect", "127.0.0.1:27015"])).IsTrue();
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task Boot_NoMode_MenuStartsNoWorldAndQuitWaitsForRequest()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var ui = CreateUi(scene, dir, characters: false);
        await scene.WaitFrames(5);

        AssertThat(ui.Menu.State).IsEqual(MenuState.Main);
        AssertThat(ui.Current is null).IsTrue();
        AssertThat(ui.FindChild("Root", true, false) is null).IsTrue();
        AssertThat(Directory.Exists(Path.Combine(dir, "user", "saves"))).IsFalse();

        // Without a session and without a request the process-level coordinator never ends the process.
        for (var i = 0; i < 10; i++)
        {
            AssertThat(ui.Quit!.Advance()).IsFalse();
        }

        ui.Quit!.RequestStop();
        AssertThat(ui.Quit.Advance()).IsTrue();
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public async Task Ui_SoloHostJoinBackAndRestore_ButtonsFollowState()
    {
        await using var scene = EngineTestScene.Create();
        var ui = CreateUi(scene, ModeFixture.NewDirectory(), characters: false);
        await scene.WaitFrames(3);

        AssertThat(Visible(ui, "MainMenu")).IsTrue();
        Press(ui, "SoloButton");
        await scene.WaitFrames(2);
        AssertThat(ui.Menu.State).IsEqual(MenuState.Worlds);
        AssertThat(Visible(ui, "WorldListPanel")).IsTrue();
        AssertThat(Visible(ui, "MainMenu")).IsFalse();

        Press(ui, "BackButton", "WorldListPanel");
        await scene.WaitFrames(2);
        AssertThat(ui.Menu.State).IsEqual(MenuState.Main);

        Press(ui, "JoinButton");
        await scene.WaitFrames(2);
        AssertThat(ui.Menu.State).IsEqual(MenuState.Join);
        AssertThat(Visible(ui, "JoinPanel")).IsTrue();

        // An invalid port is refused before any transport exists.
        ui.FindChild("HostInput", true, false)!.Set("text", "127.0.0.1");
        ui.FindChild("PortInput", true, false)!.Set("text", "70000");
        Press(ui, "ConnectButton");
        await scene.WaitFrames(2);
        AssertThat(ui.Menu.State).IsEqual(MenuState.Join);
        AssertThat(ui.Current is null).IsTrue();

        Press(ui, "BackButton", "JoinPanel");
        await scene.WaitFrames(2);
        AssertThat(ui.Menu.State).IsEqual(MenuState.Main);

        // Only one screen is visible at any time.
        var visible = _screens.Count(name => Visible(ui, name));
        AssertThat(visible).IsEqual(1);
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task ReturnToMenu_ActiveHost_DrainsReleasesAndAllowsNewWorld()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var ui = CreateUi(scene, dir, characters: false);
        await scene.WaitFrames(3);

        for (var round = 0; round < 2; round++)
        {
            Press(ui, "HostButton");
            await scene.WaitFrames(2);
            ui.BeginLaunch(new LaunchOptions { Mode = ServerProcessMode.Host, WorldId = Guid.NewGuid(), Port = ModeFixture.NextPort(), BindAddress = "127.0.0.1" });
            AssertThat(await WaitUntil(scene, () => ui.Menu.State == MenuState.Playing, 120)).IsTrue();
            AssertThat(ui.Current is not null).IsTrue();

            // Return to the menu: the old world is stopped and released before the next one may start.
            ui.Menu.Cancel();
            AssertThat(await WaitUntil(scene, () => ui.Menu.State == MenuState.Worlds, 120)).IsTrue();
            AssertThat(ui.Current is null).IsTrue();
            ui.Menu.Cancel();
            await scene.WaitFrames(2);
            AssertThat(ui.Menu.State).IsEqual(MenuState.Main);
        }
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task Pause_FocusedModal_BlocksMovementAndToolsButServerTicks()
    {
        await using var scene = EngineTestScene.Create();
        var ui = CreateUi(scene, ModeFixture.NewDirectory(), characters: true);
        await scene.WaitFrames(3);
        Press(ui, "SoloButton");
        await scene.WaitFrames(2);
        ui.BeginLaunch(new LaunchOptions { Mode = ServerProcessMode.Solo, WorldId = Guid.NewGuid() });
        AssertThat(await WaitUntil(scene, () => ui.Menu.State == MenuState.Playing, 120)).IsTrue();

        var reader = ui.Current!.ClientNode!.GetNode<LocalInputReader>("Input");
        AssertThat(ReferenceEquals(reader.Gate, ui)).IsTrue();
        ui.Menu.Pause();
        AssertThat(ui.IsBlocked).IsTrue();

        Input.ActionPress("move_forward");
        var sample = reader.Take();
        Input.ActionRelease("move_forward");
        AssertThat(sample).IsEqual(LocalInputSample.None);

        // The server keeps ticking behind the pause.
        var tick = ui.Current.Server!.State.CurrentTick.Value;
        AssertThat(await WaitUntil(scene, () => ui.Current.Server.State.CurrentTick.Value > tick + 10, 30)).IsTrue();
        ui.Menu.Resume();
        AssertThat(ui.IsBlocked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Hud_ToggleKey_ShowsMetricsWithUnavailableValuesAsDashes()
    {
        var hud = GD.Load<PackedScene>("res://scenes/UI/DeveloperHud.tscn").Instantiate<DeveloperHud>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(hud);
        try
        {
            hud.Bind(new HudSampler(new NoMetrics(), TimeProvider.System), null);
            AssertThat(hud.IsShown).IsFalse();
            hud._UnhandledInput(new InputEventAction { Action = "dev_hud_toggle", Pressed = true });
            AssertThat(hud.IsShown).IsTrue();
            hud._Process(0.3);
            AssertThat(hud.MetricsText.Contains('—', StringComparison.Ordinal)).IsTrue();
            AssertThat(hud.ToolsText).IsEqual("—");
        }
        finally
        {
            hud.GetParent().RemoveChild(hud);
            hud.QueueFree();
        }
    }

    private static GameUiCoordinator CreateUi(EngineTestScene scene, string dir, bool characters)
    {
        var env = new GameEnvironment
        {
            Time = TimeProvider.System,
            Loggers = NullLoggerFactory.Instance,
            UserDataDir = Path.Combine(dir, "user"),
            ExecutableDir = dir,
            Render = false,
            Characters = characters,
        };
        var ui = new GameUiCoordinator { Name = "Ui" };
        ui.Initialize(env, lobby: null, platformShutdown: null, registerSignals: false);
        return scene.Add(ui);
    }

    private static bool Visible(Node ui, string name) => ui.FindChild(name, true, false) is Control { Visible: true };

    private static void Press(Node ui, string button, string? within = null)
    {
        var root = within is null ? ui : ui.FindChild(within, true, false)!;
        ((BaseButton)root.FindChild(button, true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static async Task<bool> WaitUntil(EngineTestScene scene, Func<bool> condition, double seconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds)
            {
                return false;
            }

            await scene.WaitFrames(1);
        }

        return true;
    }

    private sealed class NoMetrics : IHudMetricsSource
    {
        public bool IsReadOnly => false;

        public int SessionGeneration => 0;

        public bool TryGetServerTick(out ulong tick)
        {
            tick = 0;
            return false;
        }

        public bool TryGetServerTickMilliseconds(out double milliseconds)
        {
            milliseconds = 0;
            return false;
        }

        public bool TryGetRtt(out double milliseconds)
        {
            milliseconds = 0;
            return false;
        }

        public bool TryGetTransportBytes(out long sent, out long received)
        {
            sent = 0;
            received = 0;
            return false;
        }

        public bool TryGetTools(out int pending, out int rejections)
        {
            pending = 0;
            rejections = 0;
            return false;
        }
    }
}
