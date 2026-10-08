using System.Numerics;
using System.Text;

using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Checks the FsCheck generators themselves (they need no codec): they must cover every case and produce only values that are valid
/// according to K1-03 and inside the <see cref="ProtocolConstants"/> limits.
/// </summary>
public sealed class GeneratorSanityTests
{
    private const int Samples = 3000;

    private static T[] Draw<T>(Gen<T> gen, int count = Samples) => gen.Sample(count, 50).ToArray();

    [Fact]
    public void Messages_CoverAllTwentyTypes() =>
        Assert.Equal(Enum.GetValues<MessageType>().Order(), Draw(CodecGen.Message).Select(m => m.Type).Distinct().Order());

    [Fact]
    public void Operations_CoverAllSevenKinds() =>
        Assert.Equal(Enum.GetValues<OperationKind>().Order(), Draw(CodecGen.Operation).Select(o => o.Kind).Distinct().Order());

    [Fact]
    public void Intents_CoverAllKindsAndBothBlockTargets()
    {
        var intents = Draw(CodecGen.Intent);

        Assert.Contains(intents, i => i is TerrainToolIntent);
        Assert.Contains(intents, i => i is RemoveBlockIntent);
        Assert.Contains(intents, i => i is PlaceBlockIntent { Target: ConstructFaceTarget });
        Assert.Contains(intents, i => i is PlaceBlockIntent { Target: TerrainTarget });
    }

    [Fact]
    public void Quaternions_AreNormalized()
    {
        foreach (var q in Draw(CodecGen.UnitQuaternion))
        {
            Assert.InRange(q.Length(), 0.99999f, 1.00001f);
        }
    }

    [Fact]
    public void Floats_AreFinite()
    {
        foreach (var v in Draw(CodecGen.Vector))
        {
            Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
        }

        foreach (var v in Draw(CodecGen.HalfVector))
        {
            Assert.True(Math.Abs(v.X) <= 65504f && Math.Abs(v.Y) <= 65504f && Math.Abs(v.Z) <= 65504f);
        }
    }

    [Fact]
    public void Snapshots_HaveDistinctFramesFrameIndexesInsideTheTableAndDefaultsForAbsentFields()
    {
        foreach (var s in Draw(CodecGen.Snapshot, 1000))
        {
            Assert.InRange(s.Frames.Count, 1, ProtocolConstants.MaxFramesPerSnapshot);
            Assert.Equal(s.Frames.Count, s.Frames.Select(f => f.Frame).Distinct().Count());
            Assert.InRange(s.Entities.Count, 0, ProtocolConstants.MaxEntitiesPerSnapshot);
            foreach (var e in s.Entities)
            {
                AssertDefaults(e);
                Assert.True(e.FrameIndex < s.Frames.Count);
            }
        }
    }

    [Fact]
    public void EntitySpawns_UseFrameIndexZeroAndDefaultsForAbsentFields()
    {
        foreach (var spawn in Draw(CodecGen.EntitySpawn, 1000))
        {
            Assert.Equal(0, spawn.Initial.FrameIndex);
            Assert.True(spawn.KindData.Length <= ProtocolConstants.MaxEntityKindDataBytes);
            AssertDefaults(spawn.Initial);
        }
    }

    [Fact]
    public void ConstructStates_HaveDistinctChunksOwnedByTheHeaderAndValidParts()
    {
        foreach (var state in Draw(CodecGen.ConstructState, 300))
        {
            Assert.InRange(state.Chunks.Count, 0, ProtocolConstants.MaxChunksPerConstructStatePart);
            Assert.True(state.PartCount >= 1 && state.PartIndex < state.PartCount);
            Assert.Equal(state.Chunks.Count, state.Chunks.Select(c => c.Key.Chunk).Distinct().Count());
            Assert.All(state.Chunks, c =>
            {
                Assert.Equal(state.Header.Id, c.Key.Construct);
                Assert.Equal(4096, c.Blocks.Length);
                Assert.All(c.Blocks, b => Assert.True(new Core.World.Constructs.BlockState(b).IsWellFormed));
            });
        }
    }

