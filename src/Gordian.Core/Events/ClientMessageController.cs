// src/Gordian.Core/Events/ClientMessageController.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Animation;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Events
{
    /// <summary>
    /// Prints the messages the client's own tables hold (<see cref="ClientMessageTables"/>) and plays the emotes of
    /// everyone in range:
    /// <list type="bullet">
    /// <item>S2C 0x053 system messages: the system message table's text with the packet's two numbers, to the log's
    /// system channel.</item>
    /// <item>S2C 0x05A emotes, our own included (the server echoes C2S 0x05D to everyone in range and to us): the emote
    /// table's line ("Name waves to Target.") to the emote channel unless the mode is motion-only, and the race's emote
    /// motion on the caster (<see cref="EmoteMotion"/>, the same banks event opcode 0x6E plays) unless the mode is
    /// text-only.</item>
    /// </list>
    /// Handlers run on the network thread: the log and the animation queue are thread-safe, and the DATs are read once.
    /// </summary>
    public sealed class ClientMessageController
    {
        private readonly ClientMessageTables _tables;
        private readonly Func<string, byte[]?>? _pathLoader;
        private readonly object _bankSync = new();
        private readonly Dictionary<(CharacterRace Race, int Emote, int Variant), EventMotionBank?> _emoteBanks = new();

        private WorldState? _world;
        private LocalPlayerState? _player;
        private StockUiChat? _chat;
        private Func<string> _playerName = () => string.Empty;

        /// <summary>
        /// A controller reading its tables through <paramref name="tables"/> (default: the app's file-id loader) and the
        /// emote motions through <paramref name="pathLoader"/> (default: <see cref="EventDialogController.DatPathLoader"/>).
        /// </summary>
        public ClientMessageController(ClientMessageTables? tables = null, Func<string, byte[]?>? pathLoader = null)
        {
            _tables = tables ?? new ClientMessageTables();
            _pathLoader = pathLoader;
        }

        /// <summary>Subscribes to a session's system messages and emotes.</summary>
        public void Attach(PlayerCommandState commands, WorldState world, LocalPlayerState player, StockUiChat chat, Func<string> playerName)
        {
            ArgumentNullException.ThrowIfNull(commands);
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _chat = chat ?? throw new ArgumentNullException(nameof(chat));
            _playerName = playerName ?? throw new ArgumentNullException(nameof(playerName));
            commands.SystemMessageReceived += OnSystemMessage;
            commands.EmotePerformed += OnEmote;
        }

        #region System messages (S2C 0x053)

        /// <summary>The lines of a system message, or the standard message text when the table is not available.</summary>
        public IReadOnlyList<string> FormatSystemMessage(SystemMessageInfo message)
        {
            var decoded = _tables.SystemMessages?.GetMessage(message.MessageId);
            if (decoded == null)
            {
                // No DAT (tests, a broken install): the few standard texts the client knows, else the id.
                return new[] { StandardMessages.TryGetMessage(message.MessageId, out string known) ? known : $"Msg#{message.MessageId}" };
            }
            var context = new SimpleMessageContext(new[] { unchecked((int)message.Para), unchecked((int)message.Para2) }, _playerName(), string.Empty,
                EventDialogController.NameResolver);
            return EventMessageFormatter.FormatLines(decoded, context);
        }

        private void OnSystemMessage(SystemMessageInfo message)
        {
            var chat = _chat;
            if (chat == null) return;
            foreach (string line in FormatSystemMessage(message))
            {
                if (line.Length > 0) chat.Log.Add(ChatLogChannel.System, line);
            }
        }

        #endregion

        #region Emotes (S2C 0x05A)

        /// <summary>
        /// The log line of an emote: the emote table's message <c>2 * id</c> with a target, <c>2 * id + 1</c> without
        /// (<see cref="ClientMessageTables.EmoteMessageId"/>), with the caster and target as message entities 0 and 1.
        /// Empty when the table is missing or the emote has no line (the table's empty slots, e.g. ids 39-42).
        /// </summary>
        public IReadOnlyList<string> FormatEmote(EmoteEcho emote)
        {
            var decoded = _tables.EmoteMessages?.GetMessage(ClientMessageTables.EmoteMessageId(emote.EmoteId, emote.HasTarget));
            if (decoded == null) return Array.Empty<string>();
            var caster = FindEntity(emote.CasterId, emote.CasterIndex);
            var target = emote.HasTarget ? FindEntity(emote.TargetId, emote.TargetIndex) : null;
            var context = new SimpleMessageContext(null, _playerName(), string.Empty, EventDialogController.NameResolver)
            {
                Entities = new MessageEntity?[]
                {
                    Describe(emote.CasterId, caster),
                    emote.HasTarget ? Describe(emote.TargetId, target) : null,
                },
                HeadingText = caster != null ? CompassName(caster.Direction) : null,
            };
            return EventMessageFormatter.FormatLines(decoded, context);
        }

        private void OnEmote(EmoteEcho emote)
        {
            if (emote.ShowsText && _chat is { } chat)
            {
                foreach (string line in FormatEmote(emote))
                {
                    if (line.Length > 0) chat.Log.Add(ChatLogChannel.Emote, line);
                }
            }
            if (emote.PlaysMotion) PlayEmoteMotion(emote);
        }

        /// <summary>
        /// Plays the emote's motion on the caster: the race's emote bank, loaded once per race, emote and variant
        /// (the nation for <c>/salute</c>, from <c>Param</c>), as an event motion. Fixed-model entities and emotes without
        /// a motion slot (dances, bell, job emotes) play nothing. Returns whether a motion was queued.
        /// </summary>
        public bool PlayEmoteMotion(EmoteEcho emote)
        {
            var entity = FindEntity(emote.CasterId, emote.CasterIndex);
            if (entity == null) return false;
            var race = (CharacterRace)((entity.Appearance.FaceModel >> 8) & 0xFF);
            if (entity.Appearance.ModelId > 0 || race == CharacterRace.Unknown) return false;
            int variant = emote.EmoteId == (ushort)EmoteId.Salute ? emote.Param : 0;
            var bank = EmoteBank(race, emote.EmoteId, variant);
            if (bank == null) return false;
            entity.Animation.AddEventMotionBank(bank);
            entity.Animation.EnqueueAction(new ActionRequest
            {
                ActorId = entity.ServerId,
                Motion = ActionMotion.EventMotion,
                Routine = EmoteMotion.RoutineName(emote.EmoteId),
                ReceivedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
            return true;
        }

        private EventMotionBank? EmoteBank(CharacterRace race, int emote, int variant)
        {
            lock (_bankSync)
            {
                if (_emoteBanks.TryGetValue((race, emote, variant), out var cached)) return cached;
                EventMotionBank? bank = null;
                var loader = _pathLoader ?? EventDialogController.DatPathLoader;
                try
                {
                    bank = loader == null ? null : EmoteMotion.LoadBank(race, emote, variant, loader);
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("EMOTE", $"Emote {emote} of {race} could not be read: {ex.Message}");
                }
                // Not cached without a loader: the app sets it after the session starts.
                if (loader != null) _emoteBanks[(race, emote, variant)] = bank;
                return bank;
            }
        }

        private WorldEntity? FindEntity(uint serverId, ushort index)
        {
            var world = _world;
            if (world == null) return null;
            if (serverId != 0 && world.TryGetByServerId(serverId, out var entity) && entity != null) return entity;
            return index != 0 && world.TryGetByTargetIndex(index, out entity) ? entity : null;
        }

        /// <summary>
        /// A message entity: the name (ours for our own id), the sex from a race look (null for fixed models), and the
        /// article. PROVISIONAL: "the" is given to monsters only (retail's rule for NPCs is not checked).
        /// </summary>
        private MessageEntity Describe(uint serverId, WorldEntity? entity)
        {
            bool self = _player != null && serverId != 0 && serverId == _player.ServerId;
            string name = self ? _playerName() : !string.IsNullOrEmpty(entity?.Name) ? entity!.Name : string.Empty;
            bool? female = null;
            if (entity != null && entity.Appearance.ModelId == 0)
            {
                female = ((entity.Appearance.FaceModel >> 8) & 0xFF) switch
                {
                    1 or 3 or 5 or 8 => false,
                    2 or 4 or 6 or 7 => true,
                    _ => null,
                };
            }
            return new MessageEntity(name, female, entity?.Type == EntityType.Monster);
        }

        /// <summary>
        /// The compass point of a wire heading (0 east, 64 south, 128 west, 192 north, clockwise), for the 0x1D code of the
        /// untargeted <c>/point</c> line. PROVISIONAL: the words and the eight-point rounding are not checked against retail.
        /// </summary>
        public static string CompassName(byte direction) => ((direction + 16) / 32 % 8) switch
        {
            0 => "east",
            1 => "southeast",
            2 => "south",
            3 => "southwest",
            4 => "west",
            5 => "northwest",
            6 => "north",
            _ => "northeast",
        };

        #endregion
    }
}
