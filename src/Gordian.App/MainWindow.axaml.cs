using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Input;
using Gordian.Core.Network;

namespace Gordian.App
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly HandoffPipeServer _ipcServer;
        private readonly DispatcherTimer _inputLoopTimer;
        private readonly IGamepadDriver _gamepadDriver;
        private long _lastInputLoopTimestamp;

        public MainWindow()
        {
            InitializeComponent();

            var consoleListBox = this.FindControl<ListBox>("ConsoleListBox");
            if (consoleListBox != null)
            {
                AutoCopyBehavior.Attach(consoleListBox);
            }
            var chatListBox = this.FindControl<ListBox>("MainChatListBox");
            if (chatListBox != null)
            {
                AutoCopyBehavior.Attach(chatListBox);
            }

            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            _viewModel.Chat.RequestScrollToEnd += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var listBox = this.FindControl<ListBox>("MainChatListBox");
                    if (listBox != null && listBox.ItemCount > 0)
                    {
                        listBox.ScrollIntoView(listBox.ItemCount - 1);
                    }
                });
            };

            _viewModel.Chat.RequestOpenPopOutWindow += (s, e) =>
            {
                var chatWin = new ChatWindow(_viewModel.Chat);
                chatWin.Show(this);
            };

            _viewModel.Console.RequestScrollToEnd += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var listBox = this.FindControl<ListBox>("ConsoleListBox");
                    if (listBox != null && listBox.ItemCount > 0)
                    {
                        listBox.ScrollIntoView(listBox.ItemCount - 1);
                    }
                });
            };

            _ipcServer = new HandoffPipeServer();
            _ipcServer.SessionReceived += OnSessionTokenIntercepted;
            _ipcServer.Start();

            // Initialize cross-platform Gamepad Driver via Silk.NET.SDL (supports XInput, DirectInput, DualSense, HID)
            var sdlDriver = new SdlGamepadDriver();
            if (sdlDriver.IsAvailable)
            {
                _gamepadDriver = sdlDriver;
            }
            else if (OperatingSystem.IsWindows())
            {
                _gamepadDriver = new XInputGamepadDriver();
            }
            else
            {
                _gamepadDriver = new VirtualGamepadDriver();
            }

            // Gameplay keyboard/mouse input (movement, camera, actions) is captured on ViewportWindow,
            // the window that actually renders and receives focus during play — not here. MainWindow
            // only needs to let Escape unfocus a text box so keyboard control can resume.
            AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

            _lastInputLoopTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            _inputLoopTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _inputLoopTimer.Tick += OnInputLoopTick;
            _inputLoopTimer.Start();
        }

        private void OnSessionTokenIntercepted(object? sender, SessionHandoffArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NET_TRACE] Secure handoff caught for character: {e.TargetCharacterName} | Target Server: {e.ServerIp}:{e.ServerPort}"
                );

                // Restore genuine FFXiMain.dll immediately upon session handoff
                string? gameDir = GameDirectoryDetector.DetectGameDirectory();
                if (gameDir != null && ProxyStager.IsStaged)
                {
                    ProxyStager.RestoreOriginal(gameDir);
                }

                var session = SessionRegistry.Default.CreateAndRegisterSession(e);
                _viewModel.RefreshAllStatuses();

                try
                {
                    await session.NetworkManager.ConnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Failed to connect character session for '{e.TargetCharacterName}': {ex.Message}");
                }
            });
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            _inputLoopTimer.Stop();
            _inputLoopTimer.Tick -= OnInputLoopTick;

            string? gameDir = GameDirectoryDetector.DetectGameDirectory();
            if (gameDir != null)
            {
                ProxyStager.RestoreOriginal(gameDir);
            }

            _ipcServer.SessionReceived -= OnSessionTokenIntercepted;
            _ipcServer.Dispose();
            _gamepadDriver.Dispose();
            _viewModel.Dispose();
            SessionRegistry.Default.Clear();
            base.OnUnloaded(e);
        }

        private bool IsTextBoxFocused()
        {
            var focused = FocusManager?.GetFocusedElement();
            return focused is TextBox;
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            // When typing in a text box, Escape unfocuses it so keyboard control can resume elsewhere.
            if (IsTextBoxFocused() && e.Key == Key.Escape)
            {
                FocusManager?.Focus(null, NavigationMethod.Unspecified);
                e.Handled = true;
            }
        }

        private void OnInputLoopTick(object? sender, EventArgs e)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_lastInputLoopTimestamp, now);
            _lastInputLoopTimestamp = now;

            // Clamp max single frame delta to prevent teleporting after a long freeze or window drag
            if (elapsed > TimeSpan.FromMilliseconds(100))
            {
                elapsed = TimeSpan.FromMilliseconds(100);
            }

            // In multi-boxing, only the primary client rendering 3D graphics receives gamepad input.
            var primarySession = SessionRegistry.Default.PrimaryRenderingSession 
                                 ?? _viewModel.Console.SelectedSession;

            var gamepadSettings = primarySession?.Locomotion?.Profile?.GamepadSettings;

            bool isGamepadEnabled = gamepadSettings?.GamepadEnabled ?? _viewModel.Controls.GamepadEnabled;
            bool alwaysEnable = gamepadSettings?.AlwaysEnableGamepad ?? _viewModel.Controls.AlwaysEnableGamepad;
            bool rumbleEnabled = gamepadSettings?.RumbleEnabled ?? _viewModel.Controls.GamepadRumbleEnabled;
            bool windowFocused = this.IsActive || ViewportWindowManager.Default.IsAnyViewportActive();

            _gamepadDriver.RumbleEnabled = rumbleEnabled;

            // Polling only occurs if enabled AND (window is active OR AlwaysEnableGamepad is set)
            bool shouldPoll = isGamepadEnabled && (windowFocused || alwaysEnable);
            var padState = shouldPoll ? _gamepadDriver.Poll(0) : GamepadState.Disconnected;

            var activeSessions = SessionRegistry.Default.ActiveSessions;
            if (activeSessions.Count > 0)
            {
                foreach (var session in activeSessions)
                {
                    if (session == primarySession && session.IsRendering3D)
                    {
                        session.InputState.SetGamepadState(padState);
                    }
                    else
                    {
                        // Background headless characters must NEVER receive gamepad input
                        if (session.InputState.CurrentGamepad.IsConnected)
                        {
                            session.InputState.SetGamepadState(GamepadState.Disconnected);
                        }
                    }

                    session.Locomotion.Update(elapsed);
                }
            }
            else if (primarySession != null)
            {
                if (primarySession.IsRendering3D)
                {
                    primarySession.InputState.SetGamepadState(padState);
                }
                else
                {
                    primarySession.InputState.SetGamepadState(GamepadState.Disconnected);
                }
                primarySession.Locomotion.Update(elapsed);
            }

            _viewModel.Controls.UpdateTelemetry();
        }

        private void OnColumnHeaderDragDelta(object? sender, VectorEventArgs e)
        {
            if (sender is Control control && control.Tag is string colName && DataContext is MainWindowViewModel mainVm)
            {
                mainVm.StateInspector.AdjustColumnWidth(colName, e.Vector.X);
            }
        }

        private void OnMainChatMessageInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel.Chat.CanSendMessage())
            {
                _ = _viewModel.Chat.ExecuteSendMessageAsync();
                e.Handled = true;
            }
        }

        private void OnConsoleInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = _viewModel.Console.ExecuteInputAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _viewModel.Console.HistoryPrevious();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                _viewModel.Console.HistoryNext();
                e.Handled = true;
            }
        }

        private ProfileItemViewModel? _draggedProfile;

        private void OnRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is LaunchFolderViewModel folder)
            {
                if (e.Key == Key.Enter)
                {
                    folder.CommitRename();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    folder.CancelRename();
                    e.Handled = true;
                }
            }
        }

        private async void OnProfilePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Do not initiate drag if user clicked interactive buttons or checkboxes
            if (e.Source is Button || e.Source is CheckBox || e.Source is TextBox)
            {
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                sender is Control ctrl &&
                ctrl.DataContext is ProfileItemViewModel profile)
            {
                _draggedProfile = profile;
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(profile.ProfileName));
                try
                {
                    await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
                }
                finally
                {
                    _draggedProfile = null;
                }
            }
        }

        private void OnFolderDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = _draggedProfile != null ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnFolderDrop(object? sender, DragEventArgs e)
        {
            if (_draggedProfile != null &&
                sender is Control ctrl &&
                ctrl.DataContext is LaunchFolderViewModel folder)
            {
                _viewModel.MoveProfileToFolder(_draggedProfile, folder);
            }
        }

        private void OnProfileDragOver(object? sender, DragEventArgs e)
        {
            if (_draggedProfile != null &&
                sender is Control ctrl &&
                ctrl.DataContext is ProfileItemViewModel targetProfile &&
                targetProfile != _draggedProfile)
            {
                e.DragEffects = DragDropEffects.Move;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private void OnProfileDrop(object? sender, DragEventArgs e)
        {
            if (_draggedProfile != null &&
                sender is Control ctrl &&
                ctrl.DataContext is ProfileItemViewModel targetProfile &&
                targetProfile != _draggedProfile)
            {
                var pos = e.GetPosition(ctrl);
                bool insertAfter = pos.Y > (ctrl.Bounds.Height / 2);
                _viewModel.MoveProfileRelative(_draggedProfile, targetProfile, insertAfter);
            }
        }

        private void OnRootDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = _draggedProfile != null ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnRootDrop(object? sender, DragEventArgs e)
        {
            if (_draggedProfile != null)
            {
                _viewModel.MoveProfileToFolder(_draggedProfile, null);
            }
        }
    }
}

