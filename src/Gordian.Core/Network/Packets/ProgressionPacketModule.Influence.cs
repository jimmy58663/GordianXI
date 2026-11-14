// src/Gordian.Core/Network/Packets/ProgressionPacketModule.Influence.cs
using System;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Packets
{
    public sealed partial class ProgressionPacketModule
    {
        /// <summary>S2C 0x071: the Campaign (mode 2) or Colonization (mode 3) map's figures for Region Info (#492).</summary>
        private void HandleInfluence(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var influence = new S2C_0x071_Influence(payload);
            if (!influence.IsValid)
            {
                GordianLog.Debug(LogCategories.Conquest, $"S2C 0x071 mode {influence.Mode} ignored ({payload.Length} bytes).");
                return;
            }
            _progressionState.UpdateInfluence(in influence);
        }
    }
}
