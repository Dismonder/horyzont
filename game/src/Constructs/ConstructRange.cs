namespace Horyzont.Game.Constructs;

/// <summary>Viewer interest shared by loading and collision decisions.</summary>
internal static class ConstructRange
{
    internal static bool Includes(int viewerCount, bool loadAllWithoutViewers, double distance, double radius) =>
        viewerCount == 0 ? loadAllWithoutViewers : distance <= radius;
}
