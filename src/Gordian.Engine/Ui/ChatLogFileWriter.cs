using System;
using System.Globalization;
using System.Text;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// Writes every line a character's in-game log shows to its chat log file,
    /// <c>logs/chat/&lt;server&gt;/&lt;character&gt;/yyyy-MM-dd.log</c> (#565), through <see cref="GordianLog.Chat"/>, which
    /// does the file work on the logger's writer thread. Lines are <c>[HH:mm:ss] [Channel] text</c> as plain text. Built
    /// by <c>CharacterSession</c>; it only listens to <see cref="StockUiChatLog.LineAdded"/>, so it changes nothing the
    /// log shows. The <c>ChatLog</c> logging setting turns it off.
    /// </summary>
    public sealed class ChatLogFileWriter : IDisposable
    {
        private readonly StockUiChatLog _log;
        private readonly Func<string> _server;
        private readonly Func<string> _character;

        /// <summary>Starts writing <paramref name="log"/>'s new lines under the given server and character names (read per line).</summary>
        public ChatLogFileWriter(StockUiChatLog log, Func<string> server, Func<string> character)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _log.LineAdded += OnLineAdded;
        }

        /// <summary>Stops writing.</summary>
        public void Dispose() => _log.LineAdded -= OnLineAdded;

        private void OnLineAdded(ChatLogLine line)
        {
            if (!GordianLog.ChatLogEnabled) return;
            GordianLog.Chat(_server(), _character(), line.Timestamp, Format(line));
        }

        /// <summary>The file line for <paramref name="line"/>: <c>[HH:mm:ss] [Channel] text</c>.</summary>
        public static string Format(ChatLogLine line)
        {
            ArgumentNullException.ThrowIfNull(line);
            return $"[{line.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] [{line.Channel}] {PlainText(line.Text)}";
        }

        /// <summary>
        /// The log text as plain text for a file: element icons (<see cref="ElementGlyphs"/>) become their element words,
        /// line breaks and tabs become spaces, and other control and private-use characters (the stock UI's drawing codes)
        /// are dropped.
        /// </summary>
        public static string PlainText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string words = ElementGlyphs.ToPlainText(text);
            var builder = new StringBuilder(words.Length);
            foreach (char c in words)
            {
                if (c is '\r' or '\n' or '\t')
                {
                    if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
                }
                else if (!char.IsControl(c) && !IsPrivateUse(c) && c != '�')
                {
                    builder.Append(c);
                }
            }
            return builder.ToString().TrimEnd();
        }

        private static bool IsPrivateUse(char c) => c >= '' && c <= '';
    }
}
