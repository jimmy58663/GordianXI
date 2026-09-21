// src/Gordian.App/Services/ViewportWindowManager.cs
using System;
using System.Collections.Concurrent;
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
            _primaryViewModel.ViewportWindowRequested += (_, _) => ShowPrimaryWindow();
        }

        public ViewportViewModel PrimaryViewModel => _primaryViewModel;

        public ViewportWindow? PrimaryWindow => _primaryWindow;

        /// <summary>
        /// Checks whether the primary or any secondary popped-out 3D viewport window currently has keyboard/window focus.
        /// </summary>
        public bool IsAnyViewportActive()
        {
            if (_primaryWindow?.IsActive == true) return true;
            foreach (var window in _secondaryWindows.Values)
            {
                if (window.IsActive) return true;
            }
            return false;
        }

        public void SetPrimaryViewModel(ViewportViewModel viewModel)
        {
            ArgumentNullException.ThrowIfNull(viewModel);

            if (_primaryViewModel != null)
            {
                _primaryViewModel.TabPoppedOut -= OnTabPoppedOut;
            }

            _primaryViewModel = viewModel;
            _primaryViewModel.TabPoppedOut += OnTabPoppedOut;
            _primaryViewModel.ViewportWindowRequested += (_, _) => ShowPrimaryWindow();
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

                _primaryWindow.Closed += (_, _) =>
                {
                    _primaryWindow = null;
                };

                _primaryWindow.Show();
            }
            else
            {
                _primaryWindow.Activate();
            }

            return _primaryWindow;
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

                // If the primary viewport window has no remaining connected character tabs, close it cleanly
                if (_primaryViewModel.CharacterTabs.Count == 0 && _primaryWindow != null)
                {
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
                    SelectedBackend = _primaryViewModel.SelectedBackend,
                    SelectedDisplayMode = ViewportDisplayMode.Windowed
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
                };

                tab.IsPoppedOut = true;
                _secondaryWindows[tab.SessionId] = secondaryWindow;
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
