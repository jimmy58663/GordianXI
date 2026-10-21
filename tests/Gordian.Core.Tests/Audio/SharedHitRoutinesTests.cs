// tests/Gordian.Core.Tests/Audio/SharedHitRoutinesTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>The shared <c>ROM/0/0</c> hit routines' conditionals and the sounds they reach (#328).</summary>
    public class SharedHitRoutinesTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public SharedHitRoutinesTests(ITestOutputHelper output) => _output = output;

        private static byte[] Command(byte op, params byte[] body)
        {
            int size = 8 + body.Length;
            var bytes = new byte[size];
            bytes[0] = op;
            bytes[1] = (byte)(size / 4);
            body.CopyTo(bytes, 8);
            return bytes;
        }

        private static byte[] Name(string name) => [.. System.Text.Encoding.ASCII.GetBytes(name.PadRight(4, '\0')), 0, 0, 0, 0];
        private static byte[] PushRegister(int register) => Command(0x6B, 0x1C, 0, 3, 0, (byte)register, 0, 0, 0);
        private static byte[] PushConstant(int value) => Command(0x6B, 0x1C, 0, 1, 0, (byte)value, 0, 0, 0);
        private static byte[] Operator(int op) => Command(0x6B, (byte)op, 0, 0, 0);

        private static byte[] Routine(params byte[][] commands)
        {
            var body = commands.SelectMany(c => c).Concat(new byte[8]).ToArray();
            var payload = new byte[0x20 + body.Length];
            System.BitConverter.GetBytes(0x20 + 16).CopyTo(payload, 0x14);
            body.CopyTo(payload, 0x20);
            return payload;
        }

        /// <summary>
        /// if (r28 == 1) { sound a } else { if (r2B &amp; 2) { generator b } }, as <c>dam0</c> / <c>crtl</c> are built; links followed.
        /// </summary>
        [Fact]
        public void Conditionals_PickTheBranch_ByTheResultFields()
        {
            var routines = new Dictionary<string, byte[]>
            {
                ["dada"] = Routine(Command(0x03, Name("pick"))),
                ["pick"] = Routine(
                    Command(0x69, 0, 0, 0, 0),
                    PushRegister(0x28), PushConstant(1), Operator(0x0C), Command(0x64, 0, 0, 0, 0),
                    Command(0x69, 0, 0, 0, 0), Command(0x0A, Name("snda")), Command(0x6A, 0, 0, 0, 0),
                    Command(0x67, 0, 0, 0, 0),
                    Command(0x69, 0, 0, 0, 0),
                    PushRegister(0x2B), PushConstant(2), Operator(0x11), Command(0x64, 0, 0, 0, 0),
                    Command(0x69, 0, 0, 0, 0), Command(0x02, Name("genb")), Command(0x6A, 0, 0, 0, 0),
                    Command(0x6A, 0, 0, 0, 0),
                    Command(0x6A, 0, 0, 0, 0))
            };
            var shared = SharedHitRoutines.FromPayloads(routines, n => n == "snda" ? 100 : 0, n => n == "genb" ? 200 : 0);

            Assert.Equal([100], shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Miss }));
            Assert.Equal([200], shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Hit, Info = 3 }));
            Assert.Empty(shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Hit, Info = 1 }));
        }

        /// <summary>
        /// On the retail file: a normal hit reaches no shared sound (<c>atpr</c>'s <c>wapr</c> has none), a critical hit its
        /// <c>crtl</c> sounds, an Enfire additional effect <c>hit3</c>'s, Blaze Spikes <c>sb00</c>'s (5045).
        /// </summary>
        [Fact]
        public void Retail_SoundsByResult()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var shared = SharedHitRoutines.Parse(rm.LoadDatBytes(Path.Combine("ROM", "0", "0.DAT"))!);
            Assert.True(shared.HasRoutine("dada"));

            var plain = shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Hit });
            var critical = shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Hit, Info = 2 });
            var enfire = shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Hit, HasProc = true, ProcKind = (ActionProcAddEffect)1 });
            var spikes = shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Hit, HasReaction = true, ReactionKind = ActionReactKind.BlazeSpikes });
            _output.WriteLine($"plain [{string.Join(",", plain)}] critical [{string.Join(",", critical)}] enfire [{string.Join(",", enfire)}] spikes [{string.Join(",", spikes)}]");

            Assert.Empty(plain);
            Assert.NotEmpty(critical);
            Assert.NotEmpty(enfire);
            Assert.Contains(5045, spikes);
            Assert.Empty(shared.MeleeHitSounds(new CombatActionResult { Resolution = ActionResolution.Miss }));
        }
    }
}
