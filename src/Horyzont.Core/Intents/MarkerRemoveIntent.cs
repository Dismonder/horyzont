using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>Remove a map marker (K5-27): a shared one by anyone, a private one by its owner.</summary>
/// <param name="Marker">Marker id.</param>
public sealed record MarkerRemoveIntent(MarkerId Marker) : Intent;
