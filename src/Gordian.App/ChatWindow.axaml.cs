// src/Gordian.App/ChatWindow.axaml.cs
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.App.ViewModels;

namespace Gordian.App
{
    public partial class ChatWindow : Window
    {
        public ChatWindow()
        {
            InitializeComponent();
            AutoCopyBehavior.Attach(ChatListBox);
            DataContextChanged += OnDataContextChanged;
        }

        public ChatWindow(ChatViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is ChatViewModel vm)
            {
                vm.RequestScrollToEnd += OnRequestScrollToEnd;
            }
        }

        private void OnRequestScrollToEnd(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ChatListBox.ItemCount > 0)
                {
                    ChatListBox.ScrollIntoView(ChatListBox.ItemCount - 1);
                }
            });
        }

        private void OnMessageInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ChatViewModel vm && vm.CanSendMessage())
            {
                _ = vm.ExecuteSendMessageAsync();
                e.Handled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is ChatViewModel vm)
            {
                vm.RequestScrollToEnd -= OnRequestScrollToEnd;
            }
        }
    }
}
