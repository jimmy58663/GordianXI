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
        private long _lastInputLoopTimestamp;
        private Avalonia.Point? _lastPointerPosition;
        private bool _isRightDragging;

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

            // Cross-Platform Input Subsystem Event Hooks
            AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            AddHandler(InputElement.KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel);

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
            if (IsTextBoxFocused())
            {
                // When typing in a text box, if user presses Escape, unfocus the textbox so they can resume movement
                if (e.Key == Key.Escape)
                {
                    FocusManager?.Focus(null, NavigationMethod.Unspecified);
                    e.Handled = true;
                }
                return;
            }

            var session = _viewModel.Console.SelectedSession;
            if (session == null) return;

            var gKey = AvaloniaInputMapper.ToGordianKey(e.Key);
            var mods = AvaloniaInputMapper.ToInputModifiers(e.KeyModifiers);

            if (gKey != GordianKey.None)
            {
                session.InputState.SetModifiers(mods);
                session.InputState.SetKeyDown(gKey);

                // Suppress default UI navigation for gameplay keys like Tab or arrows
                if (e.Key is Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right)
                {
                    e.Handled = true;
                }
            }
        }

        private void OnWindowKeyUp(object? sender, KeyEventArgs e)
        {
            var session = _viewModel.Console.SelectedSession;
            if (session == null) return;

            var gKey = AvaloniaInputMapper.ToGordianKey(e.Key);
            var mods = AvaloniaInputMapper.ToInputModifiers(e.KeyModifiers);

            if (gKey != GordianKey.None)
            {
                session.InputState.SetModifiers(mods);
                session.InputState.SetKeyUp(gKey);
            }
        }

        private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var session = _viewModel.Console.SelectedSession;
            if (session == null) return;

            var point = e.GetCurrentPoint(this);
            var btn = AvaloniaInputMapper.ToMouseButton(point.Properties);
            session.InputState.SetMouseButtonDown(btn);

            if (point.Properties.IsRightButtonPressed)
            {
                _isRightDragging = true;
                _lastPointerPosition = point.Position;
            }
        }

        private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var session = _viewModel.Console.SelectedSession;
            if (session == null) return;

            var point = e.GetCurrentPoint(this);
            var btn = AvaloniaInputMapper.ToMouseButton(point.Properties);
            session.InputState.SetMouseButtonUp(btn);

            if (!point.Properties.IsRightButtonPressed)
            {
                _isRightDragging = false;
                _lastPointerPosition = null;
            }
        }

        private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
        {
            var session = _viewModel.Console.SelectedSession;
            if (session == null) return;

            var currentPos = e.GetPosition(this);
            if (_isRightDragging && _lastPointerPosition.HasValue)
            {
                float dx = (float)(currentPos.X - _lastPointerPosition.Value.X);
                float dy = (float)(currentPos.Y - _lastPointerPosition.Value.Y);
                session.InputState.AddMouseDelta(dx, dy);
            }
            _lastPointerPosition = currentPos;
        }

        private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var session = _viewModel.Console.SelectedSession;
            if (session == null) return;

            session.InputState.AddMouseWheel((float)e.Delta.Y);
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

            var session = _viewModel.Console.SelectedSession;
            if (session != null)
            {
                session.Locomotion.Update(elapsed);
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
    }
}

