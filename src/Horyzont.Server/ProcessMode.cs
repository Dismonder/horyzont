namespace Horyzont.Server;

/// <summary>What one game process does; chosen by the command line and fixed for the life of the process.</summary>
public enum ProcessMode : byte
{
    /// <summary>Server and local client in one process; nothing listens on the network.</summary>
    Solo = 1,

    /// <summary>Server and local client in one process; remote clients connect over UDP.</summary>
    Host = 2,

    /// <summary>Only a client that connects to a remote server.</summary>
    Client = 3,

    /// <summary>Only the server, without graphics, driven by a console.</summary>
    DedicatedServer = 4,
}
