// src/Gordian.Core/Network/CharacterSession.Synthesis.cs
using Gordian.Core.World;

namespace Gordian.Core.Network
{
    public sealed partial class CharacterSession
    {
        /// <summary>
        /// The Synthesis windows and the guild shop window (#330): the grid sends C2S 0x096 through the crafting module and is
        /// refused while a synthesis runs (<see cref="CraftingState.IsSynthesizing"/>); a synthesis that ran (S2C 0x06F: a success or a break) is
        /// recorded in the character's History (<see cref="SynthesisHistoryStore"/>). An open guild (S2C 0x086) opens the guild
        /// shop window, whose lists and purchases go out as C2S 0x0AA-0x0AD; a zone change closes it.
        /// </summary>
        private void AttachSynthesisAndGuildShop()
        {
            var menus = ActionService.Menus;
            var history = SynthesisHistoryStore.GetForCharacter(CharacterName);
            menus.SynthesisHistory = () => history;
            menus.IsSynthesizing = () => Crafting.IsSynthesizing;
            menus.SynthesisRequested = (crystal, slot, ingredients) => CraftingModule.SynthesizeAsync(crystal, slot, ingredients);
            Crafting.SynthesisCompleted += outcome =>
            {
                // Successes and breaks (crystal, or crystal and materials, lost) go into the History; cancels did not synthesize.
                bool ran = outcome.IsSuccess || outcome.Result is Packets.SynthesisAnswer.Failed or Packets.SynthesisAnswer.Interrupted
                    or Packets.SynthesisAnswer.InterruptedCritical;
                if (ran && Crafting.LastRequest is { } request) history.Record(request, outcome.IsSuccess ? outcome.ItemId : (ushort)0);
            };

            menus.GuildRequestBuyList = () => InventoryModule.RequestGuildBuyListAsync();
            menus.GuildRequestSellList = () => InventoryModule.RequestGuildSellListAsync();
            menus.GuildBuy = (item, count) => InventoryModule.BuyGuildItemAsync(item, count);
            menus.GuildSell = (item, slot, count) => InventoryModule.SellGuildItemAsync(item, slot, count);
            Inventory.GuildStatusReceived += (status, _) => menus.OnGuildStatus(status);
            Inventory.GuildListCompleted += (_, _) => menus.OnGuildListChanged();
            Inventory.GuildTransactionReceived += _ => menus.OnGuildTransaction();
            World.ZoneChanged += _ => menus.CloseGuildShop();
        }
    }
}
