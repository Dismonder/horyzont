using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Integration.Tests.Tools;

internal static class ToolTestHelpers
{
    public static ToolInputFrame Input(
        bool primaryPressed = false, bool primaryHeld = false, bool secondaryPressed = false, bool secondaryHeld = false, ToolKind? select = null,
        int radius = 0, int palette = 0, int material = 0, int yaw = 0, int up = 0) =>
        new(primaryPressed, primaryHeld, secondaryPressed, secondaryHeld, select, radius, palette, material, yaw, up);

    /// <summary>Point of the terrain sphere at a given distance from a point (bisection over the x coordinate, z = 0).</summary>
    public static Vector3 GroundAtDistance(ToolRig rig, Vector3 from, float distance)
    {
        float low = 0f, high = 14f;
        for (var i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2f;
            if (Vector3.Distance(from, rig.Ground(mid)) < distance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return rig.Ground(low);
    }

    public static Vector3 Direction(Vector3 from, Vector3 to) => Vector3.Normalize(to - from);

    public static TerrainToolIntent Dig(ToolRig rig, ToolRig.ToolClient client, Vector3 target, float radius = 1f, float originShift = 0f)
    {
        var eye = client.Eye;
        var direction = Direction(eye, target);
        return new TerrainToolIntent(WorldFactory.HomePlanet, eye + (direction * originShift), direction, radius, TerrainEditMode.Remove, 0);
    }

    public static PlaceBlockIntent PlaceOnTerrain(ToolRig rig, ToolRig.ToolClient client, Vector3 target, Vector3? look = null, float originShift = 0f)
    {
        var eye = client.Eye;
        var direction = Direction(eye, target);
        rig.Sim.State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        return new PlaceBlockIntent(
            new TerrainTarget(WorldFactory.HomePlanet, eye + (direction * originShift), direction, look ?? -Vector3.UnitZ), stone, 0);
    }

    /// <summary>Submits one raw intent, pumps until its reply arrives and returns the reason.</summary>
    public static RejectReason Raw(ToolRig rig, ToolRig.ToolClient client, Intent intent)
    {
        var seq = client.SubmitRaw(intent);
        Assert.True(rig.RunUntil(() => client.Replies.Exists(r => r.ClientSeq == seq)), "no reply to the raw intent");
        return client.Replies.Find(r => r.ClientSeq == seq)!.Reason;
    }

    public static EntityId AddConstruct(ToolRig rig, Vector3 origin, Quaternion? rotation = null)
    {
        rig.Sim.State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        var id = rig.Sim.State.EntityIds.PeekNext();
        rig.Sim.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(WorldFactory.HomePlanet), origin, rotation ?? Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(stone, 0)));
        return id;
    }

    public static List<ulong> EditTicks(ToolRig rig) =>
        [.. rig.Journal.Entries.Where(e => e.Operation is TerrainEditOp).Select(e => e.Tick.Value)];

    public static void AssertEveryWindowAtMost(IReadOnlyList<ulong> ticks, ulong firstTick, int length, int window, int max)
    {
        for (var start = firstTick; start + (ulong)window <= firstTick + (ulong)length; start++)
        {
            var count = ticks.Count(t => t >= start && t < start + (ulong)window);
            Assert.True(count <= max, $"{count} edits in the {window} ticks from {start}");
        }
    }
}
