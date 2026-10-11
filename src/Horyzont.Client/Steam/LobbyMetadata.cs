namespace Horyzont.Client.Steam;

/// <summary>Keys and limits of the lobby metadata (schema 1).</summary>
public static class LobbyMetadata
{
    /// <summary>Schema key.</summary>
    public const string Schema = "horyzont_schema";

    /// <summary>Host SteamID key.</summary>
    public const string Host = "horyzont_host";

    /// <summary>Build hash key.</summary>
    public const string Build = "horyzont_build";

    /// <summary>Protocol version key.</summary>
    public const string Protocol = "horyzont_protocol";

    /// <summary>World id key.</summary>
    public const string World = "horyzont_world";

    /// <summary>Capacity key.</summary>
    public const string Capacity = "horyzont_capacity";

    /// <summary>Password-required flag key (0 or 1).</summary>
    public const string Password = "horyzont_password";

    /// <summary>Ready flag key (0 or 1), always written last.</summary>
    public const string Ready = "horyzont_ready";

    /// <summary>Supported schema value.</summary>
    public const string SchemaValue = "1";

    /// <summary>Longest accepted value.</summary>
    public const int MaxValueLength = 128;
}
