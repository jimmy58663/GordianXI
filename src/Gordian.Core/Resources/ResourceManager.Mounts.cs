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
        private readonly ConcurrentDictionary<CharacterRace, (List<AnimationClip> Clips, List<RawMotionRoutine> Routines)?> _mountPacks = new();
        private ConditionalWeakTable<EntityModel, EntityModel> _riders = new();
        private ConditionalWeakTable<EntityModel, ConcurrentDictionary<byte, EntityModel?>> _poseRiders = new();
        private ConditionalWeakTable<EntityModel, StrongBox<float>> _mountScales = new();
        private readonly ConcurrentDictionary<string, MountSeating.RideData?> _rideData = new();

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
        /// <paramref name="character"/> seated on a chocobo: the same meshes playing its race's chocobo riding pack (#87,
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

        /// <summary>
        /// <paramref name="character"/> holding mount rider pose <paramref name="pose"/> (#87, round 5,
        /// <see cref="MountSeating"/>): its race's mount rider pack's <c>{pose}hi</c> / <c>lk</c> / <c>un</c> / <c>vb</c> as its
        /// idle and locomotion, on its own meshes. Built once per character and pose; false without the pack or the pose.
        /// </summary>
        public bool TryGetPoseRiderModel(EntityModel character, CharacterRace race, byte pose, out EntityModel? rider)
        {
            var byPose = _poseRiders.GetValue(character, _ => new ConcurrentDictionary<byte, EntityModel?>());
            rider = byPose.GetOrAdd(pose, p =>
            {
                if (_mountPacks.GetOrAdd(race, r => MountSeating.LoadPack(r, LoadDatBytes)) is not { } pack) return null;
                var clips = MountSeating.PoseClips(pack.Clips, p);
                return clips.Count == 0 ? null : MountModels.BuildRider(character, clips, pack.Routines);
            });
            return rider != null;
        }

        /// <summary>How much the chocobo is scaled under <paramref name="rider"/> (#87, <see cref="MountModels.MountScale"/>), computed once per rider.</summary>
        public float GetMountScale(EntityModel rider) => _mountScales.GetValue(rider, r => new StrongBox<float>(MountModels.MountScale(r))).Value;

        /// <summary>A mount's ride data (size per build, pose per race; #87 round 5, <see cref="MountSeating.Read"/>), read once per mount; null for the chocobo.</summary>
        public MountSeating.RideData? GetRideData(in MountAppearance mount)
        {
            if (!mount.IsMounted || mount.RidesChocobo) return null;
            var key = MountModels.CacheKey(mount);
            if (_rideData.TryGetValue(key, out var cached)) return cached;
            byte[]? dat = LoadDatBytesByFileId(MountModels.MountFileId(mount.MountId));
            var data = dat == null || dat.Length == 0 ? null : MountSeating.Read(dat);
            _rideData[key] = data;
            return data;
        }

        private ConditionalWeakTable<EntityModel, StrongBox<System.Numerics.Vector3>> _riderHips = new();

        /// <summary>Where <paramref name="rider"/>'s hips are in its pose (#87 round 5, <see cref="MountModels.RiderHips"/>), computed once per rider.</summary>
        public System.Numerics.Vector3 GetRiderHips(EntityModel rider) =>
            _riderHips.GetValue(rider, r => new StrongBox<System.Numerics.Vector3>(MountModels.RiderHips(r))).Value;

        /// <summary>Drops the riding packs and riders built from them (part of <see cref="ClearCache"/>).</summary>
        private void ClearMountCaches()
        {
            _riderPacks.Clear();
            _mountPacks.Clear();
            _rideData.Clear();
            _riderHips = new ConditionalWeakTable<EntityModel, StrongBox<System.Numerics.Vector3>>();
            _riders = new ConditionalWeakTable<EntityModel, EntityModel>();
            _poseRiders = new ConditionalWeakTable<EntityModel, ConcurrentDictionary<byte, EntityModel?>>();
            _mountScales = new ConditionalWeakTable<EntityModel, StrongBox<float>>();
        }
    }
}
