// src/Gordian.Core/Network/CharacterSession.KeyItemsAndEquipmentSets.cs
using System;
using Gordian.Core.World;

namespace Gordian.Core.Network
{
    public sealed partial class CharacterSession
    {
        /// <summary>
        /// The Key Items window (#91) and the equipment sets with the style lock (#92): the key item tables (S2C 0x055)
        /// feed the window, a new key item viewed there is marked read (C2S 0x064); the character's sets are loaded, the
        /// Equipment Sets windows equip (0x051), lock (0x053 Set) and check (0x052 / S2C 0x116) them; the style lock's state
        /// follows the server's messages and goes out again on zone-in (0x053 Continue / Disable) and job change (Disable).
        /// </summary>
        private void AttachKeyItemsAndEquipmentSets()
        {
            var menus = ActionService.Menus;

            // Key items.
            menus.KeyItemHeld = id => Progression.HasKeyItem(id);
            menus.KeyItemSeen = id => Progression.IsKeyItemSeen(id);
            menus.KeyItemViewed = id => _ = ActionService.MarkKeyItemSeenAsync(id);
            Progression.KeyItemsUpdated += menus.RefreshKeyItems;

            // Equipment sets.
            var book = EquipmentSetStore.GetForCharacter(CharacterName);
            ActionService.EquipmentSets = book;
            menus.EquipSets = () => ActionService.EquipmentSets;
            menus.EquipSetEquip = number => ActionService.EquipsetAsync(number);
            menus.EquipSetStyleLock = number => ActionService.LockstyleSetAsync(number);
            menus.EquipSetCheck = (slot, change, rest) => InventoryModule.CheckEquipsetAsync(slot, change, rest);
            menus.EquipSetValidation = Inventory.SnapshotEquipsetValidation;
            Inventory.EquipsetChanged += () => menus.OnEquipSetValidated(Inventory.SnapshotEquipsetValidation());
            Chat.ImportEquipmentSets = folder => Ui.RetailEquipSets.Import(folder, ActionService.EquipmentSets);

            // S2C 0x009 messages the client's short list does not know print from the system message table (MsgStd ids), the
            // style lock's "Style lock mode enabled." among them (round 1: they printed as "Msg#267").
            Gordian.Core.Ui.StandardMessages.TableText = (id, parameters) =>
            {
                int[] numbers = Gordian.Core.Ui.StandardMessages.TryGetNumbers(parameters, out uint first, out uint second)
                    ? new[] { unchecked((int)first), unchecked((int)second) }
                    : Array.Empty<int>();
                var lines = Messages.FormatTableMessage(id, numbers);
                return lines == null ? null : string.Join(" ", lines).Trim();
            };

            // The style lock: the server's on / off messages, zone-ins and job changes.
            ChatModule.SystemMessageReceived += message => ActionService.NoteSystemMessage(message.MessageId);
            NetworkManager.Parser.HandshakeCompleted += () => _ = ActionService.OnZoneInAsync();
            LocalPlayer.StatsUpdated += () => _ = ActionService.OnJobsReportedAsync(LocalPlayer.MainJob, LocalPlayer.SubJob);
        }
    }
}
