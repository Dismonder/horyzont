using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Net.Session;

/// <summary>A player looking at the world.</summary>
/// <param name="Player">The player.</param>
/// <param name="Region">Frame in which the player is (planet or system frame).</param>
/// <param name="Position">Position of the player in the region's frame.</param>
public readonly record struct InterestViewer(PlayerId Player, FrameRef Region, Vector3 Position);
