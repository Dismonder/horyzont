using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Survival;

/// <summary>The rest of one player in memory: the posture, the running warm-up and the last evaluation. Not saved: the warm-up starts again after a restart (K6-33 D14).</summary>
internal sealed class RestState
{
    /// <summary>Gets or sets whether the player lies in a bed (a seat comes from the state of the player).</summary>
    public bool Lying { get; set; }

    public EntityId LyingConstruct { get; set; }

    public CellPos LyingCell { get; set; }

    public Vector3 LyingFeet { get; set; }

    public uint LyingParentSeq { get; set; }

    /// <summary>Gets or sets whether the player has a posture of rest and the evaluation runs.</summary>
    public bool Active { get; set; }

    /// <summary>Gets or sets whether the previous evaluation found the conditions (roof and heat) met.</summary>
    public bool WasMet { get; set; }

    public bool ConditionsMet { get; set; }

    public ulong WarmupTicks { get; set; }

    public byte Comfort { get; set; }

    public Tick LastEvaluation { get; set; }

    public Tick NextEvaluation { get; set; }

    /// <summary>Forgets the warm-up and the conditions (the posture ended or was never there).</summary>
    public void Clear()
    {
        Active = false;
        WasMet = false;
        ConditionsMet = false;
        WarmupTicks = 0;
        Comfort = 0;
    }
}
