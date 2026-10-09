using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horyzont.Server;

/// <summary>Source-generated JSON metadata of <see cref="ServerConfig"/>: no reflection, unknown members are errors.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ServerConfig))]
internal sealed partial class ServerConfigJsonContext : JsonSerializerContext
{
}
