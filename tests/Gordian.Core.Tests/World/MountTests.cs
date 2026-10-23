// tests/Gordian.Core.Tests/World/MountTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Tests.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>Mounts and chocobos (#87): the wire state, the models, the mount list, the mounted menu and Dismount.</summary>
    public class MountTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        // ---- Decoding ----

        [Theory]
        [InlineData(0, 0, MountKind.None, 0)]          // on foot, whatever the kept mount number
        [InlineData(0, 2, MountKind.None, 0)]
        [InlineData(5, 0, MountKind.Chocobo, 0)]       // a chocobo (LandSandBoat xi.animation.CHOCOBO)
        [InlineData(5, 34, MountKind.Chocobo, 33)]     // the Noble Chocobo rides as a chocobo
        [InlineData(85, 2, MountKind.Mount, 1)]        // wire 2 = the raptor (LandSandBoat adds 1 to the client's id)
        [InlineData(85, 31, MountKind.Mount, 30)]      // the red raptor
        [InlineData(85, 1, MountKind.Chocobo, 0)]      // QUEST_RAPTOR: "now appears as a plain chocobo"
        [InlineData(85, 34, MountKind.Chocobo, 33)]
        public void FromWire_DecodesStatusAndMountNumber(byte status, byte wire, MountKind kind, byte id)
        {
            var mount = MountAppearance.FromWire(status, wire, 1);
            Assert.Equal((kind, kind == MountKind.None ? (byte)0 : id), (mount.Kind, mount.MountId));
            Assert.Equal(kind != MountKind.None, mount.IsMounted);
        }

        [Fact]
        public void Dig_OnlyOnAChocobo_PersonalByItsIndex()
        {
            Assert.True(MountAppearance.FromWire(5, 0, 1).CanDig);   // rental
            Assert.True(MountAppearance.FromWire(5, 0, 2).CanDig);   // personal
            Assert.True(MountAppearance.FromWire(5, 0, 2).IsPersonalChocobo);
            Assert.False(MountAppearance.FromWire(5, 0, 1).IsPersonalChocobo);
            Assert.False(MountAppearance.FromWire(85, 2, 2).CanDig); // raptor
            Assert.False(MountAppearance.FromWire(5, 34, 4).CanDig); // Noble Chocobo
        }

        private static byte[] CharStatus(byte status, byte mountId, byte chocoboIndex, ushort speed = 40)
        {
            var p = new byte[0x5C];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(36), (uint)(chocoboIndex << 13) | (100u << 16)); // flags0: Chocobo_Index, hpp
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(40), speed | (50u << 17));                       // flags1: Speed, SpeedBase
            p[44] = status;
            p[0x57] = mountId;
            return p;
        }

        [Fact]
        public void CharStatus_ReadsChocoboIndex_AndTheLocalPlayerRides()
        {
            var status = new S2C_0x037_CharStatus(CharStatus(5, 0, 2));
            Assert.Equal((byte)2, status.ChocoboIndex);
            var local = new LocalPlayerState();
            local.UpdateFromCharStatus(status);
            Assert.True(local.IsMounted);
            Assert.True(local.Mount.IsPersonalChocobo);
            local.UpdateFromCharStatus(new S2C_0x037_CharStatus(CharStatus(0, 2, 0, 50)));
            Assert.False(local.IsMounted);
        }

        [Fact]
        public void CharStatus_PutsTheMountOnTheLocalEntity()
        {
            var world = new WorldState();
            var local = new LocalPlayerState { ServerId = 0x01000001 };
            world.UpsertEntity(new PlayerEntity(0x01000001, 1));
            var dispatcher = new PacketDispatcher();
            var module = new EntityPacketModule(world, local, (_, _) => Task.CompletedTask);
            module.Register(dispatcher);
            dispatcher.Dispatch(new PacketHeader(S2C_0x037_CharStatus.PacketId, 0x5C / 4, 0), CharStatus(85, 2, 2));
            Assert.True(world.TryGetByServerId(0x01000001, out var entity));
            Assert.Equal((MountKind.Mount, (byte)1), (entity!.Mount.Kind, entity.Mount.MountId));
        }

        private static byte[] CharPc(byte status, byte wireMount, byte chocoboIndex)
        {
            var p = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), 0x01000002);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), 0x402);
            p[6] = (byte)EntityUpdateFlags.General;
            p[26] = 100;    // Hpp
            p[27] = status; // server_status
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(28), (uint)chocoboIndex << 5); // Flags1.ChocoboIndex
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x40), (uint)wireMount << 4);   // Flags6.MountIndex
            return p;
        }

        [Fact]
        public void CharPc_PutsTheMountOnAnotherPlayer_AndTheStatusTakesItOff()
        {
            var world = new WorldState();
            var local = new LocalPlayerState { ServerId = 0x01000001 };
            var dispatcher = new PacketDispatcher();
            new EntityPacketModule(world, local, (_, _) => Task.CompletedTask).Register(dispatcher);
            void Send(byte[] p) => dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(p.Length + 4), 1), p);

            Send(CharPc(85, 2, 2));
            Assert.True(world.TryGetByServerId(0x01000002, out var other));
            Assert.Equal((MountKind.Mount, (byte)1, (byte)2), (other!.Mount.Kind, other.Mount.MountId, other.Mount.ChocoboIndex));
            Send(CharPc(0, 2, 0)); // LandSandBoat keeps sending the mount number after a dismount
            Assert.False(other.Mount.IsMounted);
            Send(CharPc(5, 0, 1));
            Assert.Equal(MountKind.Chocobo, other.Mount.Kind);
        }

        /// <summary>
        /// Round 6: another player on a Beetle (mount 10, wire 11) draws the Beetle, and a short update (LandSandBoat's
        /// position-only 0x00D, its other fields left from the last full one) or a General one without Flags6 keeps it, not
        /// "status 85, mount 0" (a chocobo).
        /// </summary>
        [Fact]
        public void CharPc_KeepsAnotherPlayersMountAcrossShortUpdates()
        {
            var world = new WorldState();
            var local = new LocalPlayerState { ServerId = 0x01000001 };
            var dispatcher = new PacketDispatcher();
            new EntityPacketModule(world, local, (_, _) => Task.CompletedTask).Register(dispatcher);
            void Send(byte[] p) => dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(p.Length + 4), 1), p);

            Send(CharPc(85, 11, 3));
            Assert.True(world.TryGetByServerId(0x01000002, out var rider));
            Assert.Equal((MountKind.Mount, (byte)10), (rider!.Mount.Kind, rider.Mount.MountId));

            // A position-only update, 62 bytes, its status byte left at 85 and no Flags6.
            var shortUpdate = CharPc(85, 0, 3).AsSpan(0, 0x3A).ToArray();
            shortUpdate[6] = (byte)EntityUpdateFlags.Position;
            Send(shortUpdate);
            Assert.Equal((MountKind.Mount, (byte)10), (rider.Mount.Kind, rider.Mount.MountId));

            // First seen from a short update (no spawn packet seen first): no mount yet, not a chocobo; the next full update has it.
            var world2 = new WorldState();
            var dispatcher2 = new PacketDispatcher();
            new EntityPacketModule(world2, local, (_, _) => Task.CompletedTask).Register(dispatcher2);
            dispatcher2.Dispatch(new PacketHeader(0x00D, (ushort)(shortUpdate.Length + 4), 1), shortUpdate);
            Assert.True(world2.TryGetByServerId(0x01000002, out var seen));
            Assert.False(seen!.Mount.IsMounted);
            var full = CharPc(85, 11, 3);
            dispatcher2.Dispatch(new PacketHeader(0x00D, (ushort)(full.Length + 4), 2), full);
            Assert.Equal((MountKind.Mount, (byte)10), (seen.Mount.Kind, seen.Mount.MountId));

            // A General update cut before Flags6: the status is read, the mount number kept.
            var noFlags6 = CharPc(85, 0, 3).AsSpan(0, 0x40).ToArray();
            Send(noFlags6);
            Assert.Equal((MountKind.Mount, (byte)10), (rider.Mount.Kind, rider.Mount.MountId));
        }

        [Fact]
        public void LocomotionController_RunsMountedAtTwiceTheServerSpeed()
        {
            var world = new WorldState();
            var local = new LocalPlayerState { ServerId = 0x01000001 };
            var entity = new PlayerEntity(0x01000001, 1) { SpeedBase = 50 };
            world.UpsertEntity(entity);
            var controller = new PlayerLocomotionController(new InputState(), InputProfile.CreateCompact(), world, local);
            local.UpdateFromCharStatus(new S2C_0x037_CharStatus(CharStatus(0, 0, 0, 50)));
            Assert.Equal(50, controller.GetEffectiveRunSpeed(entity));
            // LandSandBoat sends MOUNT_SPEED (80) halved while mounted; retail moves 8 yalms/s (Mounts.mp4).
            local.UpdateFromCharStatus(new S2C_0x037_CharStatus(CharStatus(5, 0, 1, 40)));
            Assert.Equal(80, controller.GetEffectiveRunSpeed(entity));
            Assert.Equal(40, controller.GetEffectiveWalkSpeed(entity));
        }

        [Fact]
        public void Classifier_AMountedWalkIsAWalk()
        {
            var entity = new PlayerEntity(0x01000001, 1) { Hpp = 100, SpeedBase = 50, Speed = 40, Mount = MountAppearance.FromWire(5, 0, 1) };
            Assert.Equal(Gordian.Core.Animation.AnimationCategory.Walk, Gordian.Core.Animation.AnimationStateClassifier.Classify(entity, false, isLocalPlayer: true));
            entity.Speed = 80;
            Assert.Equal(Gordian.Core.Animation.AnimationCategory.Run, Gordian.Core.Animation.AnimationStateClassifier.Classify(entity, false, isLocalPlayer: true));
        }

        // ---- Models ----

        [Fact]
        public void MountFiles_FollowTheMountIdExceptTheChocobo()
        {
            Assert.Equal(-1, MountModels.MountFileId(0));
            Assert.Equal(-1, MountModels.MountFileId(MountAppearance.NobleChocoboId));
            Assert.Equal(102706, MountModels.MountFileId(1));
            Assert.Equal(102743, MountModels.MountFileId(38));
            Assert.Equal((Path.Combine("ROM", "90", "14.DAT"), Path.Combine("ROM", "90", "16.DAT")), MountModels.RiderPackPaths(CharacterRace.HumeMale));
            Assert.Equal(MountModels.RiderPackPaths(CharacterRace.TaruMale), MountModels.RiderPackPaths(CharacterRace.TaruFemale));
            Assert.Equal(Path.Combine("ROM", "90", "50.DAT"), MountModels.RiderPackPaths(CharacterRace.Galka).Motions);
            Assert.Equal(string.Empty, MountModels.RiderPackPaths(CharacterRace.GirlChild).Motions);
        }

        [Fact]
        public void ChocoboParts_TheSaddledBodyAndAPersonalChocobosColourAndTraits()
        {
            var rental = MountAppearance.FromWire(5, 0, 1, 0xFFFFFFFF); // a rental ignores the properties
            Assert.Equal(new[] { "169/12", "169/14", "169/16", "169/18" }, MountModels.ChocoboPartPaths(rental).Select(Short));
            // Personal, blue (colour 2 in bits 9-11), large beak (bit 0) and full tail (bit 6).
            var blue = MountAppearance.FromWire(5, 0, 2, (2u << 9) | 1u | (1u << 6));
            Assert.Equal(MountModels.ChocoboColor.Blue, MountModels.ChocoboColorOf(blue));
            Assert.Equal(new[] { "169/30", "169/32", "169/35", "169/37" }, MountModels.ChocoboPartPaths(blue).Select(Short));
            var green = MountAppearance.FromWire(5, 0, 2, 4u << 9);
            Assert.Equal(new[] { "170/49", "170/51", "170/53", "170/55" }, MountModels.ChocoboPartPaths(green).Select(Short));
            Assert.NotEqual(MountModels.CacheKey(rental), MountModels.CacheKey(blue));
            Assert.Equal(MountModels.CacheKey(rental), MountModels.CacheKey(MountAppearance.FromWire(5, 34, 4)));

            static string Short(string path)
            {
                var parts = path.Split(Path.DirectorySeparatorChar);
                return $"{parts[1]}/{Path.GetFileNameWithoutExtension(parts[2])}";
            }
        }

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        /// <summary>The retail chocobo (race model and parts) and raptor (file 102706) load with an idle under <c>idl</c>.</summary>
        [Fact]
        public void RetailMounts_LoadWithTheirMotions()
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(5, 0, 1), out var chocobo));
            Assert.Equal(58, chocobo!.Skeleton!.Count);
            Assert.Contains(chocobo.Textures.Keys, t => t.Contains("kura", StringComparison.OrdinalIgnoreCase)); // the saddle
            Assert.Same(chocobo.Animations["chi"], chocobo.Animations["idl"]);
            Assert.Contains("run", chocobo.Animations.Keys);

            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(85, 2, 2), out var raptor));
            Assert.Equal(76, raptor!.Skeleton!.Count);
            Assert.Contains(raptor.Textures.Keys, t => t.Contains("wy", StringComparison.OrdinalIgnoreCase));
            Assert.Same(raptor.Animations["chi"], raptor.Animations["idl"]);
            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(85, 2, 2), out var again));
            Assert.Same(raptor, again);
        }

        /// <summary>
        /// Every race's rider plays its riding pack: <c>idl</c> is the seated <c>chi</c>, which lifts the hips at least 0.4 yalms
        /// over the standing idle (Hume male 1.05 to 1.78), on the character's own meshes.
        /// </summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale)]
        [InlineData(CharacterRace.HumeFemale)]
        [InlineData(CharacterRace.ElvaanMale)]
        [InlineData(CharacterRace.ElvaanFemale)]
        [InlineData(CharacterRace.TaruMale)]
        [InlineData(CharacterRace.Mithra)]
        [InlineData(CharacterRace.Galka)]
        public void RetailRiders_SitOnTheirRidingPack(CharacterRace race)
        {
            var rm = Retail();
            if (rm == null) return;
            var character = EntityModelLoader.AssembleCharacter(race, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.True(rm.TryGetRiderModel(character, race, out var rider));
            Assert.Same(rider!.Animations["chi"], rider.Animations["idl"]);
            Assert.Same(character.AnimatedMeshGroups[0], rider.AnimatedMeshGroups[0]);
            Assert.Contains("coff", rider.MotionRoutines.Keys);
            Assert.True(rm.TryGetRiderModel(character, race, out var again));
            Assert.Same(rider, again);
            var skeleton = character.Skeleton!;
            float Hips(Gordian.Core.Resources.Models.AnimationClip clip) =>
                -Gordian.Core.Resources.Graphics.SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, 0f, true).Translations[2].Y;
            Assert.True(Hips(rider.Animations["idl"]) - Hips(character.Animations["idl"]) > 0.4f,
                $"{race}: seated hips {Hips(rider.Animations["idl"]):F2} vs standing {Hips(character.Animations["idl"]):F2}");
        }

        public static TheoryData<CharacterRace, float> RaceScales => new()
        {
            { CharacterRace.HumeMale, 1.000f }, { CharacterRace.HumeFemale, 0.995f }, { CharacterRace.ElvaanMale, 1.233f },
            { CharacterRace.ElvaanFemale, 1.216f }, { CharacterRace.TaruMale, 0.955f }, { CharacterRace.TaruFemale, 0.955f },
            { CharacterRace.Mithra, 0.982f }, { CharacterRace.Galka, 1.298f },
        };

        /// <summary>
        /// Round 2 (image-9.png, the video of 2026-10-23): retail scales the mount by the rider's race. Each race's riding pack
        /// is authored on a chocobo scaled by its hip height over the Hume male's: through <c>chi</c>, <c>run</c> and
        /// <c>wlk</c> its hips stay 0.28-0.42 x s over the scaled chocobo's seat (reference 1) and 0.15-0.55 x s ahead of it (0.34 and
        /// 0.27 at rest). So the pack carries the chocobo's bob, which another mount's seat replaces.
        /// </summary>
        [Theory]
        [MemberData(nameof(RaceScales))]
        public void RetailRidingPacks_FollowAChocoboScaledByRace(CharacterRace race, float expectedScale)
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(5, 0, 1), out var chocobo));
            var character = EntityModelLoader.AssembleCharacter(race, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.True(rm.TryGetRiderModel(character, race, out var rider));
            float scale = rm.GetMountScale(rider!);
            Assert.InRange(scale, expectedScale - 0.005f, expectedScale + 0.005f);
            foreach (var name in new[] { "chi", "run", "wlk" })
            {
                var clip = rider!.Animations[name];
                for (int i = 0; i < 6; i++)
                {
                    float t = i * clip.DurationSeconds / 6;
                    var hips = Gordian.Core.Resources.Graphics.SkeletonPoseEvaluator.EvaluatePose(character.Skeleton!, clip, t, true).Translations[2];
                    var own = chocobo!.Animations.TryGetValue(clip.Name, out var c) ? c : chocobo.Animations["idl"];
                    var chocoboPose = Gordian.Core.Resources.Graphics.SkeletonPoseEvaluator.EvaluatePose(chocobo.Skeleton!, own, t, true);
                    var seat = MountModels.ReferencePoint(chocobo, chocoboPose, MountModels.ChocoboSeatReference) * scale;
                    Assert.InRange(-(hips.Y - seat.Y) / scale, 0.28f, 0.42f);
                    Assert.InRange((hips.X - seat.X) / scale, 0.15f, 0.55f);
                }
            }
        }

        private static MountSeating.RideData Ride(ResourceManager rm, byte mountId) =>
            rm.GetRideData(MountAppearance.FromWire(85, (byte)(mountId + 1), 1))!;

        /// <summary>
        /// Round 5: each mount's size per build is its Info bytes 10-12 (Hume, Mithra, Tarutaru; Elvaan; Galka). The raptor's
        /// 85 / 100 / 115 match image-12.png's fits; Fenrir is 60 under every race (image-13.png's five Fenrirs alike); the
        /// chocobo has no ride data (the pack's race scale sizes it).
        /// </summary>
        [Fact]
        public void RetailRideData_SizesPerBuild()
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.Null(rm.GetRideData(MountAppearance.FromWire(5, 0, 1)));
            var raptor = Ride(rm, 1);
            Assert.Equal(0.85f, raptor.ScaleFor(CharacterRace.HumeMale), 3);
            Assert.Equal(0.85f, raptor.ScaleFor(CharacterRace.Mithra), 3);
            Assert.Equal(0.85f, raptor.ScaleFor(CharacterRace.TaruFemale), 3);
            Assert.Equal(1.00f, raptor.ScaleFor(CharacterRace.ElvaanFemale), 3);
            Assert.Equal(1.15f, raptor.ScaleFor(CharacterRace.Galka), 3);
            var fenrir = Ride(rm, 9);
            Assert.All(new[] { CharacterRace.HumeMale, CharacterRace.ElvaanMale, CharacterRace.Galka, CharacterRace.TaruMale },
                race => Assert.Equal(0.60f, fenrir.ScaleFor(race), 3));
            Assert.Equal(0.50f, Ride(rm, 12).ScaleFor(CharacterRace.Mithra), 3); // Magic Pot
            Assert.Equal(1.30f, Ride(rm, 10).ScaleFor(CharacterRace.Mithra), 3); // Beetle
        }

        /// <summary>
        /// Round 5: the <c>moun</c> section's bytes 8-15 are each race's pose: the raptor and Fenrir straddle (6), the Magic Pot's
        /// riders stand (5, image-26.png), the Spectral Chair's sit (3); the tiger differs by race.
        /// </summary>
        [Fact]
        public void RetailRideData_PosePerRace()
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.All(Enum.GetValues<CharacterRace>().Where(r => r is >= CharacterRace.HumeMale and <= CharacterRace.Galka),
                race =>
                {
                    Assert.Equal(6, Ride(rm, 1).PoseFor(race));
                    Assert.Equal(6, Ride(rm, 9).PoseFor(race));
                    Assert.Equal(5, Ride(rm, 12).PoseFor(race));
                    Assert.Equal(3, Ride(rm, 17).PoseFor(race));
                });
            var tiger = Ride(rm, 2);
            Assert.Equal(new byte[] { 6, 3, 6, 3, 3, 3, 3, 6 }, new[] { CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale,
                CharacterRace.ElvaanFemale, CharacterRace.TaruMale, CharacterRace.TaruFemale, CharacterRace.Mithra, CharacterRace.Galka }
                .Select(tiger.PoseFor).ToArray());
        }

        [Fact]
        public void RideData_ReadsTheInfoScalesAndThePoses()
        {
            // An "info" section (16-byte header + body) with scales 85 / 100 / 115 / 140 at bytes 10-13, then a "moun" one with
            // poses 6 3 6 3 3 3 3 6 at bytes 8-15.
            var dat = new byte[64];
            void Section(int at, string id)
            {
                System.Text.Encoding.ASCII.GetBytes(id).CopyTo(dat, at);
                BinaryPrimitives.WriteUInt32LittleEndian(dat.AsSpan(at + 4), 0x45 | ((32u / 16) << 7));
            }
            Section(0, "info");
            new byte[] { 85, 100, 115, 140 }.CopyTo(dat, 16 + 10);
            Section(32, "moun");
            new byte[] { 6, 3, 6, 3, 3, 3, 3, 6 }.CopyTo(dat, 48 + 8);
            var data = MountSeating.Read(dat)!;
            Assert.Equal(new[] { 0.85f, 1.00f, 1.15f, 1.40f }, data.Scales);
            Assert.Equal(1.15f, data.ScaleFor(CharacterRace.Galka));
            Assert.Equal(3, data.PoseFor(CharacterRace.Mithra));
            Assert.Null(MountSeating.Read(new byte[8]));
            Assert.Equal(48, MountSeating.SeatReference(CharacterRace.HumeMale));
            Assert.Equal(55, MountSeating.SeatReference(CharacterRace.Galka));
        }

        /// <summary>
        /// Round 5: every mount carries one seat reference per race (r48 Hume male ... r55 Galka) where the rider's hips go: on
        /// the raptor's back at one height for every race, on Fenrir's mid-back behind its hip joint (where round 3 had to move
        /// the seat by hand), over the Magic Pot's lid by each race's standing hip height (the Tarutaru's lowest).
        /// </summary>
        [Fact]
        public void RetailMounts_CarryAPerRaceSeat()
        {
            var rm = Retail();
            if (rm == null) return;
            System.Numerics.Vector3 Seat(byte mountId, CharacterRace race)
            {
                Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(85, (byte)(mountId + 1), 1), out var model));
                Assert.True(MountModels.HasReference(model!, MountSeating.SeatReference(race)));
                var pose = Gordian.Core.Resources.Graphics.SkeletonPoseEvaluator.EvaluatePose(model!.Skeleton!, model.Animations["idl"], 0f, true);
                return MountModels.ReferencePoint(model, pose, MountSeating.SeatReference(race));
            }
            Assert.InRange(-Seat(1, CharacterRace.HumeMale).Y, 1.40f, 1.55f);
            Assert.InRange(-Seat(1, CharacterRace.Galka).Y, 1.40f, 1.55f);
            Assert.InRange(Seat(9, CharacterRace.Galka).X, -0.9f, -0.4f);
            Assert.True(-Seat(12, CharacterRace.TaruMale).Y < -Seat(12, CharacterRace.HumeMale).Y - 1f);
        }

        /// <summary>
        /// The mount rider packs (<c>ROM/351/61</c> + 3 per race) carry the poses: standing (5) puts a Mithra's hips at its
        /// standing height; its own seat (reference 107) is just under them (round 6).
        /// </summary>
        [Fact]
        public void RetailPoseRiders_HoldTheMountsPose()
        {
            var rm = Retail();
            if (rm == null) return;
            var mithra = EntityModelLoader.AssembleCharacter(CharacterRace.Mithra, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.True(rm.TryGetPoseRiderModel(mithra, CharacterRace.Mithra, 5, out var standing));
            Assert.InRange(-rm.GetRiderSeat(standing!).Y, 0.8f, 0.95f); // its reference 107, 0.11 under the hips at 0.99
            Assert.True(rm.TryGetPoseRiderModel(mithra, CharacterRace.Mithra, 6, out var straddling));
            Assert.InRange(-rm.GetRiderSeat(straddling!).Y, 1.6f, 1.7f); // the chocobo pack's straddle: hips at 1.75
            Assert.Same(standing, rm.TryGetPoseRiderModel(mithra, CharacterRace.Mithra, 5, out var again) ? again : null);
            Assert.Equal((System.IO.Path.Combine("ROM", "351", "79.DAT"), System.IO.Path.Combine("ROM", "351", "80.DAT")), MountSeating.PackPaths(CharacterRace.Galka));
        }

        /// <summary>Of the retail mounts only Fenrir (id 9) carries a blur aura (Section 0x5E), drawn as on the avatar.</summary>
        [Fact]
        public void RetailFenrirMount_HasItsBlurAura()
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(85, 10, 3), out var fenrir));
            Assert.NotNull(fenrir!.BlurAura);
            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(85, 2, 2), out var raptor));
            Assert.Null(raptor!.BlurAura);
        }

        // ---- Mount list ----

        [Fact]
        public void MountRows_NamedOwnedMountsInIdOrder_WithTheSharedRecast()
        {
            var names = new Dictionary<int, string> { [0] = "Chocobo", [1] = "Raptor", [30] = "Red Raptor" };
            var rows = StockUiActionMenu.MountRows(new[] { 0, 1, 30, 63 }, id => names.GetValueOrDefault(id), id => $"help {id}",
                TimeSpan.FromSeconds(30), 1000);
            Assert.Equal(new[] { "Chocobo", "Raptor", "Red Raptor" }, rows.Select(r => r.Name)); // 63 has no name
            Assert.All(rows, r => Assert.Equal(StockUiActionType.Mount, r.Type));
            Assert.All(rows, r => Assert.Null(r.GreyedReason));
            Assert.Equal("help 30", rows[2].Help);
            Assert.True(rows[0].RecastEndsAt > 1000);
            Assert.Equal(0, StockUiActionMenu.MountRows(new[] { 1 }, id => names.GetValueOrDefault(id), _ => null, TimeSpan.Zero, 5)[0].RecastEndsAt);
        }

        // ---- Mounted command menu and Dismount ----

        private static StockUiTargetContext Self(bool mounted, bool canDig = false, bool treasure = false) =>
            new(StockUiTargetKind.Self, 0x01000001, "Me", HasTreasure: treasure, Mounted: mounted, CanDig: canDig);

        [Fact]
        public void MountedCommandMenu_ChatDigDismount()
        {
            Assert.Equal(new[] { "Chat", "Dig", "Dismount" }, StockUiCommandMenu.Compose(Self(true, canDig: true)).Select(r => r.Label.Text));
            Assert.Equal(new[] { "Chat", "Dismount" }, StockUiCommandMenu.Compose(Self(true)).Select(r => r.Label.Text));
            Assert.Equal(new[] { "Chat", "Dig", "Treasure", "Dismount" },
                StockUiCommandMenu.Compose(Self(true, canDig: true, treasure: true)).Select(r => r.Label.Text));
            Assert.Equal(("choconor", 4), (StockUiCommandMenu.Dismount.Menu, StockUiCommandMenu.Dismount.Button));
            Assert.Equal(("choconor", 2), (StockUiCommandMenu.Dig.Menu, StockUiCommandMenu.Dig.Button));
            Assert.Equal(StockUiMenuCommand.Dismount, StockUiCommandMenu.Compose(Self(true))[1].Entry.Command);
            // Any target, while mounted (PROVISIONAL for monsters).
            Assert.Equal(new[] { "Chat", "Dismount" },
                StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Monster, 2, "Hare", Mounted: true)).Select(r => r.Label.Text));
        }

        [Fact]
        public async Task Dismount_AsksThenGetsOff_AndTheMenuShowsTheListOnFoot()
        {
            var menus = new StockUiMenuController { Library = StockUiMenuControllerTests.SyntheticLibrary() };
            var commands = new List<StockUiMenuCommand>();
            menus.TargetCommand = (command, _) =>
            {
                lock (commands) commands.Add(command);
                return Task.FromResult(PlayerActionResult.Ok("Dismounted."));
            };
            Assert.True(menus.OpenCommandMenu(Self(true, canDig: true)));
            menus.Move(InputAction.MenuUp); // the ring: Dismount, the third row
            Assert.Equal(3, menus.Top!.SelectedButtonId);
            menus.Activate();
            var prompt = menus.Top!;
            Assert.True(prompt.IsPrompt);
            Assert.Equal((StockUiMenuEntries.MessageYesNoMenu, StockUiMenuController.DismountQuestion, 1), (prompt.Name, prompt.Message, prompt.SelectedButtonId));
            menus.Activate(); // Yes
            for (int i = 0; i < 200 && (menus.Top is not { IsCommandMenu: true } m || m.Target?.Mounted != false); i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            lock (commands) Assert.Equal(new[] { StockUiMenuCommand.Dismount }, commands);
            var onFoot = menus.Top!;
            Assert.True(onFoot.IsCommandMenu);
            Assert.Equal(new[] { "Chat", "Magic", "Abilities", "Trust", "Items", "Trade", "Check" }, onFoot.CommandRows.Select(r => r.Label.Text));
            Assert.Equal(3, onFoot.SelectedButtonId); // Abilities, where Dismount was (retail capture)
        }

        [Fact]
        public async Task Dismount_No_LeavesTheMountedMenu()
        {
            var menus = new StockUiMenuController { Library = StockUiMenuControllerTests.SyntheticLibrary() };
            int sent = 0;
            menus.TargetCommand = (_, _) =>
            {
                sent++;
                return Task.FromResult(PlayerActionResult.Ok("Dismounted."));
            };
            Assert.True(menus.OpenCommandMenu(Self(true)));
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            menus.Move(InputAction.MenuDown); // No (the synthetic comyn lists its buttons in a column)
            menus.Activate();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.Equal(0, sent);
            Assert.Equal(new[] { "Chat", "Dismount" }, menus.Top!.CommandRows.Select(r => r.Label.Text));
        }

        // ---- Commands ----

        private sealed class Service
        {
            public readonly WorldState World = new();
            public readonly LocalPlayerState Local = new() { ServerId = 0x01000001 };
            public readonly List<byte[]> Sent = new();
            public readonly PlayerActionService Actions;
            public readonly ProgressionState Progression = new();

            public Service()
            {
                Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
                {
                    lock (Sent) Sent.Add(mem.ToArray());
                    return Task.CompletedTask;
                }
                var profile = new SessionProfile();
                Actions = new PlayerActionService(profile, World, Local,
                    new CombatPacketModule(new CombatState(), Local, Capture), new ChatPacketModule(Capture),
                    new PartyPacketModule(new PartyState(), Capture), new EntityPacketModule(World, Local, Capture),
                    new LifecyclePacketModule(profile, Capture), Capture)
                {
                    ProgressionModule = new ProgressionPacketModule(Progression, Local, Capture, null),
                    MountName = id => id switch { 0 => "Chocobo", 1 => "Raptor", 2 => "Tiger", _ => null },
                };
                World.UpsertEntity(new PlayerEntity(0x01000001, 10) { Name = "Me" });
                var table = new byte[12];
                table[0] = 0b011; // Chocobo, Raptor
                Progression.UpdateMounts(new S2C_0x0AE_MountData(table));
            }

            public (CliActionId Action, uint Param) LastAction()
            {
                byte[] packet;
                lock (Sent) packet = Sent.Last(p => (BinaryPrimitives.ReadUInt16LittleEndian(p) & 0x1FF) == 0x01A);
                return ((CliActionId)BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)), BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
            }
        }

        [Fact]
        public async Task MountCommand_ByName_SendsTheClientMountId()
        {
            var s = new Service();
            Assert.True((await s.Actions.ExecuteCommandAsync("/mount raptor")).Success);
            Assert.Equal((CliActionId.Mount, 1u), s.LastAction());
            Assert.False((await s.Actions.ExecuteCommandAsync("/mount Tiger")).Success); // not owned
            Assert.Equal(new[] { 0, 1 }, s.Actions.UnlockedMounts);
        }

        [Fact]
        public async Task DismountCommand_OnlyWhileMounted()
        {
            var s = new Service();
            Assert.False((await s.Actions.ExecuteCommandAsync("/dismount")).Success);
            s.Local.UpdateFromCharStatus(new S2C_0x037_CharStatus(CharStatus(85, 2, 2)));
            Assert.True((await s.Actions.ExecuteCommandAsync("/dismount")).Success);
            Assert.Equal((CliActionId.Dismount, 0u), s.LastAction());
            Assert.False((await s.Actions.ExecuteCommandAsync("/mount raptor")).Success); // already mounted
        }

        [Fact]
        public void MountList_ListsTheOwnedMounts()
        {
            var s = new Service();
            s.Actions.Menus.Library = Gordian.Core.Resources.Ui.UiResourceLibrary.FromDefinitions(new[]
            {
                new Gordian.Core.Resources.Ui.UiMenuDefinition
                {
                    Name = StockUiActionMenu.MountListMenu,
                    Frame = new Gordian.Core.Resources.Ui.UiMenuFrame { X = 16, Y = 96, Width = 182, Height = 200 },
                    Buttons = Enumerable.Range(1, 12).Select(i => new Gordian.Core.Resources.Ui.UiMenuButton
                    {
                        ButtonId = (short)i, X = 16, Y = (short)(4 + 16 * (i - 1)), Width = 132, Height = 16,
                        NavUp = (sbyte)Math.Max(1, i - 1), NavDown = (sbyte)Math.Min(12, i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                    }).ToList(),
                },
            });
            var list = s.Actions.Menus.OpenActionList(StockUiActionList.Mounts, 0, null)!;
            Assert.Equal(StockUiActionMenu.MountListMenu, list.Name);
            Assert.Equal(new[] { "Chocobo", "Raptor" }, list.ActionRows.Select(r => r.Name));
        }
    }
}
