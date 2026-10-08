using System.IO.Hashing;
using System.Text;

namespace Horyzont.Core;

/// <summary>
/// Identity of the running build. <see cref="Commit"/> is generated before compilation (MSBuild target
/// <c>GenerateBuildInfo</c>): the <c>HORYZONT_COMMIT</c> environment variable, otherwise <c>git rev-parse HEAD</c>,
/// otherwise <c>"dev"</c>.
/// </summary>
public static partial class BuildInfo
{
    /// <summary>
    /// Returns the build hash compared in the handshake: XxHash64 of the UTF-8 bytes of <see cref="Commit"/>, a zero
    /// byte and <paramref name="generatorProfileVersion"/>, because another generator would produce other terrain.
    /// </summary>
    /// <param name="generatorProfileVersion">Version of the terrain generator profile.</param>
    /// <returns>The build hash.</returns>
    public static ulong ComputeBuildHash(string generatorProfileVersion)
    {
        ArgumentNullException.ThrowIfNull(generatorProfileVersion);
        var bytes = Encoding.UTF8.GetBytes(Commit + "\0" + generatorProfileVersion);
        return XxHash64.HashToUInt64(bytes);
    }
}
