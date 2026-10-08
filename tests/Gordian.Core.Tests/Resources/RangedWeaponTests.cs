// tests/Gordian.Core.Tests/Resources/RangedWeaponTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The ranged weapon (#158): part of a PC's model as weapon slot 2, hidden by the race base's <c>init</c> (<c>hwpc</c>),
    /// shown by the ranged routines (<c>calg</c> / <c>shlg</c> link <c>hwso</c>) while the RangeType's <c>lc&lt;NN&gt;</c> /
    /// <c>ls&lt;NN&gt;</c> clips carry its mount bone into the hand.
    /// </summary>
    public class RangedWeaponTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Tick = 1f / 60f;

        private static byte[] Op(byte op, int dwords, int delay = 0, string reference = "")
        {
            var command = new byte[dwords * 4];
            command[0] = op;
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(1), (ushort)dwords);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(4), (ushort)delay);
            if (reference.Length > 0) System.Text.Encoding.ASCII.GetBytes(reference).CopyTo(command, 8);
            return command;
        }

        private static byte[] ShowHide(int slot, bool hide)
        {
            var command = Op(0x75, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(8), hide ? 1u : 0u);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x0C), (ushort)slot);
            return command;
        }

        /// <summary>The Hume male race base's ranged routines, shaped as in <c>ROM/27/82</c>.</summary>
        private static Dictionary<string, RawMotionRoutine> RangedRoutines()
        {
            RawMotionRoutine R(string name, byte[] payload) => MotionRoutineDecoder.Decode(payload, name)!;
            return new Dictionary<string, RawMotionRoutine>
            {
                ["init"] = R("init", MotionRoutineDecoderTests.Routine(0, MotionRoutineDecoderTests.Link(0x03, "hwpc", 0))),
                ["calg"] = R("calg", MotionRoutineDecoderTests.Routine(0, Op(0x76, 3), MotionRoutineDecoderTests.Link(0x03, "hwso", 0))),
                ["shlg"] = R("shlg", MotionRoutineDecoderTests.Routine(1,
                    MotionRoutineDecoderTests.Link(0x03, "hwso", 0), Op(0x77, 3, delay: 1), MotionRoutineDecoderTests.Link(0x3B, "wash", 0))),
                ["lc06"] = R("lc06", MotionRoutineDecoderTests.Routine(800,
                    MotionRoutineDecoderTests.PlayClip("yu0?", 14, 116, 10, 10, 1),
                    MotionRoutineDecoderTests.Link(0x57, "lgin", 102),
                    MotionRoutineDecoderTests.PlayClip("yu1?", 0, 85, 0, 20, 8))),
                ["ls06"] = R("ls06", MotionRoutineDecoderTests.Routine(86,
                    MotionRoutineDecoderTests.Link(0x03, "hwso", 0),
                    MotionRoutineDecoderTests.PlayClip("yu2?", 4, 86, 0, 10, 1),
                    MotionRoutineDecoderTests.Link(0x57, "kalg", 26),
                    MotionRoutineDecoderTests.Link(0x03, "ldad", 38),
                    MotionRoutineDecoderTests.Link(0x57, "lgot", 0))),
                ["cabk"] = R("cabk", MotionRoutineDecoderTests.Routine(33,
                    MotionRoutineDecoderTests.Link(0x03, "hwmg", 0), MotionRoutineDecoderTests.PlayClip("mb0?", 0, 33, 16, 10, 63))),
                ["lc11"] = R("lc11", MotionRoutineDecoderTests.Routine(800,
                    ShowHide(2, false), ShowHide(1, true), ShowHide(0, true), MotionRoutineDecoderTests.PlayClip("gc0?", 0, 40, 20, 0, 1))),
            };
        }

        [Fact]
        public void RangedStartAndFinish_RunTheRangeTypesRoutines_AndShowTheRangedWeapon()
        {
            var routines = RangedRoutines();
            var built = MotionRoutineDecoder.BuildAll(routines, clip => clip.StartsWith("yu", StringComparison.Ordinal), rangeType: 6);

            var calg = built["calg"];
            Assert.Equal(["yu0", "yu1"], calg.Segments.Select(s => s.ClipName));
            Assert.True(calg.IsSustained);
            // hwso: ranged shown, sub and main hidden.
            Assert.Equal([new WeaponVisibilityChange(0, 2, false), new WeaponVisibilityChange(0, 1, true), new WeaponVisibilityChange(0, 0, true)],
                calg.WeaponChanges);

            var shlg = built["shlg"];
            Assert.Equal(["yu2"], shlg.Segments.Select(s => s.ClipName));
            Assert.Equal([30], shlg.HitTicks); // the ldad link, as a melee swing's dada
            Assert.Contains(new WeaponVisibilityChange(0, 2, false), shlg.WeaponChanges);
        }

        [Fact]
        public void RangedStart_WithoutARangeType_PlaysNothing()
        {
            var built = MotionRoutineDecoder.BuildAll(RangedRoutines(), _ => true);
            Assert.False(built.ContainsKey("calg"));
            Assert.Equal(string.Empty, MotionRoutineDecoder.RangedRoutineName(false, -1));
            Assert.Equal("ls11", MotionRoutineDecoder.RangedRoutineName(true, 11));
        }

        [Fact]
        public void OwnShowHideOps_AndSharedLinks_AreWeaponChanges()
        {
            var built = MotionRoutineDecoder.BuildAll(RangedRoutines(), _ => true);
            Assert.Equal([new WeaponVisibilityChange(0, 0, true), new WeaponVisibilityChange(0, 1, true), new WeaponVisibilityChange(0, 2, true)],
                built["cabk"].WeaponChanges);
            Assert.Equal([new WeaponVisibilityChange(0, 2, false), new WeaponVisibilityChange(0, 1, true), new WeaponVisibilityChange(0, 0, true)],
                built["lc11"].WeaponChanges);
        }

        [Fact]
        public void InitLinkingHwpc_HidesTheRangedSlot()
        {
            Assert.Equal([2], EntityModelLoader.InitialHiddenWeaponSlots(RangedRoutines()).ToArray());
        }

        private static EntityModel Model(int rangeType)
        {
            var model = new EntityModel { Name = "Test", RangedType = rangeType, DefaultHiddenWeaponSlots = 1 << 2 };
            foreach (var (name, seconds) in new[] { ("idl", 1f), ("btl", 1f), ("yu0", 1f), ("yu1", 1.4f), ("yu2", 1.4f) })
            {
                model.Animations[name] = new AnimationClip { Name = name, KeyFrameDuration = 1f, NumFrames = (int)(seconds * 30f) + 1 };
            }
            foreach (var (name, raw) in RangedRoutines()) model.RawMotionRoutines[name] = raw;
            model.RebuildMotionRoutines();
            return model;
        }

        private static ActionRequest Request(string routine) => new()
        {
            Motion = ActionMotion.Routine,
            Routine = routine,
            Hits = [],
            ReceivedTimestamp = Stopwatch.GetTimestamp()
        };

        [Fact]
        public void AnimationState_ShowsTheRangedWeaponForTheShot_AndHidesItAfter()
        {
            var model = Model(6);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            Assert.Equal(1 << 2, state.HiddenWeaponSlots); // stowed

            state.EnqueueAction(Request("calg"));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal("yu0", state.CurrentClip!.Name);
            Assert.Equal(0b011, state.HiddenWeaponSlots); // bow out, sword and shield away

            state.EnqueueAction(Request("shlg")); // replaces the held aim
            for (int i = 0; i < 10; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal("yu2", state.CurrentClip!.Name);
            Assert.Equal(0b011, state.HiddenWeaponSlots);

            for (int i = 0; i < 120; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.False(state.IsPlayingAction);
            Assert.Equal(1 << 2, state.HiddenWeaponSlots);
        }

        // ---- Retail data (skipped without the game install) ----

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        /// <summary>The shared weapon routines GordianXI keeps in code are the ones the retail <c>ROM/0/0.DAT</c> carries.</summary>
        [Fact]
        public void SharedWeaponRoutines_MatchTheRetailDat()
        {
            var rm = Retail();
            if (rm == null) return;
            var raw = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(Path.Combine("ROM", "0", "0.DAT"))!, "shared").Routines!;
            foreach (var (name, expected) in SharedWeaponRoutines.ByName)
            {
                var routine = raw.Single(r => r.Name == name);
                var ops = routine.Commands.Where(c => c.Op == 0x75).Select(c => (c.WeaponSlot, c.HideWeapon)).ToArray();
                Assert.Equal(expected, ops);
            }
        }

        private static EntityModel HumeMale(ResourceManager rm, ushort ranged)
        {
            var grap = new ushort[9];
            grap[8] = ranged;
            return EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
        }

        /// <summary>The skinned centre of the model's ranged weapon meshes in a pose, and whether any of it has size.</summary>
        private static (Vector3 Centre, float Extent) RangedWeaponAt(ResourceManager rm, SkeletonPoseEvaluator.EvaluatedPose pose, int fileId)
        {
            var meshes = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(fileId)!, "ranged").Meshes;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue), sum = Vector3.Zero;
            int n = 0;
            foreach (var v in meshes.SelectMany(m => m.Vertices))
            {
                var (p, _) = SkeletonPoseEvaluator.SkinVertex(v, pose);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                sum += p;
                n++;
            }
            return (sum / n, (max - min).Length());
        }

        /// <summary>
        /// Hume male with a bow (ranged model 31, RangeType 6): the bow is in the model as slot 2, hidden by default; a ranged
        /// attack plays <c>lc06</c> / <c>ls06</c> (yu0-yu2), whose clips put the bow's mount bone (joint 89) in the left hand
        /// (joint 85, reference 126) at full size, while the stance holds it at scale 0.
        /// </summary>
        [Fact]
        public void HumeMaleBow_IsHiddenUntilTheShot_AndTheShotPutsItInTheLeftHand()
        {
            var rm = Retail();
            if (rm == null) return;
            var model = HumeMale(rm, 31);
            Assert.Equal(6, model.RangedType);
            Assert.True(EntityModel.IsWeaponSlotHidden(model.DefaultHiddenWeaponSlots, 2));
            Assert.Contains(model.AnimatedMeshGroups, g => g.WeaponSlot == 2);

            var calg = model.MotionRoutines["calg"];
            Assert.Equal(["yu0", "yu1"], calg.Segments.Select(s => s.ClipName));
            Assert.Contains(new WeaponVisibilityChange(0, 2, false), calg.WeaponChanges);
            var shlg = model.MotionRoutines["shlg"];
            Assert.Equal(["yu2"], shlg.Segments.Select(s => s.ClipName));
            Assert.Equal([30], shlg.HitTicks);

            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Ranged, 31, out int bowFile));
            var skeleton = model.Skeleton!;
            int leftHand = skeleton.References[126].Index;

            var aim = SkeletonPoseEvaluator.EvaluatePose(skeleton, model.Animations["yu1"], 0.5f, true);
            var (centre, extent) = RangedWeaponAt(rm, aim, bowFile);
            Assert.True(extent > 0.5f, $"the drawn bow spans {extent}");
            Assert.True(Vector3.Distance(aim.Translations[89], aim.Translations[leftHand]) < 0.02f, "mount bone on the left hand");
            Assert.True(Vector3.Distance(centre, aim.Translations[leftHand]) < 0.6f, $"bow centre {centre} vs left hand {aim.Translations[leftHand]}");

            var idle = SkeletonPoseEvaluator.EvaluatePose(skeleton, model.Animations["idl"], 0.5f, true);
            Assert.True(RangedWeaponAt(rm, idle, bowFile).Extent < 0.01f, "the stance holds the bow at scale 0");
        }

        /// <summary>A gun (RangeType 3) goes to the right hand (joint 68, reference 127) in <c>gu1</c>.</summary>
        [Fact]
        public void HumeMaleGun_GoesToTheRightHand()
        {
            var rm = Retail();
            if (rm == null) return;
            var model = HumeMale(rm, 57);
            Assert.Equal(3, model.RangedType);
            Assert.Equal(["gu0", "gu1"], model.MotionRoutines["calg"].Segments.Select(s => s.ClipName));
            var aim = SkeletonPoseEvaluator.EvaluatePose(model.Skeleton!, model.Animations["gu1"], 0.5f, true);
            Assert.True(Vector3.Distance(aim.Translations[89], aim.Translations[model.Skeleton!.References[127].Index]) < 0.02f);
        }

        /// <summary>Info byte 14 is the RangeType: flutes 1, harps 2, guns 3, bows 6, handbells 11, stubs none.</summary>
        [Theory]
        [InlineData(64, 1)]
        [InlineData(72, 2)]
        [InlineData(57, 3)]
        [InlineData(24, 4)]
        [InlineData(29, 5)]
        [InlineData(31, 6)]
        [InlineData(113, 11)]
        [InlineData(1, -1)]
        public void RangedType_IsInfoByte14(ushort modelId, int expected)
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Ranged, modelId, out int file));
            Assert.Equal(expected, EntityModelLoader.ReadRangedType(rm.LoadDatBytesByFileId(file)!));
        }
    }
}
