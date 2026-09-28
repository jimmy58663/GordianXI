// src/Gordian.Core/Events/EventWorkZone.cs
using System;

namespace Gordian.Core.Events
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

        public void Clear()
        {
            Array.Clear(Zone);
            Array.Clear(Tables);
            Array.Clear(Memorize);
            Array.Clear(Zone1700);
        }
    }
}
