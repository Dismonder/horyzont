using System.Numerics;

using Horyzont.Server.Space;

namespace Horyzont.Server.Tests.Space;

public sealed class TerrainColliderConfirmationsTests
{
    private static readonly TerrainBlockBox _box = new(0, 0, 0, 1, 0, 0);

    [Fact]
    public void AllActive_MeshedBlockWhoseColliderIsStillDisabled_IsNotReady()
    {
        // The descent race (K5-22 Y11): Voxel Tools marks a LOD 0 block meshed and builds its collider disabled; it enables the collider
        // only after all 8 siblings under its LOD 1 parent loaded, one update task later. Meshed alone must not read as Ready.
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe();
        probe.Set(1, 0, 0, TerrainBlockCollider.Disabled);

        Assert.False(confirmations.AllActive(_box, 10, ref probe));

        probe.Set(1, 0, 0, TerrainBlockCollider.Active);
        Assert.True(confirmations.AllActive(_box, 11, ref probe));
    }

    [Fact]
    public void AllActive_BlocksWithoutSurface_NeedNoCollider()
    {
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe { Default = TerrainBlockCollider.Absent };

        Assert.True(confirmations.AllActive(_box, 1, ref probe));
    }

    [Fact]
    public void AllActive_SeenEveryFrame_ProbesEachBlockOnce()
    {
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe();

        for (ulong frame = 5; frame < 50; frame++)
        {
            Assert.True(confirmations.AllActive(_box, frame, ref probe));
            Assert.True(confirmations.AllActive(_box, frame, ref probe));
        }

        Assert.Equal(2, probe.Calls);
        Assert.Equal(2, confirmations.Probes);
    }

    [Fact]
    public void AllActive_BlockNotSeenForAFrame_IsProbedAgain()
    {
        // Between two frames without a query a block may have been unloaded and loaded again, meshed with a disabled collider.
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe();
        Assert.True(confirmations.AllActive(_box, 1, ref probe));

        probe.Set(1, 0, 0, TerrainBlockCollider.Disabled);
        Assert.True(confirmations.AllActive(_box, 2, ref probe));
        Assert.False(confirmations.AllActive(_box, 4, ref probe));
        Assert.Equal(4, probe.Calls);
    }

    [Fact]
    public void AllActive_FailedBlock_IsNotConfirmed()
    {
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe();
        probe.Set(0, 0, 0, TerrainBlockCollider.Disabled);

        Assert.False(confirmations.AllActive(_box, 1, ref probe));
        Assert.False(confirmations.AllActive(_box, 1, ref probe));
        Assert.False(confirmations.AllActive(_box, 2, ref probe));
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public void AllActive_EmptyBox_IsActive()
    {
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe { Default = TerrainBlockCollider.Disabled };

        Assert.True(confirmations.AllActive(new TerrainBlockBox(1, 0, 0, 0, 0, 0), 1, ref probe));
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void AllActive_SteadyState_DoesNotAllocate()
    {
        var confirmations = new TerrainColliderConfirmations();
        var probe = new FakeProbe();
        var box = new TerrainBlockBox(-2, -2, -2, 3, 3, 3);
        Assert.True(confirmations.AllActive(box, 1, ref probe));
        Assert.True(confirmations.AllActive(box, 2, ref probe));

        // Both sets are warm. Tiered JIT promotion may still allocate once under concurrent test load;
        // a steady-state allocation occurs in every window, so the smallest complete window must be zero.
        var smallest = long.MaxValue;
        ulong frame = 3;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var allActive = true;
            for (var i = 0; i < 197; i++, frame++)
            {
                allActive &= confirmations.AllActive(box, frame, ref probe);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(allActive);
        }

        Assert.Equal(0, smallest);
    }

    [Fact]
    public void OfVoxelArea_MirrorsVoxelToolsIsAreaMeshed()
    {
        // Box3i(aabb.position, aabb.size) truncates toward zero, then downscaled(16): floordiv of the min and of max - 1.
        var box = TerrainBlockBox.OfVoxelArea(new Vector3(-20.5f, 3.9f, 0f), new Vector3(28f, 28.7f, 32f), 16);

        Assert.Equal(new TerrainBlockBox(-2, 0, 0, 0, 1, 1), box);
        Assert.True(TerrainBlockBox.OfVoxelArea(new Vector3(16f, 5f, 5f), new Vector3(0.5f, 4f, 4f), 16).IsEmpty);
    }

    private struct FakeProbe : ITerrainColliderProbe
    {
        private (int X, int Y, int Z) _key;
        private TerrainBlockCollider _value;
        private bool _has;

        public FakeProbe()
        {
            Default = TerrainBlockCollider.Active;
        }

        public TerrainBlockCollider Default { get; set; }

        public int Calls { get; private set; }

        public void Set(int x, int y, int z, TerrainBlockCollider value)
        {
            _key = (x, y, z);
            _value = value;
            _has = true;
        }

        public TerrainBlockCollider Probe(int x, int y, int z)
        {
            Calls++;
            return _has && _key == (x, y, z) ? _value : Default;
        }
    }
}
