// src/Gordian.Core/Network/MapLoginDatagramBuilder.cs
namespace Gordian.Core.Network
{
    /// <summary>
    /// Builds the unencrypted first datagram a map session sends (the C2S <c>0x00A</c> login), given how many zone changes
    /// the session has made: each zone change moves the map key on, and a builder whose packet depends on the key
    /// (retail's ticket) follows it.
    /// </summary>
    public delegate byte[] MapLoginDatagramBuilder(int zoneChanges);
}
