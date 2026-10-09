namespace Horyzont.Constructs.Scheduling;

/// <summary>Urgency of a chunk rebuild; lower values are served first.</summary>
public enum RemeshPriority
{
    /// <summary>Caused by an operation (a player edit): served before everything else.</summary>
    Edit = 0,

    /// <summary>Caused by a world load, a resync or entering the view range.</summary>
    Rebuild = 1,
}
