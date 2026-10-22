// src/Gordian.Core/Resources/ResourceManager.Mounts.cs
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Core.Resources
{
    public sealed partial class ResourceManager
    {
        private readonly ConcurrentDictionary<CharacterRace, (List<AnimationClip> Clips, List<RawMotionRoutine> Routines)?> _riderPacks = new();
        private ConditionalWeakTable<EntityModel, EntityModel> _riders = new();

        /// <summary>
        /// The model of the mount <paramref name="mount"/> rides (#87, <see cref="MountModels.LoadMount"/>), cached with the
        /// entity models; false when not mounted or the mount's files are missing.
        /// </summary>
        public bool TryLoadMountModel(in MountAppearance mount, out EntityModel? model)
        {
            model = null;
            if (!mount.IsMounted) return false;
            string key = MountModels.CacheKey(mount);
            if (_entityModelCache.TryGetValue(key, out model)) return model != null;
            model = MountModels.LoadMount(mount, LoadDatBytes, LoadDatBytesByFileId);
            if (model == null) return false;
            _entityModelCache[key] = model;
            return true;
        }

        /// <summary>
        /// <paramref name="character"/> seated: the same meshes playing its race's chocobo riding pack (#87,
        /// <see cref="MountModels.BuildRider"/>), built once per character model. False for a race without a pack.
        /// </summary>
        public bool TryGetRiderModel(EntityModel character, CharacterRace race, out EntityModel? rider)
        {
            var riders = _riders;
            if (riders.TryGetValue(character, out rider)) return true;
            var pack = _riderPacks.GetOrAdd(race, r => MountModels.LoadRiderPack(r, LoadDatBytes));
            if (pack is not { } loaded)
            {
                rider = null;
                return false;
            }
            rider = riders.GetValue(character, c => MountModels.BuildRider(c, loaded.Clips, loaded.Routines));
            return true;
        }

        private ConditionalWeakTable<EntityModel, ConcurrentDictionary<EntityModel, float>> _seats = new();

        /// <summary>
        /// How far <paramref name="rider"/> is moved up or down to sit on <paramref name="mount"/> (#87,
        /// <see cref="MountModels.SeatOffset"/>), computed once per rider and mount.
        /// </summary>
        public float GetSeatOffset(EntityModel rider, EntityModel mount) =>
            _seats.GetValue(rider, _ => new ConcurrentDictionary<EntityModel, float>())
                .GetOrAdd(mount, m => MountModels.SeatOffset(rider, m));

        /// <summary>Drops the riding packs and riders built from them (part of <see cref="ClearCache"/>).</summary>
        private void ClearMountCaches()
        {
            _riderPacks.Clear();
            _riders = new ConditionalWeakTable<EntityModel, EntityModel>();
            _seats = new ConditionalWeakTable<EntityModel, ConcurrentDictionary<EntityModel, float>>();
        }
    }
}
