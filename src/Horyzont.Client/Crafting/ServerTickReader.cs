namespace Horyzont.Client.Crafting;

/// <summary>Reads the tick of the server from the same source as the HUD (K6-32 D23): the server itself for a host, the snapshots for a remote client.</summary>
/// <param name="tick">The tick when available.</param>
/// <returns>True when the tick is known.</returns>
public delegate bool ServerTickReader(out ulong tick);
