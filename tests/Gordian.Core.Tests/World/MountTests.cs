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
                    var seat = MountModels.ChocoboSeat(chocobo!, clip, t) * scale;
                    Assert.InRange(-(hips.Y - seat.Y) / scale, 0.28f, 0.42f);
                    Assert.InRange((hips.X - seat.X) / scale, 0.15f, 0.55f);
                }
            }
        }

        /// <summary>
        /// The top of each mount's back over its seat reference, where the hips go: the rider is raised by the difference from
        /// the chocobo's (round 2 H11: on Fenrir, whose seat reference is a hip joint deep in a thick body, riders sank in).
        /// </summary>
        [Fact]
        public void RetailMounts_BackHeightOverTheSeat()
        {
            var rm = Retail();
            if (rm == null) return;
            float Back(MountAppearance mount)
            {
                Assert.True(rm.TryLoadMountModel(mount, out var model));
                return rm.GetBackHeight(model!);
            }
            Assert.InRange(Back(MountAppearance.FromWire(5, 0, 1)), 0.18f, 0.25f);   // chocobo
            Assert.InRange(Back(MountAppearance.FromWire(85, 2, 2)), 0.24f, 0.32f);  // raptor
            Assert.InRange(Back(MountAppearance.FromWire(85, 10, 3)), 0.95f, 1.10f); // Fenrir at its hip joint: the shoulder fur
            // Round 3 H15: Fenrir's riders sit 0.6 further back, on its flat mid-back.
            Assert.True(rm.TryLoadMountModel(MountAppearance.FromWire(85, 10, 3), out var fenrir));
            Assert.InRange(MountModels.BackHeight(fenrir!, MountModels.SeatShift(MountAppearance.FromWire(85, 10, 3))), 0.30f, 0.45f);
        }

        /// <summary>
        /// Round 3 (image-12.png, image-13.png): each mount's own size against the riding chocobo, on top of the race scale.
        /// </summary>
        [Fact]
        public void MountBaseScales_AndSeatShifts()
        {
            Assert.Equal(1f, MountModels.MountBaseScale(MountAppearance.FromWire(5, 0, 2)));        // chocobo
            Assert.Equal(1f, MountModels.MountBaseScale(MountAppearance.FromWire(5, 34, 4)));       // Noble Chocobo
            Assert.Equal(0.85f, MountModels.MountBaseScale(MountAppearance.FromWire(85, 2, 2)));    // raptor
            Assert.Equal(0.85f, MountModels.MountBaseScale(MountAppearance.FromWire(85, 31, 7)));   // red raptor
            Assert.Equal(0.65f, MountModels.MountBaseScale(MountAppearance.FromWire(85, 10, 3)));   // Fenrir
            Assert.Equal(1f, MountModels.MountBaseScale(MountAppearance.FromWire(85, 3, 3)));       // tiger, not measured
            Assert.Equal(-0.6f, MountModels.SeatShift(MountAppearance.FromWire(85, 10, 3)));
            Assert.Equal(0f, MountModels.SeatShift(MountAppearance.FromWire(85, 2, 2)));
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
