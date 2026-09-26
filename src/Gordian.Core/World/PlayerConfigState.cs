// src/Gordian.Core/World/PlayerConfigState.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// The server's copy of the character's configuration (S2C 0x0B4): the player flags (invite, away, anonymity,
    /// auto-target, mentor, headgear...), the two chat-filter words and the party-search languages. The server sends
    /// it on login and after every config change; the client must echo the flag word when it sends chat filters
    /// (C2S 0x0DB copies the whole word), so the last received value is kept here.
    /// </summary>
    public sealed class PlayerConfigState
    {
        private readonly object _sync = new();

        /// <summary>The SAVE_CONF flag word (see <see cref="PlayerConfigFlags"/>).</summary>
        public uint Flags { get; private set; }

        /// <summary>The first chat-filter word (<see cref="ChatFilter1"/>); a set bit hides that message kind.</summary>
        public uint MessageFilter1 { get; private set; }

        /// <summary>The second chat-filter word (<see cref="ChatFilter2"/>).</summary>
        public uint MessageFilter2 { get; private set; }

        public ushort PvpFlags { get; private set; }
        public byte AreaCode { get; private set; }
        public PartyLanguages PartyLanguages { get; private set; }

        /// <summary>True once the server has sent its configuration at least once this session.</summary>
        public bool Received { get; private set; }

        /// <summary>Raised after the server's configuration was applied.</summary>
        public event Action? Changed;

        public bool IsSet(PlayerConfigFlags flag) => (Flags & (uint)flag) != 0;

        /// <summary>The 2-bit system message filter level (0-3) carried in the flag word.</summary>
        public int SystemMessageFilterLevel => (int)((Flags & (uint)PlayerConfigFlags.SysMesFilterLevelMask) >> ConfigOutboundPackets.SystemMessageFilterLevelShift);

        /// <summary>Stores the values of an S2C 0x0B4 packet.</summary>
        public void Apply(in S2C_0x0B4_Config config)
        {
            lock (_sync)
            {
                Flags = config.Flags;
                MessageFilter1 = config.MessageFilter1;
                MessageFilter2 = config.MessageFilter2;
                PvpFlags = config.PvpFlags;
                AreaCode = config.AreaCode;
                PartyLanguages = config.PartyLanguages;
                Received = true;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Records what the client just sent, so the marker is right before the server's 0x0B4 reply arrives.
        /// </summary>
        internal void Expect(uint? flags = null, uint? filter1 = null, uint? filter2 = null)
        {
            lock (_sync)
            {
                if (flags is { } f) Flags = f;
                if (filter1 is { } f1) MessageFilter1 = f1;
                if (filter2 is { } f2) MessageFilter2 = f2;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                Flags = 0;
                MessageFilter1 = 0;
                MessageFilter2 = 0;
                PvpFlags = 0;
                AreaCode = 0;
                PartyLanguages = 0;
                Received = false;
            }
        }
    }
}
