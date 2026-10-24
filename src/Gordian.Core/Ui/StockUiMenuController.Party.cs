// src/Gordian.Core/Ui/StockUiMenuController.Party.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiMenuController
    {
        /// <summary>The character's party state for the main menu's Party entry (<see cref="StockUiParty"/>); unset = not in a party.</summary>
        public Func<StockUiPartyContext>? PartyContext { get; set; }

        /// <summary>
        /// Runs the Party menu's Disband once confirmed: <c>true</c> when the character leads the party (breaks it up,
        /// C2S 0x070), <c>false</c> for a member (leaves it, C2S 0x06F).
        /// </summary>
        public Func<bool, Task>? PartyDisbandRequested { get; set; }

        /// <summary>The prompts before Disband. <b>Provisional:</b> whether retail asks, and its wording, is not captured.</summary>
        public const string DisbandPartyPrompt = "Disband party?", LeavePartyPrompt = "Leave party?";

        private StockUiPartyContext CurrentParty => PartyContext?.Invoke() ?? default;

        /// <summary>Opens the Party list for the character's state beside the main menu. Call under the lock.</summary>
        private void OpenPartyMenu(StockUiOpenMenu from)
        {
            Push(StockUiParty.MenuFor(CurrentParty), from, Array.Empty<string>(), null, null);
        }

        /// <summary>Greys the entries a leader cannot use yet (<see cref="StockUiParty.GreyedButtons"/>).</summary>
        private void RefreshPartyMenu(StockUiOpenMenu menu) => menu.SetGreyed(StockUiParty.GreyedButtons(menu.Name, CurrentParty));

        private async Task ConfirmPartyDisbandAsync(bool leader)
        {
            if (!await PromptYesNoAsync(leader ? DisbandPartyPrompt : LeavePartyPrompt, defaultYes: false).ConfigureAwait(false)) return;
            CloseAll();
            try
            {
                if (PartyDisbandRequested != null) await PartyDisbandRequested(leader).ConfigureAwait(false);
                else NoticePosted?.Invoke("Disband is not available in this session.");
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Party disband failed: {ex.Message}");
                NoticePosted?.Invoke($"Disband failed: {ex.Message}");
            }
        }
    }
}
