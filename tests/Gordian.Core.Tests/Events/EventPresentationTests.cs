// tests/Gordian.Core.Tests/Events/EventPresentationTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// Scene resource DATs (camera Routes and routines) and the event presentation that plays them (#165).
    /// </summary>
    public class EventPresentationTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? OpenGame()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        private static CameraRoute Route(params CameraKey[] keys) => new() { Name = "c000", Keys = keys, Mode = 0 };

        private static CameraKey Key(float x, float time, float focal = 350f) => new(new Vector3(x, 0, 0), focal, new Vector3(x, 0, 2), 0f, time);

        /// <summary>A scene resource DAT: evte, the given sections (type, name, payload), end.</summary>
        private static byte[] SceneDat(params (byte Type, string Name, byte[] Payload)[] sections)
        {
            var bytes = new List<byte>();
            void Section(byte type, string name, byte[] payload)
            {
                int size = 16 + payload.Length;
                size = (size + 15) / 16 * 16;
                var header = new byte[16];
                System.Text.Encoding.ASCII.GetBytes(name.PadRight(4)).CopyTo(header, 0);
                uint word = (uint)type | (uint)(size / 16) << 7;
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), word);
                bytes.AddRange(header);
                bytes.AddRange(payload);
                bytes.AddRange(new byte[size - 16 - payload.Length]);
            }
            Section(0x01, "evte", new byte[16]);
            foreach (var (type, name, payload) in sections) Section(type, name, payload);
            Section(0x00, "end", Array.Empty<byte>());
            return bytes.ToArray();
        }

        private static byte[] RoutePayload(int mode, params CameraKey[] keys)
        {
            var payload = new byte[CameraRoute.HeaderSize + keys.Length * CameraRoute.KeySize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x10), (ushort)keys.Length);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), mode);
            for (int i = 0; i < keys.Length; i++)
            {
                int baseAt = CameraRoute.HeaderSize + i * CameraRoute.KeySize;
                void F(int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(baseAt + at), v);
                F(0, keys[i].Eye.X); F(4, keys[i].Eye.Y); F(8, keys[i].Eye.Z); F(12, keys[i].FocalLength);
                F(16, keys[i].LookAt.X); F(20, keys[i].LookAt.Y); F(24, keys[i].LookAt.Z); F(28, keys[i].Roll); F(32, keys[i].Time);
            }
            return payload;
        }

        /// <summary>A routine payload: header (commands at +0x40 of the section), then the commands, then the end marker.</summary>
        private static byte[] RoutinePayload(int total, params byte[][] commands)
        {
            var payload = new List<byte>(new byte[0x30]);
            var head = payload.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(0x14), 0x40);
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(0x1C), total);
            payload = new List<byte>(head);
            foreach (var c in commands) payload.AddRange(c);
            payload.AddRange(new byte[] { 0x00, 0x02, 0, 0, 0, 0, 0, 0 });
            return payload.ToArray();
        }

        private static byte[] Command(byte op, int dwords, int delay, int duration, byte[] data)
        {
            var c = new byte[dwords * 4];
            c[0] = op;
            c[1] = (byte)dwords;
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(4), (ushort)delay);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(6), (ushort)duration);
            data.CopyTo(c, 8);
            return c;
        }

        [Fact]
        public void Route_TwoKeys_MovesInAStraightLine_AndHoldsItsEnds()
        {
            var route = Route(Key(0, 0), Key(10, 1));
            Assert.Equal(0f, route.Evaluate(0f).Eye.X, 3);
            Assert.Equal(5f, route.Evaluate(0.5f).Eye.X, 3);
            Assert.Equal(10f, route.Evaluate(2f).Eye.X, 3);
        }

        [Fact]
        public void Route_ManyKeys_PassesThroughEveryKey()
        {
            var route = Route(Key(0, 0), Key(4, 0.25f), Key(6, 0.75f), Key(10, 1));
            Assert.Equal(4f, route.Evaluate(0.25f).Eye.X, 3);
            Assert.Equal(6f, route.Evaluate(0.75f).Eye.X, 3);
            float mid = route.Evaluate(0.5f).Eye.X;
            Assert.InRange(mid, 4f, 6f);
        }

        [Fact]
        public void Route_FocalLengthGivesTheFieldOfView()
        {
            // 2 atan2(192, 350): the client's default, about 57.5 degrees.
            Assert.Equal(57.5f, Key(0, 0).FieldOfView * 180f / MathF.PI, 1);
        }

        [Theory]
        [InlineData(0, 0.25f, 0.25f)]
        [InlineData(1, 0.5f, 0.75f)]
        [InlineData(2, 0.5f, 0.25f)]
        [InlineData(4, 0.5f, 0.5f)]
        [InlineData(4, 0.25f, 0.15625f)]
        public void Ease_FollowsTheMode(int mode, float t, float expected) => Assert.Equal(expected, CameraRoute.Ease(t, mode), 4);

        [Fact]
        public void ActorRelativeRoute_IsPlacedAtTheActor()
        {
            var route = new CameraRoute { Flags = 1, Keys = new[] { new CameraKey(new Vector3(-4, 0, 0), 350, Vector3.Zero, 0, 0) } };
            var pose = route.Evaluate(0, new Vector3(100, -5, 20));
            Assert.Equal(new Vector3(96, -5, 20), pose.Eye);
            Assert.Equal(new Vector3(100, -5, 20), pose.LookAt);
        }

        [Fact]
        public void Shot_PlaysItsRouteOverItsDuration_ThenHoldsTheEnd_UntilTheCameraIsReleased()
        {
            var dat = SceneDat(
                (0x06, "c001", RoutePayload(0, Key(0, 0), Key(10, 1))),
                (0x07, "s001", RoutinePayload(60, Command(0x04, 6, 0, 60, System.Text.Encoding.ASCII.GetBytes("c001")))));
            var resource = EventSceneResource.Parse(dat);
            Assert.True(resource.TryGetRoutine("s001", out var routine));
            Assert.Equal(60, routine.TotalFrames);
            var command = Assert.Single(routine.Commands);
            Assert.Equal((SceneCommandKind.Camera, 0, 60, "c001"), (command.Kind, command.StartFrame, command.Duration, command.Reference));

            double now = 100;
            var presentation = new EventPresentation { Clock = () => now };
            Assert.False(presentation.TryGetCamera(out _));
            presentation.SetCameraHeld(true);
            presentation.Play(1, resource, routine, Vector3.Zero);
            Assert.True(presentation.TryGetCamera(out var start));
            Assert.Equal(0f, start.Eye.X, 3);
            now += 0.5;
            Assert.True(presentation.TryGetCamera(out var half));
            Assert.Equal(5f, half.Eye.X, 3);
            now += 5;
            Assert.True(presentation.TryGetCamera(out var held));
            Assert.Equal(10f, held.Eye.X, 3);

            presentation.SetCameraHeld(false);
            Assert.False(presentation.TryGetCamera(out _));
        }

        [Fact]
        public void StoppedShot_KeepsThePoseItReached()
        {
            var dat = SceneDat(
                (0x06, "c001", RoutePayload(0, Key(0, 0), Key(10, 1))),
                (0x07, "s001", RoutinePayload(60, Command(0x04, 6, 0, 60, System.Text.Encoding.ASCII.GetBytes("c001")))));
            var resource = EventSceneResource.Parse(dat);
            resource.TryGetRoutine("s001", out var routine);
            double now = 0;
            var presentation = new EventPresentation { Clock = () => now };
            presentation.Play(7, resource, routine, Vector3.Zero);
            now = 0.25;
            presentation.Stop(7);
            now = 5;
            Assert.True(presentation.TryGetCamera(out var pose));
            Assert.Equal(2.5f, pose.Eye.X, 3);
        }

        [Fact]
        public void Fades_MoveFromTheColourOnScreen_AndTheInterfaceFadesApart()
        {
            // fdo: 0F to black over 60 frames; fdi: 0F back to 0x80 over 60; fao: 51 to black.
            var dat = SceneDat(
                (0x07, "fdo1", RoutinePayload(60, Command(0x0F, 3, 60, 60, new byte[] { 0, 0, 0, 0x80 }))),
                (0x07, "fdi1", RoutinePayload(60, Command(0x0F, 3, 60, 60, new byte[] { 0x80, 0x80, 0x80, 0 }))),
                (0x07, "fao1", RoutinePayload(60, Command(0x51, 3, 60, 60, new byte[] { 0, 0, 0, 0x80 }))));
            var resource = EventSceneResource.Parse(dat);
            double now = 0;
            var presentation = new EventPresentation { Clock = () => now };
            resource.TryGetRoutine("fdo1", out var fdo);
            resource.TryGetRoutine("fdi1", out var fdi);
            resource.TryGetRoutine("fao1", out var fao);

            presentation.Play(1, resource, fdo, Vector3.Zero);
            now = 0.5;
            Assert.Equal(0.5f, presentation.SceneColor.X, 3);
            Assert.Equal(1f, presentation.InterfaceOpacity, 3);
            now = 1.5;
            Assert.Equal(0f, presentation.SceneColor.X, 3);

            presentation.Play(2, resource, fdi, Vector3.Zero);
            presentation.Play(3, resource, fao, Vector3.Zero);
            now = 2.0;
            Assert.Equal(0.5f, presentation.SceneColor.Y, 3);
            Assert.Equal(0.5f, presentation.InterfaceOpacity, 3);
            now = 3.0;
            Assert.Equal(Vector3.One, presentation.SceneColor);
            Assert.Equal(0f, presentation.InterfaceOpacity, 3);

            presentation.Reset();
            Assert.Equal(1f, presentation.InterfaceOpacity, 3);
        }

        private static byte[] Name(string fourCc, int padTo = 4) => System.Text.Encoding.ASCII.GetBytes(fourCc).Concat(new byte[padTo - 4]).ToArray();

        [Fact]
        public void EffectCommands_DecodeTheirGeneratorsAndRoutines()
        {
            // As in Port Jeuno 324's scene files (#192): 0x3F names two generators, each in an 8-byte slot.
            var dat = SceneDat((0x07, "test", RoutinePayload(100,
                Command(0x02, 4, 0, 160, Name("bk02")),
                Command(0x3F, 7, 10, 0, Name("mb00", 8).Concat(Name("mb02", 8)).ToArray()),
                Command(0x1E, 4, 0, 0, Name("bk00")),
                Command(0x03, 4, 0, 0, Name("strt")),
                Command(0x73, 5, 0, 0, Name("loop", 12)),
                Command(0x5F, 4, 0, 0, Name("tama")),
                Command(0x72, 3, 0, 6, new byte[] { 0x3A, 0x30, 0x44, 0x80 }),
                Command(0x60, 8, 0, 0, Name("8158", 24)))));
            var routine = EventSceneResource.Parse(dat).Routines["test"];
            Assert.True(routine.HasEffects);
            var c = routine.Commands;
            Assert.Equal((SceneCommandKind.SpawnGenerator, "bk02", 0, 160), (c[0].Kind, c[0].Reference, c[0].StartFrame, c[0].Duration));
            Assert.Equal((SceneCommandKind.ReplaceGenerator, "mb00", "mb02"), (c[1].Kind, c[1].Reference, c[1].Reference2));
            Assert.Equal((SceneCommandKind.KillGenerator, "bk00", 10), (c[2].Kind, c[2].Reference, c[2].StartFrame));
            Assert.Equal((SceneCommandKind.StartRoutine, "strt"), (c[3].Kind, c[3].Reference));
            Assert.Equal((SceneCommandKind.LoopRoutine, "loop"), (c[4].Kind, c[4].Reference));
            Assert.Equal((SceneCommandKind.StopRoutine, "tama"), (c[5].Kind, c[5].Reference));
            Assert.Equal(SceneCommandKind.ScreenFlash, c[6].Kind);
            Assert.Equal(new Vector3(0x44, 0x30, 0x3A) / 255f, EventPresentation.FlashColorOf(c[6].Color));
            Assert.Equal((SceneCommandKind.Sound, "8158"), (c[7].Kind, c[7].Reference)); // a sound (#167)
            Assert.False(c[6].IsEffect);
        }

        [Fact]
        public void WhiteFade_AddsWhiteOverTheScene_AndFadesBack()
        {
            // who1 / whi1 of file 30905: 0x72 to FF FF FF over 60 frames, then back to 00.
            var dat = SceneDat(
                (0x07, "who1", RoutinePayload(60, Command(0x72, 3, 60, 60, new byte[] { 0xFF, 0xFF, 0xFF, 0x80 }))),
                (0x07, "whi1", RoutinePayload(60, Command(0x72, 3, 60, 60, new byte[] { 0, 0, 0, 0x80 }))));
            var resource = EventSceneResource.Parse(dat);
            double now = 0;
            var presentation = new EventPresentation { Clock = () => now };
            Assert.Equal(Vector3.Zero, presentation.SceneFlash);
            presentation.Play(1, resource, resource.Routines["who1"], Vector3.Zero);
            now = 0.5;
            Assert.Equal(0.5f, presentation.SceneFlash.X, 3);
            Assert.Equal(Vector3.One, presentation.SceneColor); // the scene itself is not darkened
            now = 2;
            Assert.Equal(Vector3.One, presentation.SceneFlash);
            presentation.Play(2, resource, resource.Routines["whi1"], Vector3.Zero);
            now = 2.25;
            Assert.Equal(0.75f, presentation.SceneFlash.Z, 3);
            now = 4;
            Assert.Equal(Vector3.Zero, presentation.SceneFlash);
            presentation.Play(3, resource, resource.Routines["who1"], Vector3.Zero);
            now = 5;
            presentation.Reset();
            Assert.Equal(Vector3.Zero, presentation.SceneFlash);
        }

        [Fact]
        public void EffectLog_RecordsTasksThatRunGenerators_TheirStops_AndTheEnd()
        {
            var dat = SceneDat(
                (0x07, "bl00", RoutinePayload(3, Command(0x02, 4, 3, 2, Name("bk00")))),
                (0x07, "fdo1", RoutinePayload(60, Command(0x0F, 3, 60, 60, new byte[] { 0, 0, 0, 0x80 }))));
            var resource = EventSceneResource.Parse(dat);
            var presentation = new EventPresentation { Clock = () => 0 };
            presentation.Play(4, resource, resource.Routines["bl00"], new Vector3(1, 2, 3), 51402, 0x0100_0001, 0x0100_0002, 1.5f);
            presentation.Play(5, resource, resource.Routines["fdo1"], Vector3.Zero, 30904, 0x0100_0001, 0, 0f); // no effects: not logged
            presentation.Stop(5);
            presentation.Stop(4);
            presentation.Stop(4); // already stopped

            var log = new List<SceneEffectEvent>();
            long last = presentation.CopySceneEffects(0, log);
            Assert.Equal(2, log.Count);
            var start = log[0];
            Assert.Equal((SceneEffectEventKind.Start, 4, 51402, "bl00", 0x0100_0001u, 0x0100_0002u), (start.Kind, start.TaskId, start.FileId, start.Routine, start.CasterServerId, start.TargetServerId));
            Assert.Equal((new Vector3(1, 2, 3), 1.5f), (start.Origin, start.Heading));
            Assert.Same(resource, start.Resource);
            Assert.Equal((SceneEffectEventKind.Stop, 4), (log[1].Kind, log[1].TaskId));

            presentation.Reset();
            log.Clear();
            Assert.Equal(last + 1, presentation.CopySceneEffects(last, log));
            Assert.Equal(SceneEffectEventKind.Reset, Assert.Single(log).Kind);
        }

        /// <summary>
        /// Op 0x0E moves the blur like a fade (the A byte the share kept, B G R the tint, the float the zoom) and op 0x10
        /// starts a cross-dissolve from the frame before it (#205).
        /// </summary>
        [Fact]
        public void BlurAndCrossDissolve_AreScheduledLikeTheFades()
        {
            static byte[] Blur(uint bgra, float zoom)
            {
                var data = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(data, bgra);
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), zoom);
                return data;
            }
            var dat = SceneDat(
                (0x07, "blon", RoutinePayload(15, Command(0x0E, 5, 15, 15, Blur(0x30A0A0A0, 0.98f)))),
                (0x07, "blof", RoutinePayload(15, Command(0x0E, 5, 15, 15, Blur(0x00808080, 1.0f)))),
                (0x07, "ovl1", RoutinePayload(60, Command(0x10, 2, 60, 60, Array.Empty<byte>()))));
            var resource = EventSceneResource.Parse(dat);
            var command = Assert.Single(resource.Routines["blon"].Commands);
            Assert.Equal((SceneCommandKind.Blur, 0x30A0A0A0u, 0.98f), (command.Kind, command.Color, command.Factor));
            Assert.Equal(SceneCommandKind.CrossDissolve, Assert.Single(resource.Routines["ovl1"].Commands).Kind);

            double now = 10;
            var presentation = new EventPresentation { Clock = () => now };
            Assert.False(presentation.Blur.IsActive);
            Assert.Equal((0, 0f), presentation.CrossDissolve);

            presentation.Play(1, resource, resource.Routines["blon"], Vector3.Zero);
            now += 7.5 / 60;
            var half = presentation.Blur;
            Assert.Equal(0.1875f, half.Amount, 3); // half-way to 0x30 / 0x80
            Assert.Equal(0.99f, half.Zoom, 3);
            now += 1;
            var on = presentation.Blur;
            Assert.Equal((0.375f, 1.25f, 0.98f), (on.Amount, on.Tint.X, on.Zoom));

            presentation.Play(2, resource, resource.Routines["blof"], Vector3.Zero);
            now += 1;
            Assert.False(presentation.Blur.IsActive);
            Assert.Equal(Vector3.One, presentation.Blur.Tint);

            presentation.Play(3, resource, resource.Routines["ovl1"], Vector3.Zero);
            var (sequence, opacity) = presentation.CrossDissolve;
            Assert.True(sequence > 0);
            Assert.Equal(1f, opacity);
            now += 0.5;
            Assert.Equal((sequence, 0.5f), (presentation.CrossDissolve.Sequence, MathF.Round(presentation.CrossDissolve.Opacity, 3)));
            presentation.Play(4, resource, resource.Routines["ovl1"], Vector3.Zero);
            Assert.True(presentation.CrossDissolve.Sequence > sequence); // a new dissolve holds a new frame
            now += 2;
            Assert.Equal((0, 0f), presentation.CrossDissolve);

            presentation.Play(5, resource, resource.Routines["blon"], Vector3.Zero);
            presentation.Reset();
            now += 1;
            Assert.False(presentation.Blur.IsActive);
        }

        /// <summary>
        /// The Port Bastok intro's shots (scene resource p = 136, file 30840) and the shared fades (p = 200, file 30904)
        /// from the installed game: each shot routine plays the Route of its number, and the fades carry the colours the
        /// presentation reads.
        /// </summary>
        [Fact]
        public void RetailSceneResources_Decode()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var shots = EventSceneResource.Parse(rm.LoadDatBytesByFileId(EventSceneResource.GetFileId(136))!);
            Assert.True(shots.TryGetRoutine("s002", out var s002));
            Assert.Equal(1080, s002.TotalFrames);
            var move = Assert.Single(s002.Commands, c => c.Kind == SceneCommandKind.Camera);
            Assert.Equal(("c002", 0, 1080), (move.Reference, move.StartFrame, move.Duration));
            Assert.True(shots.TryGetRoute("c002", out var c002));
            Assert.All(c002.Keys, k => Assert.InRange(k.FocalLength, 100f, 2000f));
            Assert.Equal(0f, c002.Keys[0].Time);
            Assert.Equal(1f, c002.Keys[^1].Time);

            // The opening shot starts black and fades in as its camera move starts.
            Assert.True(shots.TryGetRoutine("s00s", out var opening));
            var fades = opening.Commands.Where(c => c.Kind == SceneCommandKind.SceneFade).ToList();
            Assert.Equal(2, fades.Count);
            Assert.Equal(Vector3.Zero, EventPresentation.ColorOf(fades[0].Color));
            Assert.Equal(Vector3.One, EventPresentation.ColorOf(fades[1].Color));

            // Every shot routine that plays a route finds it in the same file.
            int moves = 0;
            foreach (var routine in shots.Routines.Values)
            {
                foreach (var c in routine.Commands.Where(c => c.Kind == SceneCommandKind.Camera))
                {
                    Assert.True(shots.TryGetRoute(c.Reference, out _), $"{routine.Name} -> {c.Reference}");
                    moves++;
                }
            }
            Assert.True(moves > 30);

            var common = EventSceneResource.Parse(rm.LoadDatBytesByFileId(EventSceneResource.GetFileId(200))!);
            Assert.True(common.TryGetRoutine("fdo1", out var fdo1));
            var fadeOut = Assert.Single(fdo1.Commands, c => c.Kind == SceneCommandKind.SceneFade);
            Assert.Equal((60, Vector3.Zero), (fadeOut.Duration, EventPresentation.ColorOf(fadeOut.Color)));
            Assert.True(common.TryGetRoutine("fdi2", out var fdi2));
            Assert.Equal(Vector3.One, EventPresentation.ColorOf(Assert.Single(fdi2.Commands, c => c.Kind == SceneCommandKind.SceneFade).Color));
            Assert.True(common.TryGetRoutine("fao1", out var fao1));
            Assert.Single(fao1.Commands, c => c.Kind == SceneCommandKind.InterfaceFade);

            // The blur and cross-dissolve routines (#205).
            var blurOn = Assert.Single(common.Routines["blon"].Commands, c => c.Kind == SceneCommandKind.Blur);
            Assert.Equal((15, 0x30A0A0A0u), (blurOn.Duration, blurOn.Color));
            Assert.Equal(0.98f, blurOn.Factor, 3);
            Assert.Equal(0x00808080u, Assert.Single(common.Routines["blof"].Commands, c => c.Kind == SceneCommandKind.Blur).Color);
            Assert.Equal(60, Assert.Single(common.Routines["ovl1"].Commands, c => c.Kind == SceneCommandKind.CrossDissolve).Duration);
            Assert.Equal(120, Assert.Single(common.Routines["ovl2"].Commands, c => c.Kind == SceneCommandKind.CrossDissolve).Duration);
        }
    }
}
