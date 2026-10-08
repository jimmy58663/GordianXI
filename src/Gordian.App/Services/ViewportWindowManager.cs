// src/Gordian.App/Services/ViewportWindowManager.cs
using System;
using System.Collections.Concurrent;
using System.Threading;
using Avalonia.Threading;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;

namespace Gordian.App.Services
{
    /// <summary>
    /// Coordinates the lifecycle of 3D viewport rendering windows, supporting automatic
    /// launch on character connection, character tab management, and multi-monitor tear-off pop-out windows.
    /// </summary>
    public sealed class ViewportWindowManager : IDisposable
    {
        private static readonly Lazy<ViewportWindowManager> _defaultInstance =
            new(() => new ViewportWindowManager());

        public static ViewportWindowManager Default => _defaultInstance.Value;

        /// <summary>
        /// Optional UI dispatcher override for testing environments where the Avalonia UIThread is not running.
        /// </summary>
        public static Action<Action>? UiDispatcher { get; set; }

        private static void PostToUi(Action action)
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

        private readonly SessionRegistry _sessionRegistry;
        private readonly ConcurrentDictionary<Guid, ViewportWindow> _secondaryWindows = new();
        private ViewportWindow? _primaryWindow;
        private ViewportViewModel _primaryViewModel;
        private bool _isDisposed;

        public ViewportWindowManager(
            SessionRegistry? sessionRegistry = null,
            ViewportViewModel? primaryViewModel = null)
        {
            _sessionRegistry = sessionRegistry ?? SessionRegistry.Default;
            _primaryViewModel = primaryViewModel ?? new ViewportViewModel(enableAutoSave: false);

            _sessionRegistry.SessionRegistered += OnSessionRegistered;
            _sessionRegistry.SessionUnregistered += OnSessionUnregistered;

            _primaryViewModel.TabPoppedOut += OnTabPoppedOut;
            _primaryViewModel.ViewportWindowRequested += OnViewportWindowRequested;
            _primaryViewModel.PropertyChanged += OnPrimaryViewModelPropertyChanged;
        }

        public ViewportViewModel PrimaryViewModel => _primaryViewModel;

        public ViewportWindow? PrimaryWindow => _primaryWindow;

        /// <summary>
        /// Which viewport window (primary or popped out) has the focus and which character it shows (#150): the gamepad
        /// follows it.
        /// </summary>
        public InputFocusTracker InputFocus { get; } = new();

        /// <summary>
        /// Checks whether the primary or any secondary popped-out 3D viewport window currently has keyboard/window focus.
        /// </summary>
        public bool IsAnyViewportActive() => InputFocus.IsViewportFocused;

        public void SetPrimaryViewModel(ViewportViewModel viewModel)
        {
            ArgumentNullException.ThrowIfNull(viewModel);

            if (_primaryViewModel != null)
            {
                _primaryViewModel.TabPoppedOut -= OnTabPoppedOut;
                _primaryViewModel.ViewportWindowRequested -= OnViewportWindowRequested;
                _primaryViewModel.PropertyChanged -= OnPrimaryViewModelPropertyChanged;
            }

            _primaryViewModel = viewModel;
            _primaryViewModel.TabPoppedOut += OnTabPoppedOut;
            _primaryViewModel.ViewportWindowRequested += OnViewportWindowRequested;
            _primaryViewModel.PropertyChanged += OnPrimaryViewModelPropertyChanged;
        }

        private void OnViewportWindowRequested(object? sender, EventArgs e) => ShowPrimaryWindow();

