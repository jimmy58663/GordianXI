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
        }
    }
}
