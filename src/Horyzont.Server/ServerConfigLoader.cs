using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Horyzont.Server;

/// <summary>Reads and checks the JSON configuration of a dedicated server; the file is treated like hostile data.</summary>
public static class ServerConfigLoader
{
    /// <summary>Largest configuration file accepted.</summary>
    public const int MaxFileBytes = 64 * 1024;

    /// <summary>Reads, parses and validates a configuration file; never throws for bad data.</summary>
    /// <param name="path">The file.</param>
    /// <param name="config">The configuration; the defaults when the method returns false.</param>
    /// <param name="error">What is wrong, naming the field; empty when the method returns true.</param>
    /// <returns>True when the file is a valid configuration.</returns>
    public static bool TryLoad(string path, out ServerConfig config, out string error)
    {
        config = new ServerConfig();
        error = string.Empty;
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = $"The configuration file '{path}' does not exist.";
                return false;
            }

            if (info.Length > MaxFileBytes)
            {
                error = $"The configuration file '{path}' is larger than {MaxFileBytes} bytes.";
                return false;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"The configuration file '{path}' cannot be read: {ex.Message}";
            return false;
        }

        if (bytes.Length > MaxFileBytes)
        {
            error = $"The configuration file '{path}' is larger than {MaxFileBytes} bytes.";
            return false;
        }

        ServerConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(bytes, ServerConfigJsonContext.Default.ServerConfig);
        }
        catch (JsonException ex)
        {
            error = string.IsNullOrEmpty(ex.Path) || ex.Path == "$"
                ? $"The configuration file is not valid JSON: {ex.Message}"
                : $"Invalid JSON or value at {ex.Path}: {ex.Message}";
            return false;
        }

        if (parsed is null)
        {
            error = "The configuration file is empty or null.";
            return false;
        }

        if (!TryValidate(parsed, out error))
        {
            return false;
        }

        config = parsed;
        return true;
    }

    /// <summary>Writes a configuration file with the default values (the server does not create one by itself).</summary>
    /// <param name="path">The file to create or replace.</param>
    public static void WriteDefault(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(new ServerConfig(), ServerConfigJsonContext.Default.ServerConfig) + "\n", new UTF8Encoding(false));
    }

    private static bool TryValidate(ServerConfig config, out string error)
    {
        error = string.Empty;
        if (config.WorldId == Guid.Empty)
        {
            error = "worldId must be a non-empty GUID or null.";
        }
        else if (!IPAddress.TryParse(config.BindAddress, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            error = "bindAddress must be an IPv4 address.";
        }
        else if (config.Port is < 1024 or > 65535)
        {
            error = Range("port", config.Port, 1024, 65535);
        }
        else if (config.Password is null || Encoding.UTF8.GetByteCount(config.Password) > 64)
        {
            error = "password must be at most 64 bytes in UTF-8.";
        }
        else if (config.MaxPlayers is < 1 or > 8)
        {
            error = Range("maxPlayers", config.MaxPlayers, 1, 8);
        }
        else if (config.CheckpointIntervalSeconds is < 10 or > 3600)
        {
            error = Range("checkpointIntervalSeconds", config.CheckpointIntervalSeconds, 10, 3600);
        }
        else if (config.BackupIntervalHours is < 1 or > 168)
        {
            error = Range("backupIntervalHours", config.BackupIntervalHours, 1, 168);
        }
        else if (config.BackupsToKeep is < 1 or > 100)
        {
            error = Range("backupsToKeep", config.BackupsToKeep, 1, 100);
        }
        else if (string.IsNullOrWhiteSpace(config.LogDirectory) || config.LogDirectory.Contains('\0', StringComparison.Ordinal))
        {
            error = "logDirectory must be a directory name.";
        }

        return error.Length == 0;
    }

    private static string Range(string field, int value, int min, int max) =>
        string.Create(CultureInfo.InvariantCulture, $"{field} is {value}; it must be {min}..{max}.");
}
