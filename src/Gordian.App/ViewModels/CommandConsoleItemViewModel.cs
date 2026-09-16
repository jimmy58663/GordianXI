// src/Gordian.App/ViewModels/CommandConsoleItemViewModel.cs
using System;

namespace Gordian.App.ViewModels
{
    public enum ConsoleEntryKind
    {
        Input,
        Success,
        Info,
        Warning,
        Error,
        System
    }

    /// <summary>
    /// ViewModel representing a single color-coded terminal log entry in the Interactive Command Console.
    /// </summary>
    public sealed class CommandConsoleItemViewModel : ViewModelBase
    {
        public DateTime Timestamp { get; }
        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
        public ConsoleEntryKind Kind { get; }
        public string Prefix { get; }
        public string PrefixColor { get; }
        public string Message { get; }
        public string MessageColor { get; }
        public bool IsCommandInput => Kind == ConsoleEntryKind.Input;

        public CommandConsoleItemViewModel(
            ConsoleEntryKind kind,
            string prefix,
            string prefixColor,
            string message,
            string messageColor,
            DateTime? timestamp = null)
        {
            Timestamp = timestamp ?? DateTime.Now;
            Kind = kind;
            Prefix = prefix;
            PrefixColor = prefixColor;
            Message = message;
            MessageColor = messageColor;
        }

        public static CommandConsoleItemViewModel CreateInput(string command)
        {
            return new CommandConsoleItemViewModel(
                ConsoleEntryKind.Input,
                ">>",
                "#E5C07B",
                command,
                "#FFFFFF");
        }

        public static CommandConsoleItemViewModel CreateSuccess(string message)
        {
            return new CommandConsoleItemViewModel(
                ConsoleEntryKind.Success,
                "[OK]",
                "#98C379",
                message,
                "#98C379");
        }

        public static CommandConsoleItemViewModel CreateInfo(string message)
        {
            return new CommandConsoleItemViewModel(
                ConsoleEntryKind.Info,
                "[INFO]",
                "#61AFEF",
                message,
                "#DCDCDC");
        }

        public static CommandConsoleItemViewModel CreateWarning(string message)
        {
            return new CommandConsoleItemViewModel(
                ConsoleEntryKind.Warning,
                "[WARN]",
                "#E5C07B",
                message,
                "#E5C07B");
        }

        public static CommandConsoleItemViewModel CreateError(string message)
        {
            return new CommandConsoleItemViewModel(
                ConsoleEntryKind.Error,
                "[ERR]",
                "#E06C75",
                message,
                "#E06C75");
        }

        public static CommandConsoleItemViewModel CreateSystem(string message)
        {
            return new CommandConsoleItemViewModel(
                ConsoleEntryKind.System,
                "[SYS]",
                "#C678DD",
                message,
                "#C678DD");
        }
    }
}
