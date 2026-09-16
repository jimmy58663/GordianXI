// src/Gordian.App/Common/AutoCopyBehavior.cs
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Common
{
    /// <summary>
    /// Attached behavior providing SSH terminal emulator-style "copy-on-select" functionality.
    /// Whenever text is highlighted in a <see cref="SelectableTextBlock"/> (via mouse drag, double-click,
    /// or keyboard selection), it is automatically copied to the system clipboard without requiring Ctrl+C.
    /// </summary>
    public static class AutoCopyBehavior
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "IsEnabled",
                typeof(AutoCopyBehavior),
                defaultValue: false);

        /// <summary>
        /// Test hook invoked whenever text is auto-copied to the clipboard.
        /// </summary>
        public static Action<string>? OnCopiedForTesting { get; set; }

        static AutoCopyBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        }

        public static bool GetIsEnabled(Control element)
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(Control element, bool value)
        {
            ArgumentNullException.ThrowIfNull(element);
            element.SetValue(IsEnabledProperty, value);
        }

        public static void Attach(Control element)
        {
            SetIsEnabled(element, true);
        }

        private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.NewValue is true)
            {
                control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble | RoutingStrategies.Tunnel, handledEventsToo: true);
                control.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble | RoutingStrategies.Tunnel, handledEventsToo: true);
            }
            else
            {
                control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
                control.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            }
        }

        private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var stb = (e.Source as SelectableTextBlock) ?? (e.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>();
            if (stb != null)
            {
                TryCopySelectedText(stb, sender as Visual);
            }
        }

        private static void OnKeyUp(object? sender, KeyEventArgs e)
        {
            var stb = (e.Source as SelectableTextBlock) ?? (e.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>();
            if (stb != null)
            {
                TryCopySelectedText(stb, sender as Visual);
            }
        }

        /// <summary>
        /// Copies the non-empty selected text from a <see cref="SelectableTextBlock"/> to the clipboard.
        /// If no text is selected, the clipboard is left untouched.
        /// </summary>
        public static bool TryCopySelectedText(SelectableTextBlock stb, Visual? fallbackVisual = null)
        {
            ArgumentNullException.ThrowIfNull(stb);
            string? text = stb.SelectedText;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            try
            {
                stb.Copy();
                OnCopiedForTesting?.Invoke(text);
                GordianLog.Debug("CLIPBOARD", $"Auto-copied {text.Length} characters to clipboard: '{text}'");
                return true;
            }
            catch (Exception ex)
            {
                GordianLog.Debug("CLIPBOARD", $"Auto-copy failed: {ex.Message}");
                return false;
            }
        }
    }
}
