// src/Gordian.Core/Ui/StockUiMenuController.Mounts.cs
// The mounted command menu's Dismount (#87): the retail capture (Mounts.mp4, 36-45 s) shows "Dismount?" in the comyn
// window beside the open command menu, Yes / No side by side; after Yes the menu stays open and shows the full list,
// the cursor on the same row (Abilities, where Dismount was).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Engine.Actions;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiMenuController
    {
        /// <summary>The Dismount prompt's text (retail capture).</summary>
        public const string DismountQuestion = "Dismount?";

        /// <summary>
        /// Dismount: asks <see cref="DismountQuestion"/> over the command menu (Yes by default: PROVISIONAL, the capture shows
        /// the cursor moved to No and back), then on Yes sends the dismount and shows the command menu on foot.
        /// </summary>
        private async Task ConfirmDismountAsync(StockUiMenuEntry entry)
        {
            bool yes = await PromptYesNoAsync(DismountQuestion, defaultYes: true).ConfigureAwait(false);
            if (!yes || CommandMenuTarget is not { } target) return;
            var run = TargetCommand;
            if (run == null)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available in this session.");
                return;
            }
            PlayerActionResult result;
            try
            {
                result = await run(StockUiMenuCommand.Dismount, target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NoticePosted?.Invoke($"{entry.Label} failed: {ex.Message}");
                return;
            }
            if (result.Kind is PlayerActionResultKind.Warning or PlayerActionResultKind.Error)
            {
                if (!string.IsNullOrEmpty(result.Message)) NoticePosted?.Invoke(result.Message);
                return;
            }
            RecomposeCommandMenu(target with { Mounted = false, CanDig = false });
        }

        /// <summary>
        /// Rebuilds the open command menu for <paramref name="context"/> in place, the cursor on the same row (the last one
        /// when the list is shorter). False when no command menu is the root, or its new list is empty.
        /// </summary>
        public bool RecomposeCommandMenu(in StockUiTargetContext context)
        {
            var library = _library;
            if (library == null) return false;
            var rows = StockUiCommandMenu.Compose(context);
            if (rows.Count == 0) return false;
            lock (_sync)
            {
                if (_open.Length == 0 || !_open[0].IsCommandMenu) return false;
                var old = _open[0];
                var definition = StockUiCommandMenu.Build(library, rows, out var frameImage);
                if (definition == null) return false;
                var menu = new StockUiOpenMenu(definition, null, Array.Empty<string>(), null)
                {
                    CommandRows = rows,
                    Target = context,
                    FrameImage = frameImage,
                };
                var greyed = new HashSet<int>();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Greyed) greyed.Add(i + 1);
                }
                menu.SetGreyed(greyed.Count > 0 ? greyed : null);
                menu.SelectedButtonId = Math.Clamp(old.SelectedButtonId, 1, rows.Count);
                var open = (StockUiOpenMenu[])_open.Clone();
                open[0] = menu;
                _open = open;
            }
            Changed?.Invoke();
            return true;
        }
    }
}
