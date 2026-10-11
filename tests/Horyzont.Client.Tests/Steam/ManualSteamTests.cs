using System.Diagnostics;
using System.Globalization;

using Horyzont.Client.Steam;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Steam;

/// <summary>
/// Real Steam scenarios, excluded from CI (<c>Category=Manual</c>). Set <c>HORYZONT_STEAM_MANUAL</c> to <c>host</c> on one account and to
/// <c>join:&lt;lobbyId&gt;</c> on another; Steam must run, App ID 480. Real time is allowed here (ZASADY pkt 8). Results go into the PR.
/// </summary>
[Trait("Category", "Manual")]
public sealed class ManualSteamTests
{
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(120);

    [Fact]
    public void SteamInvite_TwoAccounts_JoinsHostThroughRelay()
    {
        var mode = Environment.GetEnvironmentVariable("HORYZONT_STEAM_MANUAL");
        Assert.SkipWhen(string.IsNullOrEmpty(mode), "Manual: set HORYZONT_STEAM_MANUAL=host (account A) or join:<lobbyId> (account B).");
        using var api = new SteamworksLobbyApi(NullLogger<SteamworksLobbyApi>.Instance);
        Assert.True(api.TryInitialize(480), "SteamAPI.Init failed: is Steam running?");
        using var controller = new SteamLobbyController(api, TimeProvider.System, () => false, NullLogger<SteamLobbyController>.Instance);
        controller.Configure("1", 3);
        try
        {
            if (mode == "host")
            {
                controller.SetSession(new LobbySessionInfo(Guid.NewGuid().ToString("D"), false));
                controller.Host(LobbyVisibility.FriendsOnly, 8);
                Assert.True(PumpUntil(api, controller, () => controller.State == LobbyState.Hosting), "lobby was not created");
                Console.WriteLine("LOBBY " + controller.Current!.Value.LobbyId.ToString(CultureInfo.InvariantCulture));
                Assert.True(PumpUntil(api, controller, () => controller.Current is { Members: >= 2 }), "nobody joined within 120 s");
            }
            else
            {
                var lobby = ulong.Parse(mode!["join:".Length..], CultureInfo.InvariantCulture);
                SteamEndpoint? endpoint = null;
                controller.ConnectRequested += (e, _) => endpoint = e;
                controller.Join(lobby);
                Assert.True(PumpUntil(api, controller, () => endpoint is not null), "join did not reach ConnectRequested");
                Assert.NotEqual(lobby, endpoint!.SteamId);
            }
        }
        finally
        {
            controller.Leave();
            api.Shutdown();
        }
    }

    [Fact]
    public void SteamLobby_EightPlayersThenHostExit_AllReceiveSessionEnd()
    {
        Assert.SkipWhen(true, "Manual and pending: needs eight Steam accounts; the final scenario is coordinated in K4-19.");
    }

    private static bool PumpUntil(SteamworksLobbyApi api, SteamLobbyController controller, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < _limit)
        {
            api.Pump();
            controller.Update();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }
}
