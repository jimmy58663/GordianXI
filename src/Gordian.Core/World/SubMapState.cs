// src/Gordian.Core/World/SubMapState.cs
using System;
using System.Threading;

namespace Gordian.Core.World
{
    /// <summary>
    /// Where the player is inside the zone below the zone level, for a renderer that draws building interiors (#68):
    /// <list type="bullet">
    /// <item><see cref="SubMapNumber"/>: the sub-map (sub-region, LandSandBoat's "boundary") the player is in. Set by S2C
    /// 0x00A at every zone-in (<c>SubMapNumber</c>), by the server's S2C 0x10E answer, and by what the client tells the
    /// server with C2S 0x0F2.</item>
    /// <item><see cref="IndoorRoom"/>: the indoor room an event opened (event opcode 0x75 sub 0,
    /// <c>OpenIndoorNoSend</c>), -1 when none. A zone-in clears it.</item>
    /// <item><see cref="RequestPending"/>: C2S 0x0EB was sent (event opcode 0xA6 sub 0) and S2C 0x10E has not answered;
    /// the event waits on it (0xA6 sub 1).</item>
    /// </list>
    /// Behaviour referenced from XiPackets (https://github.com/atom0s/XiPackets, <c>world/server/0x010E</c>,
    /// <c>world/client/0x00F2</c>) and XiEvents (https://github.com/atom0s/XiEvents, <c>OpCodes/0x0075</c>,
    /// <c>OpCodes/0x00A6</c>). Thread-safe: the network thread writes, the event VM and the renderer read.
    /// </summary>
    public sealed class SubMapState
    {
        private int _subMapNumber;
        private int _indoorRoom = -1;
        private int _requestPending;

        /// <summary>The player's sub-map number (0 outside any sub-region).</summary>
        public int SubMapNumber => Volatile.Read(ref _subMapNumber);

        /// <summary>The indoor room an event opened, or -1.</summary>
        public int IndoorRoom => Volatile.Read(ref _indoorRoom);

        /// <summary>Whether a C2S 0x0EB request is waiting for its S2C 0x10E answer.</summary>
        public bool RequestPending => Volatile.Read(ref _requestPending) != 0;

        /// <summary>Raised with the new sub-map number whenever it changes.</summary>
        public event Action<int>? SubMapChanged;

        /// <summary>Raised with the room number when an event opens an indoor room, and with -1 when a zone-in clears it.</summary>
        public event Action<int>? IndoorRoomChanged;

        /// <summary>A zone-in (S2C 0x00A): the login packet's sub-map number, no indoor room, no request pending.</summary>
        public void OnZoneLogin(int subMapNumber)
        {
            Volatile.Write(ref _requestPending, 0);
            SetSubMapNumber(subMapNumber);
            if (Interlocked.Exchange(ref _indoorRoom, -1) != -1) IndoorRoomChanged?.Invoke(-1);
        }

        /// <summary>Records the sub-map number (S2C 0x10E, or the region sent with C2S 0x0F2).</summary>
        public void SetSubMapNumber(int subMapNumber)
        {
            if (Interlocked.Exchange(ref _subMapNumber, subMapNumber) != subMapNumber) SubMapChanged?.Invoke(subMapNumber);
        }

        /// <summary>C2S 0x0EB was sent: the event waits until <see cref="ReceiveSubMapNumber"/>.</summary>
        public void MarkRequested() => Volatile.Write(ref _requestPending, 1);

        /// <summary>S2C 0x10E: stores the number and ends the wait.</summary>
        public void ReceiveSubMapNumber(uint mapNum)
        {
            SetSubMapNumber(unchecked((int)mapNum));
            Volatile.Write(ref _requestPending, 0);
        }

        /// <summary>Event opcode 0x75 sub 0: the event opens an indoor room without telling the server.</summary>
        public void OpenIndoorRoom(int room)
        {
            if (Interlocked.Exchange(ref _indoorRoom, room) != room) IndoorRoomChanged?.Invoke(room);
        }
    }
}
