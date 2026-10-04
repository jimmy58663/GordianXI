// src/Gordian.Core/Network/Packets/ChatPacketModule.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Packets
{
    #region Chat Models

    /// <summary>
    /// Represents an immutable decoded in-game chat message. <see cref="ZoneId"/> is set only for Yell and the ranks
    /// only for the assist channels. <see cref="Formatted"/> is set when the packet's <c>Attr</c> 0x08 makes the
    /// message a DAT message reference (<see cref="Message"/> then holds the raw value list, which retail never shows).
    /// </summary>
    public sealed record ChatMessage(
        ChatMessageType Type,
        string Sender,
        string Message,
        ushort ZoneId,
        bool IsGm,
        byte MasteryRank,
        byte MentorRank,
        DateTime Timestamp,
        bool HasAutoTranslate,
        byte Attr = 0,
        ChatFormattedMessage? Formatted = null);

    /// <summary>
    /// Represents an immutable decoded general-purpose system message.
    /// </summary>
    public sealed record SystemMessage(
        uint UniqueNo,
        ushort ActorIndex,
        ushort MessageId,
        byte Attr,
        string Parameters,
        DateTime Timestamp)
    {
        /// <summary>The <see cref="Parameters"/> parsed into their keyed values, as the retail client reads them.</summary>
        public SystemMessageParameters ParsedParameters => SystemMessageParameters.Parse(Parameters);

        /// <summary>True for a player's message (<c>Attr</c> 0x10), which the blacklist can filter.</summary>
        public bool IsBlacklistable => (Attr & S2C_0x009_SysMessage.AttrBlacklistable) != 0;
    }

    /// <summary>
    /// Represents an immutable decoded auto-translate response.
    /// </summary>
    public sealed record TranslateMessage(
        ushort ItemNo,
        TranslateLanguage FromLanguage,
        TranslateLanguage ToLanguage,
        string SourcePhrase,
        string TranslatedPhrase,
        DateTime Timestamp);

    /// <summary>
    /// Represents an immutable decoded linkshell message or status update.
    /// </summary>
    public sealed record LinkshellMessage(
        LinkshellSlot Slot,
        string Message,
        string Modifier,
        string LinkshellName,
        DateTimeOffset UpdateDateTime,
        byte ReadLevel,
        byte WriteLevel,
        byte PubEditLevel,
        ushort OpType,
        DateTime Timestamp);

    #endregion

    /// <summary>
    /// Coordinates communication and chat packets:
    /// Inbound decoders (0x017, 0x009, 0x047, 0x0CC) and
    /// outbound builders (0x0B5, 0x0B6, 0x0B7, 0x02B, 0x0E0, 0x0E1, 0x0E2, 0x0E4).
    /// </summary>
    public sealed class ChatPacketModule
    {
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public bool LogOutboundOnRoute { get; set; } = true;

        /// <summary>
        /// Tells whether a player (by server id) is on the blacklist. S2C 0x009 messages flagged <c>Attr</c> 0x10 from a
        /// blacklisted sender are dropped, as the retail client does (XiPackets 0x0009).
        /// </summary>
        public Func<uint, bool>? IsBlacklisted { get; set; }

        public event Action<ChatMessage>? ChatMessageReceived;
        public event Action<SystemMessage>? SystemMessageReceived;
        public event Action<TranslateMessage>? TranslateReceived;
        public event Action<LinkshellMessage>? LinkshellMessageReceived;

        public ChatPacketModule(
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x017_ChatStd.PacketId, HandleChatStd);
            dispatcher.Register(S2C_0x009_SysMessage.PacketId, HandleSysMessage);
            dispatcher.Register(S2C_0x047_Translate.PacketId, HandleTranslate);
            dispatcher.Register(S2C_0x0CC_LinkshellMessage.PacketId, HandleLinkshellMessage);
        }

        private void HandleChatStd(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var chat = new S2C_0x017_ChatStd(payload);
            if (!chat.IsValid) return;

            ChatFormattedMessage? formatted = null;
            if (chat.IsFormatted)
            {
                if (chat.TryGetFormattedMessage(out var reference)) formatted = reference;
                else GordianLog.Warning("CHAT", $"[{chat.Kind}] Attr 0x08 message with an unreadable body: '{chat.GetMessage()}'");
            }

            var model = new ChatMessage(
                Type: chat.Kind,
                Sender: chat.GetSenderName(),
                Message: chat.GetMessage(),
                ZoneId: chat.ZoneId,
                IsGm: chat.IsGm,
                MasteryRank: chat.MasteryRank,
                MentorRank: chat.MentorRank,
                Timestamp: DateTime.UtcNow,
                HasAutoTranslate: chat.HasAutoTranslate(),
                Attr: chat.Attr,
                Formatted: formatted
            );

            GordianLog.Debug("CHAT", formatted is { } f
                ? $"[{model.Type}] {model.Sender}: DAT message {f.Table} #{f.MessageId} ({f.Param0}, {f.Param1}, {f.Param2}, {f.Param3}, {f.Param4})"
                : $"[{model.Type}] {model.Sender}: {model.Message}");
            ChatMessageReceived?.Invoke(model);
        }

        private void HandleSysMessage(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var msg = new S2C_0x009_SysMessage(payload);
            if (!msg.IsValid) return;

            if (msg.IsBlacklistable && IsBlacklisted?.Invoke(msg.UniqueNo) == true)
            {
                GordianLog.Debug("CHAT", $"[SYS_MSG] Dropped message {msg.MessageId} from blacklisted 0x{msg.UniqueNo:X8}");
                return;
            }

            var model = new SystemMessage(
                UniqueNo: msg.UniqueNo,
                ActorIndex: msg.ActorIndex,
                MessageId: msg.MessageId,
                Attr: msg.Attr,
                Parameters: msg.GetData(),
                Timestamp: DateTime.UtcNow
            );

            GordianLog.Debug("CHAT", $"[SYS_MSG] Id={model.MessageId}, Target=0x{model.UniqueNo:X8}, Params='{model.Parameters}'");
            SystemMessageReceived?.Invoke(model);
        }

        private void HandleTranslate(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var tr = new S2C_0x047_Translate(payload);
            if (!tr.IsValid) return;

            var model = new TranslateMessage(
                ItemNo: tr.ItemNo,
                FromLanguage: tr.FromIndex,
                ToLanguage: tr.ToIndex,
                SourcePhrase: tr.GetFromString(),
                TranslatedPhrase: tr.GetToString(),
                Timestamp: DateTime.UtcNow
            );

            GordianLog.Debug("CHAT", $"[TRANSLATE] {model.SourcePhrase} -> {model.TranslatedPhrase} (Item={model.ItemNo})");
            TranslateReceived?.Invoke(model);
        }

        private void HandleLinkshellMessage(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var ls = new S2C_0x0CC_LinkshellMessage(payload);
            if (!ls.IsValid) return;

            var model = new LinkshellMessage(
                Slot: ls.Slot,
                Message: ls.GetMessage(),
                Modifier: ls.GetModifier(),
                LinkshellName: ls.GetLinkshellName(),
                UpdateDateTime: ls.UpdateDateTime,
                ReadLevel: ls.ReadLevel,
                WriteLevel: ls.WriteLevel,
                PubEditLevel: ls.PubEditLevel,
                OpType: ls.OpType,
                Timestamp: DateTime.UtcNow
            );

            GordianLog.Debug("CHAT", $"[LS_MSG] Slot={model.Slot}, Name={model.LinkshellName}, Modifier={model.Modifier}: {model.Message}");
            LinkshellMessageReceived?.Invoke(model);
        }

        public async Task SendChatAsync(ChatSendKind kind, string message)
        {
            ArgumentNullException.ThrowIfNull(message);
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildChatStd(kind, message, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0B5, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task SendTellAsync(string recipient, string message)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            ArgumentNullException.ThrowIfNull(message);
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildChatTell(recipient, message, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0B6, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task SendAssistActionAsync(AssistActionKind kind, string targetPlayer)
        {
            ArgumentNullException.ThrowIfNull(targetPlayer);
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildAssistChannel(kind, targetPlayer, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0B7, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task RequestTranslateAsync(TranslateLanguage fromLang, TranslateLanguage toLang, string term)
        {
            ArgumentNullException.ThrowIfNull(term);
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildTranslateRequest(fromLang, toLang, term, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x02B, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task SetSearchMessageAsync(string message, SearchMessageType type = SearchMessageType.Default)
        {
            ArgumentNullException.ThrowIfNull(message);
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildSetUserMsg(message, type, sequenceId: seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0E0, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task RequestLinkshellMessageAsync(LinkshellSlot slot)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildGetLsMsg(slot, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0E1, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task SetLinkshellMessageAsync(LinkshellSlot slot, string message)
        {
            ArgumentNullException.ThrowIfNull(message);
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildSetLsMsg(slot, message, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0E2, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task SetLinkshellWriteLevelAsync(LinkshellSlot slot, LinkshellWriteLevel writeLevel)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildSetLsWriteLevel(slot, writeLevel, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0E2, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task RequestLinkshellPrivilegesAsync(LinkshellSlot slot)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = ChatOutboundPackets.BuildGetLsPriv(slot, seq);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0E4, seq, packet);
            }

            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }
    }
}
