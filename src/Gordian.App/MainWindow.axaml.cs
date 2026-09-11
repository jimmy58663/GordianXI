// src/Gordian.App/MainWindow.axaml.cs
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

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
            _ipcServer.SessionReceived -= OnSessionTokenIntercepted;
            _ipcServer.Dispose();
            _viewModel.Dispose();
            SessionRegistry.Default.Clear();
            base.OnUnloaded(e);
        }
    }
}
