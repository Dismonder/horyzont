using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Progress;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Hashing;

/// <summary>
/// XxHash64 of a canonical byte stream of the state: exactly what checkpoints plus journal replay restore.
/// Excludes CurrentTick, the kinematics of online players (the pose of offline players is hashed) and the pose of <see cref="ConstructMode.Ship"/> constructs (they change
/// outside the journal) and terrain voxels (outside Core; only block versions are hashed). Floats are hashed bitwise.
/// The survival section (item types, next container id, containers) is appended only when the world has item types or containers or a
/// container counter other than 1, so the hash of a world without items is the same as before K6-29 (D7 of K6-29W). K6-30 (D22) appends
/// block states (0x42) and respawn points (0x50) to that section, each only when it has entries, and also makes them a reason for the section.
/// K6-31 (D15) appends the harvested props (0x48) after them under the same rule.
/// K6-32 (D22) adds the station state to 0x42, and after 0x48 the discoveries (0x44) and the progress keys (0x4B), each only when non-empty; the keys are a
/// reason for the section too (discoveries live in progress records, which already are). A world without them hashes as before. K6-33 (D7) appends the
/// food slots (0x46) after them, only when a player has food; the vitals (health, stamina) are never hashed, online or offline. K6-35 (D22) appends the persistent
/// entities (0x54) and the settings of the world (0x57, only when they are not the defaults) at the very end; both are a reason for the section too.
/// </summary>
public static class StateHasher
{
    private const byte SurvivalSectionTag = 0x53;
    private const byte BlockEntitiesTag = 0x42;
    private const byte ProgressTag = 0x50;
    private const byte HarvestedPropsTag = 0x48;
    private const byte DiscoveriesTag = 0x44;
    private const byte ProgressKeysTag = 0x4B;
    private const byte FoodTag = 0x46;
    private const byte PersistentEntitiesTag = 0x54;
    private const byte SettingsTag = 0x57;

