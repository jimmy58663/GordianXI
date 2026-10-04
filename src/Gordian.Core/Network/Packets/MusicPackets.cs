// src/Gordian.Core/Network/Packets/MusicPackets.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// S2C 0x05F <c>GP_SERV_COMMAND_MUSIC</c>: sets one music slot to a track. Payload (after the 4-byte header):
    /// <c>u16 Slot</c> (0-7, see <see cref="MusicSlot"/>), <c>u16 MusicNum</c>.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x005F);
    /// LandSandBoat sends it for battle music and Lua <c>changeMusic</c> (https://github.com/LandSandBoat/server).
    /// </summary>
    public readonly ref struct S2C_0x05F_Music
    {
        /// <summary>The opcode.</summary>
        public const ushort PacketId = 0x05F;

        /// <summary>The slot to change.</summary>
        public ushort Slot { get; }

        /// <summary>The music number (<c>musicNNN.bgw</c>); 0 = silence.</summary>
        public ushort MusicNum { get; }

        /// <summary>Whether the payload was long enough.</summary>
        public bool IsValid { get; }

        /// <summary>Decodes the payload.</summary>
        public S2C_0x05F_Music(ReadOnlySpan<byte> payload)
        {
            IsValid = payload.Length >= 4;
            Slot = IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(payload) : (ushort)0;
            MusicNum = IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)) : (ushort)0;
        }
    }

    /// <summary>
    /// S2C 0x060 <c>GP_SERV_COMMAND_MUSICVOLUME</c>: fades the music volume. Payload: <c>u16 time</c> (the client lerps
    /// over it; the unit is not documented), <c>u16 volume</c> (0-127).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0060).
    /// LandSandBoat declares it but never sends it.
    /// </summary>
    public readonly ref struct S2C_0x060_MusicVolume
    {
        /// <summary>The opcode.</summary>
        public const ushort PacketId = 0x060;

        /// <summary>The fade time.</summary>
        public ushort Time { get; }

        /// <summary>The target volume, 0-127.</summary>
        public ushort Volume { get; }

        /// <summary>Whether the payload was long enough.</summary>
        public bool IsValid { get; }

        /// <summary>Decodes the payload.</summary>
        public S2C_0x060_MusicVolume(ReadOnlySpan<byte> payload)
        {
            IsValid = payload.Length >= 4;
            Time = IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(payload) : (ushort)0;
            Volume = IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)) : (ushort)0;
        }
    }

    /// <summary>Feeds S2C 0x05F / 0x060 into a <see cref="ZoneMusicState"/> (0x00A's table arrives through the lifecycle module).</summary>
    public sealed class MusicPacketModule
    {
        private readonly ZoneMusicState _music;

        /// <summary>Builds the module over the state it writes.</summary>
        public MusicPacketModule(ZoneMusicState music)
        {
            _music = music ?? throw new ArgumentNullException(nameof(music));
        }

        /// <summary>Registers the handlers.</summary>
        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x05F_Music.PacketId, HandleMusic);
            dispatcher.Register(S2C_0x060_MusicVolume.PacketId, HandleMusicVolume);
        }

        private void HandleMusic(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var packet = new S2C_0x05F_Music(payload);
            if (!packet.IsValid)
            {
                return;
            }

            GordianLog.Debug("AUDIO", $"0x05F music slot {packet.Slot} = {packet.MusicNum}");
            _music.SetSlot(packet.Slot, packet.MusicNum);
        }

        private void HandleMusicVolume(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var packet = new S2C_0x060_MusicVolume(payload);
            if (!packet.IsValid)
            {
                return;
            }

            GordianLog.Debug("AUDIO", $"0x060 music volume {packet.Volume} over {packet.Time}");
            _music.SetVolume(packet.Time, packet.Volume);
        }
    }
}
