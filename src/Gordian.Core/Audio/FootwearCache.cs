// src/Gordian.Core/Audio/FootwearCache.cs
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// The footstep digits and movement type of each actor (#40), read from its DAT on a worker and cached per file: a
    /// character's feet item, else a creature's own model DAT (xi-tools <c>docs/sounds/footsteps.md</c> §3,
    /// https://github.com/vekien/xi-tools). Unknown until the DAT is read; meanwhile <see cref="FootwearInfo.Default"/>
    /// digits, and for a creature an unset movement type (no footprints), as for a creature with no Info section (xim's
    /// default InfoDefinition). The step sounds and the footprints share one cache (<see cref="Shared"/>).
    /// </summary>
    public sealed class FootwearCache
    {
        private readonly ConcurrentDictionary<int, FootwearInfo> _byFile = new();
        private int _generation = -1;

        /// <summary>A file not read yet, or without an Info section: the default digits, no movement type.</summary>
        private static readonly FootwearInfo Unread = FootwearInfo.Default with { MovementType = FootwearInfo.Unset };

        /// <summary>The cache the step sounds and the footprints share.</summary>
        public static FootwearCache Shared { get; } = new();

        /// <summary>
        /// The actor's footwear: a character's feet item (a character walks: its race sets the movement type), a creature's
        /// model DAT; the default digits while the DAT is read or when it has none.
        /// </summary>
        public FootwearInfo Of(WorldEntity entity, ResourceManager? resources)
        {
            if (resources is null)
            {
                return FootwearInfo.Default;
            }

            // A VFS reload (#374) reads every file again.
            int generation = resources.CacheGeneration;
            if (generation != _generation)
            {
                _generation = generation;
                _byFile.Clear();
            }

            int fileId;
            bool character = false;
            ushort face = entity.Appearance.FaceModel;
            var race = (CharacterRace)((face >> 8) & 0xFF);
            if (race != CharacterRace.Unknown)
            {
                character = true;
                ushort feet = (ushort)(entity.Appearance.Feet & 0x0FFF);
                if (!CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Feet, feet, out fileId))
                {
                    return FootwearInfo.Default;
                }
            }
            else if (entity.Appearance.ModelId != 0)
            {
                fileId = CharacterEquipmentResolver.GetMonsterFileId(entity.Appearance.ModelId);
                if (fileId <= 0)
                {
                    return Unread;
                }
            }
            else
            {
                return Unread;
            }

            if (!_byFile.TryGetValue(fileId, out FootwearInfo info) && _byFile.TryAdd(fileId, Unread))
            {
                _ = Task.Run(() =>
                {
                    byte[]? dat = resources.LoadDatBytesByFileId(fileId);
                    if (dat is not null && FootwearInfo.TryRead(dat, out FootwearInfo read))
                    {
                        _byFile[fileId] = read;
                    }
                });
                info = Unread;
            }

            return character ? info with { MovementType = FootwearInfo.Walking } : info;
        }
    }
}
