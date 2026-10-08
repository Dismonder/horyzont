using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;

namespace Horyzont.Persistence;

/// <summary>
/// A world read from disk. The game replays <see cref="JournalTail"/> with <c>ServerSimulation.Replay</c>, then calls
/// <c>LeaveAllOnlinePlayers()</c> and requests a checkpoint at once.
/// </summary>
/// <param name="State">State of the last checkpoint.</param>
/// <param name="JournalTail">Journal entries after the checkpoint, in order, checked for CRC and continuity.</param>
/// <param name="LastCheckpointOpId">Barrier of the last checkpoint.</param>
/// <param name="GameVersion">Game version of the last checkpoint (or of the creation).</param>
public sealed record LoadedWorld(WorldState State, IReadOnlyList<JournalEntry> JournalTail, OpId LastCheckpointOpId, string GameVersion);
