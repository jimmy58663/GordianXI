// src/Gordian.Core/Events/IEventVmHost.cs
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
        /// Prints a zone dialog message. Returns true when the message ends with a prompt, so the VM waits for the
        /// player's confirm (<see cref="EventVm.Confirm"/>) before it goes on.
        /// </summary>
        bool PrintMessage(int messageId, EventSpeaker speaker, uint speakerServerId, ushort speakerIndex);

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
    }
}
