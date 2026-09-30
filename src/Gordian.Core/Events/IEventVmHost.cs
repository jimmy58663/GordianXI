// src/Gordian.Core/Events/IEventVmHost.cs
using System.Numerics;

namespace Gordian.Core.Events
{
    /// <summary>Who says a message the VM prints.</summary>
    public enum EventSpeaker
    {
        /// <summary>No speaker: the text alone (opcode 0x48 / 0x49).</summary>
        None,
        /// <summary>The event's entity, or the entity the opcode names, with its name in front (0x1D, 0x2B, 0xB0).</summary>
        Entity,
    }

    /// <summary>
    /// What the event VM asks of the client: printing messages, showing a choice list, talking to the server and
    /// reading a few facts about the player and the entities. Everything here runs on the VM's tick thread.
    /// </summary>
    public interface IEventVmHost
    {
        /// <summary>
        /// Prints a zone dialog message. Returns how long (seconds) the message stays open, holding the following
        /// 0x23 wait; 0 for a message that does not wait. The player's confirm (<see cref="EventVm.Confirm"/>)
        /// closes it early.
        /// </summary>
        double PrintMessage(int messageId, EventSpeaker speaker, uint speakerServerId, ushort speakerIndex);

        /// <summary>
        /// Shows a query menu: the message's lines before its choice list as comments, its choices as options.
        /// <paramref name="hiddenMask"/> bit n set hides option n; <paramref name="defaultIndex"/> is the option the
        /// cursor starts on (counted over the shown options). The VM then polls <see cref="QueryResult"/>.
        /// </summary>
        void OpenQuery(int messageId, int defaultIndex, uint hiddenMask);

        /// <summary>
        /// 0 while the query is open, 255 when the player cancelled it, otherwise the 1-based number of the chosen
        /// option counted over every option of the message (hidden ones included, as retail numbers them).
        /// </summary>
        int QueryResult { get; }

        /// <summary>Closes the query menu (after the VM took its result).</summary>
        void CloseQuery();

        /// <summary>Sends the event update (0x05B mode 1) with the given parameter and marks a server reply pending.</summary>
        void SendEventUpdate(uint endParameter);

        /// <summary>
        /// Sends the event position update (0x05C mode 1: a warp within the zone, as a home point teleport) with the
        /// given parameter and destination (game units; heading in radians), and marks a server reply pending.
        /// </summary>
        void SendEventUpdateXzy(uint endParameter, float x, float y, float z, float heading);

        /// <summary>Whether an event update is still waiting for the server's reply (0x052 mode 1 clears it).</summary>
        bool ReceivePending { get; }

        /// <summary>Locks or releases the player's character control (opcode 0x20).</summary>
        void SetControlLock(bool locked);

        /// <summary>Whether an entity with this server id is in the zone (opcode 0x44).</summary>
        bool EntityExists(uint serverId);

        /// <summary>
        /// A fact about an entity the scripts read through the 0x7F00 / 0x7F80 operand keys: 0x06 job, 0x07 race,
        /// 0x08 level, 0x0A server id, 0x00-0x02 position (x, y, z in millimetres), 0x03 heading (0-4095).
        /// </summary>
        int GetEntityValue(uint serverId, int key);

        /// <summary>The Vana'diel game time value scripts store (opcode 0x83).</summary>
        int GameTime { get; }

        /// <summary>An opcode the VM stepped over without running, for diagnostics.</summary>
        void OnSkippedOpcode(byte opcode, int pc);

        /// <summary>
        /// The member at <paramref name="slot"/> of party <paramref name="party"/> (0 = the player's own party, where
        /// slot 0 is the player; 1 and 2 = the alliance's other parties), for the scripts' party actor codes
        /// (<see cref="EventVm.TryGetPartySlot"/>). False when the slot is empty.
        /// </summary>
        bool TryGetPartyMember(int party, int slot, out uint serverId, out ushort index)
        {
            serverId = 0;
            index = 0;
            return false;
        }

        /// <summary>
        /// Where an entity stands (internal axes, Y = height), which way it faces (wire-convention radians) and its
        /// movement speed in yalms per second: an event entity's starting <c>EventPos</c> / <c>EventDir</c> /
        /// <c>MainSpeed</c> (XiEvent::XiEventInit), and the target of a look-at. False when the entity is unknown.
        /// </summary>
        bool TryGetEntityPose(uint serverId, out Vector3 position, out float heading, out float speed)
        {
            position = default;
            heading = 0;
            speed = 0;
            return false;
        }

        /// <summary>
        /// Places an entity for the event (the staging opcodes 0x36 / 0x37 / 0xBA, walks 0x1F / 0x5A, facing 0x39 /
        /// 0x1E / 0x4A / 0x4B): position, heading and, while walking, the walk speed (0 when standing).
        /// </summary>
        void SetEntityPose(uint serverId, Vector3 position, float heading, float speed)
        {
        }

        /// <summary>Sets an entity's event hide flag (opcodes 0x22 / 0x4E).</summary>
        void SetEntityHidden(uint serverId, bool hidden)
        {
        }

        /// <summary>Turns the HUD's cutscene mode on (opcode 0x67) or off (0x68).</summary>
        void SetCutsceneHud(bool on)
        {
        }

        /// <summary>
        /// Opcode 0x77: stops the game clock at <paramref name="hour"/> (minute 0) and / or sets the weather number;
        /// -1 leaves that one alone.
        /// </summary>
        void LockEnvironment(int hour, int weather)
        {
        }

        /// <summary>Opcode 0x78: the clock runs again and the zone's weather returns.</summary>
        void UnlockEnvironment()
        {
        }
    }
}
