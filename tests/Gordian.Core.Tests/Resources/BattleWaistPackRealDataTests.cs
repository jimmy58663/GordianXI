// tests/Gordian.Core.Tests/Resources/BattleWaistPackRealDataTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Tests.Resources
{
    /// <summary>
    /// #546 round 6: each battle pack's waist pack (<see cref="CharacterEquipmentResolver.GetBattleWaistPackPath"/>) holds
    /// the whole-body reaction poses <c>gdi6</c> (guard and block) and <c>pyi6</c> (parry), and its draw waist parts match
    /// the pack's draws. Skipped without the game install.
    /// </summary>
    public class BattleWaistPackRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static readonly CharacterRace[] Races =
        [
            CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.ElvaanFemale,
            CharacterRace.TaruMale, CharacterRace.Mithra, CharacterRace.Galka,
        ];

        private static HashSet<string> Clips(ResourceManager rm, string path) =>
            rm.LoadDatBytes(path) is { Length: > 0 } bytes
                ? EntityModelLoader.ParseDatContainer(bytes, path).Animations.Select(a => a.Name).ToHashSet(StringComparer.Ordinal)
                : [];

        /// <summary>
        /// Every race's battle packs: the waist pack (or, on Tarutaru, the pack itself) has the guard pose <c>gdi6</c>, and the
        /// waist pack's draw parts (<c>in?2</c>) are the pack's draws (<c>in?1</c>), so it is that pack's. The parry pose
        /// <c>pyi6</c> is there for every pack but a few that have no parry of their own (listed in the output).
        /// </summary>
        [Fact]
        public void EveryBattlePack_HasItsWaistPack_WithTheReactionPoses()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var failures = new List<string>();
            int checkedPacks = 0, withParry = 0;
            foreach (var race in Races)
            {
                foreach (var pack in Enumerable.Range(0, 16).Select(t => (Type: t, Path: CharacterEquipmentResolver.GetBattlePackPath(race, t))).DistinctBy(p => p.Path))
                {
                    var packClips = Clips(rm, pack.Path);
                    string waistPath = CharacterEquipmentResolver.GetBattleWaistPackPath(race, pack.Type);
                    var waistClips = Clips(rm, waistPath);
                    checkedPacks++;
                    bool own = packClips.Contains("gdi6");
                    if (!own && !waistClips.Contains("gdi6")) failures.Add($"{race} {pack.Path} -> {waistPath}: no gdi6");
                    if (packClips.Contains("pyi6") || waistClips.Contains("pyi6")) withParry++;
                    if (own) continue;
                    var draws = packClips.Where(n => n.Length == 4 && n.StartsWith("in", StringComparison.Ordinal) && n[3] == '1').Select(n => n[..3]).ToHashSet();
                    var waistDraws = waistClips.Where(n => n.Length == 4 && n.StartsWith("in", StringComparison.Ordinal) && n[3] == '2').Select(n => n[..3]).ToHashSet();
                    if (!draws.SetEquals(waistDraws)) failures.Add($"{race} {pack.Path} -> {waistPath}: draws {string.Join(",", draws)} vs {string.Join(",", waistDraws)}");
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Assert.True(checkedPacks >= 60, $"{checkedPacks} packs");
            Assert.True(withParry >= checkedPacks - 4, $"{withParry} of {checkedPacks} packs have a parry pose");
        }
    }
}
