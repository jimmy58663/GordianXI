// src/Gordian.Core/Resources/Graphics/SharedHitRoutines.cs
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Events;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// The shared hit routines of <c>ROM/0/0.DAT</c> a melee hit runs (#328): every swing links <c>dada</c> at its hit,
    /// which links <c>atpr</c> (the additional effect), <c>crtl</c> (the critical hit) and <c>dam0</c> (the target's
    /// reaction and spikes). They choose by conditional commands on action-result registers; this class evaluates them for
    /// one result and collects the sounds they reach: sound commands (op 0x0A / 0x0B / 0x4A / 0x53 / 0x60, a 0x3D pointer
    /// name at +8) and sound generators started with op 0x02 (a generator whose linked data is a 0x3D pointer).
    /// <para>
    /// Conditionals (op names from xi-tools docs/reference/ps2_beta_2001.md, 2001 PS2 client: 0x64 <c>IdleIf</c>, 0x67
    /// <c>IdleElse</c>, 0x69 <c>IdleNestIn</c>, 0x6A <c>IdleNestOut</c>, 0x6B <c>IdleInstruction</c>; xi-tools
    /// docs/fx/effect_system.md "conditional branch / condition, regs"). The 0x6B layout was read from the retail file
    /// (2026-10-18): 16 bytes <c>u16 0x1C, u16 kind, u32 value</c> pushes a register (kind 3) or a constant (kind 1); 12 bytes
    /// <c>u32 operator</c> pops two and pushes the result: 0x0C equal, 0x11 bit test, 0x0F used once (<c>atpr</c>'s "register
    /// 0x2F against 0", read as less or equal). 0x64 pops a condition and runs the 0x69 ... 0x6A block after it, else the
    /// block after a following 0x67. <b>Beyond xi-tools</b>, which names the ops but not their layout.
    /// </para>
    /// <para>
    /// Registers, matched to the S2C 0x028 result fields (XiPackets world/server/0x0028) by the values the routines test
    /// (<b>Beyond XiPackets</b>): 0x28 <c>miss</c> (<c>dam0</c>: 0 → <c>damg</c>, 1 → <c>sway</c>, 2 → <c>gurd</c>, 3 →
    /// <c>pary</c>, 4 → <c>gur1</c>, the hit, miss, guard, parry and block reactions), 0x2B <c>info</c> (<c>crtl</c> tests
    /// bit 2: info 2 / 3 is a critical hit), 0x2F <c>proc_kind</c> (<c>atpr</c>: 0 → <c>wapr</c>, 1-23 → the additional
    /// effect hits <c>hit3</c>... in the order of XiPackets' proc list, Enfire first), 0x33 <c>react_kind</c> (<c>dam0</c>:
    /// 1-10 → the spikes <c>sb00</c>-<c>sb09</c>, 63 → <c>cnt0</c>, LandSandBoat's counter). Not known, read as 0: 0x38
    /// (<c>crtl</c> picks <c>hi14</c> when set, else <c>hi29</c>), 0x3B (<c>damh</c> instead of <c>damg</c>), 0x3D
    /// (<c>nrm0</c>).
    /// </para>
    /// </summary>
    public sealed class SharedHitRoutines
    {
        /// <summary>The routine every melee swing links at its hit.</summary>
        public const string MeleeHitRoutine = "dada";

        private const int MaxDepth = 6;
        private const byte OpGenerator = 0x02;
        private const byte OpLink = 0x03;
        private const byte OpLinkTarget = 0x09;
        private const byte OpLinkWait = 0x3B;
        private const byte OpIf = 0x64;
        private const byte OpElse = 0x67;
        private const byte OpNestIn = 0x69;
        private const byte OpNestOut = 0x6A;
        private const byte OpInstruction = 0x6B;

        private readonly record struct Command(byte Op, string Name, int Kind, uint Value, int Size);

        private readonly Dictionary<string, Command[]> _routines;
        private readonly Func<string, int> _sound;
        private readonly Func<string, int> _generatorSound;
        private readonly ConcurrentDictionary<(int, int, int, int), IReadOnlyList<int>> _cache = new();

        private SharedHitRoutines(Dictionary<string, Command[]> routines, Func<string, int> sound, Func<string, int> generatorSound)
        {
            _routines = routines;
            _sound = sound;
            _generatorSound = generatorSound;
        }

        /// <summary>Reads the routines and sounds of <c>ROM/0/0.DAT</c> (the first routine of each name counts).</summary>
        public static SharedHitRoutines Parse(ReadOnlySpan<byte> sharedDat)
        {
            var scene = EventSceneResource.Parse(sharedDat);
            var routines = new Dictionary<string, Command[]>(StringComparer.Ordinal);
            foreach (var header in DatSectionWalker.ReadHeaders(sharedDat))
            {
                if (header.RawTypeCode != 0x07 || routines.ContainsKey(header.DatId)) continue;
                if (header.DataOffset + header.DataSizeBytes > sharedDat.Length) continue;
                if (Decode(sharedDat.Slice(header.DataOffset, header.DataSizeBytes)) is { } commands) routines[header.DatId] = commands;
            }
            return new SharedHitRoutines(routines,
                name => scene.TryGetSound(name, out int id) ? id : 0,
                name => scene.TryGetGeneratorSound(name, out var g) ? g.SoundId : 0);
        }

        /// <summary>Builds from decoded routines (tests): <paramref name="routines"/> as raw section payloads by name.</summary>
        internal static SharedHitRoutines FromPayloads(IReadOnlyDictionary<string, byte[]> routines, Func<string, int> sound, Func<string, int> generatorSound)
        {
            var decoded = new Dictionary<string, Command[]>(StringComparer.Ordinal);
            foreach (var (name, payload) in routines)
            {
                if (Decode(payload) is { } commands) decoded[name] = commands;
            }
            return new SharedHitRoutines(decoded, sound, generatorSound);
        }

        /// <summary>Whether the file has routine <paramref name="name"/>.</summary>
        public bool HasRoutine(string name) => _routines.ContainsKey(name);

        /// <summary>The register values a result gives the shared routines (see the class remarks).</summary>
        public static int Register(int register, in CombatActionResult result) => register switch
        {
            0x28 => (int)result.Resolution,
            0x2B => result.Info,
            0x2F => result.HasProc ? (int)result.ProcKind : 0,
            0x33 => result.HasReaction ? (int)result.ReactionKind : 0,
            _ => 0,
        };

        /// <summary>
        /// The sounds a melee hit's shared routines play for <paramref name="result"/> (in file order, each a sound effect
        /// id), beside the reaction's own (<c>damg</c> and the attacker's <c>chit</c>, which the target's and attacker's
        /// models carry). Cached per distinct register values.
        /// </summary>
        public IReadOnlyList<int> MeleeHitSounds(in CombatActionResult result)
        {
            var key = (Register(0x28, result), Register(0x2B, result), Register(0x2F, result), Register(0x33, result));
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var copy = result;
            var sounds = new List<int>();
            Run(MeleeHitRoutine, r => Register(r, copy), sounds, 0);
            IReadOnlyList<int> list = sounds.Count == 0 ? Array.Empty<int>() : sounds.ToArray();
            _cache[key] = list;
            return list;
        }

        /// <summary>Runs routine <paramref name="name"/> with the given registers, collecting the sounds it reaches.</summary>
        internal void Run(string name, Func<int, int> registers, List<int> sounds, int depth)
        {
            if (depth > MaxDepth || !_routines.TryGetValue(name, out var commands)) return;
            var stack = new Stack<int>();
            int i = 0;
            Block(commands, ref i, registers, sounds, depth, stack, execute: true, topLevel: true);
        }

        private void Block(Command[] commands, ref int i, Func<int, int> registers, List<int> sounds, int depth, Stack<int> stack, bool execute, bool topLevel)
        {
            while (i < commands.Length)
            {
                var c = commands[i++];
                switch (c.Op)
                {
                    case OpNestOut:
                        if (!topLevel) return;
                        break;

                    case OpNestIn:
                        Block(commands, ref i, registers, sounds, depth, stack, execute, topLevel: false);
                        break;

                    case OpInstruction:
                        if (execute) Instruction(c, registers, stack);
                        break;

                    case OpIf:
                        bool condition = execute && stack.Count > 0 && stack.Pop() != 0;
                        if (i < commands.Length && commands[i].Op == OpNestIn)
                        {
                            i++;
                            Block(commands, ref i, registers, sounds, depth, stack, execute && condition, topLevel: false);
                        }
                        if (i < commands.Length && commands[i].Op == OpElse)
                        {
                            i++;
                            if (i < commands.Length && commands[i].Op == OpNestIn)
                            {
                                i++;
                                Block(commands, ref i, registers, sounds, depth, stack, execute && !condition, topLevel: false);
                            }
                        }
                        break;

                    case OpGenerator when execute:
                        int generated = _generatorSound(c.Name);
                        if (generated > 0) sounds.Add(generated);
                        break;

                    case OpLink or OpLinkWait when execute:
                        Run(c.Name, registers, sounds, depth + 1);
                        break;

                    case OpLinkTarget when execute && c.Name.StartsWith("sb", StringComparison.Ordinal):
                        // 0x09 runs a routine on the other actor: dam0's spikes sb00-sb09 are shared routines; its damg /
                        // sway / gurd / cnt0 are the target's own, whose sounds and motion the reaction already plays.
                        Run(c.Name, registers, sounds, depth + 1);
                        break;

                    default:
                        if (execute && RoutineSoundCollector.IsSoundOp(c.Op))
                        {
                            int id = _sound(c.Name);
                            if (id > 0) sounds.Add(id);
                        }
                        break;
                }
            }
        }

        private static void Instruction(Command c, Func<int, int> registers, Stack<int> stack)
        {
            if (c.Size >= 16)
            {
                stack.Push(c.Kind == 3 ? registers((int)c.Value) : (int)c.Value);
                return;
            }
            if (stack.Count < 2) return;
            int right = stack.Pop(), left = stack.Pop();
            stack.Push(c.Value switch
            {
                0x0C => left == right ? 1 : 0,
                0x0F => left <= right ? 1 : 0,
                0x11 => (left & right) != 0 ? 1 : 0,
                _ => 0,
            });
        }

        private static Command[]? Decode(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 0x20) return null;
            int p = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - 16;
            if (p < 0 || p >= payload.Length) return null;
            var commands = new List<Command>();
            while (p + 8 <= payload.Length)
            {
                byte op = payload[p];
                int size = Math.Max(1, BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F) * 4;
                if (op == 0) break;
                var command = payload.Slice(p, Math.Min(size, payload.Length - p));
                if (op == OpInstruction && command.Length >= 16)
                {
                    commands.Add(new Command(op, string.Empty, BinaryPrimitives.ReadUInt16LittleEndian(command.Slice(10)),
                        BinaryPrimitives.ReadUInt32LittleEndian(command.Slice(12)), 16));
                }
                else if (op == OpInstruction && command.Length >= 12)
                {
                    commands.Add(new Command(op, string.Empty, 0, BinaryPrimitives.ReadUInt32LittleEndian(command.Slice(8)), 12));
                }
                else
                {
                    commands.Add(new Command(op, command.Length >= 12 ? ReadName(command.Slice(8, 4)) : string.Empty, 0, 0, size));
                }
                p += size;
            }
            return commands.ToArray();
        }

        private static string ReadName(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            foreach (byte b in span.Slice(0, end))
            {
                if (b < 0x20 || b > 0x7E) return string.Empty;
            }
            return Encoding.ASCII.GetString(span.Slice(0, end));
        }
    }
}
