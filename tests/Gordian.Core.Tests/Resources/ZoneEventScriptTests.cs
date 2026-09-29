// tests/Gordian.Core.Tests/Resources/ZoneEventScriptTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneEventScriptTests
    {
        /// <summary>Builds an event DAT from (actor, event ids, offsets, references, code) blocks as retail lays them out.</summary>
        internal static byte[] BuildEventDat(params (uint Actor, ushort[] Ids, ushort[] Offsets, uint[] References, byte[] Code)[] blocks)
        {
            var blockBytes = new List<byte[]>();
            foreach (var (actor, ids, offsets, references, code) in blocks)
            {
                var b = new List<byte>();
                void U32(uint v) { var t = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(t, v); b.AddRange(t); }
                void U16(ushort v) { var t = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(t, v); b.AddRange(t); }
                U32(actor);
                U32((uint)ids.Length);
                foreach (var o in offsets) U16(o);
                foreach (var id in ids) U16(id);
                U32((uint)references.Length);
                foreach (var r in references) U32(r);
                U32((uint)code.Length);
                b.AddRange(code);
                while (b.Count % 4 != 0) b.Add(0);
                blockBytes.Add(b.ToArray());
            }
            var file = new List<byte>();
            var tmp = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(tmp, blockBytes.Count);
            file.AddRange(tmp);
            foreach (var block in blockBytes)
            {
                BinaryPrimitives.WriteInt32LittleEndian(tmp, block.Length);
                file.AddRange(tmp);
            }
            foreach (var block in blockBytes) file.AddRange(block);
            return file.ToArray();
        }

        [Fact]
        public void Parse_ReadsBlocksEventsAndReferences()
        {
            var file = BuildEventDat(
                (EventBlock.PlayerActor, new ushort[] { 100, EventBlock.AnyEventId }, new ushort[] { 0, 3 }, new uint[] { 7 }, new byte[] { 0x21, 0x00, 0x00, 0x00, 0x00 }),
                (0x010E6001, new ushort[] { 615 }, new ushort[] { 0 }, Array.Empty<uint>(), new byte[] { 0x00 }));
            var script = ZoneEventScript.Parse(file);
            Assert.NotNull(script);
            Assert.Equal(2, script!.Blocks.Count);

            Assert.True(script.TryGetBlock(EventBlock.PlayerActor, out var player));
            Assert.Equal(new uint[] { 7 }, player.References);
            Assert.True(player.TryGetEvent(100, out int start, out int end));
            Assert.Equal((0, 3), (start, end));
            // An unknown id falls back to the block's 0xFFFE event.
            Assert.True(player.TryGetEvent(999, out start, out end));
            Assert.Equal((3, 5), (start, end));

            Assert.True(script.TryGetBlock(0x010E6001, out var npc));
            Assert.False(npc.TryGetEvent(999, out _, out _));
            Assert.Same(npc, script.FindEvent(615));
            Assert.Null(script.FindEvent(616));
        }

        [Fact]
        public void Parse_RejectsTruncatedFiles()
        {
            var file = BuildEventDat((1, new ushort[] { 1 }, new ushort[] { 0 }, Array.Empty<uint>(), new byte[] { 0 }));
            Assert.Null(ZoneEventScript.Parse(file.AsSpan(0, file.Length - 4)));
            Assert.Null(ZoneEventScript.Parse(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }));
        }

        [Theory]
        [InlineData(230, 6050)]
        [InlineData(0, 5820)]
        [InlineData(256, 84991)]
        [InlineData(300, -1)]
        public void GetFileId_FollowsTheRetailFileTable(int zone, int expected)
        {
            Assert.Equal(expected, ZoneEventScript.GetFileId(zone));
        }
    }
}
