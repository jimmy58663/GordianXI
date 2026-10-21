// src/Gordian.Core/World/MountAppearance.cs
namespace Gordian.Core.World
{
    /// <summary>What a player rides (#87): nothing, a chocobo (the chocobo race model) or another mount (its own model).</summary>
    public enum MountKind : byte
    {
        None = 0,
        Chocobo,
        Mount,
    }

    /// <summary>
    /// A player's mount as the server reports it (#87): from S2C 0x00D for other players (<c>server_status</c>,
    /// <c>Flags6.MountIndex</c>, <c>Flags1.ChocoboIndex</c>, <c>CustomProperties[0]</c>) and from S2C 0x037 for the local
    /// player (<c>server_status</c>, <c>mount_id</c>, <c>Chocobo_Index</c>).
    /// <para>
    /// Status 5 (<c>xi.animation.CHOCOBO</c>) rides a chocobo, status 85 (<c>xi.animation.MOUNT</c>) any other mount. The
    /// wire mount number is the client's mount id (the mount list's order: 0 Chocobo, 1 Raptor, 2 Tiger ... 33 Noble
    /// Chocobo) plus one, 0 for a plain chocobo: LandSandBoat adds 1 to the id the client sends in C2S 0x01A
    /// (<c>0x01a_action.cpp</c>) and sends that back in 0x00D and 0x037, and its <c>xi.mount</c> enum carries the extra
    /// <c>QUEST_RAPTOR = 1</c> slot, which "now appears as a plain chocobo" (wire 1 = client id 0). Mounted speed is the
    /// server's speed doubled: LandSandBoat sends <c>MOUNT_SPEED</c> (80) halved (<c>CBattleEntity::UpdateSpeed</c>), and
    /// the maintainer's retail capture (Mounts.mp4, 2026-10-16/17) measures 8 yalms/s on a chocobo and a raptor alike.
    /// </para>
    /// Field layout referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000D and 0x0037, and
    /// LandSandBoat (https://github.com/LandSandBoat/server) <c>scripts/effects/mounted.lua</c>, <c>utils/mountutils.cpp</c>.
    /// </summary>
    /// <param name="Kind">What is ridden.</param>
    /// <param name="MountId">The client's mount id (0 Chocobo, 1 Raptor ... 33 Noble Chocobo); 0 when not mounted.</param>
    /// <param name="ChocoboIndex">The server's chocobo index: 1 a rental chocobo, 2 a personal chocobo (its look in
    /// <paramref name="CustomProperty"/>), (id % 8) + 1 other mounts; XiPackets: the rider's seating changes with it.</param>
    /// <param name="CustomProperty">A personal chocobo's look and stats (LandSandBoat <c>ChocoboCustomProperties</c>).</param>
    public readonly record struct MountAppearance(MountKind Kind, byte MountId, byte ChocoboIndex = 0, uint CustomProperty = 0)
    {
        /// <summary>Server status of a player on a chocobo (LandSandBoat <c>xi.animation.CHOCOBO</c>).</summary>
        public const byte StatusChocobo = 5;

        /// <summary>Server status of a player on another mount (LandSandBoat <c>xi.animation.MOUNT</c>).</summary>
        public const byte StatusMount = 85;

        /// <summary>The client's mount id of the Noble Chocobo, which has no model of its own and rides the chocobo.</summary>
        public const byte NobleChocoboId = 33;

        /// <summary>The server's chocobo index of a personal (raised) chocobo (LandSandBoat <c>mountutils::packetDefinition</c>).</summary>
        public const byte PersonalChocoboIndex = 2;

        /// <summary>How much faster a mounted player moves than the speed the server reports (see the type's remarks).</summary>
        public const int ServerSpeedFactor = 2;

        public static readonly MountAppearance None = default;

        public bool IsMounted => Kind != MountKind.None;

        /// <summary>Whether the ridden model is the chocobo race model (a chocobo, or the Noble Chocobo).</summary>
        public bool RidesChocobo => Kind == MountKind.Chocobo;

        /// <summary>A personal (raised) chocobo, whose colour and parts <see cref="CustomProperty"/> sets.</summary>
        public bool IsPersonalChocobo => Kind == MountKind.Chocobo && ChocoboIndex == PersonalChocoboIndex;

        /// <summary>
        /// Whether the mounted command menu offers Dig: on a chocobo, rented or raised (LandSandBoat lets any chocobo dig,
        /// <c>0x01a_action.cpp</c> ChocoboDig: "Only rented and personal chocobos can dig"), not on other mounts or the
        /// Noble Chocobo.
        /// </summary>
        public bool CanDig => Kind == MountKind.Chocobo && MountId == 0;

        /// <summary>
        /// Decodes the mount from a server status and the wire mount number (0x00D <c>MountIndex</c>, 0x037 <c>mount_id</c>).
        /// The wire number is kept by the server after a dismount (LandSandBoat sends it "almost always"), so only the
        /// status says whether the player rides.
        /// </summary>
        public static MountAppearance FromWire(byte serverStatus, byte wireMount, byte chocoboIndex = 0, uint customProperty = 0)
        {
            switch (serverStatus)
            {
                case StatusChocobo:
                    // A plain or personal chocobo is wire 0; the Noble Chocobo also rides as a chocobo (LandSandBoat mounted.lua).
                    return new MountAppearance(MountKind.Chocobo, wireMount > 0 ? (byte)(wireMount - 1) : (byte)0, chocoboIndex, customProperty);
                case StatusMount:
                    byte id = wireMount > 0 ? (byte)(wireMount - 1) : (byte)0;
                    return new MountAppearance(id is 0 or NobleChocoboId ? MountKind.Chocobo : MountKind.Mount, id, chocoboIndex, customProperty);
                default:
                    return None;
            }
        }
    }
}
