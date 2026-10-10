namespace Horyzont.Server;

/// <summary>Reasons of the backups the server asks for; the only values passed to <c>PersistenceWriter.BackupAsync</c> (lesson 3d).</summary>
public static class ServerBackupReasons
{
    /// <summary>The copy made after the startup checkpoint.</summary>
    public const string Startup = "startup";

    /// <summary>The copy made before the journal tail is dropped by <c>--recover-from-checkpoint</c>.</summary>
    public const string PreRecover = "pre-recover";
}
