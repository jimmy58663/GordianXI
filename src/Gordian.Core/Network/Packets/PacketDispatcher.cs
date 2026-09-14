// src/Gordian.Core/Network/Packets/PacketDispatcher.cs
using System;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Handler delegate for incoming FFXI sub-packets with zero-allocation span slicing.
    /// </summary>
    public delegate void InboundPacketHandler(PacketHeader header, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Contract for the high-performance packet routing dispatcher.
    /// </summary>
    public interface IPacketDispatcher
    {
        /// <summary>
        /// Registers a handler for a specific 9-bit FFXI Packet ID (0x000 to 0x1FF).
        /// </summary>
        void Register(ushort packetId, InboundPacketHandler handler);

        /// <summary>
        /// Unregisters the handler for a specific Packet ID.
        /// </summary>
        void Unregister(ushort packetId);

        /// <summary>
        /// Dispatches an incoming sub-packet in O(1) time without allocations.
        /// </summary>
        bool Dispatch(PacketHeader header, ReadOnlySpan<byte> payload);
    }

    /// <summary>
    /// Ultra-fast direct-indexed packet dispatcher.
    /// Maps 9-bit FFXI Packet IDs (0x000..0x1FF) directly to an array of 512 handler delegates
    /// for O(1) lookup with zero dictionary overhead and zero memory allocations per packet.
    /// </summary>
    public sealed class PacketDispatcher : IPacketDispatcher
    {
        public const int MaxPacketOpcodes = 512;

        private readonly InboundPacketHandler?[] _handlers = new InboundPacketHandler?[MaxPacketOpcodes];

        /// <summary>
        /// Raised when a sub-packet is encountered with no registered handler.
        /// </summary>
        public event InboundPacketHandler? UnhandledPacket;

        /// <inheritdoc />
        public void Register(ushort packetId, InboundPacketHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ushort index = (ushort)(packetId & 0x1FF);
            _handlers[index] = handler;
            GordianLog.Debug("DISPATCHER", $"Registered handler for Packet ID 0x{index:X3}");
        }

        /// <inheritdoc />
        public void Unregister(ushort packetId)
        {
            ushort index = (ushort)(packetId & 0x1FF);
            _handlers[index] = null;
            GordianLog.Debug("DISPATCHER", $"Unregistered handler for Packet ID 0x{index:X3}");
        }

        /// <inheritdoc />
        public bool Dispatch(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            ushort index = (ushort)(header.PacketId & 0x1FF);
            InboundPacketHandler? handler = _handlers[index];

            if (handler != null)
            {
                handler.Invoke(header, payload);
                return true;
            }

            UnhandledPacket?.Invoke(header, payload);
            return false;
        }

        /// <summary>
        /// Checks whether a handler is registered for the specified packet ID.
        /// </summary>
        public bool HasHandler(ushort packetId)
        {
            ushort index = (ushort)(packetId & 0x1FF);
            return _handlers[index] != null;
        }
    }
}
