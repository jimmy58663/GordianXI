// src/Gordian.Core/Network/Packets/StandardMessages.cs
using System.Collections.Generic;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Formatter and dictionary mapping standard FFXI system message IDs (MsgStd) to human-readable text.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/msg_std.h).
    /// </summary>
    public static class StandardMessages
    {
        private static readonly Dictionary<ushort, string> KnownMessages = new()
        {
            [2] = "You could not enter the next area.",
            [5] = "You could not enter your room.",
            [11] = "Your invitation was declined.",
            [12] = "That person is a party member.",
            [13] = "You cannot perform that job emote.",
            [14] = "That request cannot be processed.",
            [20] = "Your call has been placed in the queue.",
            [21] = "You are not the party leader.",
            [22] = "Caution: All unclaimed treasure will be lost if you join a party.",
            [23] = "You cannot invite that person at this time.",
            [32] = "Unable to perform action in Mog House.",
            [33] = "That person is currently away and cannot respond.",
            [36] = "There are no party members.",
            [37] = "You do not have a linkshell item equipped.",
            [38] = "You must wait longer to perform that action.",
            [107] = "Trade canceled.",
            [108] = "Equip a linkshell, pearlsack, or linkpearl before using that command.",
            [109] = "You have been kicked out of the linkshell group.",
            [110] = "That linkshell group no longer exists.",
            [117] = "Event skipped.",
            [125] = "Your tell was not received. The recipient is offline or unavailable.",
            [142] = "You cannot use that command at the moment. Please try again later.",
            [181] = "Your tell was not received. The recipient is currently away.",
            [182] = "That person is already in an alliance.",
            [183] = "Unable to process request.",
            [221] = "Blockaid activated.",
            [222] = "Blockaid canceled.",
            [225] = "Target is currently blocking outside magical assistance, trades, and party invites.",
            [226] = "Interaction from a non-party character was blocked by /blockaid.",
            [235] = "Warning! This is a Level Sync party.",
            [236] = "You cannot invite that person at this time. This player is either undergoing Level Sync or unable to sync.",
            [237] = "You cannot join this party. You are either undergoing Level Sync or unable to sync.",
            [238] = "The party's level has been restricted.",
            [256] = "You cannot use that command in this area.",
            [265] = "You are unable to join a party whose leader currently has an alter ego present.",
            [266] = "You are unable to join an alliance whose leader currently has an alter ego present.",
            [296] = "You cannot use Trust magic while seeking a party.",
            [297] = "While inviting a party member, you must wait a while before using Trust magic.",
            [298] = "You have called forth your maximum number of alter egos.",
            [308] = "An error has occurred."
        };

        /// <summary>
        /// Attempts to get the localized/standard English string for a standard message ID.
        /// </summary>
        public static bool TryGetMessage(ushort messageId, out string message)
        {
            return KnownMessages.TryGetValue(messageId, out message!);
        }

        /// <summary>
        /// Formats a SystemMessage into a user-friendly display string.
        /// </summary>
        public static string FormatMessage(SystemMessage msg)
        {
            if (TryGetMessage(msg.MessageId, out string knownText))
            {
                return !string.IsNullOrEmpty(msg.Parameters) ? $"{knownText} ({msg.Parameters})" : knownText;
            }

            return !string.IsNullOrEmpty(msg.Parameters)
                ? $"Msg#{msg.MessageId}: {msg.Parameters}"
                : $"Msg#{msg.MessageId}";
        }
    }
}
