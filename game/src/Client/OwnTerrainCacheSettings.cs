using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client;

/// <summary>Where and how the remote client keeps its terrain cache.</summary>
/// <param name="Root">Directory of the caches; each world gets <c>&lt;Root&gt;/&lt;WorldId&gt;/terrain.db</c>.</param>
/// <param name="Time">Clock of the cache (usage stamps and the close deadline).</param>
/// <param name="BuildHash">Hash of the build: a cache of another build is not trusted.</param>
/// <param name="Loggers">Loggers.</param>
public sealed record OwnTerrainCacheSettings(string Root, TimeProvider Time, ulong BuildHash, ILoggerFactory Loggers);
