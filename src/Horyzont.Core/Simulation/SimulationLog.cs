using Horyzont.Core.Operations;

using Microsoft.Extensions.Logging;

namespace Horyzont.Core.Simulation;

/// <summary>Log messages of the simulation.</summary>
internal static partial class SimulationLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Kinematics of player {PlayerId} rejected: non-finite or non-normalized values")]
    internal static partial void KinematicsRejected(ILogger logger, ulong playerId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Critical, Message = "Simulation faulted while applying operation {OpId} ({Kind})")]
    internal static partial void Faulted(ILogger logger, Exception exception, ulong opId, OperationKind kind);
}
