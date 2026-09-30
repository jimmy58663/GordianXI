// src/Gordian.Core/World/NamePlateFlags.cs
using System;

namespace Gordian.Core.World
{
    /// <summary>
    /// The entity-update flags that change how a name plate is drawn over an entity: its colour, the icon beside it,
    /// the job mastery stars above it, or whether it is drawn at all. Decoded from S2C 0x00D (other players), 0x00E
    /// (NPCs and monsters) and 0x037 (the local player).
    /// Flag meanings referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x000D, 0x000E and
    /// 0x0037 flag fields).
    /// </summary>
    [Flags]
    public enum NamePlateFlags : uint
    {
        None = 0,

        /// <summary>LfgFlag: seeking a party (the glass orb with a green "!", lavender name).</summary>
        SeekingParty = 1 << 0,

        /// <summary>AnonymousFlag: job hidden (<c>/anon</c>), dark blue name.</summary>
        Anonymous = 1 << 1,

        /// <summary>YellFlag / CfhFlag: called for help on, orange name.</summary>
        CalledForHelp = 1 << 2,

        /// <summary>AwayFlag: <c>/away</c> (the blue and white circle).</summary>
        Away = 1 << 3,

        /// <summary>PlayOnelineFlag: logged out to PlayOnline (the blue PlayOnline globe).</summary>
        PlayOnline = 1 << 4,

        /// <summary>LinkShellFlag: wearing a linkshell item (the pearl, tinted with the linkshell's colour).</summary>
        Linkshell = 1 << 5,

        /// <summary>LinkDeadFlag: disconnecting (the red circle).</summary>
        LinkDead = 1 << 6,

        /// <summary>BazaarFlag: has a bazaar (the brown bag).</summary>
        Bazaar = 1 << 7,

        /// <summary>GmIconFlag: a GM hiding the GM icon.</summary>
        GmIconHidden = 1 << 8,

        /// <summary>AutoPartyFlag: auto-seeking a party (the red seeking orb).</summary>
        AutoParty = 1 << 9,

        /// <summary>LfgMasterFlag: seeking a master party (the green seeking orb with the master star).</summary>
        SeekingMasterParty = 1 << 10,

        /// <summary>TrialFlag: trial account (the green and yellow arrow).</summary>
        Trial = 1 << 11,

        /// <summary>NewCharacterFlag: new player (the red "?").</summary>
        NewPlayer = 1 << 12,

        /// <summary>MentorFlag on a player: the mentor "M".</summary>
        Mentor = 1 << 13,

        /// <summary>JobMasterFlag: current job mastered (three stars above the name).</summary>
        JobMaster = 1 << 14,

        /// <summary>MentorFlag on an NPC: an A.M.A.N. Liaison (the tutorial "i").</summary>
        InfoNpc = 1 << 15,

        /// <summary>
        /// An NPC whose name the client does not draw: the 0x00E flag that hides the health bar and the name
        /// (flags3 <c>unknown_3_5</c>).
        /// </summary>
        NameHidden = 1 << 16,
    }
}
