// src/Gordian.Core/Network/ChatCommandRouter.cs
using System;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Network
{
    public enum ChatCommandResultKind
    {
        SendChat,
        SendTell,
        PartyInvite,
        PartyAccept,
        PartyDecline,
        PartyLeave,
        PartyDisband,
        PartyKick,
        CombatAttack,
        CombatAttackOff,
        CombatCast,
        CombatWeaponskill,
        CombatJobAbility,
        CombatRanged,
        CombatAssist,
        CombatBuffCancel,
        CombatJump,
        Emote,
        LocalEcho,
        LocalNotice,
        ServerCommand,
        Unrecognized
    }

    public sealed class ChatCommandResult
    {
        public ChatCommandResultKind Kind { get; init; }
        public ChatSendKind SpeechKind { get; init; }
        public string Recipient { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public uint TargetServerId { get; init; }
        public ushort TargetIndex { get; init; }
        public string TargetName { get; init; } = string.Empty;
        public ushort ActionParam { get; init; }
        public EmoteId Emote { get; init; }
    }

    /// <summary>
    /// Evaluates and parses chat input strings, routing client slash commands,
    /// party commands, speech channel switches, local echoes, and server !commands.
    /// </summary>
    public static class ChatCommandRouter
    {
        /// <summary>
        /// Parses the raw user input text and determines the appropriate action.
        /// </summary>
        /// <param name="input">The raw text entered by the user.</param>
        /// <param name="defaultSpeechKind">The default speech channel if no slash command is used.</param>
        /// <param name="world">Active WorldState to look up entities for targeted commands (e.g. /invite).</param>
        public static ChatCommandResult Parse(string input, ChatSendKind defaultSpeechKind = ChatSendKind.Say, WorldState? world = null)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Message is empty." };
            }

            string trimmed = input.Trim();

            // 1. Check for server !commands (e.g. !pos, !zone, !heal)
            if (trimmed.StartsWith('!'))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.ServerCommand,
                    SpeechKind = ChatSendKind.Say,
                    Message = trimmed
                };
            }

            // 2. Check for slash /commands
            if (trimmed.StartsWith('/'))
            {
                int firstSpace = trimmed.IndexOf(' ');
                string verb = (firstSpace >= 0 ? trimmed.Substring(1, firstSpace - 1) : trimmed.Substring(1)).ToLowerInvariant();
                string args = firstSpace >= 0 ? trimmed.Substring(firstSpace + 1).Trim() : string.Empty;

                return verb switch
                {
                    // Speech / Chat channels
                    "t" or "tell" or "w" => ParseTell(args),
                    "s" or "say" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Say, Message = args },
                    "p" or "party" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Party, Message = args },
                    "sh" or "shout" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Shout, Message = args },
                    "y" or "yell" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Yell, Message = args },
                    "l" or "l1" or "linkshell" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Linkshell1, Message = args },
                    "l2" or "linkshell2" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Linkshell2, Message = args },
                    "echo" => new ChatCommandResult { Kind = ChatCommandResultKind.LocalEcho, Message = args },

                    // Party commands
                    "join" or "accept" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyAccept },
                    "decline" or "refuse" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyDecline },
                    "leave" or "break" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyLeave },
                    "disband" or "breakup" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyDisband },
                    "invite" => ParseInvite(args, world),
                    "kick" => ParseKick(args, world),
                    "pcmd" => ParsePartyCmd(args, world),

                    // Combat & Action commands
                    "a" or "attack" => ParseCombatTargetCommand(ChatCommandResultKind.CombatAttack, args, world),
                    "aoff" or "attackoff" or "disengage" => new ChatCommandResult { Kind = ChatCommandResultKind.CombatAttackOff },
                    "ma" or "magic" or "cast" => ParseCombatActionCommand(ChatCommandResultKind.CombatCast, args, world),
                    "ws" or "weaponskill" => ParseCombatActionCommand(ChatCommandResultKind.CombatWeaponskill, args, world),
                    "ja" or "jobability" => ParseCombatActionCommand(ChatCommandResultKind.CombatJobAbility, args, world),
                    "ra" or "shoot" => ParseCombatTargetCommand(ChatCommandResultKind.CombatRanged, args, world),
                    "as" or "assist" => ParseCombatTargetCommand(ChatCommandResultKind.CombatAssist, args, world),
                    "cancel" => ParseBuffCancel(args),
                    "jump" => new ChatCommandResult { Kind = ChatCommandResultKind.CombatJump },
                    "emote" or "em" => ParseEmote(args, world),

                    // Standard Emotes
                    "cheer" or "clap" or "wave" or "bow" or "point" or "salute" or "kneel" or "laugh" or "cry" or "no" or "yes" or "surprised" or "blush" or "sit" or "farewell" or "joy" or "comfort" or "panic" or "disgusted" or "angry" or "shocked" => ParseEmoteDirect(verb, args, world),

                    _ => new ChatCommandResult
                    {
                        Kind = ChatCommandResultKind.Unrecognized,
                        Message = $"Command '/{verb}' is not recognized."
                    }
                };
            }

            // 3. Standard speech (no command prefix)
            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.SendChat,
                SpeechKind = defaultSpeechKind,
                Message = trimmed
            };
        }

        private static ChatCommandResult ParseTell(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /tell <player> <message>" };
            }

            int spaceIdx = args.IndexOf(' ');
            if (spaceIdx < 0)
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /tell <player> <message>" };
            }

            string recipient = args.Substring(0, spaceIdx).Trim();
            string message = args.Substring(spaceIdx + 1).Trim();

            if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(message))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /tell <player> <message>" };
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.SendTell,
                Recipient = recipient,
                Message = message
            };
        }

        private static ChatCommandResult ParseInvite(string name, WorldState? world)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /invite <player>" };
            }

            string targetName = name.Trim();
            if (world != null && world.TryGetByName(targetName, out var entity) && entity != null)
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.PartyInvite,
                    TargetServerId = entity.ServerId,
                    TargetIndex = entity.TargetIndex,
                    TargetName = entity.Name
                };
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.PartyInvite,
                TargetServerId = 0,
                TargetIndex = 0,
                TargetName = targetName
            };
        }

        private static ChatCommandResult ParseKick(string name, WorldState? world)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /kick <player>" };
            }

            string targetName = name.Trim();
            if (world != null && world.TryGetByName(targetName, out var entity) && entity != null)
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.PartyKick,
                    TargetServerId = entity.ServerId,
                    TargetIndex = entity.TargetIndex,
                    TargetName = entity.Name
                };
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.PartyKick,
                TargetServerId = 0,
                TargetIndex = 0,
                TargetName = targetName
            };
        }

        private static ChatCommandResult ParsePartyCmd(string args, WorldState? world)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /pcmd <add|leave|breakup|kick|accept|decline> [player]" };
            }

            int spaceIdx = args.IndexOf(' ');
            string subVerb = (spaceIdx >= 0 ? args.Substring(0, spaceIdx) : args).ToLowerInvariant();
            string subArgs = spaceIdx >= 0 ? args.Substring(spaceIdx + 1).Trim() : string.Empty;

            return subVerb switch
            {
                "add" or "invite" => ParseInvite(subArgs, world),
                "accept" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyAccept },
                "decline" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyDecline },
                "leave" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyLeave },
                "breakup" or "disband" => new ChatCommandResult { Kind = ChatCommandResultKind.PartyDisband },
                "kick" => ParseKick(subArgs, world),
                _ => new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = $"Unknown party command: /pcmd {subVerb}. Valid options: add, leave, breakup, kick, accept, decline."
                }
            };
        }

        private static ChatCommandResult ParseCombatTargetCommand(ChatCommandResultKind kind, string args, WorldState? world)
        {
            string targetName = args.Trim();
            if (targetName.StartsWith('<') && targetName.EndsWith('>'))
            {
                targetName = targetName.Substring(1, targetName.Length - 2).Trim();
            }

            if (!string.IsNullOrWhiteSpace(targetName) && targetName != "t" && world != null)
            {
                if (world.TryGetByName(targetName, out var entity) && entity != null)
                {
                    return new ChatCommandResult
                    {
                        Kind = kind,
                        TargetServerId = entity.ServerId,
                        TargetIndex = entity.TargetIndex,
                        TargetName = entity.Name
                    };
                }
            }

            return new ChatCommandResult
            {
                Kind = kind,
                TargetName = targetName
            };
        }

        private static ChatCommandResult ParseCombatActionCommand(ChatCommandResultKind kind, string args, WorldState? world)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = $"Usage: /{kind switch { ChatCommandResultKind.CombatCast => "magic <spell> [target]", ChatCommandResultKind.CombatWeaponskill => "ws <skill> [target]", _ => "ja <ability> [target]" }}"
                };
            }

            string trimmed = args.Trim();
            string actionName;
            string targetPart = string.Empty;

            // Handle quoted actions: e.g. "Fast Blade" <t> or "Cure IV" <t>
            if (trimmed.StartsWith('"'))
            {
                int closingQuote = trimmed.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    actionName = trimmed.Substring(1, closingQuote - 1).Trim();
                    targetPart = trimmed.Substring(closingQuote + 1).Trim();
                }
                else
                {
                    actionName = trimmed.Trim('"');
                }
            }
            else
            {
                int spaceIdx = trimmed.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    actionName = trimmed.Substring(0, spaceIdx).Trim();
                    targetPart = trimmed.Substring(spaceIdx + 1).Trim();
                }
                else
                {
                    actionName = trimmed;
                }
            }

            ushort actionId = 0;
            _ = ushort.TryParse(actionName, out actionId);

            uint targetId = 0;
            ushort targetIndex = 0;
            string targetName = targetPart;

            if (targetName.StartsWith('<') && targetName.EndsWith('>'))
            {
                targetName = targetName.Substring(1, targetName.Length - 2).Trim();
            }

            if (!string.IsNullOrWhiteSpace(targetName) && targetName != "t" && world != null)
            {
                if (world.TryGetByName(targetName, out var entity) && entity != null)
                {
                    targetId = entity.ServerId;
                    targetIndex = entity.TargetIndex;
                    targetName = entity.Name;
                }
            }

            return new ChatCommandResult
            {
                Kind = kind,
                Message = actionName,
                ActionParam = actionId,
                TargetServerId = targetId,
                TargetIndex = targetIndex,
                TargetName = targetName
            };
        }

        private static ChatCommandResult ParseBuffCancel(string args)
        {
            if (string.IsNullOrWhiteSpace(args) || !ushort.TryParse(args.Trim(), out ushort buffId))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = "Usage: /cancel <buffId>"
                };
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.CombatBuffCancel,
                ActionParam = buffId
            };
        }

        private static ChatCommandResult ParseEmote(string args, WorldState? world)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = "Usage: /emote <motion> [target]"
                };
            }

            int spaceIdx = args.IndexOf(' ');
            string emoteName = (spaceIdx >= 0 ? args.Substring(0, spaceIdx) : args).ToLowerInvariant();
            string targetStr = spaceIdx >= 0 ? args.Substring(spaceIdx + 1).Trim() : string.Empty;

            return ParseEmoteDirect(emoteName, targetStr, world);
        }

        private static ChatCommandResult ParseEmoteDirect(string emoteName, string targetStr, WorldState? world)
        {
            EmoteId emote = emoteName switch
            {
                "cheer" => EmoteId.Cheer,
                "clap" => EmoteId.Clap,
                "wave" => EmoteId.Wave,
                "bow" => EmoteId.Bow,
                "point" => EmoteId.Point,
                "salute" => EmoteId.Salute,
                "kneel" => EmoteId.Kneel,
                "laugh" => EmoteId.Laugh,
                "cry" => EmoteId.Cry,
                "no" => EmoteId.No,
                "yes" => EmoteId.Yes,
                "surprised" => EmoteId.Surprised,
                "blush" => EmoteId.Blush,
                "sit" => EmoteId.Sit,
                "farewell" => EmoteId.Farewell,
                "joy" => EmoteId.Joy,
                "comfort" => EmoteId.Comfort,
                "panic" => EmoteId.Panic,
                "disgusted" => EmoteId.Disgusted,
                "angry" => EmoteId.Angry,
                "shocked" => EmoteId.Shocked,
                _ => EmoteId.None
            };

            if (emote == EmoteId.None)
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = $"Unknown emote '{emoteName}'."
                };
            }

            uint targetId = 0;
            ushort targetIndex = 0;
            string targetName = targetStr;

            if (targetName.StartsWith('<') && targetName.EndsWith('>'))
            {
                targetName = targetName.Substring(1, targetName.Length - 2).Trim();
            }

            if (!string.IsNullOrWhiteSpace(targetName) && targetName != "t" && world != null)
            {
                if (world.TryGetByName(targetName, out var entity) && entity != null)
                {
                    targetId = entity.ServerId;
                    targetIndex = entity.TargetIndex;
                    targetName = entity.Name;
                }
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.Emote,
                Emote = emote,
                ActionParam = (byte)emote,
                TargetServerId = targetId,
                TargetIndex = targetIndex,
                TargetName = targetName
            };
        }
    }
}
