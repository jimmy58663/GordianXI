// src/Gordian.Core/Audio/FootwearInfo.cs
using System;
using Gordian.Core.Resources.Containers;

namespace Gordian.Engine.Audio
{
    /// <summary>
    /// The footstep digits of a footwear (or creature) model: the <c>move</c> character and <c>shake</c> value that, with
    /// the ground's terrain, name the zone's footstep pointer <c>0&lt;terrain&gt;&lt;move&gt;&lt;shake + 1&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Read from the first Info section (0x45) of the model DAT: byte 0 the movement type (<see cref="MovementType"/>),
    /// byte 1 the move value written as a base-36 digit (0xFF reads as <c>0</c>), byte 2 the shake value. Referenced from
    /// xi-tools <c>docs/sounds/footsteps.md</c> (https://github.com/vekien/xi-tools), §3. For a player the feet item's DAT
    /// gives both digits.
    /// </remarks>
    public readonly record struct FootwearInfo(char MovementChar, int ShakeFactor)
    {
        /// <summary>What an actor whose footwear is unknown uses (the <c>..11</c> pointers every terrain has).</summary>
        public static FootwearInfo Default { get; } = new('1', 0);

        /// <summary>Info byte 0 of a model that walks (xi-tools §3: Walking 0, Sliding 1, Large 2, Flying 3, unset 0xFF).</summary>
        public const byte Walking = 0;

        /// <summary>Info byte 0 of a model whose movement type is not set (and of a creature with no Info section).</summary>
        public const byte Unset = 0xFF;

        /// <summary>
        /// The model's movement type (Info byte 0). Only a walking model leaves footprints and foot effects; every model's
        /// steps are heard (xi-tools §3, after xim <c>ZoneDrawer.emitFootSteps</c>). A player's comes from the race, which
        /// walks; a feet item's own byte is not used for it.
        /// </summary>
        public byte MovementType { get; init; }

        /// <summary>Whether the model leaves footprints and foot effects (<see cref="MovementType"/> is <see cref="Walking"/>).</summary>
        public bool LeavesFootprints => MovementType == Walking;

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
                info = new FootwearInfo(ToBase36(move), shake == 0xFF ? 0 : shake) { MovementType = dat[head.DataOffset] };
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
