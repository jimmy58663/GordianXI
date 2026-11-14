using System.Text;
using Avalonia.Logging;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Services
{
    /// <summary>
    /// Forwards Avalonia's own log (binding errors, layout and platform warnings) into <see cref="GordianLog"/> under
    /// <see cref="LogCategories.Avalonia"/>, at <see cref="LogEventLevel.Warning"/> and above. Replaces <c>LogToTrace</c>,
    /// which wrote into the old trace listeners.
    /// </summary>
    public sealed class AvaloniaLogSink : ILogSink
    {
        private readonly LogEventLevel _minimum;

        /// <summary>Creates a sink that forwards events at <paramref name="minimum"/> and above.</summary>
        public AvaloniaLogSink(LogEventLevel minimum = LogEventLevel.Warning) => _minimum = minimum;

        /// <inheritdoc/>
        public bool IsEnabled(LogEventLevel level, string area) =>
            level >= _minimum && GordianLog.IsEnabled(Map(level), LogCategories.Avalonia);

        /// <inheritdoc/>
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, null);

        /// <inheritdoc/>
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[]? propertyValues)
        {
            if (!IsEnabled(level, area)) return;
            string message = $"[{area}] {Fill(messageTemplate, propertyValues)}{(source is null ? string.Empty : $" ({source.GetType().Name})")}";
            LogLevel mapped = Map(level);
            if (mapped == LogLevel.Error) GordianLog.Error(LogCategories.Avalonia, message);
            else if (mapped == LogLevel.Warn) GordianLog.Warn(LogCategories.Avalonia, message);
            else if (mapped == LogLevel.Info) GordianLog.Info(LogCategories.Avalonia, message);
            else GordianLog.Debug(LogCategories.Avalonia, message);
        }

        private static LogLevel Map(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Info,
            LogEventLevel.Warning => LogLevel.Warn,
            _ => LogLevel.Error,
        };

        /// <summary>Replaces each <c>{Name}</c> hole of an Avalonia message template with the next value, in order.</summary>
        internal static string Fill(string template, object?[]? values)
        {
            if (values is null || values.Length == 0 || template.IndexOf('{') < 0) return template;
            var builder = new StringBuilder(template.Length + 32);
            int next = 0;
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                int close = c == '{' ? template.IndexOf('}', i + 1) : -1;
                if (close > i && next < values.Length)
                {
                    builder.Append(values[next++]);
                    i = close;
                }
                else
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }
    }
}
