// tests/Gordian.Core.Tests/Network/PacketDecoderRegistrationTests.cs
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// Regression guard: every S2C decoder in <c>Gordian.Core.Network.Packets</c> must be reachable
    /// from the dispatcher that <see cref="PacketParser"/> wires up. A decoder that no module registers
    /// (as S2C 0x073 once was) is dead code and its packets fall through to <c>UnhandledPacket</c>.
    /// </summary>
    public class PacketDecoderRegistrationTests
    {
        private const string DecoderPrefix = "S2C_0x";

        private static Type[] DecoderTypes() =>
            typeof(PacketDispatcher).Assembly.GetTypes()
                .Where(t => t.Namespace == typeof(PacketDispatcher).Namespace
                    && t.Name.StartsWith(DecoderPrefix, StringComparison.Ordinal))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToArray();

        private static ushort ReadPacketId(Type decoder)
        {
            FieldInfo? field = decoder.GetField("PacketId", BindingFlags.Public | BindingFlags.Static);
            Assert.True(field is { IsLiteral: true, FieldType: var ft } && ft == typeof(ushort),
                $"{decoder.Name} has no public const ushort PacketId");
            return (ushort)field!.GetRawConstantValue()!;
        }

        [Fact]
        public void DecoderDiscovery_FindsDecoders()
        {
            // Guards the reflection filter itself: an empty set would make the reachability test vacuous.
            Assert.True(DecoderTypes().Length > 50, $"Only found {DecoderTypes().Length} S2C decoders");
        }

        [Fact]
        public void EveryDecoder_PacketIdMatchesTypeName()
        {
            var mismatched = DecoderTypes()
                .Where(t => ushort.Parse(t.Name.AsSpan(DecoderPrefix.Length, 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture) != ReadPacketId(t))
                .Select(t => $"{t.Name} (PacketId 0x{ReadPacketId(t):X3})")
                .ToArray();

            Assert.True(mismatched.Length == 0, "PacketId disagrees with the type name: " + string.Join(", ", mismatched));
        }

        [Fact]
        public void EveryDecoder_IsRegisteredWithDispatcher()
        {
            var dispatcher = new PacketDispatcher();
            _ = new PacketParser(new SessionProfile(), (_, _) => Task.CompletedTask, dispatcher: dispatcher);

            var orphaned = DecoderTypes()
                .Where(t => !dispatcher.HasHandler(ReadPacketId(t)))
                .Select(t => t.Name)
                .ToArray();

            Assert.True(orphaned.Length == 0, "S2C decoders with no dispatcher registration: " + string.Join(", ", orphaned));
        }
    }
}