    /// <summary>Hashes a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The hash.</returns>
    public static ulong Hash(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hash = new XxHash64();
        Span<byte> guid = stackalloc byte[16];
        state.Id.Value.TryWriteBytes(guid);
        hash.Append(guid);
        U64(hash, state.GalaxySeed);
        U64(hash, state.EntityIds.NextValue);
        U64(hash, state.PlayerIds.NextValue);
        U64(hash, state.LastOpId.Value);

        Registry(hash, state.BlockTypes.Entries);
        Registry(hash, state.Materials.Entries);

        U32(hash, (uint)state.Planets.All.Count);
        foreach (var planet in state.Planets.All)
        {
            U64(hash, planet.Id.Value);
            U64(hash, planet.System.Value);
            U64(hash, planet.Seed);
            Text(hash, planet.GeneratorProfile);
            F32(hash, planet.RadiusMeters);
            F32(hash, planet.VoxelSizeMeters);
            F32(hash, planet.Gravity);
            F32(hash, planet.AtmosphereHeightMeters);
            F32(hash, planet.CoreDepthMeters);
            F32(hash, planet.DayLengthSeconds);
        }

        // K5-21 decision 9: the star system section exists only when the world has one, so worlds without it hash as before.
        if (state.StarSystem is { } system)
        {
            StarSystem(hash, system);
        }

        U32(hash, (uint)state.Constructs.Count);
        foreach (var construct in state.Constructs.All)
        {
            U64(hash, construct.Id.Value);
            Frame(hash, construct.Parent);
            U32(hash, construct.ParentSeq);
            U32(hash, (uint)construct.Mode);
            if (construct.Mode == ConstructMode.Static)
            {
                Vec(hash, construct.Origin);
                Quat(hash, construct.Rotation);
            }

            U32(hash, construct.Version);
            U32(hash, (uint)construct.ChunkCount);
            foreach (var chunk in construct.Chunks)
            {
                U32(hash, (uint)chunk.Key.X);
                U32(hash, (uint)chunk.Key.Y);
                U32(hash, (uint)chunk.Key.Z);
                Blocks(hash, chunk.Value.Blocks);
            }
        }

        U32(hash, (uint)state.TerrainVersions.Count);
        foreach (var version in state.TerrainVersions.All)
        {
            U64(hash, version.Key.Planet.Value);
            U32(hash, version.Key.Lod);
            U32(hash, (uint)version.Key.X);
            U32(hash, (uint)version.Key.Y);
            U32(hash, (uint)version.Key.Z);
            U32(hash, version.Version);
        }

        U32(hash, (uint)state.Players.Count);
        foreach (var player in state.Players.All)
        {
            U64(hash, player.Player.Value);
            U32(hash, (uint)player.Identity.Platform);
            U64(hash, player.Identity.Value);
            Text(hash, player.DisplayName);
            U64(hash, player.Entity.Value);
            Frame(hash, player.Frame);
            U32(hash, player.ParentSeq);
            if (player.Seat is { } seat)
            {
                U32(hash, 1);
                U32(hash, (uint)seat.X);
                U32(hash, (uint)seat.Y);
                U32(hash, (uint)seat.Z);
            }
            else
            {
                U32(hash, 0);
            }

            U32(hash, player.IsOnline ? 1u : 0u);
            if (!player.IsOnline)
            {
                // An offline pose is deterministic (set only by PlayerLeaveOp, PlayerJoinOp and EntityReparentOp, restored from the
                // checkpoint); an online pose comes from physics outside the journal and stays out of the hash.
                Vec(hash, player.Position);
                Quat(hash, player.BodyRotation);
                F32(hash, player.Pitch);
            }
        }

        // K5-27 decision 7: the marker section exists only when a marker was ever created, so older worlds hash as before.
        if (state.MarkerIds.NextValue > 1)
        {
            U64(hash, state.MarkerIds.NextValue);
            U32(hash, (uint)state.Markers.Count);
            foreach (var marker in state.Markers.All)
            {
                U64(hash, marker.Id.Value);
                if (marker.Owner is { } owner)
                {
                    U32(hash, 1);
                    U64(hash, owner.Value);
                }
                else
                {
                    U32(hash, 0);
                }

                U64(hash, marker.Planet.Value);
                Vec(hash, marker.Position);
                Text(hash, marker.Name);
                U32(hash, (uint)marker.Icon);
                U32(hash, (uint)marker.Color);
            }
        }

        if (state.ItemTypes.Entries.Count > 0 || state.Containers.Count > 0 || state.ContainerIds.NextValue != 1
            || state.BlockEntities.Count > 0 || state.Progress.Count > 0 || state.HarvestedProps.Count > 0 || state.ProgressKeys.Count > 0
            || state.PersistentEntities.Count > 0 || !state.Settings.IsDefault)
        {
            Survival(hash, state);
        }

        return hash.GetCurrentHashAsUInt64();
    }

    private static void Survival(XxHash64 hash, WorldState state)
    {
        Span<byte> tag = [SurvivalSectionTag];
        hash.Append(tag);
        Registry(hash, state.ItemTypes.Entries);
        U64(hash, state.ContainerIds.NextValue);
        U32(hash, (uint)state.Containers.Count);
        foreach (var container in state.Containers.All)
        {
            U64(hash, container.Id.Value);
            U32(hash, (uint)container.Kind);
            U32(hash, (uint)container.Host.Kind);
            U64(hash, container.Host.Id);
            U32(hash, (uint)container.Host.Cell.X);
            U32(hash, (uint)container.Host.Cell.Y);
            U32(hash, (uint)container.Host.Cell.Z);
            U64(hash, container.Host.Site);
            U64(hash, container.Host.Number);
            U64(hash, container.Owner.Value);
            U32(hash, (uint)container.Access);
            U32(hash, (uint)container.Size);
            U32(hash, container.Version);
            var slots = container.Slots;
            for (var i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty)
                {
                    U32(hash, (uint)i);
                    U32(hash, slots[i].Type.Value);
                    U32(hash, slots[i].Count);
                }
            }

            U32(hash, uint.MaxValue);
        }

