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
        /// <summary><c>/fish</c>: casts a line (C2S 0x01A Fish).</summary>
        Fish,
        /// <summary><c>/sprint</c> (C2S 0x01A Sprint).</summary>
        Sprint,
        /// <summary><c>/dig</c>: chocobo digging (C2S 0x01A ChocoboDig).</summary>
        ChocoboDig,
        /// <summary><c>/blockaid [on|off]</c> (C2S 0x01A Blockaid); <see cref="ChatCommandResult.ActionParam"/> is the <see cref="BlockaidMode"/>.</summary>
        Blockaid,
        /// <summary><c>/callforhelp</c> (C2S 0x01A Help).</summary>
        CallForHelp,
        /// <summary><c>/monsterskill &lt;id&gt; [target]</c> (Monstrosity, C2S 0x01A MonsterSkill).</summary>
        MonsterSkill,
        /// <summary><c>/refa &lt;name|all&gt;</c>: releases Trusts (C2S 0x01A Talk on each).</summary>
        ReleaseTrust,
        Emote,
        InspectPos,
        InspectTargetInfo,
        InspectNearby,
        InspectVitals,
        SetTarget,
        ToggleLockOn,
        /// <summary>Talking to the targeted NPC (Confirm on it).</summary>
        Talk,
        /// <summary>Examining a target (<c>/check</c>, the command menu's Check): C2S 0x0DD.</summary>
        Check,
        SyntheticMoveTo,
        CollisionToggle,
        /// <summary><c>/anchor [on|off]</c>: ignore knockback, unless the server forbids it.</summary>
        AnchorToggle,
        UiLayout,
        /// <summary><c>/lockstyle [on|off]</c>: queries, enables or disables the style lock (C2S 0x053).</summary>
        Lockstyle,
        /// <summary><c>/lockstyleset [n]</c>: without a set number enables the style lock (C2S 0x053).</summary>
        LockstyleSet,

        /// <summary>Debug: <c>/playsound &lt;id&gt; | stop</c> plays a sound effect by id on the client (not sent to the server).</summary>
        DebugPlaySound,

        /// <summary>Debug: <c>/playmusic &lt;n&gt; | stop</c> plays a music track on the client (not sent to the server).</summary>
        DebugPlayMusic,
        /// <summary><c>/lot [slot]</c>: lots on a treasure pool item (C2S 0x041); without a slot, on every item not yet entered.</summary>
        TreasureLot,
        /// <summary><c>/pass [slot]</c>: passes on a treasure pool item (C2S 0x042); without a slot, on every item not yet entered.</summary>
        TreasurePass,
        /// <summary><c>/heal [on|off]</c>: rests (C2S 0x0E8); <see cref="ChatCommandResult.Rest"/> is the mode.</summary>
        Heal,
        /// <summary><c>/sit [on|off]</c>: sits down or stands up (C2S 0x0EA); <see cref="ChatCommandResult.Rest"/> is the mode.</summary>
        Sit,
        /// <summary><c>/sitchair [n] [on|off]</c>: sits in chair <see cref="ChatCommandResult.ActionParam"/> (C2S 0x113).</summary>
        SitChair,
        /// <summary><c>/random</c>: rolls the dice (C2S 0x0A2).</summary>
        Random,
        /// <summary><c>/nominate</c> / <c>/propose</c>: makes a proposal (C2S 0x0A0); <see cref="ChatCommandResult.Message"/> is the text and <see cref="ChatCommandResult.ActionParam"/> the <see cref="ProposalKind"/>.</summary>
        Propose,
        /// <summary><c>/vote &lt;n&gt; [proposer]</c>: votes in a proposal (C2S 0x0A1); <see cref="ChatCommandResult.ActionParam"/> is the option.</summary>
        Vote,
        /// <summary><c>/widescan</c>: asks for the wide scan list (C2S 0x0F4).</summary>
        WideScan,
        /// <summary><c>/track [target|off]</c>: tracks an entity of the wide scan (C2S 0x0F5) or stops (0x0F6).</summary>
        TrackTarget,
        /// <summary><c>/conquest</c> (<c>/cq</c>): requests the conquest overview (C2S 0x05A, answered by S2C 0x05E).</summary>
        ConquestRequest,
        /// <summary><c>/jobmasterdisp on|off</c>: shows or hides the job mastery mark (C2S 0x11B); <see cref="ChatCommandResult.Rest"/> is the mode.</summary>
        JobMasterDisplay,
        DiscoverCommands,
        DiscoverGmCommands,
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
        /// <summary>The mode of <c>/heal</c>, <c>/sit</c> and <c>/sitchair</c>: toggle, on or off.</summary>
        public RestMode Rest { get; init; }
        public EmoteId Emote { get; init; }
        public float MoveX { get; init; }
        public float MoveY { get; init; }
        public float MoveZ { get; init; }
        public float ParamFloat { get; init; }
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
                    Message = ToServerCoordinateOrder(trimmed)
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
                    "u" or "unity" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.Unity, Message = args },
                    "assistj" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.AssistJ, Message = args },
                    "assiste" => new ChatCommandResult { Kind = ChatCommandResultKind.SendChat, SpeechKind = ChatSendKind.AssistE, Message = args },
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
                    "check" => ParseCombatTargetCommand(ChatCommandResultKind.Check, args, world),
                    "lockon" => new ChatCommandResult { Kind = ChatCommandResultKind.ToggleLockOn, Message = args },
                    "ma" or "magic" or "cast" => ParseCombatActionCommand(ChatCommandResultKind.CombatCast, args, world),
                    "ws" or "weaponskill" => ParseCombatActionCommand(ChatCommandResultKind.CombatWeaponskill, args, world),
                    "ja" or "jobability" => ParseCombatActionCommand(ChatCommandResultKind.CombatJobAbility, args, world),
                    "ra" or "shoot" => ParseCombatTargetCommand(ChatCommandResultKind.CombatRanged, args, world),
                    "as" or "assist" => ParseCombatTargetCommand(ChatCommandResultKind.CombatAssist, args, world),
                    "cancel" => ParseBuffCancel(args),
                    "jump" => new ChatCommandResult { Kind = ChatCommandResultKind.CombatJump },
                    "fish" => new ChatCommandResult { Kind = ChatCommandResultKind.Fish },
                    "sprint" => new ChatCommandResult { Kind = ChatCommandResultKind.Sprint },
                    "dig" => new ChatCommandResult { Kind = ChatCommandResultKind.ChocoboDig },
                    "blockaid" => ParseBlockaid(args),
                    "callforhelp" or "cfh" => new ChatCommandResult { Kind = ChatCommandResultKind.CallForHelp },
                    "monsterskill" or "ms" => ParseMonsterSkill(args, world),
                    "refa" or "returnfaith" => new ChatCommandResult { Kind = ChatCommandResultKind.ReleaseTrust, Message = args },
                    "emote" or "em" => ParseEmote(args, world),

                    // Standard Emotes
                    _ when TryGetEmote(verb, out _, out _) => ParseEmoteDirect(verb, args, world),

                    // Inspection & Telemetry
                    "pos" or "where" or "loc" => new ChatCommandResult { Kind = ChatCommandResultKind.InspectPos },
                    "targetinfo" or "ti" => new ChatCommandResult { Kind = ChatCommandResultKind.InspectTargetInfo },
                    "nearby" or "scan" or "entities" => ParseNearbyCommand(args),
                    "vitals" or "hp" or "stats" => new ChatCommandResult { Kind = ChatCommandResultKind.InspectVitals },

                    // Targeting
                    "ta" or "target" => ParseTargetCommand(args, world),

                    // Synthetic Locomotion (Policy-Gated)
                    "moveto" or "goto" => ParseMoveToCommand(args),
                    "collision" or "col" or "noclip" => new ChatCommandResult { Kind = ChatCommandResultKind.CollisionToggle, Message = args },
                    "anchor" => new ChatCommandResult { Kind = ChatCommandResultKind.AnchorToggle, Message = args },

                    // Resting, sitting, dice, votes and wide scan
                    "heal" => ParseRest(ChatCommandResultKind.Heal, "heal", args),
                    "sit" => ParseRest(ChatCommandResultKind.Sit, "sit", args),
                    "sitchair" => ParseSitChair(args),
                    "random" or "rand" => new ChatCommandResult { Kind = ChatCommandResultKind.Random, Message = args },
                    "nominate" or "propose" => ParsePropose(args, defaultSpeechKind),
                    "vote" => ParseVote(args),
                    "widescan" or "wide" => new ChatCommandResult { Kind = ChatCommandResultKind.WideScan },
                    "track" => new ChatCommandResult { Kind = ChatCommandResultKind.TrackTarget, Message = args },
                    "conquest" or "cq" => new ChatCommandResult { Kind = ChatCommandResultKind.ConquestRequest },
                    "untrack" => new ChatCommandResult { Kind = ChatCommandResultKind.TrackTarget, Message = "off" },
                    "jobmasterdisp" => ParseJobMasterDisplay(args),

                    // Treasure pool
                    "lot" => new ChatCommandResult { Kind = ChatCommandResultKind.TreasureLot, Message = args },
                    "pass" => new ChatCommandResult { Kind = ChatCommandResultKind.TreasurePass, Message = args },

                    // Stock UI layout
                    "uilayout" or "uil" => new ChatCommandResult { Kind = ChatCommandResultKind.UiLayout, Message = args },

                    // Equipment appearance
                    "lockstyle" => new ChatCommandResult { Kind = ChatCommandResultKind.Lockstyle, Message = args },
                    "lockstyleset" => new ChatCommandResult { Kind = ChatCommandResultKind.LockstyleSet, Message = args },

                    // Debug audio (client only)
                    "playsound" => new ChatCommandResult { Kind = ChatCommandResultKind.DebugPlaySound, Message = args },
                    "playmusic" => new ChatCommandResult { Kind = ChatCommandResultKind.DebugPlayMusic, Message = args },

                    // Command Discovery & Help
                    "help" or "commands" or "cmds" or "?" => args.Equals("gm", StringComparison.OrdinalIgnoreCase)
                        ? new ChatCommandResult { Kind = ChatCommandResultKind.DiscoverGmCommands, Message = string.Empty }
                        : new ChatCommandResult { Kind = ChatCommandResultKind.DiscoverCommands, Message = args },
                    "gmhelp" or "gmcommands" or "gmcmds" => new ChatCommandResult { Kind = ChatCommandResultKind.DiscoverGmCommands, Message = args },

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

        private static bool TryParseRestMode(string word, out RestMode mode)
        {
            switch (word.ToLowerInvariant())
            {
                case "": mode = RestMode.Toggle; return true;
                case "on": mode = RestMode.On; return true;
                case "off": mode = RestMode.Off; return true;
                default: mode = RestMode.Toggle; return false;
            }
        }

        /// <summary><c>/heal</c> and <c>/sit</c>: no argument toggles, <c>on</c> and <c>off</c> force the state.</summary>
        private static ChatCommandResult ParseRest(ChatCommandResultKind kind, string verb, string args)
        {
            if (!TryParseRestMode(args.Trim(), out var mode))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = $"Usage: /{verb} [on|off]" };
            }
            return new ChatCommandResult { Kind = kind, Rest = mode };
        }

        /// <summary><c>/jobmasterdisp on|off</c>: the mode is required (the client does not know the current setting).</summary>
        private static ChatCommandResult ParseJobMasterDisplay(string args)
        {
            if (!TryParseRestMode(args.Trim(), out var mode) || mode == RestMode.Toggle)
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /jobmasterdisp on|off" };
            }
            return new ChatCommandResult { Kind = ChatCommandResultKind.JobMasterDisplay, Rest = mode };
        }

        /// <summary><c>/sitchair [n] [on|off]</c>: chair 0 (the default) is the plain chair, 1 to 11 the unlockable ones.</summary>
        private static ChatCommandResult ParseSitChair(string args)
        {
            const string Usage = "Usage: /sitchair [chair 0-20] [on|off]";
            ushort chair = 0;
            var mode = RestMode.Toggle;
            foreach (string part in args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ushort.TryParse(part, out ushort number) && number <= 20) chair = number;
                else if (TryParseRestMode(part, out var parsed) && parsed != RestMode.Toggle) mode = parsed;
                else return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = Usage };
            }
            return new ChatCommandResult { Kind = ChatCommandResultKind.SitChair, ActionParam = chair, Rest = mode };
        }

        /// <summary>
        /// <c>/nominate [say|party|shout|linkshell|linkshell2] "question" "option 1" "option 2" ...</c>: a leading scope word
        /// picks the channel; without one the current chat mode is used. No text cancels your own live proposal.
        /// </summary>
        private static ChatCommandResult ParsePropose(string args, ChatSendKind defaultSpeechKind)
        {
            var kind = defaultSpeechKind switch
            {
                ChatSendKind.Party => ProposalKind.Party,
                ChatSendKind.Shout or ChatSendKind.Yell => ProposalKind.Shout,
                ChatSendKind.Linkshell1 => ProposalKind.Linkshell1,
                ChatSendKind.Linkshell2 => ProposalKind.Linkshell2,
                _ => ProposalKind.Say
            };

            string text = args;
            int space = args.IndexOf(' ');
            string first = (space >= 0 ? args[..space] : args).ToLowerInvariant();
            ProposalKind? scope = first switch
            {
                "say" or "s" => ProposalKind.Say,
                "party" or "p" => ProposalKind.Party,
                "shout" or "sh" => ProposalKind.Shout,
                "linkshell" or "l" or "l1" or "ls" or "ls1" => ProposalKind.Linkshell1,
                "linkshell2" or "l2" or "ls2" => ProposalKind.Linkshell2,
                _ => null
            };
            if (scope.HasValue)
            {
                kind = scope.Value;
                text = space >= 0 ? args[(space + 1)..].Trim() : string.Empty;
            }

            return new ChatCommandResult { Kind = ChatCommandResultKind.Propose, ActionParam = (ushort)kind, Message = text };
        }

        /// <summary><c>/vote &lt;option 1-8&gt; [proposer]</c>; without a proposer the last one seen is used.</summary>
        private static ChatCommandResult ParseVote(string args)
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !byte.TryParse(parts[0], out byte option) || option is < 1 or > 8)
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /vote <option 1-8> [proposer]" };
            }
            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.Vote,
                ActionParam = option,
                TargetName = parts.Length > 1 ? parts[1] : string.Empty
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

        private static ChatCommandResult ParseBlockaid(string args)
        {
            BlockaidMode? mode = args.Trim().ToLowerInvariant() switch
            {
                "" or "toggle" => BlockaidMode.Toggle,
                "on" => BlockaidMode.Enable,
                "off" => BlockaidMode.Disable,
                _ => null
            };
            return mode is { } m
                ? new ChatCommandResult { Kind = ChatCommandResultKind.Blockaid, ActionParam = (ushort)m }
                : new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /blockaid [on|off]" };
        }

        private static ChatCommandResult ParseMonsterSkill(string args, WorldState? world)
        {
            string trimmed = args.Trim();
            int space = trimmed.IndexOf(' ');
            string idPart = space >= 0 ? trimmed.Substring(0, space) : trimmed;
            if (!ushort.TryParse(idPart, out ushort skillId))
            {
                return new ChatCommandResult { Kind = ChatCommandResultKind.LocalNotice, Message = "Usage: /monsterskill <skill_id> [target]" };
            }

            var target = ParseCombatTargetCommand(ChatCommandResultKind.MonsterSkill, space >= 0 ? trimmed.Substring(space + 1) : string.Empty, world);
            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.MonsterSkill,
                ActionParam = skillId,
                TargetServerId = target.TargetServerId,
                TargetIndex = target.TargetIndex,
                TargetName = target.TargetName
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
            if (!TryGetEmote(emoteName, out EmoteId emote, out ushort emoteParam))
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
                ActionParam = emoteParam,
                TargetServerId = targetId,
                TargetIndex = targetIndex,
                TargetName = targetName
            };
        }

        /// <summary>
        /// The emote a command name plays (<c>/clap</c> → <see cref="EmoteId.Clap"/>) and the C2S 0x05D <c>Param</c> it is
        /// sent with. Only the emotes every character has; the ones that need an unlock or a note (dances, bell, job emote,
        /// aim) are not routed yet. Param values referenced from XiPackets
        /// (https://github.com/atom0s/XiPackets/tree/main/world/client/0x005D).
        /// </summary>
        private static bool TryGetEmote(string name, out EmoteId emote, out ushort param)
        {
            param = 0;
            EmoteId? found = name switch
            {
                "point" => EmoteId.Point,
                "bow" => EmoteId.Bow,
                "salute" => EmoteId.Salute,
                "kneel" => EmoteId.Kneel,
                "laugh" => EmoteId.Laugh,
                "cry" => EmoteId.Cry,
                "no" => EmoteId.No,
                "yes" => EmoteId.Yes,
                "wave" => EmoteId.Wave,
                "goodbye" or "farewell" => EmoteId.Goodbye,
                "welcome" => EmoteId.Welcome,
                "joy" => EmoteId.Joy,
                "cheer" => EmoteId.Cheer,
                "clap" => EmoteId.Clap,
                "praise" => EmoteId.Praise,
                "smile" => EmoteId.Smile,
                "poke" => EmoteId.Poke,
                "slap" => EmoteId.Slap,
                "stagger" => EmoteId.Stagger,
                "sigh" => EmoteId.Sigh,
                "comfort" => EmoteId.Comfort,
                "surprised" => EmoteId.Surprised,
                "amazed" => EmoteId.Amazed,
                "stare" => EmoteId.Stare,
                "blush" => EmoteId.Blush,
                "angry" => EmoteId.Angry,
                "disgusted" => EmoteId.Disgusted,
                "muted" => EmoteId.Muted,
                "doze" => EmoteId.Doze,
                "panic" => EmoteId.Panic,
                "grin" => EmoteId.Grin,
                "dance" => EmoteId.Dance,
                "think" => EmoteId.Think,
                "fume" => EmoteId.Fume,
                "doubt" => EmoteId.Doubt,
                "sulk" => EmoteId.Sulk,
                "psych" => EmoteId.Psych,
                "huh" => EmoteId.Huh,
                "shocked" => EmoteId.Shocked,
                "hurray" => EmoteId.Hurray,
                "toss" => EmoteId.Toss,
                _ => null
            };
            emote = found.GetValueOrDefault();
            if (emote == EmoteId.Hurray) param = 1;
            return found.HasValue;
        }

        private static ChatCommandResult ParseTargetCommand(string args, WorldState? world)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.SetTarget,
                    TargetName = string.Empty
                };
            }

            string targetName = args.Trim();
            if (targetName.StartsWith('<') && targetName.EndsWith('>'))
            {
                targetName = targetName.Substring(1, targetName.Length - 2).Trim();
            }

            if (targetName.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(targetName.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint srvId))
            {
                if (world != null && world.TryGetByServerId(srvId, out var hexEntity) && hexEntity != null)
                {
                    return new ChatCommandResult
                    {
                        Kind = ChatCommandResultKind.SetTarget,
                        TargetServerId = hexEntity.ServerId,
                        TargetIndex = hexEntity.TargetIndex,
                        TargetName = hexEntity.Name
                    };
                }
            }

            if (ushort.TryParse(targetName, out ushort targetIndex))
            {
                if (world != null && world.TryGetByTargetIndex(targetIndex, out var entity) && entity != null)
                {
                    return new ChatCommandResult
                    {
                        Kind = ChatCommandResultKind.SetTarget,
                        TargetServerId = entity.ServerId,
                        TargetIndex = entity.TargetIndex,
                        TargetName = entity.Name
                    };
                }
            }

            if (world != null && world.TryGetByName(targetName, out var targetEntity) && targetEntity != null)
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.SetTarget,
                    TargetServerId = targetEntity.ServerId,
                    TargetIndex = targetEntity.TargetIndex,
                    TargetName = targetEntity.Name
                };
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.SetTarget,
                TargetName = targetName
            };
        }

        private static ChatCommandResult ParseNearbyCommand(string args)
        {
            float radius = 50.0f;
            if (!string.IsNullOrWhiteSpace(args) && float.TryParse(args.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r))
            {
                radius = Math.Max(1.0f, r);
            }

            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.InspectNearby,
                ParamFloat = radius
            };
        }

        /// <summary>
        /// Rewrites <c>!pos x y z [...]</c> typed in FFXI/Windower order (z = height) into the server's order
        /// (x, height, y), which LandSandBoat's <c>!pos</c> passes straight to <c>setPos</c>. Anything else, including a
        /// <c>!pos</c> without three numeric coordinates, is sent unchanged.
        /// </summary>
        public static string ToServerCoordinateOrder(string command)
        {
            if (!command.StartsWith("!pos", StringComparison.OrdinalIgnoreCase) ||
                (command.Length > 4 && !char.IsWhiteSpace(command[4])))
            {
                return command;
            }

            // Accept the same separators /moveto does: "1 2 3", "1, 2, 3", "(1, 2, 3)".
            var parts = command.Substring(4).Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return command;
            for (int i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    return command;
                }
            }
            (parts[1], parts[2]) = (parts[2], parts[1]);
            return "!pos " + string.Join(' ', parts);
        }

        private static ChatCommandResult ParseMoveToCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = "Usage: /moveto <x> <y> [z]"
                };
            }

            // Sanitize coordinates copied from /nearby or /pos:
            // Handles formats: "12.3 45.6 78.9", "12.3, 45.6, 78.9", "(12.3, 45.6, 78.9)", "Pos: (12.3, 45.6, 78.9)", "X=12.3, Y=45.6, Z=78.9"
            string sanitized = args;
            if (sanitized.StartsWith("Pos:", StringComparison.OrdinalIgnoreCase))
            {
                sanitized = sanitized.Substring(4);
            }

            sanitized = sanitized
                .Replace("(", " ")
                .Replace(")", " ")
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("X=", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("Y=", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("Z=", " ", StringComparison.OrdinalIgnoreCase)
                .Replace(",", " ");

            var parts = sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = "Usage: /moveto <x> <y> [z]"
                };
            }

            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
            {
                return new ChatCommandResult
                {
                    Kind = ChatCommandResultKind.LocalNotice,
                    Message = "Invalid coordinates. Usage: /moveto <x> <y> [z]"
                };
            }

            // Height is optional; NaN keeps the player's current height.
            float z = float.NaN;
            if (parts.Length >= 3 &&
                !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z))
            {
                z = float.NaN;
            }

            // Coordinates are entered in FFXI/Windower display order (x, y, z = height); internally Y is height.
            return new ChatCommandResult
            {
                Kind = ChatCommandResultKind.SyntheticMoveTo,
                MoveX = x,
                MoveY = z,
                MoveZ = y
            };
        }
    }
}
