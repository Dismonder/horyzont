using System.Globalization;

using Horyzont.Net.Transport;
using Horyzont.Net.Transport.Steam;

using Steamworks;

namespace Horyzont.Net.SteamCheck;

/// <summary>Manual two-machine check of <see cref="SteamTransport"/>; see docs/plans/K1-06-transporty.md.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var host = args.Contains("--host");
        var joinIndex = Array.IndexOf(args, "--join");
        ulong hostId = 0;
        if (!host && (joinIndex < 0 || joinIndex + 1 >= args.Length || !ulong.TryParse(args[joinIndex + 1], CultureInfo.InvariantCulture, out hostId)))
        {
            Console.WriteLine("Usage: SteamCheck --host | SteamCheck --join <SteamID of host>");
            return 2;
        }

        if (!SteamAPI.Init())
        {
            Console.WriteLine("FAIL steam-init: SteamAPI.Init() returned false (is Steam running, is steam_appid.txt present?)");
            return 1;
        }

        try
        {
            using var transport = new SteamTransport(TransportOptions.Default);
            var peer = new CheckPeer(transport);
            return host ? peer.RunHost() : peer.RunJoin(hostId);
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }
}
