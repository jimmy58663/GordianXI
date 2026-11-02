// src/Gordian.Engine/Sessions/CharacterSession.Trade.cs
using System;
using Gordian.Core.World;
using Gordian.Engine.Ui;

namespace Gordian.Engine.Sessions
{
    public sealed partial class CharacterSession
    {
        /// <summary>
        /// The trade windows (#486): the main menu's and command menu's Trade resolve the target through the world, a player
        /// trade goes out as C2S 0x032 / 0x033 / 0x034 and an NPC hand-over as C2S 0x036 through the inventory module; the
        /// server's S2C 0x021 / 0x022 / 0x023 / 0x025, decoded into <see cref="InventoryState"/>, drive the windows; a zone
        /// change closes them.
        /// </summary>
        private void AttachTrade()
        {
            var menus = ActionService.Menus;
            menus.TradeTargetOf = serverId =>
            {
                WorldEntity? entity = null;
                if (serverId is { } id) World.TryGetByServerId(id, out entity);
                else entity = ActionService.CurrentTarget;
                if (entity == null) return serverId is { } unknown ? new StockUiTradeTarget(unknown, 0, string.Empty, StockUiTradeTargetKind.Player) : null;
                var kind = entity.Type switch
                {
                    EntityType.Player => entity.ServerId == LocalPlayer.ServerId ? StockUiTradeTargetKind.Self : StockUiTradeTargetKind.Player,
                    EntityType.Npc => StockUiTradeTargetKind.Npc,
                    _ => StockUiTradeTargetKind.Other,
                };
                return new StockUiTradeTarget(entity.ServerId, entity.TargetIndex, entity.Name, kind);
            };
            menus.TradeOwnName = () => CharacterName;
            menus.TradeRequest = (serverId, index) => InventoryModule.RequestTradeAsync(serverId, index);
            menus.TradeRespond = (kind, counter) => InventoryModule.RespondTradeAsync(kind, counter);
            menus.TradeOffer = (count, itemId, slot, tradeIndex) => InventoryModule.SetTradeItemAsync(count, itemId, slot, tradeIndex);
            menus.NpcTrade = (serverId, index, items) => InventoryModule.TransferNpcTradeAsync(serverId, index, items);
            menus.SystemMessageText = (id, name) =>
            {
                var lines = Messages.FormatTableMessage(id, Array.Empty<int>(), new[] { name });
                return lines == null ? null : string.Join(" ", lines).Trim();
            };
            Inventory.TradeRequested += menus.OnTradeRequested;
            Inventory.TradeResultReceived += menus.OnTradeResult;
            Inventory.TradeChanged += menus.OnTradeChanged;
            World.ZoneChanged += _ => menus.CloseTrade();
        }
    }
}
