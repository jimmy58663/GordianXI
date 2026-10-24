// src/Gordian.Core/Actions/PlayerActionService.EquipmentSets.cs
// Equipment sets and the style lock (#92): /equipset <n> (C2S 0x051), /lockstyleset <n> [echo] (C2S 0x053 Set) and the
// 0x053 Continue / Disable retail sends on zone-in and job change. Packet layouts referenced from XiPackets
// (https://github.com/atom0s/XiPackets), world/client 0x0051 and 0x0053, and LandSandBoat
// (https://github.com/LandSandBoat/server, src/map/packets/c2s/0x051_equipset_set.cpp, 0x053_lockstyle.cpp).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Actions
{
    /// <summary>What the client knows of the server's style lock.</summary>
    public enum StyleLockStatus
    {
        /// <summary>Not known yet (after login, until a lock command or a style lock message).</summary>
        Unknown,
        On,
        Off,
    }

    public sealed partial class PlayerActionService
    {
        /// <summary>The character's equipment sets (<see cref="EquipmentSetStore"/>); the session sets the character's book.</summary>
        public EquipmentSetBook EquipmentSets { get; set; } = new();

        private volatile StyleLockStatus _styleLock = StyleLockStatus.Unknown;

        /// <summary>
        /// The style lock as the client last learned it: from the lock commands it sent and from the server's style lock
        /// messages (S2C 0x009 <c>MsgStd</c> 267 / 269 on, 268 / 270 off).
        /// </summary>
        public StyleLockStatus StyleLock => _styleLock;

        /// <summary>The style lock messages (LandSandBoat <c>MsgStd</c> StyleLockOn 267, StyleLockOff 268, StyleLockIsOn 269, StyleLockIsOff 270).</summary>
        public const ushort StyleLockOnMessage = 267, StyleLockOffMessage = 268, StyleLockIsOnMessage = 269, StyleLockIsOffMessage = 270;

        /// <summary>A system message (S2C 0x009) arrived: the style lock messages tell whether the lock is on.</summary>
        public void NoteSystemMessage(ushort messageId)
        {
            switch (messageId)
            {
                case StyleLockOnMessage:
                case StyleLockIsOnMessage:
                    _styleLock = StyleLockStatus.On;
                    break;
                case StyleLockOffMessage:
                case StyleLockIsOffMessage:
                    _styleLock = StyleLockStatus.Off;
                    break;
            }
        }

        /// <summary>A lock command was sent: Enable and Set turn the lock on, Disable off (the server's message confirms it).</summary>
        private void NoteStyleLockSent(LockstyleMode mode)
        {
            if (mode is LockstyleMode.Enable or LockstyleMode.Set) _styleLock = StyleLockStatus.On;
            else if (mode == LockstyleMode.Disable) _styleLock = StyleLockStatus.Off;
        }

        /// <summary>
        /// A zone-in completed. Retail sends C2S 0x053 Continue while the style lock is on and Disable while it is off
        /// (XiPackets world/client/0x0053). After login the state is not known yet, so nothing is sent then (LandSandBoat
        /// keeps the lock in the character's row across logins; PROVISIONAL: what retail sends at login).
        /// </summary>
        public async Task OnZoneInAsync()
        {
            var mode = _styleLock switch
            {
                StyleLockStatus.On => LockstyleMode.Continue,
                StyleLockStatus.Off => LockstyleMode.Disable,
                _ => (LockstyleMode?)null,
            };
            if (mode is not { } m || InventoryModule is not { } module) return;
            GordianLog.Info("LOCKSTYLE", $"Zone-in: style lock {_styleLock}, sending 0x053 {m}.");
            await module.SetLockstyleAsync(m).ConfigureAwait(false);
        }

        private (JobId Main, JobId Sub)? _lastJobs;

        /// <summary>
        /// The local player's jobs as the server last reported them; a change of main or support job sends C2S 0x053 Disable,
        /// as retail does when changing jobs (XiPackets world/client/0x0053; LandSandBoat's 0x100 handler also clears the lock).
        /// The first report after login only records the jobs.
        /// </summary>
        public async Task OnJobsReportedAsync(JobId main, JobId sub)
        {
            if (main == JobId.None) return;
            var before = _lastJobs;
            _lastJobs = (main, sub);
            if (before is not { } previous || (previous.Main == main && (previous.Sub == sub || sub == JobId.None || previous.Sub == JobId.None))) return;
            _styleLock = StyleLockStatus.Off;
            if (InventoryModule is not { } module) return;
            GordianLog.Info("LOCKSTYLE", $"Job change {previous.Main}/{previous.Sub} -> {main}/{sub}: sending 0x053 Disable.");
            await module.SetLockstyleAsync(LockstyleMode.Disable).ConfigureAwait(false);
        }

        private const string EquipsetUsage = "Usage: /equipset <1-200> - Equip an equipment set (Macros > Edit Equip. Set).";
        private const string LockstyleSetUsage = "Usage: /lockstyleset [<1-200> [echo]] - Lock your appearance to an equipment set; without a number, to what you are wearing.";

        /// <summary>
        /// <c>/lockstyleset</c>: without a set number retail sends C2S 0x053 mode Enable (as <c>/lockstyle on</c>); with one,
        /// mode Set with the set's sixteen slots in slot order (empty and "Remove piece" slots with item 0, which
        /// LandSandBoat shows as the worn item), and <c>echo</c> sets Flags bit 0 (XiPackets world/client/0x0053).
        /// </summary>
        public async Task<PlayerActionResult> LockstyleSetAsync(string args)
        {
            string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return await LockstyleAsync("on").ConfigureAwait(false);
            if (!TryParseSetNumber(words[0], out int number) || words.Length > 2
                || (words.Length == 2 && !words[1].Equals("echo", StringComparison.OrdinalIgnoreCase)))
            {
                return PlayerActionResult.Warn(LockstyleSetUsage, ChatCommandResultKind.LockstyleSet);
            }
            return await LockstyleSetAsync(number, words.Length == 2).ConfigureAwait(false);
        }

        /// <summary>Locks the appearance to equipment set <paramref name="number"/> (C2S 0x053 Set).</summary>
        public async Task<PlayerActionResult> LockstyleSetAsync(int number, bool echo = false)
        {
            if (!EquipmentSetBook.IsValidNumber(number)) return PlayerActionResult.Warn(LockstyleSetUsage, ChatCommandResultKind.LockstyleSet);
            var set = EquipmentSets.Get(number);
            if (set.IsEmpty) return PlayerActionResult.Warn($"Equipment set {number} ({set.Name}) is empty.", ChatCommandResultKind.LockstyleSet);
            var module = InventoryModule;
            if (module == null) return PlayerActionResult.Fail("Style lock is unavailable: no inventory module.", ChatCommandResultKind.LockstyleSet);

            var items = LockstyleItems(set, module.State);
            GordianLog.Info("LOCKSTYLE", $"/lockstyleset {number} ({set.Name}){(echo ? " echo" : string.Empty)}: 0x053 Set.");
            await module.SetLockstyleAsync(LockstyleMode.Set, echo, items).ConfigureAwait(false);
            NoteStyleLockSent(LockstyleMode.Set);
            return PlayerActionResult.Ok(string.Empty, ChatCommandResultKind.LockstyleSet);
        }

        /// <summary>
        /// <c>/equipset &lt;n&gt;</c>: equips set <paramref name="args"/> with C2S 0x051, one entry per slot holding an item or a
        /// "Remove piece" mark (index 0, which LandSandBoat's <c>EquipItem</c> takes as unequip). Each item is looked for where
        /// the set recorded it, else anywhere it can be worn from; items not found are named in a notice and left out.
        /// </summary>
        public async Task<PlayerActionResult> EquipsetAsync(string args)
        {
            string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 1 || !TryParseSetNumber(words[0], out int number)) return PlayerActionResult.Warn(EquipsetUsage, ChatCommandResultKind.Equipset);
            return await EquipsetAsync(number).ConfigureAwait(false);
        }

        /// <summary>Equips equipment set <paramref name="number"/> (C2S 0x051).</summary>
        public async Task<PlayerActionResult> EquipsetAsync(int number)
        {
            if (!EquipmentSetBook.IsValidNumber(number)) return PlayerActionResult.Warn(EquipsetUsage, ChatCommandResultKind.Equipset);
            var set = EquipmentSets.Get(number);
            if (set.IsEmpty) return PlayerActionResult.Warn($"Equipment set {number} ({set.Name}) is empty.", ChatCommandResultKind.Equipset);
            var module = InventoryModule;
            if (module == null) return PlayerActionResult.Fail("Equipment sets are unavailable: no inventory module.", ChatCommandResultKind.Equipset);

            var (entries, missing) = EquipsetEntries(set, module.State);
            if (entries.Count > 0)
            {
                GordianLog.Info("EQUIPSET", $"/equipset {number} ({set.Name}): 0x051 with {entries.Count} slot(s).");
                await module.EquipSetAsync(entries.ToArray()).ConfigureAwait(false);
            }
            if (missing.Count > 0)
            {
                var names = new List<string>(missing.Count);
                foreach (ushort id in missing) names.Add(StockUiShop.Lookup(Menus.ItemLookup, id).Name);
                return PlayerActionResult.Warn($"Equipment set {number}: not found in your inventory or wardrobes: {string.Join(", ", names)}.", ChatCommandResultKind.Equipset);
            }
            return PlayerActionResult.Ok(string.Empty, ChatCommandResultKind.Equipset);
        }

        private static bool TryParseSetNumber(string word, out int number) =>
            int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out number) && EquipmentSetBook.IsValidNumber(number);

        /// <summary>
        /// Where a set's item is now: where the set recorded it when the same item is still there, else the first such item
        /// in a container equipment is worn from that no other slot of the set has taken. Null when none is held.
        /// </summary>
        public static (ContainerId Container, byte Index)? LocatePiece(EquipmentSetPiece piece, InventoryState? inventory, ICollection<(ContainerId, byte)>? taken = null)
        {
            if (!piece.HasItem || inventory == null) return null;
            if (inventory.GetContainer(piece.Container).TryGetItem(piece.Index, out var at) && at.ItemId == piece.ItemId
                && taken?.Contains((piece.Container, piece.Index)) != true)
            {
                return (piece.Container, piece.Index);
            }
            foreach (var container in StockUiItems.EquippableContainers)
            {
                foreach (var item in inventory.SnapshotItems(container))
                {
                    if (item.ItemId == piece.ItemId && taken?.Contains((container, item.Slot)) != true) return (container, item.Slot);
                }
            }
            return null;
        }

        /// <summary>The C2S 0x051 entries for a set, and the ids of the items not found.</summary>
        public static (List<(byte Slot, EquipSlotId EquipSlot, ContainerId Container)> Entries, List<ushort> Missing) EquipsetEntries(EquipmentSet set, InventoryState? inventory)
        {
            var entries = new List<(byte, EquipSlotId, ContainerId)>();
            var missing = new List<ushort>();
            var taken = new HashSet<(ContainerId, byte)>();
            for (int i = 0; i < EquipmentSet.SlotCount; i++)
            {
                var piece = set.Pieces[i];
                var slot = (EquipSlotId)i;
                if (piece.Kind == EquipmentSetPieceKind.Remove)
                {
                    entries.Add((0, slot, ContainerId.Inventory));
                }
                else if (piece.HasItem)
                {
                    if (LocatePiece(piece, inventory, taken) is { } at)
                    {
                        taken.Add(at);
                        entries.Add((at.Index, slot, at.Container));
                    }
                    else
                    {
                        missing.Add(piece.ItemId);
                    }
                }
            }
            return (entries, missing);
        }

        /// <summary>
        /// The C2S 0x053 Set items for a set: all sixteen slots in slot order (LandSandBoat applies each entry to its
        /// <c>EquipKind</c> and checks ownership by item id, so an item not found is still sent with index 0).
        /// </summary>
        public static (byte Slot, EquipSlotId EquipSlot, ContainerId Container, ushort ItemId)[] LockstyleItems(EquipmentSet set, InventoryState? inventory)
        {
            var items = new (byte, EquipSlotId, ContainerId, ushort)[EquipmentSet.SlotCount];
            var taken = new HashSet<(ContainerId, byte)>();
            for (int i = 0; i < EquipmentSet.SlotCount; i++)
            {
                var piece = set.Pieces[i];
                var slot = (EquipSlotId)i;
                if (!piece.HasItem)
                {
                    items[i] = (0, slot, ContainerId.Inventory, 0);
                    continue;
                }
                if (LocatePiece(piece, inventory, taken) is { } at)
                {
                    taken.Add(at);
                    items[i] = (at.Index, slot, at.Container, piece.ItemId);
                }
                else
                {
                    items[i] = (0, slot, ContainerId.Inventory, piece.ItemId);
                }
            }
            return items;
        }
    }
}
