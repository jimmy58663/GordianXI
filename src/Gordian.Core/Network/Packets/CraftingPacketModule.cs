// src/Gordian.Core/Network/Packets/CraftingPacketModule.cs
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module for crafting: S2C 0x06F / 0x070 (synthesis results) and 0x031 (recipes) fill
    /// <see cref="CraftingState"/>; C2S 0x096 starts a synthesis and C2S 0x058 asks a guild NPC for recipes.
    /// The synthesis animation (S2C 0x030) is handled by <see cref="CombatPacketModule"/>.
    /// </summary>
    public sealed class CraftingPacketModule
    {
        private readonly CraftingState _state;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public CraftingState State => _state;
        public bool LogOutboundOnRoute { get; set; } = true;

        public CraftingPacketModule(
            CraftingState state,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x031_Recipe.PacketId, HandleRecipe);
            dispatcher.Register(S2C_0x06F_CombineAns.PacketId, HandleCombineAns);
            dispatcher.Register(S2C_0x070_CombineInf.PacketId, HandleCombineInf);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x031_Recipe.PacketId);
            dispatcher.Unregister(S2C_0x06F_CombineAns.PacketId);
            dispatcher.Unregister(S2C_0x070_CombineInf.PacketId);
        }

        #region Inbound

        private void HandleCombineAns(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x06F_CombineAns(payload);
            if (!p.IsValid) return;

            var lost = new ushort[S2C_0x06F_CombineAns.ArraySlots];
            var materials = new ushort[S2C_0x06F_CombineAns.ArraySlots];
            for (int i = 0; i < lost.Length; i++)
            {
                lost[i] = p.GetBreakNo(i);
                materials[i] = p.GetMaterialNo(i);
            }

            // UpKind lists the skills used; an entry only counts as a skill-up when its UpLevel is above zero.
            var ups = new List<SynthesisSkillUp>(S2C_0x06F_CombineAns.SkillSlots);
            for (int i = 0; i < S2C_0x06F_CombineAns.SkillSlots; i++)
            {
                if (p.GetUpLevel(i) > 0) ups.Add(new SynthesisSkillUp(p.GetUpKind(i), p.GetUpLevel(i)));
            }

            var outcome = new SynthesisOutcome(p.Result, p.ResultId, p.Grade, p.Count, p.ItemId, p.CrystalNo, lost, materials, ups.ToArray());
            GordianLog.Info("CRAFT", $"Synthesis result: {p.Result} (0x{p.ResultId:X2}), grade={p.Grade}, item={p.ItemId} x{p.Count}, crystal={p.CrystalNo}, skillups={ups.Count}");
            _state.SetOutcome(outcome);
        }

        private void HandleCombineInf(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x070_CombineInf(payload);
            if (!p.IsValid) return;

            var lost = new ushort[S2C_0x06F_CombineAns.ArraySlots];
            for (int i = 0; i < lost.Length; i++) lost[i] = p.GetBreakNo(i);

            var outcome = new OtherSynthesisOutcome(p.Result, p.ResultId, p.Grade, p.Count, p.ItemId, lost, p.UniqueNo, p.ActIndex, p.Name);
            GordianLog.Debug("CRAFT", $"Synthesis result for {p.Name}: {p.Result} (0x{p.ResultId:X2}), grade={p.Grade}, item={p.ItemId} x{p.Count}");
            _state.SetOtherOutcome(outcome);
        }

        private void HandleRecipe(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x031_Recipe(payload);
            if (!p.IsValid) return;

            switch (p.Kind)
            {
                case RecipeKind.Detail1:
                case RecipeKind.Detail2:
                {
                    var skills = new ushort[3];
                    for (int i = 0; i < skills.Length; i++) skills[i] = p.GetNeedSkill(i);
                    var items = new ushort[S2C_0x031_Recipe.IngredientSlots];
                    var counts = new ushort[S2C_0x031_Recipe.IngredientSlots];
                    for (int i = 0; i < items.Length; i++)
                    {
                        items[i] = p.GetIngredientItem(i);
                        counts[i] = p.GetIngredientCount(i);
                    }
                    GordianLog.Debug("CRAFT", $"Recipe ({p.Kind}): product={p.ProductItem}, crystal={p.NeedItem}, key item={p.NeedKeyItem}");
                    _state.SetRecipe(new RecipeDetail(p.Kind, p.ProductItem, skills, p.NeedItem, p.NeedKeyItem, items, counts));
                    break;
                }
                case RecipeKind.List:
                {
                    var list = new ushort[S2C_0x031_Recipe.ListSlots];
                    int used = 0;
                    for (int i = 0; i < list.Length; i++)
                    {
                        list[i] = p.GetListItem(i);
                        if (list[i] != 0) used = i + 1;
                    }
                    Array.Resize(ref list, used);
                    GordianLog.Debug("CRAFT", $"Recipe list: {used} items, next page starts at {p.ListNextItem}");
                    _state.SetRecipeList(new RecipeListPage(list, p.ListNextItem));
                    break;
                }
                default:
                    GordianLog.Debug("CRAFT", $"Recipe packet of unhandled type {(ushort)p.Kind}");
                    break;
            }
        }

        #endregion

        #region Outbound

        private ushort NextSequence() => unchecked(++_sequenceNumber);

        private async Task SendAsync(ushort packetId, byte[] buf, int len)
        {
            if (len == 0) return;
            if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, packetId, _sequenceNumber, buf.AsSpan(0, len));
            await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends C2S 0x096: synthesizes with the crystal at <paramref name="crystalSlot"/> and up to 8 ingredient items,
        /// each an (item id, inventory slot) pair; an item taken several times from a stack appears once per item.
        /// The array is sorted by item id in place. Returns false without sending when the ingredients are empty or
        /// more than 8.
        /// </summary>
        public async Task<bool> SynthesizeAsync(ushort crystalItemId, byte crystalSlot, (ushort ItemId, byte Slot)[] ingredients)
        {
            ArgumentNullException.ThrowIfNull(ingredients);
            if (ingredients.Length is 0 or > CraftingPacketBuilders.MaxIngredients) return false;

            byte[] buf = ArrayPool<byte>.Shared.Rent(CraftingPacketBuilders.CombineAskLength);
            try
            {
                int len = CraftingPacketBuilders.BuildCombineAsk(buf, NextSequence(), crystalItemId, crystalSlot, ingredients);
                if (len > 0) _state.BeginSynthesis();
                await SendAsync(0x096, buf, len).ConfigureAwait(false);
                return len > 0;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        /// <summary>Sends C2S 0x058: asks a crafting guild NPC for a recipe or recipe list. The answer is S2C 0x031.</summary>
        public async Task RequestRecipeAsync(ushort skill, ushort level, RecipeRequestMode mode,
            ushort param0 = 0, ushort param1 = 0, ushort param2 = 0, ushort param3 = 0, ushort param4 = 0)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(CraftingPacketBuilders.RecipeLength);
            try
            {
                int len = CraftingPacketBuilders.BuildRecipe(buf, NextSequence(), skill, level, mode, param0, param1, param2, param3, param4);
                await SendAsync(0x058, buf, len).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        #endregion
    }
}