    [Fact]
    public void ChunkBlocks_IncludeUniformSparseAndWorstCaseAlternatingShapes()
    {
        var chunks = Draw(CodecGen.ChunkBlocks, 600);
        int Runs(uint[] blocks) => 1 + Enumerable.Range(1, blocks.Length - 1).Count(i => blocks[i] != blocks[i - 1]);

        Assert.Contains(chunks, c => Runs(c) == 1);
        Assert.Contains(chunks, c => Runs(c) == 4096);
        Assert.Contains(chunks, c => Runs(c) is > 1 and < 100);
        Assert.Contains(chunks, c => Runs(c) is > 1000 and < 4096);
    }

    [Fact]
    public void OperationApplied_ChangesAreStrictlyAscending()
    {
        foreach (var m in Draw(CodecGen.OperationApplied, 500))
        {
            Assert.InRange(m.ConstructChanges.Count, 0, ProtocolConstants.MaxConstructChangesPerOperation);
            for (var i = 1; i < m.ConstructChanges.Count; i++)
            {
                Assert.True(m.ConstructChanges[i - 1].Construct.Value < m.ConstructChanges[i].Construct.Value);
            }
        }
    }

    [Fact]
    public void Strings_StayInsideTheirByteLimits()
    {
        foreach (var hello in Draw(CodecGen.ClientHello, 1000))
        {
            Assert.True(Encoding.UTF8.GetByteCount(hello.DisplayName) <= ProtocolConstants.MaxDisplayNameBytes);
            Assert.True(Encoding.UTF8.GetByteCount(hello.Password) <= ProtocolConstants.MaxPasswordBytes);
            Assert.True(hello.AuthTicket.Length <= ProtocolConstants.MaxAuthTicketBytes);
        }

        foreach (var reject in Draw(CodecGen.HandshakeReject, 1000))
        {
            Assert.True(Encoding.UTF8.GetByteCount(reject.Detail) <= ProtocolConstants.MaxRejectDetailBytes);
        }

        foreach (var planet in Draw(CodecGen.Planet, 1000))
        {
            var bytes = Encoding.UTF8.GetByteCount(planet.GeneratorProfile);
            Assert.InRange(bytes, 1, ProtocolConstants.MaxRegistryKeyBytes);
        }
    }

    [Fact]
    public void Texts_ReachTheirMaximumLengthSometimes() =>
        Assert.Contains(Draw(CodecGen.Text(0, 32)), t => Encoding.UTF8.GetByteCount(t) == 32);

    [Fact]
    public void InputBatches_HaveOneToFiveInputsWithAxesInRange()
    {
        foreach (var batch in Draw(CodecGen.InputBatch, 1000))
        {
            Assert.InRange(batch.Inputs.Count, 1, ProtocolConstants.MaxInputsPerBatch);
            Assert.All(batch.Inputs, i => Assert.True(Math.Abs(i.MoveX) <= 1f && Math.Abs(i.MoveY) <= 1f && ((int)i.Buttons & ~0x1F) == 0));
        }
    }

    [Fact]
    public void ServerHellos_RegistryIdsAreInRange()
    {
        foreach (var hello in Draw(CodecGen.ServerHello, 300))
        {
            Assert.All(hello.BlockTypes, e => Assert.InRange(e.Id, 1, ProtocolConstants.MaxBlockTypes));
            Assert.All(hello.Materials, e => Assert.InRange(e.Id, 1, ProtocolConstants.MaxMaterials));
            Assert.InRange(hello.Planets.Count, 0, ProtocolConstants.MaxPlanetsInHello);
        }
    }

    private static void AssertDefaults(EntityState e)
    {
        if (!e.Fields.HasFlag(EntityStateFields.Position))
        {
            Assert.Equal(Vector3.Zero, e.Position);
        }

        if (!e.Fields.HasFlag(EntityStateFields.Rotation))
        {
            Assert.Equal(Quaternion.Identity, e.Rotation);
        }

        if (!e.Fields.HasFlag(EntityStateFields.Velocity))
        {
            Assert.Equal(Vector3.Zero, e.Velocity);
        }

        if (!e.Fields.HasFlag(EntityStateFields.MotionFlags))
        {
            Assert.Equal(0, e.MotionFlags);
        }
    }
}
