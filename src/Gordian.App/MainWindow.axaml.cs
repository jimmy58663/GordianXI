using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Gordian.App.Common;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Network;

namespace Gordian.App
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly HandoffPipeServer _ipcServer;

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

