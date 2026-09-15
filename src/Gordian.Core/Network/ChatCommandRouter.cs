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
    }
}
