namespace Horyzont.Game.Boot;

public sealed partial class GameComposition
{
    // This instance seam is intentionally empty until its owning K3 package wires the session.
#pragma warning disable CA1822
    partial void ConfigurePlatform()
    {
    }
#pragma warning restore CA1822
}
