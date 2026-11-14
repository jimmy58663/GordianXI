using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Builds a <see cref="GordianLog.Trace"/> message from an interpolated string only when the category is at
    /// <see cref="LogLevel.Trace"/> or below; otherwise the compiler skips every hole, so a disabled line formats nothing.
    /// </summary>
    [InterpolatedStringHandler]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ref struct TraceLogHandler
    {
        private DefaultInterpolatedStringHandler _inner;

        /// <summary>Checks the category's level; the compiler appends the holes only when <paramref name="enabled"/>.</summary>
        public TraceLogHandler(int literalLength, int formattedCount, string category, out bool enabled)
        {
            enabled = GordianLog.IsEnabled(LogLevel.Trace, category);
            Enabled = enabled;
            _inner = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        internal bool Enabled { get; }

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendLiteral(string)"/>
        public void AppendLiteral(string value) => _inner.AppendLiteral(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T)"/>
        public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, string?)"/>
        public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int)"/>
        public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int, string?)"/>
        public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})"/>
        public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string?)"/>
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?)"/>
        public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?, int, string?)"/>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(object?, int, string?)"/>
        public void AppendFormatted(object? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        internal string ToStringAndClear() => _inner.ToStringAndClear();
    }

    /// <summary>
    /// Builds a <see cref="GordianLog.Debug"/> message from an interpolated string only when the category is at
    /// <see cref="LogLevel.Debug"/> or below; otherwise the compiler skips every hole, so a disabled line formats nothing.
    /// </summary>
    [InterpolatedStringHandler]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ref struct DebugLogHandler
    {
        private DefaultInterpolatedStringHandler _inner;

        /// <summary>Checks the category's level; the compiler appends the holes only when <paramref name="enabled"/>.</summary>
        public DebugLogHandler(int literalLength, int formattedCount, string category, out bool enabled)
        {
            enabled = GordianLog.IsEnabled(LogLevel.Debug, category);
            Enabled = enabled;
            _inner = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        internal bool Enabled { get; }

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendLiteral(string)"/>
        public void AppendLiteral(string value) => _inner.AppendLiteral(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T)"/>
        public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, string?)"/>
        public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int)"/>
        public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int, string?)"/>
        public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})"/>
        public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string?)"/>
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?)"/>
        public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?, int, string?)"/>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(object?, int, string?)"/>
        public void AppendFormatted(object? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        internal string ToStringAndClear() => _inner.ToStringAndClear();
    }

    /// <summary>
    /// Builds a <see cref="GordianLog.Info"/> message from an interpolated string only when the category is at
    /// <see cref="LogLevel.Info"/> or below; otherwise the compiler skips every hole, so a disabled line formats nothing.
    /// </summary>
    [InterpolatedStringHandler]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ref struct InfoLogHandler
    {
        private DefaultInterpolatedStringHandler _inner;

        /// <summary>Checks the category's level; the compiler appends the holes only when <paramref name="enabled"/>.</summary>
        public InfoLogHandler(int literalLength, int formattedCount, string category, out bool enabled)
        {
            enabled = GordianLog.IsEnabled(LogLevel.Info, category);
            Enabled = enabled;
            _inner = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        internal bool Enabled { get; }

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendLiteral(string)"/>
        public void AppendLiteral(string value) => _inner.AppendLiteral(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T)"/>
        public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, string?)"/>
        public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int)"/>
        public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int, string?)"/>
        public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})"/>
        public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string?)"/>
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?)"/>
        public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?, int, string?)"/>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(object?, int, string?)"/>
        public void AppendFormatted(object? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        internal string ToStringAndClear() => _inner.ToStringAndClear();
    }

    /// <summary>
    /// Builds a <see cref="GordianLog.Warn"/> message from an interpolated string only when the category is at
    /// <see cref="LogLevel.Warn"/> or below; otherwise the compiler skips every hole, so a disabled line formats nothing.
    /// </summary>
    [InterpolatedStringHandler]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ref struct WarnLogHandler
    {
        private DefaultInterpolatedStringHandler _inner;

        /// <summary>Checks the category's level; the compiler appends the holes only when <paramref name="enabled"/>.</summary>
        public WarnLogHandler(int literalLength, int formattedCount, string category, out bool enabled)
        {
            enabled = GordianLog.IsEnabled(LogLevel.Warn, category);
            Enabled = enabled;
            _inner = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        internal bool Enabled { get; }

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendLiteral(string)"/>
        public void AppendLiteral(string value) => _inner.AppendLiteral(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T)"/>
        public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, string?)"/>
        public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int)"/>
        public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted{T}(T, int, string?)"/>
        public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char})"/>
        public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(ReadOnlySpan{char}, int, string?)"/>
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?)"/>
        public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(string?, int, string?)"/>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        /// <inheritdoc cref="DefaultInterpolatedStringHandler.AppendFormatted(object?, int, string?)"/>
        public void AppendFormatted(object? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);

        internal string ToStringAndClear() => _inner.ToStringAndClear();
    }
}
