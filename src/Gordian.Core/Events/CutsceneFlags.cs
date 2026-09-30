// src/Gordian.Core/Events/CutsceneFlags.cs
using System;

namespace Gordian.Core.Events
{
    /// <summary>
    /// The event's mode flags (the Mode field of S2C 0x032 / 0x033 / 0x034, the zone-in EventMode of S2C 0x00A).
    /// Bit meanings referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>scripts/enum/cutscene_flag.lua</c>;
    /// the new-character intros use RESET_CAMERA | NO_PCS | OPENING_MODE (Windurst Waters and Woods add NO_NPCS).
    /// </summary>
    [Flags]
    public enum CutsceneFlags : uint
    {
        None = 0,
        /// <summary>On end: the player back at the server's position, the camera behind the player.</summary>
        ResetCamera = 0x0001,
        /// <summary>Other player characters not in the event are not drawn.</summary>
        NoPcs = 0x0002,
        /// <summary>Keep sending the player's position while the event moves them.</summary>
        SendPosition = 0x0004,
        /// <summary>NPCs and monsters not in the event are not drawn.</summary>
        NoNpcs = 0x0010,
        /// <summary>Drop scheduler packets whose caster or target is in the event.</summary>
        NoParticipantAnimation = 0x0020,
        /// <summary>Suppress the server's zone messages (the TalkNum family) during the event.</summary>
        NoDialogue = 0x0040,
        /// <summary>Zone-in only: the opening cutscene mode.</summary>
        OpeningMode = 0x0080,
        /// <summary>Start at once without waiting for the actors to be idle.</summary>
        NoIdleWait = 0x0100,
        /// <summary>Hide the target window for the event.</summary>
        HideTargetWindow = 0x10000,
    }
}
