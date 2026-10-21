// src/Gordian.Core/Animation/ActionMotionLibrary.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Animation
{
    /// <summary>The kind of action presentation DAT an S2C 0x028 finish packet's animation number names.</summary>
    public enum ActionAssetKind : byte
    {
        /// <summary>A weapon skill (<c>cmd_no</c> 3): a per-race body DAT with its clips and effects, plus a waist companion.</summary>
        WeaponSkill,

        /// <summary>A monster TP move or pet skill (<c>cmd_no</c> 11 / 13): the clips are the actor's own or the DAT's.</summary>
        MonsterSkill,

        /// <summary>A job ability (<c>cmd_no</c> 6 / 14 / 15): its <c>main</c> plays clips of the actor's own pool (<c>cm0?</c>).</summary>
        JobAbility,

        /// <summary>A spell (<c>cmd_no</c> 4): its <c>main</c> links the caster's release (<c>shwh</c>, <c>shbk</c>...).</summary>
        Spell,

        /// <summary>A synthesis (S2C 0x030, #315): the crystal's element file, its <c>main</c> built from <c>frst</c> and the result's ending.</summary>
        Synthesis
    }

    /// <summary>
    /// One action presentation DAT, loaded once: the routines and clips its <c>main</c> plays on the actor
    /// (<see cref="Motion"/>) and the effect routines the renderer plays with the DAT's particle generators
    /// (<see cref="Effects"/>, the same command walk as the cutscene scene files).
    /// </summary>
    public sealed class ActionAsset
    {
        /// <summary>The routine every action presentation DAT starts from.</summary>
        public const string MainRoutine = "main";

        public ActionAsset(ActionAssetKind kind, int animation, int fileId, EventMotionBank? motion, EventSceneResource? effects)
        {
            Kind = kind;
            Animation = animation;
            FileId = fileId;
            Motion = motion;
            Effects = effects;
        }

        public ActionAssetKind Kind { get; }

        /// <summary>
        /// Whether the body holds the presentation's last clip until its <c>main</c> ends rather than giving it back at the end
        /// of the clip step (a synthesis holds the crystal up in <c>ri0?</c> through the whole effect, #315).
        /// </summary>
        public bool HoldsBodyToEnd { get; init; }

        /// <summary>The packet's animation number (the result's 12-bit <c>sub_kind</c>).</summary>
        public int Animation { get; }

        /// <summary>The DAT file id (the body DAT for a weapon skill): the renderer loads its particle generators by it.</summary>
        public int FileId { get; }

        /// <summary>The DAT's routines and clips (the waist companion's clips joined in for a weapon skill), or null.</summary>
        public EventMotionBank? Motion { get; }

        /// <summary>The DAT's routines read as effect timelines (generator starts, routine starts), or null.</summary>
        public EventSceneResource? Effects { get; }

        public override string ToString() => $"{Kind} {Animation} (file {FileId})";

        private IReadOnlyList<RoutineSoundCue>? _generatorSounds;
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityModel, IReadOnlyList<RoutineSoundCue>> _soundsOnModel = new();

        /// <summary>
        /// The sounds of the presentation's audio generators (#328): the generators <see cref="MainRoutine"/> and the
        /// routines it starts (0x03, one pass of 0x73, four deep) spawn or swap in whose linked data is a sound pointer, each
        /// at its start on the routine clock (60 Hz). Most spells and job abilities carry their sound this way rather than as
        /// a sound command; the walk and the generator-to-sound reading are the cutscene scene files' (EventPresentation).
        /// </summary>
        public IReadOnlyList<RoutineSoundCue> GeneratorSounds => _generatorSounds ??= CollectGeneratorSounds(Effects);

        /// <summary>
        /// Everything the presentation plays on an actor: the sound commands of <see cref="MainRoutine"/> through the DAT's
        /// and the actor's routines (<see cref="EventMotionBank.GetSoundsOnModel"/>) and the <see cref="GeneratorSounds"/>,
        /// by tick; cached per model.
        /// </summary>
        public IReadOnlyList<RoutineSoundCue> GetSoundsOnModel(EntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_soundsOnModel.TryGetValue(model, out var cached)) return cached;
            var commands = Motion?.GetSoundsOnModel(model, MainRoutine) ?? Array.Empty<RoutineSoundCue>();
            var generators = GeneratorSounds;
            IReadOnlyList<RoutineSoundCue> all = generators.Count == 0 ? commands
                : [.. System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Concat(commands, generators), c => c.Tick)];
            _soundsOnModel.AddOrUpdate(model, all);
            return all;
        }

        internal static IReadOnlyList<RoutineSoundCue> CollectGeneratorSounds(EventSceneResource? resource)
        {
            if (resource == null || !resource.TryGetRoutine(MainRoutine, out var main)) return Array.Empty<RoutineSoundCue>();
            var cues = new List<RoutineSoundCue>();
            void Walk(SceneRoutine routine, int start, int depth)
            {
                foreach (var command in routine.Commands)
                {
                    int at = start + command.StartFrame;
                    switch (command.Kind)
                    {
                        case SceneCommandKind.SpawnGenerator when resource.TryGetGeneratorSound(command.Reference, out var spawned):
                            cues.Add(new RoutineSoundCue(at, [spawned.SoundId], 0x02));
                            break;
                        case SceneCommandKind.ReplaceGenerator when resource.TryGetGeneratorSound(command.Reference2, out var replacement):
                            cues.Add(new RoutineSoundCue(at, [replacement.SoundId], 0x02));
                            break;
                        case SceneCommandKind.StartRoutine or SceneCommandKind.LoopRoutine when depth < 4
                            && command.Reference != routine.Name && resource.TryGetRoutine(command.Reference, out var child):
                            Walk(child, at, depth + 1);
                            break;
                    }
                }
            }
            Walk(main, 0, 0);
            if (cues.Count == 0) return Array.Empty<RoutineSoundCue>();
            cues.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            return cues;
        }
    }

    /// <summary>
    /// Finds and caches the presentation DAT of a weapon skill, monster TP move, pet skill, job ability or spell from the
    /// animation number S2C 0x028 sends in the result's <c>sub_kind</c> (XiPackets world/server/0x0028: "sub_kind is set
    /// to the animation id to be played").
    /// <list type="bullet">
    /// <item><b>Weapon skills</b>: two per-race banks chosen by comparing with 256, each a body block plus two waist
    /// companion blocks: animation &lt; 256 is file <c>primary[race] + animation</c> (companions +256 / +512), 256-271
    /// is <c>extended[race] + animation - 256</c> (companions +16 / +32). Bases and the rule referenced from xi-tools
    /// (docs/anim/weapon-skills.md, "The two banks in FFXiMain.dll"); the companion used is A (which lower-body variant
    /// picks B is not known there either).</item>
    /// <item><b>Job abilities</b>: file 4412 + animation; <b>spells</b>: file 2800 (0xAF0) + animation. Referenced from
    /// xi-tools (docs/ability/inspect.md, docs/fx/spells.md "The resolution chain").</item>
    /// <item><b>Monster TP moves and pet skills</b>: animation 0-511 is file 3900 + animation, 512-1791 is file
    /// 50159 + animation - 512. <b>Beyond xi-tools</b> (which says only that the number indexes the monster's own
    /// animation): located in the retail file table (2026-10-11, #132). Both ranges begin with the placeholder
    /// <c>ROM/13/38</c>; the crab's Bubble Shower / Bubble Curtain / Big Scissors / Scissor Guard (LandSandBoat
    /// mob_anim_id 186-189) are files 4086-4089, three of them carrying clips for the crab skeleton (48 joints), Big
    /// Scissors playing the crab's own <c>ma0?</c> / <c>at2?</c>; the avatars' blood pacts line up by skeleton in the
    /// second range by the highest joint their clips key (Ifrit 526-534: 71, Titan 539-547: 83, Leviathan 552-560: 105, Garuda 565-573: 90)
    /// with their spell pacts (Fire II 527, Stone II 540, Water II 553, Aero II 566) the only ones without clips. The
    /// files from 60729 and 86791 also begin with the placeholder and hold monster moves, but no mapping from the
    /// animation numbers above 1791 to them was found, so those play the stand-in.</item>
    /// </list>
    /// </summary>
    public sealed class ActionMotionLibrary
    {
        /// <summary>The library the session uses: it reads through <see cref="ZoneDatLoader.Load"/> (the app's resource manager).</summary>
        public static ActionMotionLibrary Shared { get; } = new(null);

        /// <summary>Job ability presentation files: 4412 + animation (xi-tools docs/ability/inspect.md).</summary>
        public const int JobAbilityFileBase = 4412;

        /// <summary>The highest job ability animation (4412 + 499 = 4911, just below the weapon skill effect files).</summary>
        public const int MaxJobAbilityAnimation = 499;

        /// <summary>Spell presentation files: 0xAF0 + animation (xi-tools docs/fx/spells.md).</summary>
        public const int SpellFileBase = 0xAF0;

        /// <summary>The highest spell animation read (retail reaches 1011; 2800 + 1099 stays below the monster files at 3900).</summary>
        public const int MaxSpellAnimation = 1099;

        /// <summary>Monster TP move files for animations 0-511.</summary>
        public const int MonsterSkillFileBase = 3900;

        /// <summary>Monster TP move files for animations 512-1791 (file = this + animation - 512).</summary>
        public const int MonsterSkillSecondFileBase = 50159;

        /// <summary>The highest monster animation whose file is known (1791, the end of the second range).</summary>
        public const int MaxMonsterSkillAnimation = 1791;

        /// <summary>Weapon skill body files per retail race index (Hume male ... Galka), animation 0-255 (xi-tools).</summary>
        private static readonly int[] PrimaryWeaponSkillBase = [33227, 33995, 34763, 35531, 36299, 36299, 37067, 37835];

        /// <summary>Weapon skill body files per retail race index, animation 256-271 (xi-tools).</summary>
        private static readonly int[] ExtendedWeaponSkillBase = [61451, 61499, 61547, 61595, 61643, 61643, 61691, 61739];

        /// <summary>The first extended weapon skill animation.</summary>
        public const int ExtendedWeaponSkillFirst = 256;

        /// <summary>One past the last extended weapon skill animation (16 slots per race in the retail tables).</summary>
        public const int ExtendedWeaponSkillEnd = 272;

        private readonly Func<int, byte[]?>? _loader;
        private readonly ConcurrentDictionary<(ActionAssetKind Kind, int Race, int Animation), ActionAsset?> _cache = new();

        /// <param name="loader">Reads a DAT by file id; null reads through <see cref="ZoneDatLoader.Load"/> when it is set.</param>
        public ActionMotionLibrary(Func<int, byte[]?>? loader)
        {
            _loader = loader;
        }

        private Func<int, byte[]?>? Loader => _loader ?? ZoneDatLoader.Load;

        /// <summary>
        /// Called with a presentation's file id after it is first read (on the reading thread), so the app can decode its
        /// particle generators ahead of the renderer; null to skip.
        /// </summary>
        public static Action<int>? EffectsPreloader { get; set; }

        /// <summary>Drops the cache (a VFS reload).</summary>
        public void Clear()
        {
            _cache.Clear();
            _synthesisCache.Clear();
        }

        /// <summary>
        /// The synthesis effect files: 4912 + S2C 0x030's <c>EffectNum</c> (0x10-0x17), ROM/16/56-63, one per element in
        /// LandSandBoat's <c>SynthesisEffect</c> order (water, wind, fire, earth, lightning, ice, light, dark; textures
        /// <c>cris_aq</c>, <c>cris_ar</c>, <c>cris_ig</c>, <c>cris_tr</c>, <c>cris_th</c>, <c>cris_ic</c>, <c>cris_lt</c>,
        /// <c>cris_dk</c>). 4912 starts the item use effect files (4912 / 4913 the antidote's <c>doku</c>, potions and ethers
        /// after). <b>Beyond XiPackets / xi-tools / LandSandBoat</b>: found in the retail DATs 2026-10-18 by texture name.
        /// </summary>
        public const int SynthesisEffectFileBase = 4912;

        /// <summary>The first and last synthesis <c>EffectNum</c>.</summary>
        public const int FirstSynthesisEffect = 0x10, LastSynthesisEffect = 0x17;

        /// <summary>
        /// A synthesis effect file's routines (#315): <c>frst</c> (630 ticks) plays the crafter's <c>ri0?</c> (the right hand
        /// up with the crystal) and starts <c>strt</c> (the crystal <c>cri0</c> and the element's intro) and <c>loop</c> five
        /// times; then one ending: <c>ednn</c> success (rays, rings, the flash), <c>edmm</c> break (the crystal <c>cri1</c>
        /// again, then <c>san3</c> scattering it, no rays), <c>edxx</c> high quality; <c>stcr</c> stops everything. The file's
        /// own test routines (<c>s000</c> / <c>t000</c>) run exactly this: <c>frst</c>, an ending at tick 630, then <c>stcr</c>.
        /// Its <c>main</c> links a template name (<c>ed x</c>) the client fills in, so the ending is picked here from
        /// <c>Type</c>: 0 success <c>ednn</c>, 1 break <c>edmm</c>, 2-4 high quality <c>edxx</c>. <c>Type</c> is LandSandBoat's
        /// <c>RESULT_*</c> value (synthutils.cpp: <c>RESULT_SUCCESS</c> 0, <c>RESULT_FAIL</c> 1, <c>RESULT_HQ</c> 2), not its
        /// <c>SYNTHESIS_*</c> enum (fail 0, success 1); reading it as the latter swapped the endings in the maintainer's round 3
        /// (2026-10-18: successes broke and breaks succeeded for wind, fire and lightning). (The eight files ROM/160/19-26
        /// carry the same endings named <c>ng00</c> / <c>ok00</c> / <c>okex</c>).
        /// </summary>
        public const string SynthesisStart = "frst", SynthesisSuccess = "ednn", SynthesisBreak = "edmm", SynthesisHighQuality = "edxx", SynthesisStop = "stcr";

        /// <summary>The ending routine for 0x030's <c>Type</c> (the result the server computed up front).</summary>
        public static string SynthesisEnding(int type) => type switch
        {
            0 => SynthesisSuccess,
            1 => SynthesisBreak,
            _ => SynthesisHighQuality,
        };

        private readonly ConcurrentDictionary<(int Effect, string Ending), ActionAsset?> _synthesisCache = new();

        /// <summary>
        /// The synthesis presentation for S2C 0x030 (#315): the element's file with a <c>main</c> that runs <c>frst</c>, the
        /// ending for <paramref name="type"/> when <c>frst</c> ends, then <c>stcr</c>, on the body (the crystal held up through
        /// it all) and as effect timelines. Null outside 0x10-0x17 or when the file cannot be read.
        /// </summary>
        public ActionAsset? ResolveSynthesis(int effectNum, int type)
        {
            if (effectNum is < FirstSynthesisEffect or > LastSynthesisEffect) return null;
            var loader = Loader;
            if (loader == null) return null;
            string ending = SynthesisEnding(type);
            if (_synthesisCache.TryGetValue((effectNum, ending), out var cached)) return cached;
            var asset = _synthesisCache.GetOrAdd((effectNum, ending), key => LoadSynthesis(key.Effect, key.Ending, loader));
            if (asset?.Effects != null && _loader == null && EffectsPreloader is { } preload) Preload(preload, asset.FileId, asset.ToString());
            return asset;
        }

        private static ActionAsset? LoadSynthesis(int effectNum, string ending, Func<int, byte[]?> loader)
        {
            int fileId = SynthesisEffectFileBase + effectNum;
            try
            {
                var bytes = loader(fileId);
                if (bytes == null || bytes.Length == 0) return null;
                var container = EntityModelLoader.ParseDatContainer(bytes, $"Synthesis_{fileId}");
                var effects = EventSceneResource.Parse(bytes);
                int startTicks = 0, endTicks = 0;
                foreach (var raw in container.Routines)
                {
                    if (raw.Name == SynthesisStart) startTicks = raw.TotalTicks;
                }
                if (startTicks <= 0 || !effects.TryGetRoutine(ending, out var end)) return null;
                endTicks = end.TotalFrames;
                int stop = startTicks + endTicks;
                var routines = new List<RawMotionRoutine>(container.Routines);
                routines.RemoveAll(r => r.Name == ActionAsset.MainRoutine);
                routines.Add(new RawMotionRoutine
                {
                    Name = ActionAsset.MainRoutine,
                    TotalTicks = stop,
                    Commands = new[] { Link(0, SynthesisStart), Link(startTicks, ending) },
                    SoundCommands = new[] { new RoutineSoundCommand(0x03, 0, SynthesisStart, 0, -1), new RoutineSoundCommand(0x03, startTicks, ending, 0, -1) },
                });
                effects.AddRoutine(new SceneRoutine
                {
                    Name = ActionAsset.MainRoutine,
                    TotalFrames = stop,
                    Commands = new[]
                    {
                        new SceneCommand(0x03, SceneCommandKind.StartRoutine, 0, 0, SynthesisStart, 0),
                        new SceneCommand(0x03, SceneCommandKind.StartRoutine, startTicks, 0, ending, 0),
                        new SceneCommand(0x03, SceneCommandKind.StartRoutine, stop, 0, SynthesisStop, 0),
                    },
                });
                var motion = new EventMotionBank(fileId, container.Animations, routines);
                return new ActionAsset(ActionAssetKind.Synthesis, effectNum, fileId, motion, effects) { HoldsBodyToEnd = true };
            }
            catch (Exception ex)
            {
                Gordian.Core.Diagnostics.GordianLog.Warning("ANIM", $"Synthesis effect 0x{effectNum:X2} (file {fileId}) could not be read: {ex.Message}");
                return null;
            }

            static MotionRoutineCommand Link(int tick, string name) => new(0x03, tick, 0, name, 0, 0, 0, 1f, 0, 0);
        }

        /// <summary>
        /// The body file of weapon skill <paramref name="animation"/> for a race, or with <paramref name="companion"/> 1 / 2 its
        /// waist companion A / B; -1 outside the banks or for a race the banks do not hold.
        /// </summary>
        public static int WeaponSkillFileId(CharacterRace race, int animation, int companion = 0)
        {
            int raceIndex = CharacterEquipmentResolver.GetRetailRaceIndex(race);
            if (raceIndex < 0 || companion is < 0 or > 2) return -1;
            if (animation is >= 0 and < ExtendedWeaponSkillFirst) return PrimaryWeaponSkillBase[raceIndex] + animation + companion * 256;
            if (animation is >= ExtendedWeaponSkillFirst and < ExtendedWeaponSkillEnd)
            {
                return ExtendedWeaponSkillBase[raceIndex] + (animation - ExtendedWeaponSkillFirst) + companion * 16;
            }
            return -1;
        }

        /// <summary>The presentation file of monster animation <paramref name="animation"/>, or -1 when its range is not known.</summary>
        public static int MonsterSkillFileId(int animation) => animation switch
        {
            > 0 and < 512 => MonsterSkillFileBase + animation,
            >= 512 and <= MaxMonsterSkillAnimation => MonsterSkillSecondFileBase + animation - 512,
            _ => -1
        };

        /// <summary>The presentation file of job ability animation <paramref name="animation"/>, or -1.</summary>
        public static int JobAbilityFileId(int animation) =>
            animation is >= 0 and <= MaxJobAbilityAnimation ? JobAbilityFileBase + animation : -1;

        /// <summary>The presentation file of spell animation <paramref name="animation"/>, or -1.</summary>
        public static int SpellFileId(int animation) =>
            animation is > 0 and <= MaxSpellAnimation ? SpellFileBase + animation : -1;

        /// <summary>The kind of presentation a finish packet plays, or null for the categories that have none.</summary>
        public static ActionAssetKind? KindOf(ActionCategory category) => category switch
        {
            ActionCategory.SkillFinish => ActionAssetKind.WeaponSkill,
            ActionCategory.MobSkillFinish or ActionCategory.PetSkillFinish => ActionAssetKind.MonsterSkill,
            ActionCategory.AbilityFinish or ActionCategory.Dancer or ActionCategory.RuneFencer => ActionAssetKind.JobAbility,
            ActionCategory.MagicFinish => ActionAssetKind.Spell,
            _ => null
        };

        /// <summary>
        /// The presentation of a finish packet's animation on an actor of <paramref name="race"/> (only weapon skills depend
        /// on it; <see cref="CharacterRace.Unknown"/> for a fixed-model actor, which has no weapon skill bank), or null when
        /// the category has none, the file is not known or cannot be read. Cached, including misses, once a loader is set.
        /// </summary>
        public ActionAsset? Resolve(ActionCategory category, int animation, CharacterRace race)
        {
            if (KindOf(category) is not { } kind) return null;
            var loader = Loader;
            if (loader == null) return null;
            int raceKey = kind == ActionAssetKind.WeaponSkill ? CharacterEquipmentResolver.GetRetailRaceIndex(race) : -1;
            if (kind == ActionAssetKind.WeaponSkill && raceKey < 0) return null;
            // Both Tarutaru load one file.
            if (raceKey == 5) raceKey = 4;
            if (_cache.TryGetValue((kind, raceKey, animation), out var cached)) return cached;
            var asset = _cache.GetOrAdd((kind, raceKey, animation), key => Load(key.Kind, key.Animation, race, loader));
            if (asset?.Effects != null && _loader == null && EffectsPreloader is { } preload) Preload(preload, asset.FileId, asset.ToString());
            return asset;
        }

        /// <summary>
        /// Decodes a presentation's particle effects on the thread pool, so neither the network thread (which would hold
        /// every packet behind it) nor the renderer waits for it.
        /// </summary>
        private static void Preload(Action<int> preload, int fileId, string label) =>
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    preload(fileId);
                }
                catch (Exception ex)
                {
                    Gordian.Core.Diagnostics.GordianLog.Warning("ANIM", $"Effects of {label} could not be preloaded: {ex.Message}");
                }
            });

        /// <summary>
        /// The effects of spell animation <paramref name="animation"/> for an op 0x19 (a Trust's <c>spop</c>), or null.
        /// Spell presentations do not depend on the race.
        /// </summary>
        public ActionAsset? ResolveSpellEffect(int animation) => Resolve(ActionCategory.MagicFinish, animation, CharacterRace.Unknown);

        private static ActionAsset? Load(ActionAssetKind kind, int animation, CharacterRace race, Func<int, byte[]?> loader)
        {
            int fileId = kind switch
            {
                ActionAssetKind.WeaponSkill => WeaponSkillFileId(race, animation),
                ActionAssetKind.MonsterSkill => MonsterSkillFileId(animation),
                ActionAssetKind.JobAbility => JobAbilityFileId(animation),
                _ => SpellFileId(animation)
            };
            if (fileId < 0) return null;
            try
            {
                var bytes = loader(fileId);
                if (bytes == null || bytes.Length == 0) return null;
                var container = EntityModelLoader.ParseDatContainer(bytes, $"Action_{fileId}");
                var clips = new List<AnimationClip>(container.Animations);
                if (kind == ActionAssetKind.WeaponSkill && loader(WeaponSkillFileId(race, animation, companion: 1)) is { Length: > 0 } waist)
                {
                    // The waist part (part 2) of the body's clips: b002 beside b000 / b001, joined into b00 by the bank.
                    clips.AddRange(EntityModelLoader.ParseDatContainer(waist, $"Action_{fileId}_Waist").Animations);
                }
                var motion = container.Routines.Count > 0 ? new EventMotionBank(fileId, clips, container.Routines) : null;
                var effects = EventSceneResource.Parse(bytes);
                if (motion == null && effects.Routines.Count == 0) return null;
                return new ActionAsset(kind, animation, fileId, motion, effects);
            }
            catch (Exception ex)
            {
                Gordian.Core.Diagnostics.GordianLog.Warning("ANIM", $"{kind} animation {animation} (file {fileId}) could not be read: {ex.Message}");
                return null;
            }
        }
    }
}
