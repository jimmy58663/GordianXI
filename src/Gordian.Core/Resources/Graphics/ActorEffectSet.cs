// src/Gordian.Core/Resources/Graphics/ActorEffectSet.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// The particle effects a model DAT carries for its actor: every Section 0x05 generator that draws a particle mesh or
    /// sprite sheet (as an emitter layer), the textures they sample, and the Section 0x07 routines the client plays on the
    /// actor. Effect-only models such as the Home Point crystal (model 51, <c>ROM/3/25.DAT</c>) are drawn entirely by
    /// their auto-running generators; their skeleton mesh is a placeholder.
    /// Effect-only entities referenced from xi-tools (docs/dats/ROM_3_25.md, docs/entity/npc-look.md).
    /// </summary>
    public sealed class ActorEffectSet
    {
        public ActorEffectSet(
            IReadOnlyList<WeatherSkyLayer> layers,
            IReadOnlyList<WeatherRoutineVariant> routines,
            IReadOnlyDictionary<string, DecodedTexture> textures)
        {
            Layers = layers ?? throw new ArgumentNullException(nameof(layers));
            Textures = textures ?? throw new ArgumentNullException(nameof(textures));
            var byName = new Dictionary<string, WeatherRoutineVariant>(StringComparer.OrdinalIgnoreCase);
            foreach (var routine in routines ?? throw new ArgumentNullException(nameof(routines))) byName.TryAdd(routine.DatId, routine);
            Routines = byName;
        }

        /// <summary>
        /// Emitter layers (child-only layers included), each with its <see cref="WeatherSkyLayer.Emitter"/> template and
        /// display-space mesh groups.
        /// </summary>
        public IReadOnlyList<WeatherSkyLayer> Layers { get; }

        /// <summary>
        /// The model's routines by FourCC (e.g. <c>aper</c> idle, <c>bind</c> Home Point activation).
        /// </summary>
        public IReadOnlyDictionary<string, WeatherRoutineVariant> Routines { get; }

        public IReadOnlyDictionary<string, DecodedTexture> Textures { get; }

        /// <summary>The routine a state-driven model plays for its idle state (<see cref="StateTriggered"/>).</summary>
        public const string IdleStateRoutine = "ids0";

        /// <summary>
        /// Auto-running generators that wait for a routine to start them instead of emitting from spawn (#257): in a model
        /// whose effects follow its state, those its routines start. Empty for other models.
        /// <para>
        /// A model is state-driven when it has the state routines <c>ids0</c>, <c>ids1</c>...: its <c>init</c> links a
        /// state routine (<c>mse0</c>-<c>mse3</c>) that picks one of them by the actor's state (ops 0x69 / 0x6B / 0x64 /
        /// 0x67 / 0x6A, not read yet). Their auto-run flag does not mean idle there: Synthesis Focuser II (model 2335,
        /// <c>ROM/.../52630</c>) flags all fifteen of its blue, yellow and red generators <c>md_s/?gp0</c>-<c>?fl0</c>
        /// auto-running, and <c>ids1</c> / <c>ids2</c> / <c>ids3</c> start one colour each (<c>bslp</c>, <c>yslp</c>,
        /// <c>rslp</c>) while <c>ids0</c> kills them all (<c>alkl</c>); the Synergy Furnace (model 2348, file 52643) flags
        /// 52 of its 163 synthesis effects <c>ren_/8???</c> auto-running, and its <c>ids0</c> stops <c>s112</c> /
        /// <c>s013</c> and kills the rest (<c>0kil</c>). Drawn from spawn, all three colours of orbs showed at once on an
        /// idle focuser, where the maintainer's retail capture of an idle furnace (Aht Urhgan Whitegate, 2026-10-17) shows
        /// no effect at all. A scan of models 1-3499 (2026-10-18) finds 437 models with auto-running drawn generators, 412
        /// of them started by a routine as well, 153 of those with <c>ids0</c>; the others (the Home Point's idle
        /// <c>bind</c> layers, which its <c>aper</c> also starts; the elementals' <c>aper</c>) keep emitting from spawn.
        /// *Inference* from the data; which state values select which routine is not known.
        /// </para>
        /// </summary>
        public IReadOnlySet<ZoneEmitterTemplate> StateTriggered { get; init; } = new HashSet<ZoneEmitterTemplate>();

        /// <summary>
        /// The routines a state-driven model's idle state routine <c>ids0</c> links (op 0x03, in its order), which the actor
        /// plays on appearing (#425); empty for other models. <c>ids0</c> is what the state routine <c>mse0</c> picks when the
        /// state it tests is not 1 (<c>mse1</c> is its mirror), and <c>init</c> / <c>pop0</c> link <c>mse0</c>.
        /// <para>
        /// The Yahse Hunting Grounds campfire (model 2411, file 52706, directory <c>taki</c>) is drawn by nothing else: its skin
        /// mesh is a 3-vertex placeholder, and its <c>ids0</c> links <c>s000</c> (the ground shadow <c>kag0</c>) and <c>s001</c>
        /// (the flames <c>hon0</c>, embers <c>kk00</c>-<c>kk02</c>, lights <c>lt00</c>-<c>lt02</c>, <c>caf0</c> and the crackle
        /// sound <c>2056</c>), while <c>ids1</c> links <c>st01</c>, which stops them. For the crafting furnaces and focusers
        /// <c>ids0</c> links only their kill routines (<c>0kil</c>, <c>alkl</c>), so they stay dark at rest, as retail shows.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> IdleStateLinks { get; init; } = Array.Empty<string>();

        /// <summary>Prefix of the routine a weapon's model DAT plays while the weapon is equipped (<see cref="EquippedRoutines"/>).</summary>
        public const string EquipRoutinePrefix = "!in";

        /// <summary>
        /// The routines that run while the weapon this DAT models is equipped (#8), reached from its <c>!in?</c> routine
        /// through the routines it starts: <see cref="EffectRoutineStart.Repeats"/> for one that op 0x73 repeats at its own
        /// length until the weapon comes off, once otherwise. Empty for every DAT without an <c>!in?</c> routine.
        /// <para>
        /// The Afterglow of the REMA and Prime weapons. Every such weapon DAT (read 2026-10-29, all three weapon slots of
        /// the Hume male's model ids 0-1499) carries one directory with two generators, <c>gp00</c> (particle mesh
        /// <c>pi1</c>) and <c>fr00</c> (mesh <c>flm</c>), both SourceActor at skeleton reference 43, and the routines
        /// <c>!in1</c> (op 0x03 <c>auon</c>, which repeats <c>lop0</c> with op 0x73), <c>!kl1</c> (op 0x03 <c>aukl</c>,
        /// which stops both generators with op 0x2D, and op 0x5F on <c>!in1</c>) and <c>lop0</c> (spawns <c>gp00</c> for
        /// 50 frames and <c>fr00</c> for 56, 56 frames long). Sub-slot DATs name them <c>!in3</c> / <c>!kl3</c> /
        /// <c>auo1</c> / <c>auk1</c> / <c>lop1</c> (<c>gp01</c>, <c>fr01</c>), ranged ones <c>!in2</c> / <c>!kl2</c> /
        /// <c>auo2</c> / <c>auk2</c> / <c>lop2</c>. Only the generators' base colours differ, one per family: Relic
        /// (0x80, 0x80, 0x76) white, Mythic (0x62, 0x76, 0x80) blue, Empyrean (0x80, 0x4E, 0x4E) red, Ergon
        /// (0x4E, 0x80, 0x4E) green, Prime (0x80, 0x12, 0x80) purple. *Inference*: that the client plays <c>!in?</c> when
        /// the weapon is equipped and <c>!kl?</c> when it comes off (no reference names these routines; the maintainer's
        /// account of retail is that the glow comes from having the weapon equipped). Other effect weapons use
        /// <c>!w00</c>-<c>!w02</c> (kill, repeat, stop their loop), which are not read.
        /// </para>
        /// </summary>
        public IReadOnlyList<EffectRoutineStart> EquippedRoutines { get; init; } = Array.Empty<EffectRoutineStart>();
    }

    /// <summary>
    /// Builds an <see cref="ActorEffectSet"/> from a model DAT with the zone effect parser in actor mode.
    /// </summary>
    public static class ActorEffectLoader
    {
        /// <summary>
        /// Returns the model's effects, or null when it has no generator that draws anything. With
        /// <paramref name="cameraEffects"/> (a cutscene scene DAT, played by <c>SceneEffectPlayer</c>) the
        /// camera-following generators are kept as well: the scene draws them in front of the camera.
        /// </summary>
        public static ActorEffectSet? Load(ReadOnlySpan<byte> datBytes, SharedEffectResources? sharedEffects = null, string? datSource = null, bool cameraEffects = false)
        {
            if (datBytes.IsEmpty) return null;
            // Most models carry no generators: skip the full parse (and a second decode of their body textures).
            bool hasGenerator = false, stateDriven = false;
            var idleLinks = new List<string>();
            string? equipRoutine = null;
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (equipRoutine == null && header.TypeCode == DatSectionType.EffectRoutine &&
                    header.DatId.StartsWith(ActorEffectSet.EquipRoutinePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    equipRoutine = header.DatId;
                }
                if (header.TypeCode == DatSectionType.ParticleGenerator) hasGenerator = true;
                // ids0 starts nothing itself, so the parse below (routines with spawns) does not list it.
                else if (header.TypeCode == DatSectionType.EffectRoutine && string.Equals(header.DatId, ActorEffectSet.IdleStateRoutine, StringComparison.OrdinalIgnoreCase))
                {
                    stateDriven = true;
                    ReadRoutineLinks(datBytes.Slice(header.DataOffset, header.DataSizeBytes), idleLinks);
                }
            }
            if (!hasGenerator) return null;

            // A weapon's equip routine (#8) and the routines it leads to.
            Dictionary<string, EffectRoutine>? routinesByName = null;
            if (equipRoutine != null)
            {
                routinesByName = new Dictionary<string, EffectRoutine>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
                {
                    if (header.TypeCode == DatSectionType.EffectRoutine &&
                        EffectRoutineDecoder.Decode(datBytes.Slice(header.DataOffset, header.DataSizeBytes), header.DatId) is { } routine)
                    {
                        routinesByName.TryAdd(header.DatId, routine);
                    }
                }
            }

            var decoded = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var parsed = ZoneDataLoader.ParseZoneContainer(datBytes, zoneId: -1, outTextures: decoded, sharedEffects: sharedEffects, actorEffects: true, datSource: datSource, cameraEffects: cameraEffects);

            var layers = new List<WeatherSkyLayer>();
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            foreach (var layer in parsed.EffectLayers)
            {
                if (layer.Emitter == null || layer.MeshGroups.Count == 0) continue;
                layers.Add(layer);
                // Keep only the textures the effect meshes sample, under the trimmed name the texture cache looks up.
                foreach (var group in layer.MeshGroups)
                {
                    string name = group.TextureName.Trim();
                    if (name.Length > 0 && !textures.ContainsKey(name) && FindTexture(decoded, name) is { } texture) textures[name] = texture;
                }
            }
            if (layers.Count == 0) return null;
            var stateTriggered = new HashSet<ZoneEmitterTemplate>(ReferenceEqualityComparer.Instance);
            if (stateDriven)
            {
                foreach (var routine in parsed.ActorRoutines)
                {
                    foreach (var spawn in routine.Spawns)
                    {
                        if (spawn.Template.Definition.AutoRun) stateTriggered.Add(spawn.Template);
                    }
                }
            }
            return new ActorEffectSet(layers, parsed.ActorRoutines, textures)
            {
                StateTriggered = stateTriggered,
                IdleStateLinks = stateDriven ? idleLinks : Array.Empty<string>(),
                EquippedRoutines = equipRoutine != null && routinesByName != null
                    ? EquippedRoutineChain(equipRoutine, routinesByName)
                    : Array.Empty<EffectRoutineStart>(),
            };
        }

        /// <summary>
        /// The routines an equip routine (<see cref="ActorEffectSet.EquippedRoutines"/>) leads to: each routine it starts
        /// once is followed in turn (to a few levels), and each it repeats is listed with <c>Repeats</c>, as is any routine
        /// on the way that spawns generators itself.
        /// </summary>
        internal static IReadOnlyList<EffectRoutineStart> EquippedRoutineChain(string root, IReadOnlyDictionary<string, EffectRoutine> routines)
        {
            var chain = new List<EffectRoutineStart>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Walk(string id, int startFrame, bool repeats, int depth)
            {
                if (depth > 4 || !seen.Add(id) || !routines.TryGetValue(id, out var routine)) return;
                if (repeats || routine.Spawns.Count > 0) chain.Add(new EffectRoutineStart(id, startFrame, repeats));
                if (repeats) return;
                foreach (var start in routine.Starts) Walk(start.RoutineId, startFrame + start.StartFrame, start.Repeats, depth + 1);
            }
            Walk(root, 0, false, 0);
            return chain;
        }

        /// <summary>
        /// Appends the routines a Section 0x07 routine links in its own DAT (op 0x03, the 4-char reference at +8), in command
        /// order. Command layout as <see cref="MotionRoutineDecoder"/> (xi-tools docs/fx/effect_system.md).
        /// </summary>
        private static readonly char[] Pad = { '\0', ' ' };

        internal static void ReadRoutineLinks(ReadOnlySpan<byte> payload, List<string> links)
        {
            if (payload.Length < 0x20) return;
            int p = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - DatSectionHeader.HeaderSize;
            if (p < 0) return;
            while (p + 8 <= payload.Length)
            {
                byte op = payload[p];
                if (op == 0x00) break;
                int size = Math.Max(1, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F) * 4;
                if (op == 0x03 && p + 12 <= payload.Length)
                {
                    string reference = System.Text.Encoding.ASCII.GetString(payload.Slice(p + 8, 4)).TrimEnd(Pad);
                    if (reference.Length > 0) links.Add(reference);
                }
                p += size;
            }
        }

        /// <summary>A decoded texture by its trimmed name, or one whose name ends with it (or it with theirs).</summary>
        internal static DecodedTexture? FindTexture(Dictionary<string, DecodedTexture> decoded, string name)
        {
            if (decoded.TryGetValue(name, out var exact)) return exact;
            foreach (var (key, texture) in decoded)
            {
                if (key.EndsWith(name, StringComparison.OrdinalIgnoreCase) || name.EndsWith(key, StringComparison.OrdinalIgnoreCase)) return texture;
            }
            return null;
        }
    }
}
