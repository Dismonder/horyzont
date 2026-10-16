using Horyzont.Core.Ids;

namespace Horyzont.Client.Props;

/// <summary>The hits the local player landed on one prop, against the hits it takes (informational; the truth is the harvest of the server).</summary>
/// <param name="Prop">The prop.</param>
/// <param name="Hits">Hits sent so far.</param>
/// <param name="Total">Hits it takes with the tool in hand.</param>
public readonly record struct PropProgress(PropId Prop, int Hits, int Total);
