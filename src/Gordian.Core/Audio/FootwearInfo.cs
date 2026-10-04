// src/Gordian.Core/Audio/FootwearInfo.cs
using System;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// The footstep digits of a footwear (or creature) model: the <c>move</c> character and <c>shake</c> value that, with
    /// the ground's terrain, name the zone's footstep pointer <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake + 1&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Read from the first Info section (0x45) of the model DAT: byte 1 is the move value written as a base-36 digit
    /// (0xFF reads as <c>0</c>), byte 2 the shake value. Referenced from xi-tools <c>docs/sounds/footsteps.md</c>
    /// (https://github.com/vekien/xi-tools), §3. For a player the feet item's DAT gives both digits.
    /// </remarks>
    public readonly record struct FootwearInfo(char MovementChar, int ShakeFactor)
    {
        /// <summary>What an actor whose footwear is unknown uses (the <c>..11</c> pointers every terrain has).</summary>
        public static FootwearInfo Default { get; } = new('1', 0);

        /// <summary>Reads the footstep digits from a model DAT's first Info section.</summary>
        public static bool TryRead(ReadOnlySpan<byte> dat, out FootwearInfo info)
        {
            info = Default;
            foreach (DatSectionHeader head in DatSectionWalker.ReadHeaders(dat))
            {
                if (head.TypeCode != DatSectionType.Info)
                {
                    continue;
                }

                if (head.DataOffset + 3 > dat.Length)
                {
                    return false;
                }

                byte move = dat[head.DataOffset + 1];
                byte shake = dat[head.DataOffset + 2];
                info = new FootwearInfo(ToBase36(move), shake == 0xFF ? 0 : shake);
                return true;
            }

            return false;
        }

        private static char ToBase36(byte value)
        {
            if (value == 0xFF || value >= 36)
            {
                return '0';
            }

            return value < 10 ? (char)('0' + value) : (char)('a' + value - 10);
        }
    }
}
