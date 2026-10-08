namespace Horyzont.Persistence;

/// <summary>The world was saved by a newer game version whose schema this version does not know.</summary>
/// <param name="schemaVersion">Schema version of the file.</param>
/// <param name="supportedVersion">Newest schema version known to this game.</param>
public sealed class WorldFromNewerVersionException(int schemaVersion, int supportedVersion)
    : Exception($"The world has schema version {schemaVersion}; this game supports up to {supportedVersion}.")
{
    /// <summary>Schema version of the file.</summary>
    public int SchemaVersion { get; } = schemaVersion;

    /// <summary>Newest schema version known to this game.</summary>
    public int SupportedVersion { get; } = supportedVersion;
}
