using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Gordian.App.ViewModels;

namespace Gordian.App.Common
{
    /// <summary>
    /// A Logs tab line (#566): draws a <see cref="LogLineItem"/> as coloured runs (timestamp, level, category, character,
    /// message; <see cref="LogLineSegments"/>) and highlights the text matching <see cref="Highlight"/>. Only the visible
    /// lines are realized, so the runs are built for those alone.
    /// </summary>
    public sealed class LogLineTextBlock : TextBlock
    {
        public static readonly StyledProperty<LogLineItem?> LineProperty =
            AvaloniaProperty.Register<LogLineTextBlock, LogLineItem?>(nameof(Line));

        public static readonly StyledProperty<string?> HighlightProperty =
            AvaloniaProperty.Register<LogLineTextBlock, string?>(nameof(Highlight));

        private static readonly Dictionary<string, IBrush> Brushes = new(StringComparer.Ordinal);
        private static readonly IBrush MatchBackground = new ImmutableSolidColorBrush(Color.Parse("#6B5300"));

        protected override Type StyleKeyOverride => typeof(TextBlock);

        public LogLineItem? Line
        {
            get => GetValue(LineProperty);
            set => SetValue(LineProperty, value);
        }

        /// <summary>The search text to highlight (case-insensitive); empty highlights nothing.</summary>
        public string? Highlight
        {
            get => GetValue(HighlightProperty);
            set => SetValue(HighlightProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == LineProperty || change.Property == HighlightProperty) Rebuild();
        }

        private void Rebuild()
        {
            var inlines = new InlineCollection();
            if (Line is { } line)
            {
                foreach (var segment in LogLineSegments.Split(line.Text, line.Level, line.HasLevels, Highlight))
                {
                    var run = new Run(segment.Text) { Foreground = BrushFor(segment.Foreground) };
                    if (segment.Match) run.Background = MatchBackground;
                    inlines.Add(run);
                }
            }
            Inlines = inlines;
        }

        private static IBrush BrushFor(string hex)
        {
            lock (Brushes)
            {
                if (!Brushes.TryGetValue(hex, out var brush))
                {
                    brush = new ImmutableSolidColorBrush(Color.Parse(hex));
                    Brushes[hex] = brush;
                }
                return brush;
            }
        }
    }
}
