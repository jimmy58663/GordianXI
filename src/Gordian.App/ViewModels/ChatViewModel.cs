// src/Gordian.App/ViewModels/ChatViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel driving the live in-game Chat &amp; Communication window and tab.
    /// Manages real-time inbound chat stream and user transmission via packet engine.
    /// </summary>
    public sealed class ChatViewModel : ViewModelBase, IDisposable
    {
        public static Action<Action>? UiDispatcher { get; set; }

        private readonly SessionRegistry _sessionRegistry;
        private CharacterSession? _selectedSession;
        private string _selectedChannel = "Say";
        private string _tellRecipient = string.Empty;
        private string _outgoingMessage = string.Empty;
        private string _channelFilter = "All";
        private string _statusText = "Ready";
        private bool _autoScroll = true;

        public ObservableCollection<CharacterSession> ActiveSessions { get; } = new();
        public ObservableCollection<ChatItemViewModel> AllMessages { get; } = new();
        public ObservableCollection<ChatItemViewModel> FilteredMessages { get; } = new();

        public IReadOnlyList<string> ChannelOptions { get; } = new[]
        {
            "Say",
            "Shout",
            "Party",
            "Linkshell 1",
            "Linkshell 2",
            "Tell",
            "Yell",
            "Unity"
        };

        public IReadOnlyList<string> FilterOptions { get; } = new[]
        {
            "All",
            "Say",
            "Tell",
            "Party",
            "Linkshell",
            "Shout/Yell",
            "System",
            "Combat"
        };

        public CharacterSession? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (SetProperty(ref _selectedSession, value))
                {
                    OnSelectedSessionChanged(value);
                    OnPropertyChanged(nameof(HasActiveSession));
                    OnPropertyChanged(nameof(SessionHeaderTitle));
                }
            }
        }

        public bool HasActiveSession => SelectedSession != null;
        public string SessionHeaderTitle => SelectedSession != null
            ? $"Active Session: {SelectedSession.CharacterName} ({SelectedSession.State})"
            : "No Active Session";

        public string SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value))
                {
                    OnPropertyChanged(nameof(IsTellChannel));
                }
            }
        }

        public bool IsTellChannel => SelectedChannel == "Tell";

        public string TellRecipient
        {
            get => _tellRecipient;
            set => SetProperty(ref _tellRecipient, value);
        }

        public string OutgoingMessage
        {
            get => _outgoingMessage;
            set => SetProperty(ref _outgoingMessage, value);
        }

        public string ChannelFilter
        {
            get => _channelFilter;
            set
            {
                if (SetProperty(ref _channelFilter, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        public ICommand SendMessageCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand RequestLsMotdCommand { get; }
        public ICommand OpenPopOutCommand { get; }

        public event EventHandler? RequestScrollToEnd;
        public event EventHandler? RequestOpenPopOutWindow;

        public ChatViewModel(SessionRegistry? registry = null)
        {
            _sessionRegistry = registry ?? SessionRegistry.Default;
            _sessionRegistry.SessionRegistered += OnSessionRegistered;
            _sessionRegistry.SessionUnregistered += OnSessionUnregistered;

            foreach (var s in _sessionRegistry.ActiveSessions)
            {
                ActiveSessions.Add(s);
            }

            if (ActiveSessions.Count > 0)
            {
                SelectedSession = ActiveSessions[0];
            }

            SendMessageCommand = new RelayCommand(async () => await ExecuteSendMessageAsync(), () => CanSendMessage());
            ClearLogCommand = new RelayCommand(ExecuteClearLog);
            RequestLsMotdCommand = new RelayCommand(async () => await ExecuteRequestLsMotdAsync(), () => HasActiveSession);
            OpenPopOutCommand = new RelayCommand(() => RequestOpenPopOutWindow?.Invoke(this, EventArgs.Empty));
        }

        private void OnSessionRegistered(object? sender, CharacterSession session)
        {
            DispatchToUi(() =>
            {
                if (!ActiveSessions.Contains(session))
                {
                    ActiveSessions.Add(session);
                }
                if (SelectedSession == null)
                {
                    SelectedSession = session;
                }
            });
        }

        private void OnSessionUnregistered(object? sender, CharacterSession session)
        {
            DispatchToUi(() =>
            {
                ActiveSessions.Remove(session);
                if (SelectedSession == session)
                {
                    SelectedSession = ActiveSessions.FirstOrDefault();
                }
            });
        }

        private CharacterSession? _hookedSession;

        private void OnSelectedSessionChanged(CharacterSession? session)
        {
            if (_hookedSession != null)
            {
                _hookedSession.ChatModule.ChatMessageReceived -= OnChatMessageReceived;
                _hookedSession.ChatModule.SystemMessageReceived -= OnSystemMessageReceived;
                _hookedSession.ChatModule.TranslateReceived -= OnTranslateReceived;
                _hookedSession.ChatModule.LinkshellMessageReceived -= OnLinkshellMessageReceived;
                _hookedSession.Party.InviteReceived -= OnPartyInviteReceived;
                _hookedSession.Party.InviteCleared -= OnPartyInviteCleared;
                _hookedSession.Party.MemberJoined -= OnPartyMemberJoined;
                _hookedSession.Party.MemberLeft -= OnPartyMemberLeft;
                _hookedSession.Party.PartyDisbanded -= OnPartyDisbanded;
                _hookedSession.Combat.ActionExecuted -= OnCombatActionExecuted;
                _hookedSession.Combat.BattleMessageReceived -= OnBattleMessageReceived;
                _hookedSession = null;
            }

            if (session != null)
            {
                session.ChatModule.ChatMessageReceived += OnChatMessageReceived;
                session.ChatModule.SystemMessageReceived += OnSystemMessageReceived;
                session.ChatModule.TranslateReceived += OnTranslateReceived;
                session.ChatModule.LinkshellMessageReceived += OnLinkshellMessageReceived;
                session.Party.InviteReceived += OnPartyInviteReceived;
                session.Party.InviteCleared += OnPartyInviteCleared;
                session.Party.MemberJoined += OnPartyMemberJoined;
                session.Party.MemberLeft += OnPartyMemberLeft;
                session.Party.PartyDisbanded += OnPartyDisbanded;
                session.Combat.ActionExecuted += OnCombatActionExecuted;
                session.Combat.BattleMessageReceived += OnBattleMessageReceived;
                _hookedSession = session;
                StatusText = $"Connected to chat stream for {session.CharacterName}";
            }
            else
            {
                StatusText = "No active character session.";
            }
        }

        private void OnPartyInviteReceived(PartyInvite invite)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.CreatePartyNotification(
                    $"{invite.InviterName} invited you to join a party. Type /join to accept or /decline to decline."
                );
                AddMessageItem(item);
                StatusText = $"Party invite received from {invite.InviterName}. Type /join to accept.";
            });
        }

        private void OnPartyInviteCleared()
        {
            DispatchToUi(() =>
            {
                StatusText = "Pending party invitation cleared.";
            });
        }

        private void OnPartyMemberJoined(PartyMember member)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.CreatePartyNotification($"{member.Name} joined the party.");
                AddMessageItem(item);
            });
        }

        private void OnPartyMemberLeft(uint serverId)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.CreatePartyNotification("A member left the party.", "#E06C75");
                AddMessageItem(item);
            });
        }

        private void OnPartyDisbanded()
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.CreatePartyNotification("The party was disbanded.", "#E06C75");
                AddMessageItem(item);
            });
        }

        private void OnChatMessageReceived(ChatMessage msg)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.FromChatMessage(msg);
                AddMessageItem(item);
            });
        }

        private void OnSystemMessageReceived(SystemMessage msg)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.FromSystemMessage(msg);
                AddMessageItem(item);
            });
        }

        private void OnTranslateReceived(TranslateMessage msg)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.FromTranslateMessage(msg);
                AddMessageItem(item);
            });
        }

        private void OnLinkshellMessageReceived(LinkshellMessage msg)
        {
            DispatchToUi(() =>
            {
                var item = ChatItemViewModel.FromLinkshellMessage(msg);
                AddMessageItem(item);
            });
        }

        private void OnCombatActionExecuted(CombatActionRecord record)
        {
            var session = _hookedSession;
            if (session == null) return;

            string? ResolveEntityName(uint id)
            {
                if (id == session.LocalPlayer.ServerId || id == session.CharacterId)
                {
                    return session.CharacterName;
                }
                if (session.World.TryGetByServerId(id, out var entity) && !string.IsNullOrEmpty(entity?.Name))
                {
                    return entity.Name;
                }
                return null;
            }

            var lines = CombatLogFormatter.FormatAction(record, ResolveEntityName);
            if (lines.Count > 0)
            {
                DispatchToUi(() =>
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        AddMessageItem(ChatItemViewModel.CreateCombat(lines[i]));
                    }
                });
            }
        }

        private void OnBattleMessageReceived(CombatMessageRecord record)
        {
            var session = _hookedSession;
            if (session == null) return;

            string? ResolveEntityName(uint id)
            {
                if (id == session.LocalPlayer.ServerId || id == session.CharacterId)
                {
                    return session.CharacterName;
                }
                if (session.World.TryGetByServerId(id, out var entity) && !string.IsNullOrEmpty(entity?.Name))
                {
                    return entity.Name;
                }
                return null;
            }

            string line = CombatLogFormatter.FormatBattleMessage(record, ResolveEntityName);
            if (!string.IsNullOrEmpty(line))
            {
                DispatchToUi(() =>
                {
                    AddMessageItem(ChatItemViewModel.CreateCombat(line));
                });
            }
        }

        public void AddMessageItem(ChatItemViewModel item)
        {
            AllMessages.Add(item);
            if (MatchesFilter(item))
            {
                FilteredMessages.Add(item);
                if (AutoScroll)
                {
                    RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
                }
            }

            // Cap buffer at 1000 messages to prevent unbounded growth
            if (AllMessages.Count > 1000)
            {
                var first = AllMessages[0];
                AllMessages.RemoveAt(0);
                FilteredMessages.Remove(first);
            }
        }

        private bool MatchesFilter(ChatItemViewModel item)
        {
            return ChannelFilter switch
            {
                "Say" => item.BadgeText.Contains("Say", StringComparison.OrdinalIgnoreCase),
                "Tell" => item.BadgeText.Contains("Tell", StringComparison.OrdinalIgnoreCase),
                "Party" => item.BadgeText.Contains("Party", StringComparison.OrdinalIgnoreCase),
                "Linkshell" => item.BadgeText.Contains("LS", StringComparison.OrdinalIgnoreCase),
                "Shout/Yell" => item.BadgeText.Contains("Shout", StringComparison.OrdinalIgnoreCase) ||
                                item.BadgeText.Contains("Yell", StringComparison.OrdinalIgnoreCase),
                "System" => item.BadgeText.Contains("System", StringComparison.OrdinalIgnoreCase) ||
                            item.BadgeText.Contains("Translate", StringComparison.OrdinalIgnoreCase),
                "Combat" => item.BadgeText.Contains("Combat", StringComparison.OrdinalIgnoreCase),
                _ => true // "All"
            };
        }

        private void ApplyFilter()
        {
            FilteredMessages.Clear();
            foreach (var msg in AllMessages)
            {
                if (MatchesFilter(msg))
                {
                    FilteredMessages.Add(msg);
                }
            }
            if (AutoScroll)
            {
                RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool CanSendMessage()
        {
            if (SelectedSession == null) return false;
            if (string.IsNullOrWhiteSpace(OutgoingMessage)) return false;
            string text = OutgoingMessage.Trim();
            if (IsTellChannel && !text.StartsWith('/') && !text.StartsWith('!') && string.IsNullOrWhiteSpace(TellRecipient))
                return false;
            return true;
        }

        public async Task ExecuteSendMessageAsync()
        {
            if (SelectedSession == null || string.IsNullOrWhiteSpace(OutgoingMessage))
                return;

            string rawText = OutgoingMessage.Trim();
            string myName = SelectedSession.CharacterName;

            ChatSendKind defaultKind = SelectedChannel switch
            {
                "Say" => ChatSendKind.Say,
                "Shout" => ChatSendKind.Shout,
                "Party" => ChatSendKind.Party,
                "Linkshell 1" => ChatSendKind.Linkshell1,
                "Linkshell 2" => ChatSendKind.Linkshell2,
                "Yell" => ChatSendKind.Yell,
                "Unity" => ChatSendKind.Unity,
                _ => ChatSendKind.Say
            };

            // If Tell tab is selected and text is not a command, default to Tell
            if (IsTellChannel && !rawText.StartsWith('/') && !rawText.StartsWith('!'))
            {
                string target = TellRecipient.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    StatusText = "Error: Tell recipient name is required.";
                    return;
                }

                try
                {
                    await SelectedSession.ChatModule.SendTellAsync(target, rawText).ConfigureAwait(false);
                    DispatchToUi(() =>
                    {
                        var outItem = ChatItemViewModel.CreateOutgoing(ChatSendKind.Say, myName, rawText, target);
                        AddMessageItem(outItem);
                        OutgoingMessage = string.Empty;
                        StatusText = $"Tell sent to {target}";
                    });
                }
                catch (Exception ex)
                {
                    GordianLog.Error("CHAT_UI", $"Failed to send tell: {ex.Message}", ex);
                    DispatchToUi(() => StatusText = $"Send error: {ex.Message}");
                }
                return;
            }

            var cmd = ChatCommandRouter.Parse(rawText, defaultKind, SelectedSession.World);

            try
            {
                switch (cmd.Kind)
                {
                    case ChatCommandResultKind.SendChat:
                    {
                        await SelectedSession.ChatModule.SendChatAsync(cmd.SpeechKind, cmd.Message).ConfigureAwait(false);
                        DispatchToUi(() =>
                        {
                            var outItem = ChatItemViewModel.CreateOutgoing(cmd.SpeechKind, myName, cmd.Message);
                            AddMessageItem(outItem);
                            OutgoingMessage = string.Empty;
                            StatusText = $"Sent {cmd.SpeechKind} message.";
                        });
                        break;
                    }

                    case ChatCommandResultKind.SendTell:
                    {
                        await SelectedSession.ChatModule.SendTellAsync(cmd.Recipient, cmd.Message).ConfigureAwait(false);
                        DispatchToUi(() =>
                        {
                            var outItem = ChatItemViewModel.CreateOutgoing(ChatSendKind.Say, myName, cmd.Message, cmd.Recipient);
                            AddMessageItem(outItem);
                            OutgoingMessage = string.Empty;
                            StatusText = $"Tell sent to {cmd.Recipient}";
                        });
                        break;
                    }

                    case ChatCommandResultKind.PartyAccept:
                    {
                        var pending = SelectedSession.Party.PendingInvite;
                        if (pending != null)
                        {
                            await SelectedSession.PartyModule.AcceptInviteAsync().ConfigureAwait(false);
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification($"Accepted party invite from {pending.InviterName}.");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = $"Joined {pending.InviterName}'s party.";
                            });
                        }
                        else
                        {
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification("No party invitation is currently pending.", "#E5C07B");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = "No party invitation pending.";
                            });
                        }
                        break;
                    }

                    case ChatCommandResultKind.PartyDecline:
                    {
                        var pending = SelectedSession.Party.PendingInvite;
                        if (pending != null)
                        {
                            await SelectedSession.PartyModule.DeclineInviteAsync().ConfigureAwait(false);
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification("Declined party invitation.", "#E06C75");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = "Party invitation declined.";
                            });
                        }
                        else
                        {
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification("No party invitation is currently pending.", "#E5C07B");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = "No party invitation pending.";
                            });
                        }
                        break;
                    }

                    case ChatCommandResultKind.PartyInvite:
                    {
                        if (cmd.TargetServerId != 0)
                        {
                            await SelectedSession.PartyModule.SendInviteAsync(cmd.TargetServerId, cmd.TargetIndex).ConfigureAwait(false);
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification($"Invited {cmd.TargetName} to join the party.");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = $"Sent party invite to {cmd.TargetName}.";
                            });
                        }
                        else
                        {
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification($"Cannot invite: Player '{cmd.TargetName}' was not found in this area.", "#E06C75");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = $"Player '{cmd.TargetName}' not found in area.";
                            });
                        }
                        break;
                    }

                    case ChatCommandResultKind.PartyLeave:
                    {
                        await SelectedSession.PartyModule.LeavePartyAsync().ConfigureAwait(false);
                        DispatchToUi(() =>
                        {
                            var notice = ChatItemViewModel.CreatePartyNotification("Left the party.", "#E06C75");
                            AddMessageItem(notice);
                            OutgoingMessage = string.Empty;
                            StatusText = "Left the party.";
                        });
                        break;
                    }

                    case ChatCommandResultKind.PartyDisband:
                    {
                        await SelectedSession.PartyModule.DisbandPartyAsync().ConfigureAwait(false);
                        DispatchToUi(() =>
                        {
                            var notice = ChatItemViewModel.CreatePartyNotification("Disbanded the party.", "#E06C75");
                            AddMessageItem(notice);
                            OutgoingMessage = string.Empty;
                            StatusText = "Disbanded the party.";
                        });
                        break;
                    }

                    case ChatCommandResultKind.PartyKick:
                    {
                        var member = SelectedSession.Party.Members.FirstOrDefault(m =>
                            string.Equals(m.Name, cmd.TargetName, StringComparison.OrdinalIgnoreCase) ||
                            (cmd.TargetServerId != 0 && m.ServerId == cmd.TargetServerId));

                        if (member != null)
                        {
                            await SelectedSession.PartyModule.KickMemberAsync(member.ServerId, member.TargetIndex, member.Name).ConfigureAwait(false);
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification($"Kicked {member.Name} from the party.", "#E06C75");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = $"Kicked {member.Name} from party.";
                            });
                        }
                        else
                        {
                            DispatchToUi(() =>
                            {
                                var notice = ChatItemViewModel.CreatePartyNotification($"Player '{cmd.TargetName}' is not in your party.", "#E5C07B");
                                AddMessageItem(notice);
                                OutgoingMessage = string.Empty;
                                StatusText = $"Player '{cmd.TargetName}' not in party.";
                            });
                        }
                        break;
                    }

                    case ChatCommandResultKind.LocalEcho:
                    {
                        DispatchToUi(() =>
                        {
                            var echo = ChatItemViewModel.CreateLocalEcho(cmd.Message);
                            AddMessageItem(echo);
                            OutgoingMessage = string.Empty;
                        });
                        break;
                    }

                    case ChatCommandResultKind.LocalNotice:
                    case ChatCommandResultKind.Unrecognized:
                    {
                        DispatchToUi(() =>
                        {
                            var notice = ChatItemViewModel.CreateLocalNotice(cmd.Message);
                            AddMessageItem(notice);
                            OutgoingMessage = string.Empty;
                            StatusText = cmd.Message;
                        });
                        break;
                    }

                    case ChatCommandResultKind.ServerCommand:
                    {
                        await SelectedSession.ChatModule.SendChatAsync(ChatSendKind.Say, cmd.Message).ConfigureAwait(false);
                        DispatchToUi(() =>
                        {
                            var outItem = ChatItemViewModel.CreateOutgoing(ChatSendKind.Say, myName, cmd.Message);
                            AddMessageItem(outItem);
                            OutgoingMessage = string.Empty;
                            StatusText = $"Sent server command: {cmd.Message}";
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("CHAT_UI", $"Failed to execute command: {ex.Message}", ex);
                DispatchToUi(() =>
                {
                    StatusText = $"Execution error: {ex.Message}";
                });
            }
        }

        public async Task ExecuteRequestLsMotdAsync()
        {
            if (SelectedSession == null) return;
            try
            {
                await SelectedSession.ChatModule.RequestLinkshellMessageAsync(LinkshellSlot.LS1).ConfigureAwait(false);
                DispatchToUi(() => StatusText = "Requested Linkshell 1 MOTD...");
            }
            catch (Exception ex)
            {
                GordianLog.Error("CHAT_UI", "Failed requesting LS MOTD", ex);
                DispatchToUi(() => StatusText = $"LS MOTD request error: {ex.Message}");
            }
        }

        public void ExecuteClearLog()
        {
            AllMessages.Clear();
            FilteredMessages.Clear();
            StatusText = "Chat log cleared.";
        }

        private static void DispatchToUi(Action action)
        {
            if (UiDispatcher != null)
            {
                UiDispatcher(action);
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }

        public void Dispose()
        {
            _sessionRegistry.SessionRegistered -= OnSessionRegistered;
            _sessionRegistry.SessionUnregistered -= OnSessionUnregistered;

            if (_hookedSession != null)
            {
                _hookedSession.ChatModule.ChatMessageReceived -= OnChatMessageReceived;
                _hookedSession.ChatModule.SystemMessageReceived -= OnSystemMessageReceived;
                _hookedSession.ChatModule.TranslateReceived -= OnTranslateReceived;
                _hookedSession.ChatModule.LinkshellMessageReceived -= OnLinkshellMessageReceived;
                _hookedSession.Party.InviteReceived -= OnPartyInviteReceived;
                _hookedSession.Party.InviteCleared -= OnPartyInviteCleared;
                _hookedSession.Party.MemberJoined -= OnPartyMemberJoined;
                _hookedSession.Party.MemberLeft -= OnPartyMemberLeft;
                _hookedSession.Party.PartyDisbanded -= OnPartyDisbanded;
                _hookedSession.Combat.ActionExecuted -= OnCombatActionExecuted;
                _hookedSession.Combat.BattleMessageReceived -= OnBattleMessageReceived;
                _hookedSession = null;
            }
        }
    }
}
