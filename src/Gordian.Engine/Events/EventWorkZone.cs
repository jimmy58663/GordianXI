// src/Gordian.Engine/Events/EventWorkZone.cs
using System;

namespace Gordian.Engine.Events
{
    /// <summary>
    /// The work values every event of a zone shares (the retail client's <c>Work_Zone</c>, <c>Work_Zone_Memorize</c>
    /// and <c>Work_Zone_1700</c> arrays, per XiEvents' VM notes): the server's event parameters land in
    /// <see cref="Zone"/>[2..9], a query's selection in <see cref="Zone"/>[0] and the value reported to the server
    /// (the 0x05B EndPara) in <see cref="Zone"/>[1].
    /// </summary>
    public sealed class EventWorkZone
    {
        public const int ParameterBase = 2;

        public int[] Zone { get; } = new int[96];

        /// <summary>
        /// The table pointer slots opcode 0x9D shares between events (the retail client's <c>Ptr_Work_Zone</c> /
        /// <c>Ptr_Refs_Zone</c>): each names a table inside a block's byte code and that block's immediate data.
        /// </summary>
        public (byte[] Code, int[] References, int Offset)?[] Tables { get; } = new (byte[], int[], int)?[64];
        public int[] Memorize { get; } = new int[64];
        public int[] Zone1700 { get; } = new int[32];

        /// <summary>Index of the last query's selection (0-based; 254 = cancelled by the player).</summary>
        public int Selection
        {
            get => Zone[0];
            set => Zone[0] = value;
        }

        /// <summary>The value sent with the event's 0x05B updates and end.</summary>
        public int EndParameter
        {
            get => Zone[1];
            set => Zone[1] = value;
        }

        /// <summary>
        /// A message's number parameter n: the eight server parameter slots (zone work values 2-9), then the 1700
        /// block from parameter 8 on (the home point script writes its zone list through a table of exactly those
        /// slots and names parameter 33 for the zone, which it stores in 1700 slot 25; 2026-09-28).
        /// </summary>
        public int GetMessageParameter(int index)
        {
            if (index < 0) return 0;
            if (index < 8) return Zone[ParameterBase + index];
            return index - 8 < Zone1700.Length ? Zone1700[index - 8] : 0;
        }

        /// <summary>Stores the server's event parameters (0x033/0x034 values) where the scripts read them.</summary>
        public void SetParameters(ReadOnlySpan<int> parameters)
        {
            for (int i = 0; i < parameters.Length && ParameterBase + i < Zone.Length; i++) Zone[ParameterBase + i] = parameters[i];
        }

        /// <summary>The zone work index where an S2C 0x031 recipe's values start (the guild scripts read 0x40-0x55).</summary>
        public const int RecipeBase = 0x40;

        /// <summary>
        /// Writes one recipe of S2C 0x031 (kinds 1 / 3) where the guild scripts read it after opcode 0x8C sub 1 (#405):
        /// zone work 0x40 the product, 0x41-0x43 the sub-craft skills, 0x44 the crystal, 0x45-0x4C the eight ingredients,
        /// 0x4D-0x54 their counts and 0x55 the key item. The order is read from the scripts (every guild and image-support
        /// script prints ingredients from 0x45 / counts from 0x4D and picks the "Necessary key item" line on 0x55), so the
        /// client moves the packet's key item (its sixth value) to the end. *Inferred from the scripts, not from the
        /// client's <c>RecvRecipe</c>.*
        /// </summary>
        public void ApplyRecipe(ushort product, ReadOnlySpan<ushort> subCraftSkills, ushort crystal, ushort keyItem,
            ReadOnlySpan<ushort> ingredients, ReadOnlySpan<ushort> counts)
        {
            Zone[RecipeBase] = product;
            for (int i = 0; i < 3; i++) Zone[RecipeBase + 1 + i] = i < subCraftSkills.Length ? subCraftSkills[i] : 0;
            Zone[RecipeBase + 4] = crystal;
            for (int i = 0; i < 8; i++)
            {
                Zone[RecipeBase + 5 + i] = i < ingredients.Length ? ingredients[i] : 0;
                Zone[RecipeBase + 13 + i] = i < counts.Length ? counts[i] : 0;
            }
            Zone[RecipeBase + 21] = keyItem;
        }

        /// <summary>
        /// Writes a page of the recipe list of S2C 0x031 (kind 2) where the guild scripts read it (#405): the sixteen
        /// items as message parameters 0-15 (zone work 2-9, then 1700 slots 0-7), the next page's first item in 1700 slot 8.
        /// The script offers each non-zero slot in its "Which synthesis recipe do you ask about?" query, and "View more
        /// recipes." when slot 8 is set. *Inferred from the scripts (Fishermen's Guild, zone 48 event 222), not from the
        /// client's <c>RecvRecipe</c>.*
        /// </summary>
        public void ApplyRecipeList(ReadOnlySpan<ushort> items, ushort nextPageItem)
        {
            for (int i = 0; i < 16; i++)
            {
                int value = i < items.Length ? items[i] : 0;
                if (i < 8) Zone[ParameterBase + i] = value;
                else Zone1700[i - 8] = value;
            }
            Zone1700[8] = nextPageItem;
        }

        public void Clear()
        {
            Array.Clear(Zone);
            Array.Clear(Tables);
            Array.Clear(Memorize);
            Array.Clear(Zone1700);
        }
    }
}
