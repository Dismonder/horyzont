using Horyzont.Client.Movement;
using Horyzont.Server.Replication;

namespace Horyzont.Game.Boot;

public sealed partial class GameComposition
{
    /// <summary>Gets the per-client snapshot byte budget the server shares with K3-15's snapshot builder; null on a client.</summary>
    public MotionSnapshotBudget? MotionBudget { get; private set; }

    /// <summary>Gets the sink and shared remote clock used by K3-15's remote player lifecycle; null without a client.</summary>
    public RemoteMotionSampleAdapter? RemoteMotion { get; private set; }

    // The budget and the remote adapter are created where K3-15 builds the replication (they must exist before the first snapshot).
    partial void ConfigureMotion()
    {
        if (Client is not null && LocalPlayer is not null && RemoteMotion is null)
        {
            // Characters without replication (no ClientReplication): the pass-through of the seam still delivers full states.
            RemoteMotion = new RemoteMotionSampleAdapter(_env.Time);
            LocalPlayer.RemoteMotion = RemoteMotion;
            _fullSnapshots?.AddSink(RemoteMotion);
            Client.AddListener(RemoteMotion);
        }
    }
}
