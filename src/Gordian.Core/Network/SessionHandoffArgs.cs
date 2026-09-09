// src/Gordian.Core/Network/SessionHandoffArgs.cs
using System;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Represents the authenticated network connection parameters received from the 32-bit proxy bootloader pipe.
    /// </summary>
    public sealed class SessionHandoffArgs
    {
        public string TargetCharacterName { get; set; } = string.Empty;
        public string ServerIp { get; set; } = "127.0.0.1";
        public ushort ServerPort { get; set; } = 54231;
        public uint CharacterId { get; set; }
        public string Base64SessionToken { get; set; } = string.Empty;
    }
}
