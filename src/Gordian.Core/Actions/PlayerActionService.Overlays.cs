// src/Gordian.Core/Actions/PlayerActionService.Overlays.cs
// Phase 5E Tier 3: the stock UI visibility API (#29, /stockui) and the hook for the viewport's performance overlay (#30, /perf).

using System;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Network;
using Gordian.Core.Ui;

namespace Gordian.Core.Actions
{
    public sealed partial class PlayerActionService
    {
        /// <summary>
        /// The stock HUD elements hidden at run time, by owner (#29): addons and ImGui replacements hide the stock element
        /// they replace, the player hides elements with <c>/stockui</c>. Read by the HUD every frame.
        /// </summary>
        public StockUiVisibilityState UiVisibility { get; } = new();

        /// <summary>
        /// Handles <c>/perf [off | fps | full | next]</c> and returns the reply; set by the client's viewport (the overlay is
        /// drawn there), null without one.
        /// </summary>
        public Func<string, string>? PerformanceOverlayCommand { get; set; }

        internal const string StockUiUsage =
            "Usage: /stockui [hide | show <element ...|all>] | reset. Elements: target, vitals, party, alliance, buffs, menus, chat, names, cursor. " +
            "Hides stock HUD elements until the client restarts (an addon's hides are its own; /uilayout <window> hide is saved per character).";

        internal const string PerformanceOverlayUsage =
            "Usage: /perf [off | fps | full | next] - Viewport performance overlay: hidden, FPS only, or frame graph, draw calls and pass timings (Ctrl+F12 cycles).";

        /// <summary>
        /// Handles <c>/stockui</c>: hides or shows stock HUD elements for the player (owner <see cref="StockUiVisibilityState.UserOwner"/>);
        /// no arguments lists what is hidden and by whom.
        /// </summary>
        public PlayerActionResult ApplyStockUiCommand(string args)
        {
            const ChatCommandResultKind Kind = ChatCommandResultKind.StockUiVisibility;
            var state = UiVisibility;
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return PlayerActionResult.Info(DescribeStockUiVisibility(state), Kind);

            string verb = parts[0].ToLowerInvariant();
            if (verb == "reset" && parts.Length == 1)
            {
                state.Release(StockUiVisibilityState.UserOwner);
                return PlayerActionResult.Ok("Stock HUD: your hidden elements are shown again. " + DescribeOthers(state), Kind);
            }
            if (verb is not ("hide" or "show") || parts.Length < 2) return PlayerActionResult.Warn(StockUiUsage, Kind);

            var elements = StockUiElement.None;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!StockUiVisibilityState.TryParse(parts[i], out var element))
                {
                    return PlayerActionResult.Warn($"Unknown stock HUD element '{parts[i]}'. {StockUiUsage}", Kind);
                }
                elements |= element;
            }
            if (verb == "hide")
            {
                state.Hide(StockUiVisibilityState.UserOwner, elements);
                return PlayerActionResult.Ok($"Stock HUD hidden: {StockUiVisibilityState.Describe(state.Hidden)}.", Kind);
            }
            state.Show(StockUiVisibilityState.UserOwner, elements);
            var still = state.Hidden & elements;
            return still == StockUiElement.None
                ? PlayerActionResult.Ok($"Stock HUD shown: {StockUiVisibilityState.Describe(elements)}.", Kind)
                : PlayerActionResult.Warn($"Still hidden by an addon: {StockUiVisibilityState.Describe(still)}. {DescribeOthers(state)}", Kind);
        }

        private static string DescribeStockUiVisibility(StockUiVisibilityState state)
        {
            var owners = state.Owners;
            if (owners.Count == 0) return "Stock HUD: every element shown. " + StockUiUsage;
            var sb = new StringBuilder("Stock HUD hidden: ").Append(StockUiVisibilityState.Describe(state.Hidden)).Append('.');
            foreach (var (owner, set) in owners) sb.Append(' ').Append(owner).Append(": ").Append(StockUiVisibilityState.Describe(set)).Append('.');
            return sb.ToString();
        }

        private static string DescribeOthers(StockUiVisibilityState state)
        {
            var sb = new StringBuilder();
            foreach (var (owner, set) in state.Owners)
            {
                if (owner.Equals(StockUiVisibilityState.UserOwner, StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append(owner).Append(" hides ").Append(StockUiVisibilityState.Describe(set)).Append(". ");
            }
            return sb.ToString().TrimEnd();
        }

        private PlayerActionResult PerformanceOverlayCommandResult(string args)
        {
            var handler = PerformanceOverlayCommand;
            return handler is null
                ? PlayerActionResult.Warn("The performance overlay needs a viewport window.", ChatCommandResultKind.PerformanceOverlay)
                : PlayerActionResult.Info(handler(args), ChatCommandResultKind.PerformanceOverlay);
        }
    }
}
