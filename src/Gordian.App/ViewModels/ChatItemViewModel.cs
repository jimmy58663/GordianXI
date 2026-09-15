// src/Gordian.App/ViewModels/ChatItemViewModel.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing a single formatted chat or system message in the UI log.
    /// </summary>
    public sealed class ChatItemViewModel : ViewModelBase
    {
        public DateTime Timestamp { get; }
        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
        public string Sender { get; }
        public string Message { get; }
        public bool IsGm { get; }
        public ushort ZoneId { get; }
        public bool HasAutoTranslate { get; }
        public string BadgeText { get; }
        public string BadgeColor { get; }
        public string SenderColor { get; }
        public string MessageColor { get; }

        public ChatItemViewModel(
            string badgeText,
            string badgeColor,
            string sender,
            string senderColor,
            string message,
            string messageColor,
            DateTime? timestamp = null,
            bool isGm = false,
            ushort zoneId = 0,
            bool hasAutoTranslate = false)
        {
            Timestamp = timestamp ?? DateTime.Now;
            BadgeText = badgeText;
            BadgeColor = badgeColor;
            Sender = sender;
            SenderColor = senderColor;
            Message = message;
            MessageColor = messageColor;
            IsGm = isGm;
            ZoneId = zoneId;
            HasAutoTranslate = hasAutoTranslate;
        }

        public static ChatItemViewModel FromChatMessage(ChatMessage chat)
        {
            (string badge, string color) = chat.Type switch
            {
                ChatMessageType.Say => ("[Say]", "#DCDCDC"),
                ChatMessageType.Shout => ("[Shout]", "#E06C75"),
                ChatMessageType.Tell => ("[Tell]", "#C586C0"),
                ChatMessageType.Party => ("[Party]", "#61AFEF"),
                ChatMessageType.Linkshell => ("[LS1]", "#98C379"),
                ChatMessageType.Linkshell2 => ("[LS2]", "#56B6C2"),
                ChatMessageType.Yell => ("[Yell]", "#E5C07B"),
                ChatMessageType.Unity => ("[Unity]", "#D19A66"),
                ChatMessageType.Emotion => ("[Emote]", "#CE9178"),
                ChatMessageType.JpAssist => ("[Assist-JP]", "#4EC9B0"),
                ChatMessageType.NaAssist => ("[Assist-NA]", "#4EC9B0"),
                ChatMessageType.System1 or ChatMessageType.System2 or ChatMessageType.System3 => ("[System]", "#E5C07B"),
                _ => ($"[{chat.Type}]", "#A0A0A0")
            };

            string senderColor = chat.IsGm ? "#FFD700" : (chat.Type == ChatMessageType.Tell ? "#C586C0" : "#4EC9B0");
            string msgColor = chat.Type switch
            {
                ChatMessageType.Tell => "#DA70D6",
                ChatMessageType.Party => "#9CDCFE",
                ChatMessageType.Linkshell => "#B5CEA8",
                ChatMessageType.Linkshell2 => "#56B6C2",
                ChatMessageType.Shout => "#F44747",
                ChatMessageType.Yell => "#E5C07B",
                _ => "#DCDCDC"
            };

            return new ChatItemViewModel(
                badgeText: badge,
                badgeColor: color,
                sender: chat.IsGm ? $"[GM] {chat.Sender}" : chat.Sender,
                senderColor: senderColor,
                message: chat.Message,
                messageColor: msgColor,
                timestamp: chat.Timestamp.ToLocalTime(),
                isGm: chat.IsGm,
                zoneId: chat.ZoneId,
                hasAutoTranslate: chat.HasAutoTranslate
            );
        }

        public static ChatItemViewModel FromSystemMessage(SystemMessage sys)
        {
            string formatted = StandardMessages.FormatMessage(sys);
            return new ChatItemViewModel(
                badgeText: "[System]",
                badgeColor: "#E5C07B",
                sender: "System",
                senderColor: "#E5C07B",
                message: formatted,
                messageColor: "#E5C07B",
                timestamp: sys.Timestamp.ToLocalTime()
            );
        }

        public static ChatItemViewModel CreatePartyNotification(string message, string color = "#98C379")
        {
            return new ChatItemViewModel(
                badgeText: "[Party]",
                badgeColor: "#61AFEF",
                sender: "Party",
                senderColor: "#61AFEF",
                message: message,
                messageColor: color,
                timestamp: DateTime.Now
            );
        }

        public static ChatItemViewModel CreateLocalEcho(string message)
        {
            return new ChatItemViewModel(
                badgeText: "[Echo]",
                badgeColor: "#ABB2BF",
                sender: "Echo",
                senderColor: "#ABB2BF",
                message: message,
                messageColor: "#ABB2BF",
                timestamp: DateTime.Now
            );
        }

        public static ChatItemViewModel CreateLocalNotice(string message, string badge = "[System]", string color = "#E5C07B")
        {
            return new ChatItemViewModel(
                badgeText: badge,
                badgeColor: color,
                sender: "System",
                senderColor: color,
                message: message,
                messageColor: color,
                timestamp: DateTime.Now
            );
        }

        public static ChatItemViewModel FromTranslateMessage(TranslateMessage tr)
        {
            return new ChatItemViewModel(
                badgeText: "[Translate]",
                badgeColor: "#4EC9B0",
                sender: $"{tr.FromLanguage} -> {tr.ToLanguage}",
                senderColor: "#4EC9B0",
                message: $"{tr.SourcePhrase} = {tr.TranslatedPhrase}",
                messageColor: "#DCDCDC",
                timestamp: tr.Timestamp.ToLocalTime()
            );
        }

        public static ChatItemViewModel FromLinkshellMessage(LinkshellMessage ls)
        {
            string slotTag = ls.Slot == LinkshellSlot.LS1 ? "[LS1-MOTD]" : "[LS2-MOTD]";
            return new ChatItemViewModel(
                badgeText: slotTag,
                badgeColor: "#98C379",
                sender: !string.IsNullOrEmpty(ls.Modifier) ? $"{ls.LinkshellName} ({ls.Modifier})" : ls.LinkshellName,
                senderColor: "#98C379",
                message: ls.Message,
                messageColor: "#B5CEA8",
                timestamp: ls.Timestamp.ToLocalTime()
            );
        }

        public static ChatItemViewModel CreateOutgoing(ChatSendKind kind, string sender, string message, string? recipient = null)
        {
            (string badge, string color) = kind switch
            {
                ChatSendKind.Say => ("[Say]", "#DCDCDC"),
                ChatSendKind.Shout => ("[Shout]", "#E06C75"),
                ChatSendKind.Party => ("[Party]", "#61AFEF"),
                ChatSendKind.Linkshell1 => ("[LS1]", "#98C379"),
                ChatSendKind.Linkshell2 => ("[LS2]", "#56B6C2"),
                ChatSendKind.Yell => ("[Yell]", "#E5C07B"),
                ChatSendKind.Unity => ("[Unity]", "#D19A66"),
                ChatSendKind.Emote => ("[Emote]", "#CE9178"),
                _ => ($"[{kind}]", "#A0A0A0")
            };

            string senderDisplay = !string.IsNullOrEmpty(recipient) ? $"{sender} -> {recipient}" : sender;
            string badgeDisplay = !string.IsNullOrEmpty(recipient) ? "[Tell]" : badge;
            string badgeColor = !string.IsNullOrEmpty(recipient) ? "#C586C0" : color;

            return new ChatItemViewModel(
                badgeText: badgeDisplay,
                badgeColor: badgeColor,
                sender: senderDisplay,
                senderColor: "#4EC9B0",
                message: message,
                messageColor: !string.IsNullOrEmpty(recipient) ? "#DA70D6" : "#FFFFFF",
                timestamp: DateTime.Now
            );
        }
    }
}
