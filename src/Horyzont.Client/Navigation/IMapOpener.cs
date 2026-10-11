namespace Horyzont.Client.Navigation;

/// <summary>Opens the map; the navigator seat (K5-25) calls it.</summary>
public interface IMapOpener
{
    /// <summary>Opens the system map when it is closed.</summary>
    void Open();
}
