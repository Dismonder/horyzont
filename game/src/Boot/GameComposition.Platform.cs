using System.Globalization;

using Horyzont.Client.Platform;
using Horyzont.Client.Steam;
using Horyzont.Core;
using Horyzont.Core.Protocol;
using Horyzont.Game.Planet.Generators;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

public sealed partial class GameComposition
{
    /// <summary>
    /// Builds the process-wide Steam services (K3-17, X8): one SDK initialization, the lobby controller and the P2P transport factory.
    /// Called once per process by Boot (command line) or by the menu coordinator on first use; the result is kept in
    /// <see cref="GameEnvironment.Platform"/> for every later session.
    /// </summary>
    /// <param name="env">Process environment (clock and loggers).</param>
    /// <param name="appId">Steam application id.</param>
    /// <returns>The services; <see cref="SteamPlatformServices.Available"/> is false when Steam is not running.</returns>
    public static SteamPlatformServices CreatePlatformServices(GameEnvironment env, uint appId)
    {
        ArgumentNullException.ThrowIfNull(env);
        var buildHash = BuildInfo.ComputeBuildHash(GeneratorProfiles.BuildHashComponent);
        var api = new SteamworksLobbyApi(env.Loggers.CreateLogger<SteamworksLobbyApi>());
        return SteamPlatformServices.Create(
            api, appId, buildHash.ToString(CultureInfo.InvariantCulture), ProtocolConstants.ProtocolVersion, env.Time, env.Loggers);
    }

    // Select platform services before Create constructs either runtime or its transport. Solo and dedicated keep the neutral platform.
    static partial void ConfigurePlatform(LaunchOptions launch, GameEnvironment env, ref ICompositionPlatform platform)
    {
        if (env.Platform is not { } services || !launch.EnableSteam || launch.Mode is not (ProcessMode.Host or ProcessMode.Client))
        {
            return;
        }

        platform = new SteamSessionBinder(
            services,
            new SteamSessionOptions
            {
                ConnectLobbyId = launch.ConnectLobbyId,
                ConnectsByAddress = launch.ConnectHost is not null,
                Visibility = launch.LobbyVisibility,
                Password = launch.Password,
            },
            env.Loggers.CreateLogger<SteamSessionBinder>());
    }

    // Late seam: the runtimes exist, so the binder gets the way to end a remote client whose lobby failed.
    partial void ConfigurePlatform()
    {
        if (_platform is SteamSessionBinder binder)
        {
            binder.Bind(EndClientSessionAfterLobbyFailure);
        }
    }

    private void EndClientSessionAfterLobbyFailure()
    {
        _clientExitCode = 1;
        if (Quit is not null)
        {
            Quit.RequestStop();
        }
        else
        {
            RequestShutdown();
        }
    }
}
