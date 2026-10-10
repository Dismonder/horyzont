using Horyzont.Client.Movement;
using Horyzont.Server.Replication;

namespace Horyzont.Game.Boot;

public sealed partial class GameComposition
{
    /// <summary>Gets the motion budget K3-15 supplies to its snapshot builder.</summary>
    public MotionSnapshotBudget? MotionBudget { get; private set; }

    /// <summary>Gets the sink and shared remote clock used by K3-15's remote player lifecycle.</summary>
    public RemoteMotionSampleAdapter? RemoteMotion { get; private set; }

    partial void ConfigureMotion()
    {
        if (Server is not null)
        {
            MotionBudget = new MotionSnapshotBudget(_env.Time);
        }

        if (Client is not null && LocalPlayer is not null)
        {
            RemoteMotion = new RemoteMotionSampleAdapter(_env.Time);
            LocalPlayer.RemoteMotion = RemoteMotion;
            _fullSnapshots?.AddSink(RemoteMotion);
            Client.AddListener(RemoteMotion);
        }
    }
}