        /// <summary>Pop-out windows follow the switcher style chosen in the settings (they show their one character).</summary>
        private void OnPrimaryViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewportViewModel.SelectedTabStyle)) return;
            foreach (var window in _secondaryWindows.Values)
            {
                if (window.DataContext is ViewportViewModel secondary) secondary.SelectedTabStyle = _primaryViewModel.SelectedTabStyle;
            }
        }

        /// <summary>Reports a viewport window's focus and the character it shows to <see cref="InputFocus"/> until it closes.</summary>
        private void TrackFocus(ViewportWindow window, ViewportViewModel viewModel)
        {
            void Sync() => InputFocus.SetWindowSession(window, viewModel.Lobby == null ? viewModel.ActiveTab?.Session : null);
            void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(ViewportViewModel.ActiveTab) or nameof(ViewportViewModel.Lobby)) Sync();
            }

            viewModel.PropertyChanged += OnChanged;
            window.Activated += (_, _) => InputFocus.Activated(window);
            window.Deactivated += (_, _) =>
            {
                InputFocus.Deactivated(window);
                // Keys and buttons released while another window has the focus never reach this one: let them go now.
                viewModel.ReleaseHeldInput();
            };
            window.Closed += (_, _) =>
            {
                viewModel.PropertyChanged -= OnChanged;
                InputFocus.Remove(window);
            };
            Sync();
        }

        /// <summary>
        /// Displays the primary 3D viewport window, creating it if not already open.
        /// </summary>
        public ViewportWindow ShowPrimaryWindow()
        {
            if (_primaryWindow == null)
            {
                _primaryWindow = new ViewportWindow("ViewportWindow")
                {
                    DataContext = _primaryViewModel
                };

                var window = _primaryWindow;
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_primaryWindow, window)) _primaryWindow = null;
                };
                TrackFocus(window, _primaryViewModel);

                window.Show();
            }
            else
            {
                _primaryWindow.Activate();
            }

            return _primaryWindow;
        }

        /// <summary>Shows a character lobby in the primary viewport window (opening it), in place of the session tabs.</summary>
        public void ShowLobby(Gordian.Core.Ui.Lobby.LobbyController lobby)
        {
            ArgumentNullException.ThrowIfNull(lobby);
            PostToUi(() =>
            {
                _primaryViewModel.Lobby = lobby;
                ShowPrimaryWindow();
            });
        }

        /// <summary>
        /// Takes a lobby off the primary window; the window closes when no character tab is left to show.
        /// </summary>
        public void CloseLobby(Gordian.Core.Ui.Lobby.LobbyController lobby)
        {
            PostToUi(() =>
            {
                if (!ReferenceEquals(_primaryViewModel.Lobby, lobby)) return;
                _primaryViewModel.Lobby = null;
                CloseIdlePrimaryWindow();
            });
        }

        /// <summary>Whether a lobby is on show.</summary>
        public bool IsLobbyOpen => _primaryViewModel.Lobby != null;

        private int _lobbyReturns;

        /// <summary>Whether a Log Out is returning to the character select screen (between Begin and End).</summary>
        public bool IsReturningToLobby => Volatile.Read(ref _lobbyReturns) > 0;

        /// <summary>
        /// Starts a Log Out's return to the character select screen (#32): until <see cref="EndLobbyReturn"/>, the primary
        /// window stays open when its last tab goes and shows black, so the lobby appears in the same window. Callable from
        /// any thread; call it before the session disconnects.
        /// </summary>
        public void BeginLobbyReturn()
        {
            Interlocked.Increment(ref _lobbyReturns);
            PostToUi(() => _primaryViewModel.IsReturningToLobby = IsReturningToLobby);
        }

        /// <summary>
        /// Ends a return begun by <see cref="BeginLobbyReturn"/>, once the lobby is shown (or could not be): with no lobby
        /// and no tab left, the window closes as it would have.
        /// </summary>
        public void EndLobbyReturn()
        {
            if (Interlocked.Decrement(ref _lobbyReturns) < 0) Interlocked.Exchange(ref _lobbyReturns, 0);
            PostToUi(() =>
            {
                _primaryViewModel.IsReturningToLobby = IsReturningToLobby;
                CloseIdlePrimaryWindow();
            });
        }

        /// <summary>Closes the primary window when it has nothing to show: no tab, no lobby, no return under way.</summary>
        private void CloseIdlePrimaryWindow()
        {
            if (_primaryViewModel.CharacterTabs.Count != 0 || _primaryViewModel.Lobby != null || IsReturningToLobby || _primaryWindow == null) return;
            try
            {
                _primaryWindow.Close();
            }
            catch (Exception ex)
            {
                GordianLog.Warning("ViewportManager", $"Failed to cleanly close primary viewport window: {ex.Message}");
            }
            _primaryWindow = null;
        }

        private void OnSessionRegistered(object? sender, CharacterSession session)
        {
            PostToUi(() =>
            {
                _primaryViewModel.AddSession(session);

                if (_primaryViewModel.AutoLaunchOnConnect && _primaryWindow == null)
                {
                    ShowPrimaryWindow();
                }
            });
        }

        private void OnSessionUnregistered(object? sender, CharacterSession session)
        {
            PostToUi(() =>
            {
                _primaryViewModel.RemoveSession(session);

                // If this session had a popped-out window, close it cleanly
                if (_secondaryWindows.TryRemove(session.SessionId, out var secondaryWindow))
                {
                    try
                    {
                        secondaryWindow.Close();
                    }
                    catch (Exception ex)
                    {
                        GordianLog.Warning("ViewportManager", $"Failed to cleanly close secondary window for {session.CharacterName}: {ex.Message}");
                    }
                }

                // If the primary viewport window has no remaining connected character tabs, close it cleanly (not while a
                // Log Out is returning to the lobby in it)
                CloseIdlePrimaryWindow();
            });
        }

        private void OnTabPoppedOut(object? sender, ViewportCharacterTabViewModel tab)
        {
            PostToUi(() =>
            {
                if (_secondaryWindows.ContainsKey(tab.SessionId))
                {
                    // Already popped out; bring to focus
                    _secondaryWindows[tab.SessionId].Activate();
                    return;
                }

                var secondaryVm = new ViewportViewModel(enableAutoSave: false)
                {
                    IsPrimary = false,
                    SwitchTarget = _primaryViewModel,
                    SelectedBackend = _primaryViewModel.SelectedBackend,
                    SelectedDisplayMode = ViewportDisplayMode.Windowed,
                    SelectedTabStyle = _primaryViewModel.SelectedTabStyle
                };

                var secondaryTab = secondaryVm.AddSession(tab.Session);
                secondaryVm.ActiveTab = secondaryTab;

                var secondaryWindow = new ViewportWindow($"ViewportWindow_{tab.CharacterName}")
                {
                    Title = $"GordianXI - {tab.CharacterName} (Dedicated Viewport)",
                    DataContext = secondaryVm
                };

                secondaryWindow.Closed += (_, _) =>
                {
                    _secondaryWindows.TryRemove(tab.SessionId, out _);
                    tab.IsPoppedOut = false;
                    _primaryViewModel.RefreshPipThumbnails();
                };

                tab.IsPoppedOut = true;
                _secondaryWindows[tab.SessionId] = secondaryWindow;
                TrackFocus(secondaryWindow, secondaryVm);
                // The primary window moves on to a character that is not popped out, so one character is not drawn
                // and driven from two windows at once.
                _primaryViewModel.MoveOffPoppedOutTab();
                secondaryWindow.Show();
            });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _sessionRegistry.SessionRegistered -= OnSessionRegistered;
            _sessionRegistry.SessionUnregistered -= OnSessionUnregistered;

            if (_primaryViewModel != null)
            {
                _primaryViewModel.TabPoppedOut -= OnTabPoppedOut;
                _primaryViewModel.ViewportWindowRequested -= OnViewportWindowRequested;
                _primaryViewModel.PropertyChanged -= OnPrimaryViewModelPropertyChanged;
            }

            foreach (var kvp in _secondaryWindows)
            {
                try { kvp.Value.Close(); } catch { }
            }
            _secondaryWindows.Clear();

            try { _primaryWindow?.Close(); } catch { }
            _primaryWindow = null;
        }
    }
}
