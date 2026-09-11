// tests/Gordian.App.Tests/PacketInspectorViewModelTests.cs
using System;
using System.Text;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Xunit;

namespace Gordian.App.Tests
{
    public class PacketInspectorViewModelTests : IDisposable
    {
        private readonly PacketInspectorViewModel _vm;

        public PacketInspectorViewModelTests()
        {
            _vm = new PacketInspectorViewModel();
        }

        public void Dispose()
        {
            _vm.Dispose();
        }

        [Fact]
        public void OnPacketInspected_AppendsPacketToCollection()
        {
            var entry = new PacketLogEntry
            {
                Direction = PacketDirection.Inbound,
                PacketId = 0x00A,
                PacketName = "GP_SERV_LOGIN",
                Size = 8,
                RawBytes = new byte[] { 0x0A, 0x02, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04 }
            };

            _vm.OnPacketInspected(null, entry);

            Assert.Single(_vm.FilteredPackets);
            Assert.Equal(1, _vm.TotalPacketCount);
            Assert.Equal(0x00A, _vm.FilteredPackets[0].PacketId);
        }

        [Fact]
        public void DirectionFilter_FiltersInboundAndOutboundCorrectly()
        {
            var inbound = new PacketLogEntry
            {
                Direction = PacketDirection.Inbound,
                PacketId = 0x008,
                PacketName = "GP_SERV_ENTERZONE",
                Size = 8,
                RawBytes = new byte[8]
            };
            var outbound = new PacketLogEntry
            {
                Direction = PacketDirection.Outbound,
                PacketId = 0x00D,
                PacketName = "GP_CLI_NETEND",
                Size = 8,
                RawBytes = new byte[8]
            };

            _vm.OnPacketInspected(null, inbound);
            _vm.OnPacketInspected(null, outbound);

            Assert.Equal(2, _vm.FilteredPackets.Count);

            // Filter Inbound only
            _vm.DirectionFilter = "Inbound";
            Assert.Single(_vm.FilteredPackets);
            Assert.Equal(PacketDirection.Inbound, _vm.FilteredPackets[0].Direction);

            // Filter Outbound only
            _vm.DirectionFilter = "Outbound";
            Assert.Single(_vm.FilteredPackets);
            Assert.Equal(PacketDirection.Outbound, _vm.FilteredPackets[0].Direction);

            // Return to All
            _vm.DirectionFilter = "All";
            Assert.Equal(2, _vm.FilteredPackets.Count);
        }

        [Fact]
        public void SearchFilter_FiltersByNameOrId()
        {
            var login = new PacketLogEntry
            {
                Direction = PacketDirection.Outbound,
                PacketId = 0x00A,
                PacketName = "GP_CLI_LOGIN",
                Size = 92,
                RawBytes = new byte[92]
            };
            var ping = new PacketLogEntry
            {
                Direction = PacketDirection.Inbound,
                PacketId = 0x015,
                PacketName = "GP_SERV_PING",
                Size = 4,
                RawBytes = new byte[4]
            };

            _vm.OnPacketInspected(null, login);
            _vm.OnPacketInspected(null, ping);

            _vm.SearchFilter = "LOGIN";
            Assert.Single(_vm.FilteredPackets);
            Assert.Equal("GP_CLI_LOGIN", _vm.FilteredPackets[0].PacketName);

            _vm.SearchFilter = "15";
            Assert.Single(_vm.FilteredPackets);
            Assert.Equal(0x015, _vm.FilteredPackets[0].PacketId);

            _vm.SearchFilter = string.Empty;
            Assert.Equal(2, _vm.FilteredPackets.Count);
        }

        [Fact]
        public void SelectedPacket_UpdatesHexAsciiDump()
        {
            byte[] payload = Encoding.ASCII.GetBytes("KupoPingPong");
            var entry = new PacketLogEntry
            {
                Direction = PacketDirection.Inbound,
                PacketId = 0x015,
                PacketName = "GP_SERV_PING",
                Size = payload.Length,
                RawBytes = payload
            };

            _vm.OnPacketInspected(null, entry);
            _vm.SelectedPacket = entry;

            Assert.Contains("KupoPingPong", _vm.FormattedDump);
            Assert.Contains("4B 75 70 6F", _vm.FormattedDump); // "Kupo" in hex
        }

        [Fact]
        public void MaxPackets_CapsBuffer()
        {
            _vm.MaxPackets = 5;

            for (int i = 0; i < 10; i++)
            {
                _vm.OnPacketInspected(null, new PacketLogEntry
                {
                    Direction = PacketDirection.Inbound,
                    PacketId = (ushort)(i + 1),
                    PacketName = $"Packet_{i}",
                    Size = 4,
                    RawBytes = new byte[4]
                });
            }

            Assert.Equal(5, _vm.TotalPacketCount);
            Assert.Equal(5, _vm.FilteredPackets.Count);
            Assert.Equal(6, _vm.FilteredPackets[0].PacketId); // First 5 discarded, packets 6..10 remain
        }

        [Fact]
        public void IsPaused_IgnoresIncomingPackets()
        {
            _vm.TogglePauseCommand.Execute(null);
            Assert.True(_vm.IsPaused);

            _vm.OnPacketInspected(null, new PacketLogEntry
            {
                Direction = PacketDirection.Inbound,
                PacketId = 0x00A,
                PacketName = "GP_SERV_LOGIN",
                Size = 4,
                RawBytes = new byte[4]
            });

            Assert.Empty(_vm.FilteredPackets);
            Assert.Equal(0, _vm.TotalPacketCount);
        }

        [Fact]
        public void ClearCommand_WipesCollectionAndSelection()
        {
            _vm.OnPacketInspected(null, new PacketLogEntry
            {
                Direction = PacketDirection.Inbound,
                PacketId = 0x00A,
                PacketName = "GP_SERV_LOGIN",
                Size = 4,
                RawBytes = new byte[4]
            });

            Assert.Single(_vm.FilteredPackets);

            _vm.ClearCommand.Execute(null);

            Assert.Empty(_vm.FilteredPackets);
            Assert.Equal(0, _vm.TotalPacketCount);
            Assert.Null(_vm.SelectedPacket);
        }
    }
}
