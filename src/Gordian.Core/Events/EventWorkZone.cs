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

        /// <summary>Stores the server's event parameters (0x033/0x034 values) where the scripts read them.</summary>
        public void SetParameters(ReadOnlySpan<int> parameters)
        {
            for (int i = 0; i < parameters.Length && ParameterBase + i < Zone.Length; i++) Zone[ParameterBase + i] = parameters[i];
        }

        public void Clear()
        {
            Array.Clear(Zone);
            Array.Clear(Memorize);
            Array.Clear(Zone1700);
        }
    }
}
