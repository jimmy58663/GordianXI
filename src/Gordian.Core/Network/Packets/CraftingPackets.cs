// src/Gordian.Core/Network/Packets/CraftingPackets.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The result id of a synthesis in S2C 0x06F (<c>Result</c>) and 0x070.
    /// Protocol specification referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x006F)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/synthesis_result.h).
    /// Any other id is treated as a failure that lost the crystal (retail's own rule).
    /// </summary>
    public enum SynthesisAnswer : byte
    {
        /// <summary>Successful synthesis; shows the item information sub-window.</summary>
        Success = 0x00,
        /// <summary>Synthesis failed. You lost the crystal you were using.</summary>
        Failed = 0x01,
        /// <summary>Synthesis interrupted. You lost the crystal and materials you were using.</summary>
        Interrupted = 0x02,
        /// <summary>Synthesis canceled. That combination of materials cannot be synthesized.</summary>
        CancelBadRecipe = 0x03,
        /// <summary>Synthesis canceled.</summary>
        Cancel = 0x04,
        /// <summary>Synthesis canceled. That formula is beyond your current craft skill level.</summary>
        CancelSkillTooLow = 0x06,
        /// <summary>Synthesis canceled. You cannot hold more than one item of that type.</summary>
        CancelRareItem = 0x07,
        /// <summary>Successful synthesis (LSB: desynthesis); shows the item information sub-window.</summary>
        SuccessDesynth = 0x0C,
        /// <summary>You must wait longer before repeating that action.</summary>
        MustWaitLonger = 0x0D,
        /// <summary>Synthesis interrupted. You lost the crystal and materials you were using (critical failure).</summary>
        InterruptedCritical = 0x0E
    }

    /// <summary>The <c>Type</c> of S2C 0x031 and the mode of C2S 0x058.</summary>
    public enum RecipeKind : ushort
    {
        /// <summary>One recipe of the character's level (LSB: a random one).</summary>
        Detail1 = 1,
        /// <summary>The list of recipes for a craft rank.</summary>
        List = 2,
        /// <summary>A recipe picked from the list, at an offset.</summary>
        Detail2 = 3,
        /// <summary>Unknown; LSB never sends it.</summary>
        Unknown = 4
    }

    /// <summary>The <c>Mode</c> of C2S 0x058.</summary>
    public enum RecipeRequestMode : ushort
    {
        /// <summary>Request the available rank list (the answer is a Detail1 recipe in LSB).</summary>
        RankList = 1,
        /// <summary>Request the recipe list of a rank (<c>Param1</c>/<c>Param2</c> page offsets, <c>Param4</c> rank).</summary>
        RecipeList = 2,
        /// <summary>Request the materials of one recipe (<c>Param3</c> index in the list, <c>Param4</c> rank).</summary>
        RecipeMaterials = 3,
        /// <summary>Campaign Operations: recipe from the Adjutant.</summary>
        CampaignRecipe = 4,
        /// <summary>Campaign Operations: material list completed.</summary>
        CampaignComplete = 5
    }

    #region S2C

    /// <summary>
    /// S2C 0x06F <c>COMBINE_ANS</c>: the result of the character's own synthesis (item, count, lost items, skill-ups).
    /// Protocol specification referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x006F)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x06f_combine_ans.h).
    /// </summary>
    public readonly ref struct S2C_0x06F_CombineAns
    {
        public const ushort PacketId = 0x06F;
        public const int ArraySlots = 8;
        public const int SkillSlots = 4;
        private const int MinPayload = 48;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        /// <summary>The raw result id; see <see cref="SynthesisAnswer"/>.</summary>
        public byte ResultId => IsValid ? _payload[0] : (byte)0;
        public SynthesisAnswer Result => (SynthesisAnswer)ResultId;

        /// <summary>
        /// The grade, which is also the message index (retail adds 160): 0 to 3 "You synthesized ...", 4 "... was lost",
        /// -1 (0xFF) on a failure.
        /// </summary>
        public sbyte Grade => IsValid ? (sbyte)_payload[1] : (sbyte)-1;

        /// <summary>The count of items made, generally 1.</summary>
        public byte Count => IsValid ? _payload[2] : (byte)0;

        /// <summary>The item made; 29695 (Mangled Mess) when the synthesis failed and lost items.</summary>
        public ushort ItemId => IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(4, 2)) : (ushort)0;

        /// <summary>The item ids of the ingredients that were lost, aligned to the request's slots (0 for none).</summary>
        public ushort GetBreakNo(int index) => IsValid && (uint)index < ArraySlots
            ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(6 + (index * 2), 2)) : (ushort)0;

        /// <summary>The craft skill ids that were used (49 to 56); when nothing skilled up, the first is the main craft.</summary>
        public sbyte GetUpKind(int index) => IsValid && (uint)index < SkillSlots ? (sbyte)_payload[22 + index] : (sbyte)0;

        /// <summary>The skill-up of the skill at <paramref name="index"/> in tenths (1 is 0.1).</summary>
        public sbyte GetUpLevel(int index) => IsValid && (uint)index < SkillSlots ? (sbyte)_payload[26 + index] : (sbyte)0;

        /// <summary>The crystal that was used.</summary>
        public ushort CrystalNo => IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(30, 2)) : (ushort)0;

        /// <summary>The materials that were used, in the request's slot order.</summary>
        public ushort GetMaterialNo(int index) => IsValid && (uint)index < ArraySlots
            ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(32 + (index * 2), 2)) : (ushort)0;

        public S2C_0x06F_CombineAns(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= MinPayload;
        }
    }

    /// <summary>
    /// S2C 0x070 <c>COMBINE_INF</c>: the result of another character's synthesis, shown to those nearby.
    /// Protocol specification referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0070)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x070_combine_inf.h).
    /// </summary>
    public readonly ref struct S2C_0x070_CombineInf
    {
        public const ushort PacketId = 0x070;
        public const int NameLength = 16;
        private const int MinPayload = 42;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        public byte ResultId => IsValid ? _payload[0] : (byte)0;
        public SynthesisAnswer Result => (SynthesisAnswer)ResultId;

        /// <summary>The grade; retail adds 200 for the message index (0 to 3 "synthesized", 4 "lost", -1 on a failure).</summary>
        public sbyte Grade => IsValid ? (sbyte)_payload[1] : (sbyte)-1;
        public byte Count => IsValid ? _payload[2] : (byte)0;
        public ushort ItemId => IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(4, 2)) : (ushort)0;

        public ushort GetBreakNo(int index) => IsValid && (uint)index < S2C_0x06F_CombineAns.ArraySlots
            ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(6 + (index * 2), 2)) : (ushort)0;

        /// <summary>The low 16 bits of the crafter's server id. The retail client ignores it.</summary>
        public ushort UniqueNo => IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(22, 2)) : (ushort)0;

        /// <summary>The crafter's entity index. The retail client ignores it.</summary>
        public ushort ActIndex => IsValid ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(24, 2)) : (ushort)0;

        /// <summary>The raw 16 name bytes; retail names the crafter from this alone.</summary>
        public ReadOnlySpan<byte> NameBytes => IsValid ? _payload.Slice(26, NameLength) : ReadOnlySpan<byte>.Empty;

        /// <summary>The crafter's name (ASCII, up to the first NUL).</summary>
        public string Name => IsValid ? TreasurePacketText.ReadName(NameBytes) : string.Empty;

        public S2C_0x070_CombineInf(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= MinPayload;
        }
    }

    /// <summary>
    /// S2C 0x031 <c>RECIPE</c>: a recipe (<see cref="RecipeKind.Detail1"/>, <see cref="RecipeKind.Detail2"/>) or a recipe
    /// list (<see cref="RecipeKind.List"/>) from a guild NPC, the answer to C2S 0x058.
    /// Protocol specification referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0031)
    /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x031_recipe.h).
    /// <para>
    /// <b>Differs from XiPackets:</b> its generic layout (<c>Data[40]</c>, then <c>Type</c> at 0x2C) disagrees with its own
    /// per-mode layouts. Those, and LandSandBoat, put <c>Type</c> at packet offset 0x30 (payload 44), which is used here.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x031_Recipe
    {
        public const ushort PacketId = 0x031;
        public const int IngredientSlots = 8;
        public const int ListSlots = 16;
        private const int MinPayload = 48;
        private const int TypeOffset = 44;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        public RecipeKind Kind => IsValid ? (RecipeKind)BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(TypeOffset, 2)) : 0;

        private bool IsDetail => Kind is RecipeKind.Detail1 or RecipeKind.Detail2;

        /// <summary>Detail kinds: the item the recipe makes.</summary>
        public ushort ProductItem => IsDetail ? U16(0) : (ushort)0;

        /// <summary>Detail kinds: the sub-craft (1 to 8) required in <paramref name="index"/> 0 to 2, or 0 when none.</summary>
        public ushort GetNeedSkill(int index) => IsDetail && (uint)index < 3 ? U16(2 + (index * 2)) : (ushort)0;

        /// <summary>Detail kinds: the crystal item id.</summary>
        public ushort NeedItem => IsDetail ? U16(8) : (ushort)0;

        /// <summary>Detail kinds: the key item id required, or 0.</summary>
        public ushort NeedKeyItem => IsDetail ? U16(10) : (ushort)0;

        /// <summary>Detail kinds: the ingredient item id at <paramref name="index"/> (0 to 7).</summary>
        public ushort GetIngredientItem(int index) => IsDetail && (uint)index < IngredientSlots ? U16(12 + (index * 2)) : (ushort)0;

        /// <summary>Detail kinds: how many of the ingredient at <paramref name="index"/> are needed.</summary>
        public ushort GetIngredientCount(int index) => IsDetail && (uint)index < IngredientSlots ? U16(28 + (index * 2)) : (ushort)0;

        /// <summary>List kind: the crafted item id at <paramref name="index"/> (0 to 15), 0 for an unused entry.</summary>
        public ushort GetListItem(int index) => Kind == RecipeKind.List && (uint)index < ListSlots ? U16(12 + (index * 2)) : (ushort)0;

        /// <summary>List kind: the first item of the next page, which makes the menu offer "View more recipes."; 0 when none.</summary>
        public ushort ListNextItem => Kind == RecipeKind.List ? U16(46) : (ushort)0;

        private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(offset, 2));

        public S2C_0x031_Recipe(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= MinPayload;
        }
    }

    #endregion

    #region Managed models

    /// <summary>One skill-up from a synthesis: a craft skill id (49 to 56) and the gain in tenths.</summary>
    public readonly record struct SynthesisSkillUp(sbyte SkillId, sbyte Tenths);

    /// <summary>The decoded result of the character's own synthesis (S2C 0x06F).</summary>
    public sealed record SynthesisOutcome(
        SynthesisAnswer Result,
        byte ResultId,
        sbyte Grade,
        byte Count,
        ushort ItemId,
        ushort CrystalId,
        ushort[] LostItemIds,
        ushort[] MaterialIds,
        SynthesisSkillUp[] SkillUps)
    {
        /// <summary>True for the two successful results (0x00 and 0x0C).</summary>
        public bool IsSuccess => Result is SynthesisAnswer.Success or SynthesisAnswer.SuccessDesynth;

        /// <summary>True when the synthesis was refused or canceled before it started, so nothing was lost.</summary>
        public bool WasCanceled => Result is SynthesisAnswer.CancelBadRecipe or SynthesisAnswer.Cancel
            or SynthesisAnswer.CancelSkillTooLow or SynthesisAnswer.CancelRareItem or SynthesisAnswer.MustWaitLonger;
    }

    /// <summary>The decoded result of another character's synthesis (S2C 0x070).</summary>
    public sealed record OtherSynthesisOutcome(
        SynthesisAnswer Result,
        byte ResultId,
        sbyte Grade,
        byte Count,
        ushort ItemId,
        ushort[] LostItemIds,
        ushort ServerIdLow,
        ushort TargetIndex,
        string Name)
    {
        public bool IsSuccess => Result is SynthesisAnswer.Success or SynthesisAnswer.SuccessDesynth;
    }

    /// <summary>A recipe from S2C 0x031 (kinds 1 and 3).</summary>
    public sealed record RecipeDetail(
        RecipeKind Kind,
        ushort ProductItem,
        ushort[] SubCraftSkills,
        ushort CrystalItem,
        ushort KeyItem,
        ushort[] IngredientItems,
        ushort[] IngredientCounts);

    /// <summary>A page of crafted item ids from S2C 0x031 (kind 2).</summary>
    public sealed record RecipeListPage(ushort[] ItemIds, ushort NextPageItem);

    #endregion

    #region C2S builders

    /// <summary>Builders for the synthesis and recipe requests.</summary>
    public static class CraftingPacketBuilders
    {
        public const int MaxIngredients = 8;
        public const int CombineAskLength = 36;
        public const int RecipeLength = 20;

        /// <summary>
        /// The <c>HashNo</c> of C2S 0x096: the retail client's light tamper check, computed from the crystal, the
        /// lowest ingredient item id and the ingredient count. LandSandBoat does not verify it.
        /// Algorithm described in XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x0096).
        /// </summary>
        /// <param name="crystal">The crystal item id.</param>
        /// <param name="lowestItemId">The lowest ingredient item id (slot 0 once sorted ascending).</param>
        /// <param name="itemCount">The number of ingredients.</param>
        public static byte ComputeCombineHash(ushort crystal, ushort lowestItemId, int itemCount)
        {
            // Crystals 4096-4103 are normal; 4238-4245 and 6506-6513 (clusters) fold onto them.
            int normalized;
            if (crystal >= 6506 && crystal < 6514) normalized = crystal - 2410;
            else if (crystal >= 4238 && crystal < 4246) normalized = crystal - 142;
            else normalized = crystal;

            return (byte)(((normalized + 3) * (lowestItemId + 7) * (itemCount + 5)) % 0x7F);
        }

        /// <summary>
        /// C2S 0x096 <c>COMBINE_ASK</c>: starts a synthesis. Each ingredient is one item of one inventory slot (a stack of
        /// four takes four of the eight entries). The entries are sorted by item id ascending, as retail does before it
        /// hashes them. The server answers with S2C 0x030 (the animation), then 0x06F and 0x070.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x0096)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x096_combine_ask.h).
        /// </summary>
        /// <param name="destination">At least <see cref="CombineAskLength"/> bytes.</param>
        /// <param name="ingredients">One (item id, inventory slot) per ingredient item, 1 to 8 entries; sorted in place.</param>
        /// <returns>The packet length, or 0 when there are no ingredients or more than 8.</returns>
        public static int BuildCombineAsk(Span<byte> destination, ushort sequenceId, ushort crystalItemId, byte crystalSlot,
            Span<(ushort ItemId, byte Slot)> ingredients)
        {
            if (ingredients.Length is 0 or > MaxIngredients) return 0;

            // Insertion sort: at most 8 entries, keeps the pairs together, no allocation.
            for (int i = 1; i < ingredients.Length; i++)
            {
                var current = ingredients[i];
                int j = i - 1;
                while (j >= 0 && ingredients[j].ItemId > current.ItemId)
                {
                    ingredients[j + 1] = ingredients[j];
                    j--;
                }
                ingredients[j + 1] = current;
            }

            PacketHeader.Write(destination, 0x096, CombineAskLength / 4, sequenceId);
            destination.Slice(4, CombineAskLength - 4).Clear();
            destination[4] = ComputeCombineHash(crystalItemId, ingredients[0].ItemId, ingredients.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), crystalItemId);
            destination[8] = crystalSlot;
            destination[9] = (byte)ingredients.Length;
            for (int i = 0; i < ingredients.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10 + (i * 2), 2), ingredients[i].ItemId);
                destination[26 + i] = ingredients[i].Slot;
            }
            return CombineAskLength;
        }

        /// <summary>
        /// C2S 0x058 <c>RECIPE</c>: asks a crafting guild NPC for a recipe or recipe list. The server answers with S2C 0x031.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x0058)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x058_recipe.h).
        /// </summary>
        /// <param name="skill">The craft: 1 Woodworking to 8 Cooking (0 Fishing, 9 Synergy, 10 Digging are not served by LSB).</param>
        /// <param name="level">The character's real skill level in that craft, 0 to 110.</param>
        public static int BuildRecipe(Span<byte> destination, ushort sequenceId, ushort skill, ushort level, RecipeRequestMode mode,
            ushort param0 = 0, ushort param1 = 0, ushort param2 = 0, ushort param3 = 0, ushort param4 = 0)
        {
            PacketHeader.Write(destination, 0x058, RecipeLength / 4, sequenceId);
            var payload = destination.Slice(4, RecipeLength - 4);
            payload.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(0, 2), skill);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(2, 2), level);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), param0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), (ushort)mode);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(8, 2), param1);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(10, 2), param2);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(12, 2), param3);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(14, 2), param4);
            return RecipeLength;
        }
    }

    /// <summary>Builders for the guild shop requests (C2S 0x0AA to 0x0AD).</summary>
    public static class GuildShopPacketBuilders
    {
        /// <summary>
        /// C2S 0x0AA <c>GUILD_BUY</c>: buys <paramref name="count"/> (1 to 99, at most a stack) of an item from the guild
        /// shop that the last S2C 0x086 opened. The server answers with S2C 0x082.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00AA)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0aa_guild_buy.h).
        /// </summary>
        public static int BuildGuildBuy(Span<byte> destination, ushort sequenceId, ushort itemId, byte count)
        {
            PacketHeader.Write(destination, 0x0AA, 2, sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), itemId);
            destination[6] = 0; // PropertyItemIndex: always 0 for a purchase
            destination[7] = count;
            return 8;
        }

        /// <summary>
        /// C2S 0x0AB <c>GUILD_BUYLIST</c>: asks for the guild shop's current stock. The server answers with S2C 0x083.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00AB).
        /// </summary>
        public static int BuildGuildBuyList(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x0AB, 1, sequenceId);
            return 4;
        }

        /// <summary>
        /// C2S 0x0AC <c>GUILD_SELL</c>: sells <paramref name="count"/> (1 to 99) of an inventory item to the guild shop.
        /// The server answers with S2C 0x084.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00AC)
        /// and LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0ac_guild_sell.h).
        /// </summary>
        public static int BuildGuildSell(Span<byte> destination, ushort sequenceId, ushort itemId, byte inventorySlot, byte count)
        {
            PacketHeader.Write(destination, 0x0AC, 2, sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), itemId);
            destination[6] = inventorySlot;
            destination[7] = count;
            return 8;
        }

        /// <summary>
        /// C2S 0x0AD <c>GUILD_SELLLIST</c>: asks for the items the guild shop accepts and their stock. The server answers
        /// with S2C 0x085.
        /// Packet layout referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00AD).
        /// </summary>
        public static int BuildGuildSellList(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x0AD, 1, sequenceId);
            return 4;
        }
    }

    #endregion
}
