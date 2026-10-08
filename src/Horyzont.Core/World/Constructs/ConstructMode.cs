namespace Horyzont.Core.World.Constructs;

/// <summary>Mode of a construct.</summary>
public enum ConstructMode : byte
{
    /// <summary>Does not move; its pose is part of the state hash.</summary>
    Static = 0,

    /// <summary>Ship; moves outside the journal (enabled by subproject 2).</summary>
    Ship = 1,
}
