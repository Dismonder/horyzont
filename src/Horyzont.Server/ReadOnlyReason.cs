namespace Horyzont.Server;

/// <summary>Why a world started without the ability to change.</summary>
public enum ReadOnlyReason : byte
{
    /// <summary>The journal tail was written with other terrain edit semantics than this build has (K2-09, decision D1).</summary>
    TerrainSemanticsMismatch = 1,
}
