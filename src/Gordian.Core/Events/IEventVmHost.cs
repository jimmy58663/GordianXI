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

        /// <summary>
        /// 0x33 (the event's own entity) / 0x59 sub 5 (a named actor): keeps the height the event places the entity at
        /// (true) or puts it on the floor (false); retail <c>Render.Flags0</c> bit 21 (XiEvents OpCodes/0x0033, 0x0059).
        /// </summary>
        void SetEntityKeepsHeight(uint serverId, bool keep)
        {
        }

        /// <summary>
        /// 0x92 (a named actor): hides the entity's name plate for the event (true) or shows it again; retail
        /// <c>Render.Flags3</c> bit 16 (XiEvents OpCodes/0x0092; the name plate reading is ours, #191).
        /// </summary>
        void SetEntityHidesName(uint serverId, bool hide)
        {
        }

        /// <summary>Sets an entity's event hide flag (opcodes 0x22 / 0x4E).</summary>
        void SetEntityHidden(uint serverId, bool hidden)
        {
        }

        /// <summary>
        /// Turns the event message mode on (opcode 0x67, with its two work values) or off (0x68): while on, the HUD is
        /// hidden and the event's lines show on the screen instead of the log.
        /// </summary>
        void SetEventMessageMode(bool on, int x, int y)
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

        /// <summary>
        /// Opcode 0x45: plays routine <paramref name="routine"/> of the scene resource DAT <paramref name="fileId"/> as task
        /// <paramref name="taskId"/> on two actors (server ids; 0 = the local player): its camera shots and fades.
        /// Returns how many 60 Hz frames the task runs; 0 when the resource or routine is missing, so a wait on it ends at once.
        /// </summary>
        int StartSceneTask(int taskId, int fileId, string routine, uint casterServerId, uint targetServerId) => 0;

        /// <summary>Opcode 0x52 (or the same task started again): stops the task's routine where it is.</summary>
        void StopSceneTask(int taskId)
        {
        }

        /// <summary>
        /// Opcodes 0x34 / 0x35: the event deletes its actors and opens another zone for the scene (a zone id), or the
        /// player's own zone again.
        /// </summary>
        void OpenEventZone(int zoneId)
        {
        }

        /// <summary>Whether the zone of <see cref="OpenEventZone"/> is still loading (0x34 / 0x35 wait for it).</summary>
        bool IsEventZoneLoading => false;

        /// <summary>
        /// Opcodes 0x1E / 0x4A / 0x79 (lookatone): an entity (server id; 0 = the local player) looks at another, with
        /// retail's speech frame; <paramref name="targetServerId"/> <see cref="uint.MaxValue"/> ends the look (0x7B).
        /// </summary>
        void SetEntityLook(uint serverId, uint targetServerId, int speechFrame)
        {
        }

        /// <summary>
        /// Opcode 0x79 sub 2: an entity (server id; 0 = the local player) holds its head on a fixed look axis, the two work
        /// values retail stores as <c>LookAxisX</c> / <c>LookAxisY</c> with look mode 2; 0x7B or another look ends it.
        /// </summary>
        void SetEntityLookAxis(uint serverId, int axisX, int axisY)
        {
        }

        /// <summary>
        /// Opcode 0x59 sub 2 / 3: the speed an entity (server id; 0 = the local player) turns its head at (retail
        /// <c>TurnSpeedHead</c>, a work value).
        /// </summary>
        void SetEntityHeadTurnSpeed(uint serverId, int speed)
        {
        }

        /// <summary>Opcode 0x46: the event takes the camera from the player (true) or gives it back.</summary>
        void SetEventCamera(bool held)
        {
        }

        /// <summary>
        /// Opcodes 0x2C / 0x5B / 0x66: an entity (server id; 0 = the local player) plays the motion routine
        /// <paramref name="routine"/> toward <paramref name="targetServerId"/>, from its own motions
        /// (<see cref="EventMotionSource.Own"/>), after loading the event motion DAT <paramref name="resource"/>
        /// (<see cref="EventMotionSource.Bank"/>: a file id), or from a player-model motion package
        /// (<see cref="EventMotionSource.Package"/>). Returns how many 60 Hz frames it plays (0 when it has none).
        /// </summary>
        int PlayEntityMotion(uint serverId, EventMotionSource source, int resource, string routine, uint targetServerId) => 0;

        /// <summary>
        /// Opcode 0x6E: an entity (server id; 0 = the local player) plays an emote (the C2S 0x05D ids: 13 = clap), with
        /// the opcode's second byte as <paramref name="variant"/>. Returns how many 60 Hz frames it plays (0 when it has none).
        /// </summary>
        int PlayEntityEmote(uint serverId, int emote, int variant) => 0;

        /// <summary>Opcode 0x50: the entity stops the event motion <paramref name="routine"/>.</summary>
        void StopEntityMotion(uint serverId, string routine)
        {
        }
    }

    /// <summary>Where an event motion comes from (<see cref="IEventVmHost.PlayEntityMotion"/>).</summary>
    public enum EventMotionSource : byte
    {
        /// <summary>The entity's own loaded motions (0x2C).</summary>
        Own,

        /// <summary>An event motion DAT loaded onto the entity first (0x5B).</summary>
        Bank,

        /// <summary>A motion package of a player-model entity (0x66, XiEvents' ReadTpcEventMotionRes).</summary>
        Package,
    }
}
