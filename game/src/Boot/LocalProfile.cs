using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Horyzont.Core.Ids;

namespace Horyzont.Game.Boot;

/// <summary>
/// The local identity of the player: a random <see cref="IdentityPlatform.Dev"/> identity and a display name, kept in
/// <c>user://profile.json</c> so the player keeps the same character between sessions. Steam replaces the identity in K3-17.
/// </summary>
public sealed class LocalProfile
{
    private const int MaxFileBytes = 4096;
    private const int MaxNameBytes = 32;

    private LocalProfile(PlatformIdentity identity, string displayName)
    {
        Identity = identity;
        DisplayName = displayName;
    }

    /// <summary>Gets the platform identity of the player.</summary>
    public PlatformIdentity Identity { get; }

    /// <summary>Gets the display name (1 to 32 UTF-8 bytes).</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Reads the profile file, or creates it with a random identity and the default name when it is missing or unusable (the file is
    /// local and may be edited by hand, so bad content is replaced, not trusted).
    /// </summary>
    /// <param name="path">Absolute path of the profile file.</param>
    /// <returns>The profile.</returns>
    public static LocalProfile LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (TryRead(path) is { } existing)
        {
            return existing;
        }

        Span<byte> bytes = stackalloc byte[8];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BitConverter.ToUInt64(bytes);
        }
        while (value == 0);

        var profile = new LocalProfile(new PlatformIdentity(IdentityPlatform.Dev, value), "Player");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path, JsonSerializer.Serialize(new ProfileData(value, profile.DisplayName), ProfileJsonContext.Default.ProfileData) + "\n", new UTF8Encoding(false));
        return profile;
    }

    private static LocalProfile? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return null;
            }

            var data = JsonSerializer.Deserialize(File.ReadAllBytes(path), ProfileJsonContext.Default.ProfileData);
            if (data is null || data.IdentityValue == 0 || string.IsNullOrWhiteSpace(data.Name) || Encoding.UTF8.GetByteCount(data.Name) > MaxNameBytes)
            {
                return null;
            }

            return new LocalProfile(new PlatformIdentity(IdentityPlatform.Dev, data.IdentityValue), data.Name);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable or malformed profile is replaced by a new one (the player gets a new character); nothing else depends on it.
            return null;
        }
    }

    internal sealed record ProfileData(ulong IdentityValue, string Name);
}

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(LocalProfile.ProfileData))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
