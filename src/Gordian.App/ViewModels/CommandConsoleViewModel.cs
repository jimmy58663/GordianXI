// src/Gordian.App/ViewModels/CommandConsoleViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.Core.Actions;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.World;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel driving the interactive character Command Console (CLI) tab.
    /// Provides terminal-style input, command history navigation, color-coded output,
    /// and direct execution through <see cref="PlayerActionService"/>.
    /// </summary>
    public sealed class CommandConsoleViewModel : ViewModelBase, IDisposable
    {
        public static Action<Action>? UiDispatcher { get; set; }

        private readonly SessionRegistry _sessionRegistry;
        private CharacterSession? _selectedSession;
        private CharacterSession? _hookedSession;
        private string _inputCommand = string.Empty;
        private string _statusText = "Ready";
        private bool _autoScroll = true;
        private string _currentTargetName = "None";

        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;

        public ObservableCollection<CharacterSession> ActiveSessions { get; } = new ObservableCollection<CharacterSession>();
        public ObservableCollection<CommandConsoleItemViewModel> Entries { get; } = new ObservableCollection<CommandConsoleItemViewModel>();

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

        public bool HasActiveSession => _selectedSession != null;

        public string SessionHeaderTitle => _selectedSession != null
            ? $"{_selectedSession.CharacterName} [0x{_selectedSession.CharacterId:X8}]"
            : "No active session";

        public string InputCommand
        {
            get => _inputCommand;
            set => SetProperty(ref _inputCommand, value);
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

        public string CurrentTargetName
        {
            get => _currentTargetName;
            set => SetProperty(ref _currentTargetName, value);
        }

        public ICommand ExecuteInputCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand HistoryUpCommand { get; }
        public ICommand HistoryDownCommand { get; }

        // Quick Command Shortcuts
        public ICommand QuickPosCommand { get; }
        public ICommand QuickNearbyCommand { get; }
        public ICommand QuickTargetInfoCommand { get; }
        public ICommand QuickVitalsCommand { get; }
        public ICommand QuickAttackCommand { get; }
        public ICommand QuickDisengageCommand { get; }

        public event EventHandler? RequestScrollToEnd;

        public CommandConsoleViewModel(SessionRegistry? registry = null)
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

            ExecuteInputCommand = new RelayCommand(async () => await ExecuteInputAsync());
            ClearConsoleCommand = new RelayCommand(ClearConsole);
            HistoryUpCommand = new RelayCommand(HistoryPrevious);
            HistoryDownCommand = new RelayCommand(HistoryNext);

            QuickPosCommand = new RelayCommand(async () => await ExecuteSlashCommandAsync("/pos"));
            QuickNearbyCommand = new RelayCommand(async () => await ExecuteSlashCommandAsync("/nearby 50"));
            QuickTargetInfoCommand = new RelayCommand(async () => await ExecuteSlashCommandAsync("/targetinfo"));
            QuickVitalsCommand = new RelayCommand(async () => await ExecuteSlashCommandAsync("/vitals"));
            QuickAttackCommand = new RelayCommand(async () => await ExecuteSlashCommandAsync("/attack"));
            QuickDisengageCommand = new RelayCommand(async () => await ExecuteSlashCommandAsync("/attackoff"));

            // Welcome banner
            AddEntry(CommandConsoleItemViewModel.CreateSystem("GordianXI Interactive Command Console (CLI) initialized."));
            AddEntry(CommandConsoleItemViewModel.CreateInfo("Type '/pos', '/target <name>', '/attack', '/magic <id>', '/moveto x y z', or '!command'."));
        }

        public void AddEntry(CommandConsoleItemViewModel entry)
        {
            Entries.Add(entry);
            if (Entries.Count > 1000)
            {
                Entries.RemoveAt(0);
            }

            if (AutoScroll)
            {
                RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ClearConsole()
        {
            Entries.Clear();
            AddEntry(CommandConsoleItemViewModel.CreateSystem("Console buffer cleared."));
        }

        public async Task ExecuteSlashCommandAsync(string command)
        {
            InputCommand = command;
            await ExecuteInputAsync();
        }

        public async Task ExecuteInputAsync()
        {
            if (string.IsNullOrWhiteSpace(InputCommand))
                return;

            string raw = InputCommand.Trim();
            InputCommand = string.Empty;

            // Add to history
            if (_history.Count == 0 || _history[^1] != raw)
            {
                _history.Add(raw);
                if (_history.Count > 100)
                {
                    _history.RemoveAt(0);
                }
            }
            _historyIndex = _history.Count;

            // Log user input echo
            AddEntry(CommandConsoleItemViewModel.CreateInput(raw));

            if (SelectedSession == null)
            {
                AddEntry(CommandConsoleItemViewModel.CreateError("Error: No active character session selected."));
                StatusText = "Error: No active session.";
                return;
            }

            try
            {
                var result = await SelectedSession.ActionService.ExecuteCommandAsync(raw).ConfigureAwait(false);

                DispatchToUi(() =>
                {
                    var item = result.Kind switch
                    {
                        PlayerActionResultKind.Success => CommandConsoleItemViewModel.CreateSuccess(result.Message),
                        PlayerActionResultKind.Warning => CommandConsoleItemViewModel.CreateWarning(result.Message),
                        PlayerActionResultKind.Error => CommandConsoleItemViewModel.CreateError(result.Message),
                        _ => CommandConsoleItemViewModel.CreateInfo(result.Message)
                    };

                    AddEntry(item);
                    StatusText = result.Message;
                });
            }
            catch (Exception ex)
            {
                GordianLog.Error("CLI", $"Command execution failed: {ex.Message}", ex);
                DispatchToUi(() =>
                {
                    AddEntry(CommandConsoleItemViewModel.CreateError($"Execution exception: {ex.Message}"));
                    StatusText = $"Error: {ex.Message}";
                });
            }
        }

        public void HistoryPrevious()
        {
            if (_history.Count == 0) return;

            if (_historyIndex > 0)
            {
                _historyIndex--;
                InputCommand = _history[_historyIndex];
            }
            else if (_historyIndex == 0)
            {
                InputCommand = _history[0];
            }
        }

        public void HistoryNext()
        {
            if (_history.Count == 0) return;

            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                InputCommand = _history[_historyIndex];
            }
            else
            {
                _historyIndex = _history.Count;
                InputCommand = string.Empty;
            }
        }

        private void OnSelectedSessionChanged(CharacterSession? session)
        {
            if (_hookedSession != null)
            {
                _hookedSession.ActionService.TargetChanged -= OnTargetChanged;
                _hookedSession = null;
            }

            if (session != null)
            {
                session.ActionService.TargetChanged += OnTargetChanged;
                _hookedSession = session;
                CurrentTargetName = session.ActionService.CurrentTarget?.Name ?? "None";
                StatusText = $"Connected to console for {session.CharacterName}";
                AddEntry(CommandConsoleItemViewModel.CreateSystem($"Switched active CLI context to character: {session.CharacterName}"));
            }
            else
            {
                CurrentTargetName = "None";
                StatusText = "No active character session.";
            }
        }

        private void OnTargetChanged(WorldEntity? target)
        {
            DispatchToUi(() =>
            {
                CurrentTargetName = target != null ? $"{target.Name} [0x{target.ServerId:X8}]" : "None";
            });
        }

        private void OnSessionRegistered(object? sender, CharacterSession session)
        {
            DispatchToUi(() =>
            {
                if (!ActiveSessions.Contains(session))
                {
                    ActiveSessions.Add(session);
                    if (SelectedSession == null)
                    {
                        SelectedSession = session;
                    }
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
                    SelectedSession = ActiveSessions.Count > 0 ? ActiveSessions[0] : null;
                }
            });
        }

        private void DispatchToUi(Action action)
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
                _hookedSession.ActionService.TargetChanged -= OnTargetChanged;
                _hookedSession = null;
            }
        }
    }
}