        // K6-30 (D22): block states and respawn points, each subsection only when it has entries, so older worlds hash as before.
        if (state.BlockEntities.Count > 0)
        {
            Span<byte> entitiesTag = [BlockEntitiesTag];
            hash.Append(entitiesTag);
            U32(hash, (uint)state.BlockEntities.Count);
            foreach (var (key, entity) in state.BlockEntities.Sorted)
            {
                U64(hash, key.Construct.Value);
                Cell(hash, key.Cell);
                U32(hash, (uint)entity.Kind);
                switch (entity)
                {
                    case ChestBlockState chest:
                        U64(hash, chest.Container.Value);
                        break;
                    case BedBlockState bed:
                        U64(hash, bed.Owner.Value);
                        break;
                    case StationBlockState station:
                        U64(hash, station.Input.Value);
                        U64(hash, station.Output.Value);
                        Text(hash, station.Recipe);
                        if (station.Started is { } started)
                        {
                            U32(hash, 1);
                            U64(hash, started.Value);
                        }
                        else
                        {
                            U32(hash, 0);
                        }

                        break;
                    case OxygenGeneratorBlockState generator:
                        // K6-34 (D12): the shape of a station without an output and a recipe; worlds without a generator hash as before.
                        U64(hash, generator.Input.Value);
                        if (generator.Started is { } burning)
                        {
                            U32(hash, 1);
                            U64(hash, burning.Value);
                        }
                        else
                        {
                            U32(hash, 0);
                        }

                        break;
                }
            }
        }

        if (state.Progress.Count > 0)
        {
            Span<byte> progressTag = [ProgressTag];
            hash.Append(progressTag);
            U32(hash, (uint)state.Progress.Count);
            foreach (var (player, progress) in state.Progress.Sorted)
            {
                U64(hash, player.Value);
                if (progress.RespawnBed is { } bed)
                {
                    U32(hash, 1);
                    U64(hash, bed.Construct.Value);
                    Cell(hash, bed.Cell);
                    U32(hash, bed.ExpectedType);
                }
                else
                {
                    U32(hash, 0);
                }
            }
        }

        // K6-31 (D15): harvested props, sorted by planet and prop; only when there are any, so worlds without them hash as before.
        if (state.HarvestedProps.Count > 0)
        {
            Span<byte> propsTag = [HarvestedPropsTag];
            hash.Append(propsTag);
            U32(hash, (uint)state.HarvestedProps.Count);
            var props = state.HarvestedProps.All;
            for (var i = 0; i < props.Count; i++)
            {
                U64(hash, props[i].Planet.Value);
                U64(hash, props[i].Prop.Value);
                U64(hash, props[i].Tick.Value);
            }
        }

        // K6-32 (D22): the discoveries of the players that have any.
        var discoverers = 0;
        foreach (var (_, progress) in state.Progress.Sorted)
        {
            discoverers += progress.Discovered is null ? 0 : 1;
        }

        if (discoverers > 0)
        {
            Span<byte> discoveriesTag = [DiscoveriesTag];
            hash.Append(discoveriesTag);
            U32(hash, (uint)discoverers);
            foreach (var (player, progress) in state.Progress.Sorted)
            {
                if (progress.Discovered is { } discovered)
                {
                    U64(hash, player.Value);
                    U32(hash, (uint)discovered.Bytes.Length);
                    hash.Append(discovered.Bytes);
                }
            }
        }

        if (state.ProgressKeys.Count > 0)
        {
            Span<byte> keysTag = [ProgressKeysTag];
            hash.Append(keysTag);
            U32(hash, (uint)state.ProgressKeys.Count);
            foreach (var key in state.ProgressKeys.Sorted)
            {
                Text(hash, key);
            }
        }

        // K6-33 (D7): the food slots of the players that have any. 0x46 stays after 0x48, 0x44 and 0x4B; a later package appends its subsection after
        // 0x46.
        var eaters = 0;
        foreach (var (_, progress) in state.Progress.Sorted)
        {
            eaters += progress.Food.IsEmpty ? 0 : 1;
        }

        if (eaters > 0)
        {
            Span<byte> foodTag = [FoodTag];
            hash.Append(foodTag);
            U32(hash, (uint)eaters);
            foreach (var (player, progress) in state.Progress.Sorted)
            {
                if (progress.Food.IsEmpty)
                {
                    continue;
                }

                U64(hash, player.Value);
                for (var i = 0; i < FoodSlots.Count; i++)
                {
                    var slot = progress.Food[i];
                    U32(hash, slot.Type.Value);
                    U64(hash, slot.Eaten.Value);
                }
            }
        }

