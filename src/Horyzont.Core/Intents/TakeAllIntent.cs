using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>Take everything from a chest into the hotbar and the bag of the player (intent 23, spec 03, K6-30 D17).</summary>
/// <param name="Container">The chest container.</param>
public sealed record TakeAllIntent(ContainerId Container) : Intent;
