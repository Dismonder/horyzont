using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Client.Tools;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Replication;

public sealed class RemoteToolWorldViewTests
{
    private static readonly PlanetId _planet = new(1);
    private static readonly EntityId _construct = new(5);
    private static readonly PlayerId _self = new(1);

    private static (RemoteToolWorldView View, ConstructMirror Constructs, EntityMirror Entities) Create()
    {
        var world = WorldFactory.CreateDefault();
        var hello = new ServerHello(world.Id, world.CurrentTick, 60, _self, new EntityId(1), world.Planets.All, [], []);
        var constructs = new ConstructMirror(new FakeTimeProvider());
        var entities = new EntityMirror();
        return (new RemoteToolWorldView(hello, NoRemoteTerrain.Instance, constructs, entities), constructs, entities);
    }

    private static uint[] Blocks(params CellPos[] cells)
    {
        var blocks = new uint[4096];
        foreach (var cell in cells)
        {
            blocks[cell.LocalIndex] = BlockState.Create(1, 0).Packed;
        }

        return blocks;
    }

    [Fact]
    public void RemoteTools_BuildAndClear_UsesMirrorForRaysPoseOverlapAndPlayers()
    {
        var (view, constructs, entities) = Create();
        var header = new ConstructHeaderRecord(_construct, FrameRef.Of(_planet), 0, ConstructMode.Static,
            new Vector3(0, 100, 0), Quaternion.Identity, 3);
        Assert.True(constructs.Apply(new ConstructState(header, 0, 1, [new(new(_construct, default), Blocks(new CellPos(0, 0, 0), new CellPos(1, 0, 0)))])));

        Assert.True(view.TryGetConstruct(_construct, out var value));
        Assert.Equal(2, value.BlockCount);
        Assert.True(view.TryGetConstructPose(_construct, out var origin, out var rotation));
        Assert.Equal(new Vector3(0, 100, 0), origin);
        Assert.Equal(Quaternion.Identity, rotation);

        // A ray along +X from outside hits the first occupied cell on its -X face.
        Assert.True(view.RaycastConstructs(_planet, new Vector3(-5, 100.5f, 0.5f), Vector3.UnitX, 20, out var hit));
        Assert.Equal(_construct, hit.Construct);
        Assert.Equal(new CellPos(0, 0, 0), hit.Cell);
        Assert.False(view.RaycastConstructs(_planet, new Vector3(-5, 105.5f, 0.5f), Vector3.UnitX, 20, out _));

        Assert.True(view.OverlapsConstruct(_planet, new Vector3(0.5f, 100.5f, 0.5f), Quaternion.Identity, null));
        Assert.False(view.OverlapsConstruct(_planet, new Vector3(0.5f, 100.5f, 0.5f), Quaternion.Identity, _construct));
        Assert.False(view.OverlapsConstruct(_planet, new Vector3(5.5f, 100.5f, 0.5f), Quaternion.Identity, null));

        // Other players come from the entity mirror; the local player is left out.
        var frame = new SnapshotFrame(FrameRef.Of(_planet), 0);
        entities.Apply(new EntitySpawn(new EntityId(1), EntityKind.Player, 0, _self, frame,
            new EntityState(new EntityId(1), (EntityStateFields)15, 0, 0, Vector3.One, Quaternion.Identity, Vector3.Zero, 0), []));
        entities.Apply(new EntitySpawn(new EntityId(2), EntityKind.Player, 0, new PlayerId(2), frame,
            new EntityState(new EntityId(2), (EntityStateFields)15, 0, 0, new Vector3(3, 4, 5), Quaternion.Identity, Vector3.Zero, 0), []));
        var feet = new Vector3[4];
        Assert.Equal(1, view.CopyOtherPlayerFeet(_planet, feet));
        Assert.Equal(new Vector3(3, 4, 5), feet[0]);

        // An edit and a removal of the last block are followed through the mirror.
        Assert.True(constructs.Apply(new OperationApplied(new OpId(9), default,
            new Horyzont.Core.Operations.BlockClearOp(_construct, new CellPos(0, 0, 0)), [new(_construct, 4, false)])));
        Assert.False(view.RaycastConstructs(_planet, new Vector3(-5, 100.5f, 0.5f), Vector3.UnitX, 0.5f, out _));
        Assert.True(constructs.Apply(new OperationApplied(new OpId(10), default,
            new Horyzont.Core.Operations.BlockClearOp(_construct, new CellPos(1, 0, 0)), [new(_construct, 5, true)])));
        Assert.False(view.TryGetConstruct(_construct, out _));
    }

    [Fact]
    public void RemoteToolWorldView_WarmedUp10000Queries_AllocatesZeroBytes()
    {
        var (view, constructs, entities) = Create();
        var header = new ConstructHeaderRecord(_construct, FrameRef.Of(_planet), 0, ConstructMode.Static,
            new Vector3(0, 100, 0), Quaternion.Identity, 3);
        constructs.Apply(new ConstructState(header, 0, 1, [new(new(_construct, default), Blocks(new CellPos(0, 0, 0), new CellPos(1, 0, 0)))]));
        entities.Apply(new EntitySpawn(new EntityId(2), EntityKind.Player, 0, new PlayerId(2), new SnapshotFrame(FrameRef.Of(_planet), 0),
            new EntityState(new EntityId(2), (EntityStateFields)15, 0, 0, Vector3.One, Quaternion.Identity, Vector3.Zero, 0), []));
        var feet = new Vector3[4];

        void Query()
        {
            view.RaycastConstructs(_planet, new Vector3(-5, 100.5f, 0.5f), Vector3.UnitX, 20, out _);
            view.OverlapsConstruct(_planet, new Vector3(0.5f, 100.5f, 0.5f), Quaternion.Identity, null);
            view.TryGetConstructPose(_construct, out _, out _);
            view.TryGetConstruct(_construct, out _);
            view.CopyOtherPlayerFeet(_planet, feet);
        }

        for (var i = 0; i < 200; i++)
        {
            Query();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            Query();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void CopyOtherPlayerFeet_PlayerInAConstructFrame_IsConvertedToThePlanetFrame()
    {
        var (view, constructs, entities) = Create();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var header = new ConstructHeaderRecord(_construct, FrameRef.Of(_planet), 0, ConstructMode.Static,
            new Vector3(0, 100, 0), rotation, 1);
        Assert.True(constructs.Apply(new ConstructState(header, 0, 1, [])));
        entities.Apply(new EntitySpawn(new EntityId(2), EntityKind.Player, 0, new PlayerId(2),
            new SnapshotFrame(FrameRef.Of(_construct), 0),
            new EntityState(new EntityId(2), (EntityStateFields)15, 0, 0, new Vector3(1, 2, 0), Quaternion.Identity, Vector3.Zero, 0), []));
        var feet = new Vector3[4];
        Assert.Equal(1, view.CopyOtherPlayerFeet(_planet, feet));
        var expected = new Vector3(0, 100, 0) + Vector3.Transform(new Vector3(1, 2, 0), rotation);
        Assert.True(Vector3.Distance(expected, feet[0]) < 1e-4f);
    }
}