        // K6-35 (D22): the persistent entities (0x54), sorted by id, and the settings of the world (0x57), each only when it exists or differs from the defaults, after 0x46.
        if (state.PersistentEntities.Count > 0)
        {
            Span<byte> entitiesTag = [PersistentEntitiesTag];
            hash.Append(entitiesTag);
            U32(hash, (uint)state.PersistentEntities.Count);
            foreach (var entity in state.PersistentEntities.All)
            {
                U64(hash, entity.Id.Value);
                U32(hash, (uint)entity.Kind);
                Frame(hash, entity.Frame);
                U32(hash, entity.ParentSeq);
                Vec(hash, entity.Position);
                Quat(hash, entity.Rotation);
                U64(hash, entity.Owner.Value);
            }
        }

        if (!state.Settings.IsDefault)
        {
            var settings = state.Settings;
            Span<byte> settingsTag = [SettingsTag];
            hash.Append(settingsTag);
            U32(hash, (settings.TeleportAll ? 1u : 0u) | (settings.FreeFuel ? 2u : 0u) | (settings.FreeBuilding ? 4u : 0u));
            U32(hash, settings.ResourceMultiplierPercent);
            U32(hash, settings.StackMultiplierPercent);
            U32(hash, settings.HazardIntensityPercent);
            U32(hash, (uint)settings.BuildingIntegrity);
            U32(hash, (uint)settings.DeathPenalty);
        }
    }

    private static void Cell(XxHash64 hash, CellPos cell)
    {
        U32(hash, (uint)cell.X);
        U32(hash, (uint)cell.Y);
        U32(hash, (uint)cell.Z);
    }

    private static void Registry(XxHash64 hash, IReadOnlyList<RegistryEntry> entries)
    {
        U32(hash, (uint)entries.Count);
        foreach (var entry in entries)
        {
            U32(hash, entry.Id);
            Text(hash, entry.Key);
        }
    }

    private static void Frame(XxHash64 hash, FrameRef frame)
    {
        U32(hash, (uint)frame.Kind);
        U64(hash, frame.Id);
    }

    private static void Blocks(XxHash64 hash, ReadOnlySpan<uint> blocks)
    {
        if (BitConverter.IsLittleEndian)
        {
            hash.Append(MemoryMarshal.AsBytes(blocks));
            return;
        }

        foreach (var value in blocks)
        {
            U32(hash, value);
        }
    }

    private static void Text(XxHash64 hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        U32(hash, (uint)bytes.Length);
        hash.Append(bytes);
    }

    private static void Vec(XxHash64 hash, Vector3 v)
    {
        F32(hash, v.X);
        F32(hash, v.Y);
        F32(hash, v.Z);
    }

    private static void Quat(XxHash64 hash, Quaternion q)
    {
        F32(hash, q.X);
        F32(hash, q.Y);
        F32(hash, q.Z);
        F32(hash, q.W);
    }

    private static void StarSystem(XxHash64 hash, StarSystemParams system)
    {
        U64(hash, system.System.Value);
        Span<byte> one = stackalloc byte[1];
        one[0] = system.LayoutVersion;
        hash.Append(one);
        U64(hash, system.HomePlanet.Value);
        F32(hash, system.SunCycleSeconds);
        U32(hash, (uint)system.Bodies.Count);
        foreach (var body in system.Bodies)
        {
            U64(hash, body.Planet.Value);
            one[0] = body.BodyIndex;
            hash.Append(one);
            F64(hash, body.Pose.Position.X);
            F64(hash, body.Pose.Position.Y);
            F64(hash, body.Pose.Position.Z);
            F64(hash, body.Pose.Rotation.X);
            F64(hash, body.Pose.Rotation.Y);
            F64(hash, body.Pose.Rotation.Z);
            F64(hash, body.Pose.Rotation.W);
        }
    }

    private static void F64(XxHash64 hash, double value) => U64(hash, BitConverter.DoubleToUInt64Bits(value));

    private static void F32(XxHash64 hash, float value) => U32(hash, BitConverter.SingleToUInt32Bits(value));

    private static void U32(XxHash64 hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.Append(bytes);
    }

    private static void U64(XxHash64 hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        hash.Append(bytes);
    }
}
