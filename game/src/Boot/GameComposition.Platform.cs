using Horyzont.Client.Platform;

namespace Horyzont.Game.Boot;

public sealed partial class GameComposition
{
    // Select platform services before Create constructs either runtime or its transport.
    static partial void ConfigurePlatform(LaunchOptions launch, GameEnvironment env, ref ICompositionPlatform platform)
    {
    }

    // This instance seam is intentionally empty until its owning K3 package wires the session.
#pragma warning disable CA1822
    partial void ConfigurePlatform()
    {
    }
#pragma warning restore CA1822
}
