namespace Horyzont.Persistence.Writing;

/// <summary>A write failed; the writer keeps journal entries in memory until <see cref="PersistenceWriter.ClearFault"/>. The server enters safe mode.</summary>
/// <param name="Error">The error.</param>
public sealed record WriterFaulted(Exception Error) : PersistenceEvent;
