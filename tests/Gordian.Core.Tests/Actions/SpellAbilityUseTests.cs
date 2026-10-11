// tests/Gordian.Core.Tests/Actions/SpellAbilityUseTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Actions
{
    /// <summary>/ma, /ja, /ws by name with targets, the sub-target cursor, and the Magic and Abilities windows (#240, #353).</summary>
    public class SpellAbilityUseTests
    {
        private const uint SelfId = 0x01020304, HareId = 0x02020202, FriendId = 0x01030303;
        private const ushort SelfIndex = 10, HareIndex = 25, FriendIndex = 11;

        private readonly WorldState _world = new();
        private readonly LocalPlayerState _localPlayer = new() { ServerId = SelfId };
        private readonly CombatState _combat = new();
        private readonly List<byte[]> _sent = new();
        private readonly List<string> _notices = new();
        private readonly PlayerActionService _service;

        public SpellAbilityUseTests()
        {
            Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
            {
                lock (_sent) _sent.Add(mem.ToArray());
                return Task.CompletedTask;
            }
            var profile = new SessionProfile();
            _service = new PlayerActionService(profile, _world, _localPlayer,
                new CombatPacketModule(_combat, _localPlayer, Capture), new ChatPacketModule(Capture),
                new PartyPacketModule(new PartyState(), Capture), new EntityPacketModule(_world, _localPlayer, Capture),
                new LifecyclePacketModule(profile, Capture), Capture)
            {
                Catalog = TestCatalog.Build(),
            };
            _service.Menus.NoticePosted += n => { lock (_notices) _notices.Add(n); };
            _world.UpsertEntity(new WorldEntity(SelfId, SelfIndex, EntityType.Player) { Name = "Gemini", Position = new Vector3(10, 5, 20) });
            _world.UpsertEntity(new WorldEntity(HareId, HareIndex, EntityType.Monster) { Name = "Forest_Hare", Position = new Vector3(15, 5, 22), Hpp = 100 });
            _world.UpsertEntity(new WorldEntity(FriendId, FriendIndex, EntityType.Player) { Name = "Tarudrake", Position = new Vector3(12, 5, 20) });
        }

        /// <summary>The last C2S 0x01A sent: (target id, target index, action, param).</summary>
        private (uint Target, ushort Index, CliActionId Action, uint Param) LastAction()
        {
            byte[] packet;
            lock (_sent) packet = _sent.Last(p => (BinaryPrimitives.ReadUInt16LittleEndian(p) & 0x1FF) == 0x01A);
            return (BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)), BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)),
                (CliActionId)BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)), BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
        }

        private int SentCount
        {
            get
            {
                lock (_sent) return _sent.Count;
            }
        }

        private void Learn(params ushort[] spells)
        {
            var magic = new byte[128];
            foreach (ushort id in spells) magic[id >> 3] |= (byte)(1 << (id & 7));
            _localPlayer.UpdateFromMagicData(new S2C_0x0AA_MagicData(magic));
        }

        private void LearnCommands(ushort[] weaponSkills, ushort[] abilities)
        {
            var data = new byte[224];
            foreach (ushort id in weaponSkills) data[id >> 3] |= (byte)(1 << (id & 7));
            foreach (ushort id in abilities) data[64 + (id >> 3)] |= (byte)(1 << (id & 7));
            _localPlayer.UpdateFromCommandData(new S2C_0x0AC_CommandData(data));
        }

        [Fact]
        public async Task Trust_ByQuotedNameOnMe_CastsOnYourself()
        {
            var result = await _service.ExecuteCommandAsync("/ma \"Shantotto\" <me>");

            Assert.True(result.Success, result.Message);
            Assert.Equal((SelfId, SelfIndex, CliActionId.CastMagic, (uint)TestCatalog.Shantotto), LastAction());
            Assert.Contains("Shantotto", result.Message);
        }

        [Fact]
        public async Task SelfOnlyActionsWithoutTarget_GoOnYourself()
        {
            Assert.True((await _service.ExecuteCommandAsync("/ma shantotto")).Success);
            Assert.Equal((SelfId, SelfIndex, CliActionId.CastMagic, (uint)TestCatalog.Shantotto), LastAction());

            Assert.True((await _service.ExecuteCommandAsync("/ja Berserk")).Success);
            Assert.Equal((SelfId, SelfIndex, CliActionId.JobAbility, (uint)TestCatalog.Berserk), LastAction());
        }

        [Fact]
        public async Task Targets_PlaceholdersNamesAndUnquotedMultiWordNames()
        {
            _service.SetTargetByName("Forest_Hare");
            Assert.True((await _service.ExecuteCommandAsync("/ws \"fast blade\" <t>")).Success);
            Assert.Equal((HareId, HareIndex, CliActionId.Weaponskill, (uint)TestCatalog.FastBlade), LastAction());

            Assert.True((await _service.ExecuteCommandAsync("/ws Fast Blade <t>")).Success);
            Assert.Equal((HareId, HareIndex, CliActionId.Weaponskill, (uint)TestCatalog.FastBlade), LastAction());

            // An unquoted name followed by a target name: the last word is the target.
            Assert.True((await _service.ExecuteCommandAsync("/ja provoke Forest_Hare")).Success);
            Assert.Equal((HareId, HareIndex, CliActionId.JobAbility, (uint)TestCatalog.Provoke), LastAction());

            Assert.True((await _service.ExecuteCommandAsync("/ma Cure Tarudrake")).Success);
            Assert.Equal((FriendId, FriendIndex, CliActionId.CastMagic, (uint)TestCatalog.Cure), LastAction());

            // Numbers still work.
            Assert.True((await _service.ExecuteCommandAsync("/ma 1 <me>")).Success);
            Assert.Equal((SelfId, SelfIndex, CliActionId.CastMagic, 1u), LastAction());
        }

        [Fact]
        public async Task UnknownNamesAndTargets_SendNothing()
        {
            int before = SentCount;
            var unknown = await _service.ExecuteCommandAsync("/ma \"Cure XII\" <me>");
            Assert.False(unknown.Success);
            Assert.Contains("Unknown spell", unknown.Message);

            var noTarget = await _service.ExecuteCommandAsync("/ws \"Fast Blade\" <t>");
            Assert.False(noTarget.Success);
            Assert.Equal("Nothing is targeted.", noTarget.Message);

            var notHere = await _service.ExecuteCommandAsync("/ma \"Cure\" Nobody");
            Assert.False(notHere.Success);
            Assert.Equal(before, SentCount);
        }

        [Fact]
        public async Task SubTargetCursor_StartsOnTheTargetMovesAndConfirms()
        {
            _service.SetTargetByName("Forest_Hare");
            var started = await _service.ExecuteCommandAsync("/ma \"Cure\" <st>");
            Assert.True(started.Success);
            Assert.NotNull(_service.SubTargeting);
            Assert.Equal(HareId, _service.DisplayedTarget!.ServerId); // Cure may land on monsters (undead)
            int before = SentCount;

            Assert.True(_service.MoveSubTarget(FriendId));
            Assert.Equal(HareId, _service.CurrentTarget!.ServerId); // your own target does not change
            var cast = await _service.ConfirmSubTargetAsync();
            Assert.True(cast.Success);
            Assert.Null(_service.SubTargeting);
            Assert.Equal((FriendId, FriendIndex, CliActionId.CastMagic, (uint)TestCatalog.Cure), LastAction());
            Assert.Equal(FriendId, _service.LastSubTarget!.ServerId);
            Assert.True(SentCount > before);

            // <lastst> reuses it; <stnpc> does not offer players; Fire (enemies only) without a target starts on the hare.
            Assert.True((await _service.ExecuteCommandAsync("/ma Cure <lastst>")).Success);
            Assert.Equal(FriendId, LastAction().Target);
            await _service.ExecuteCommandAsync("/ma Cure <stnpc>");
            Assert.False(_service.MoveSubTarget(FriendId));
            _service.CancelSubTarget();
            Assert.Null(_service.SubTargeting);
            _service.ClearTarget();
            await _service.ExecuteCommandAsync("/ma Fire");
            Assert.Equal(HareId, _service.SubTargeting!.Current.ServerId);
            Assert.False(_service.MoveSubTarget(SelfId));
        }

        [Fact]
        public void SubTargetStep_FollowsScreenOrderAndWraps()
        {
            var candidates = new List<TargetCycling.Candidate>
            {
                new(3, 0.5f, 4, false), new(1, -0.5f, 9, false), new(2, 0.0f, 0, true),
            };
            Assert.Equal(2u, SubTargetSelection.Step(candidates, 1, 1));
            Assert.Equal(3u, SubTargetSelection.Step(candidates, 2, 1));
            Assert.Equal(1u, SubTargetSelection.Step(candidates, 3, 1));  // wraps
            Assert.Equal(3u, SubTargetSelection.Step(candidates, 1, -1)); // wraps back
            Assert.Equal(1u, SubTargetSelection.Step(candidates, 99, 1)); // gone: the left-most
            Assert.Equal(0u, SubTargetSelection.Step(new List<TargetCycling.Candidate>(), 1, 1));
        }

        [Fact]
        public void SpellRows_ListLearnedSpellsOfTheCategoryForTheJobs_GreyedByLevelMpAndRecast()
        {
            var catalog = TestCatalog.Build();
            long now = 1_000_000;
            var learned = new HashSet<ushort> { TestCatalog.Cure, TestCatalog.Fire, TestCatalog.Shantotto };
            StockUiActionState State(JobId main, int level, int mp, TimeSpan cureRecast = default) => new()
            {
                HasSpell = learned.Contains,
                MainJob = main,
                MainJobLevel = level,
                SubJob = JobId.None,
                CurrentMp = mp,
                SpellRecast = id => id == TestCatalog.Cure ? cureRecast : TimeSpan.Zero,
                Now = now,
            };

            // A White Mage 1 sees Cure (8 MP) under White Magic, not Fire; a Warrior sees neither.
            var white = StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 20), 1, null);
            Assert.Equal(new[] { "Cure" }, white.Select(r => r.Name));
            Assert.Equal("8", white[0].Value);
            Assert.False(white[0].IsGreyed(now));
            Assert.Empty(StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 20), 2, null));
            Assert.Empty(StockUiActionMenu.SpellRows(catalog, State(JobId.Warrior, 75, 0), 1, null));

            // Too little MP, too low a level, a running recast: greyed.
            Assert.Contains("MP", StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 7), 1, null)[0].GreyedReason);
            var redMage = StockUiActionMenu.SpellRows(catalog, State(JobId.RedMage, 2, 50), 1, null);
            Assert.Contains("level", redMage[0].GreyedReason);
            var recast = StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 50, TimeSpan.FromSeconds(4.2)), 1, null)[0];
            Assert.Null(recast.GreyedReason);
            Assert.True(recast.IsGreyed(now));
            Assert.Equal("0:05", StockUiActionMenu.FormatRecast(recast.RecastLeft(now)));

            // Trust lists every learned trust whatever the job; from the command menu on a monster Cure stays usable, Fire on yourself not.
            Assert.Equal(new[] { "Shantotto" }, StockUiActionMenu.SpellRows(catalog, State(JobId.Warrior, 1, 0), 8, null).Select(r => r.Name));
            var self = new StockUiTargetContext(StockUiTargetKind.Self, SelfId, "Gemini");
            var blackOnSelf = StockUiActionMenu.SpellRows(catalog, State(JobId.BlackMage, 20, 50), 2, self);
            Assert.Contains("cannot use Fire on Gemini", blackOnSelf[0].GreyedReason);
        }

        [Fact]
        public void AbilityRows_ListKnownAbilitiesAndWeaponSkills_WithRecastsAndTp()
        {
            var catalog = TestCatalog.Build();
            long now = 5_000_000;
            var state = new StockUiActionState
            {
                HasJobAbility = id => id is TestCatalog.Berserk or TestCatalog.Provoke or TestCatalog.CuringWaltz or 1,
                HasWeaponSkill = id => id == TestCatalog.FastBlade,
                CurrentTp = 150,
                AbilityRecast = recastId => recastId == 1 ? TimeSpan.FromSeconds(90) : TimeSpan.Zero,
                Now = now,
            };
            var abilities = StockUiActionMenu.JobAbilityRows(catalog, state, null);
            Assert.Equal(new[] { "Berserk", "Provoke", "Curing Waltz" }, abilities.Select(r => r.Name)); // the trait is left out
            Assert.Equal("1:30", StockUiActionMenu.FormatRecast(abilities[0].RecastLeft(now)));
            Assert.True(abilities[0].IsGreyed(now));
            Assert.False(abilities[1].IsGreyed(now));
            Assert.Contains("TP", abilities[2].GreyedReason); // 200 TP

            var ws = StockUiActionMenu.WeaponSkillRows(catalog, state, null);
            Assert.Equal("Fast Blade", Assert.Single(ws).Name);
            Assert.Contains("TP", ws[0].GreyedReason);
            Assert.Null(StockUiActionMenu.WeaponSkillRows(catalog, new StockUiActionState { HasWeaponSkill = _ => true, CurrentTp = 1000 }, null)[0].GreyedReason);
        }

        [Fact]
        public void AbilityRecasts_CountDownAndKeepTheTwoHourTimer()
        {
            long clock = 0;
            var combat = new CombatState { Clock = () => clock };
            var payload = new byte[256];
            // Slot 0: the two-hour (id 0) with 3600 s; slot 1: Berserk's recast id 1 with 30 s; the rest unused (id 0, 0 s).
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 3600);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), 30);
            payload[8 + 3] = 1;
            combat.UpdateRecasts(new S2C_0x119_AbilRecast(payload));
            Assert.Equal(TimeSpan.FromSeconds(3600), combat.GetAbilityRecastRemaining(0));
            Assert.Equal(TimeSpan.FromSeconds(30), combat.GetAbilityRecastRemaining(1));
            clock += System.Diagnostics.Stopwatch.Frequency * 10;
            Assert.Equal(20, combat.GetAbilityRecastRemaining(1).TotalSeconds, 3);
            clock += System.Diagnostics.Stopwatch.Frequency * 60;
            Assert.Equal(TimeSpan.Zero, combat.GetAbilityRecastRemaining(1));

            combat.SetSpellRecast(TestCatalog.Cure, 5);
            Assert.Equal(5, combat.GetSpellRecastRemaining(TestCatalog.Cure).TotalSeconds, 3);
            clock += System.Diagnostics.Stopwatch.Frequency * 6;
            Assert.Equal(TimeSpan.Zero, combat.GetSpellRecastRemaining(TestCatalog.Cure));
        }

        private static UiMenuDefinition ListMenu(string name, int count, short x, short y, short width, short height, UiAnchor anchor, int rowWidth = 88, int firstY = 6)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= count; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 16, Y = (short)(firstY + (i - 1) * 16), Width = (short)rowWidth, Height = 16,
                    NavUp = (sbyte)Math.Max(1, i - 1), NavDown = (sbyte)Math.Min(count, i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = width, Height = height, Anchor = anchor }, Buttons = buttons };
        }

        private void LoadMenus()
        {
            _service.Menus.Library = UiResourceLibrary.FromDefinitions(new[]
            {
                ListMenu(StockUiMenuEntries.MainMenu, 12, 384, 48, 112, 202, UiAnchor.TopRight),
                ListMenu(StockUiActionMenu.MagicCategoryMenu, 8, 384, 48, 112, 138, UiAnchor.TopRight),
                ListMenu(StockUiActionMenu.AbilityMenu, 6, 384, 48, 112, 106, UiAnchor.TopRight),
                ListMenu(StockUiActionMenu.SpellListMenu, 12, 16, 96, 182, 200, UiAnchor.BottomLeft, 132, 4),
                ListMenu(StockUiActionMenu.AbilityListMenu, 12, 16, 96, 182, 200, UiAnchor.BottomLeft, 132, 4),
            });
        }

        [Fact]
        public async Task MagicWindow_TrustCategoryListsTrustsAndConfirmCastsOnYourself()
        {
            LoadMenus();
            Learn(TestCatalog.Cure, TestCatalog.Shantotto);
            var menus = _service.Menus;
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 3, out var magic));
            Assert.Equal(StockUiActionMenu.MagicCategoryMenu, magic.Opens);
            Assert.True(menus.Open(StockUiActionMenu.MagicCategoryMenu));
            for (int i = 1; i < 8; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(8, menus.Top!.SelectedButtonId); // Trust
            menus.Activate();
            var list = menus.Top!;
            Assert.True(list.IsActionList);
            Assert.Equal("Shantotto", Assert.Single(list.ActionRows).Name);

            int before = SentCount;
            menus.Activate();
            for (int i = 0; i < 100 && SentCount == before; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.False(menus.IsOpen);
            Assert.Equal((SelfId, SelfIndex, CliActionId.CastMagic, (uint)TestCatalog.Shantotto), LastAction());
        }

        [Fact]
        public async Task AbilitiesWindow_GreyedRowsPostWhyAndWeaponSkillOpensTheCursor()
        {
            LoadMenus();
            LearnCommands(new[] { TestCatalog.FastBlade }, new[] { TestCatalog.Berserk });
            var menus = _service.Menus;
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 5, out var abilities));
            Assert.Equal(StockUiActionMenu.AbilityMenu, abilities.Opens);
            Assert.True(menus.Open(StockUiActionMenu.AbilityMenu));
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown); // Weapon Skl.
            menus.Activate();
            var list = menus.Top!;
            Assert.Equal(StockUiActionList.WeaponSkills, list.ActionList);
            menus.Activate(); // 0 TP: greyed
            Assert.Contains(_notices, n => n.Contains("enough TP"));
            Assert.True(menus.IsOpen);

            menus.CloseTop();
            menus.Move(InputAction.MenuUp); // Job Abil.
            menus.Activate();
            Assert.Equal("Berserk", Assert.Single(menus.Top!.ActionRows).Name);
            int before = SentCount;
            menus.Activate();
            for (int i = 0; i < 100 && SentCount == before; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal((SelfId, SelfIndex, CliActionId.JobAbility, (uint)TestCatalog.Berserk), LastAction());
        }

        [Fact]
        public void CommandMenu_MagicAbilitiesAndTrustOpenTheirWindows()
        {
            var rows = StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Monster, HareId, "Forest_Hare"));
            Assert.Equal(StockUiActionMenu.CommandMagicCategoryMenu, rows.Single(r => r.Label.Text == "Magic").Entry.Opens);
            Assert.Equal(StockUiActionMenu.CommandAbilityMenu, rows.Single(r => r.Label.Text == "Abilities").Entry.Opens);
            var trust = rows.Single(r => r.Label.Text == "Trust").Entry;
            Assert.Equal((StockUiMenuCommand.SpellList, StockUiActionMenu.TrustKind), (trust.Command, trust.Argument));
            Assert.True(StockUiMenuEntries.TryGet(StockUiActionMenu.CommandMagicCategoryMenu, 3, out var songs));
            Assert.Equal(5, songs.Argument); // Songs are spell kind 5
            Assert.False(StockUiMenuEntries.TryGet(StockUiActionMenu.CommandMagicCategoryMenu, 8, out _)); // no Trust there
        }

        /// <summary>The retail Magic and Abilities windows (ROM/119/51). Skipped without the game install.</summary>
        [Fact]
        public void RetailMenus_MagicAndAbilityWindowsResolve()
        {
            const string gameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(gameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;
            foreach (string name in new[] { StockUiActionMenu.MagicCategoryMenu, StockUiActionMenu.CommandMagicCategoryMenu, StockUiActionMenu.AbilityMenu, StockUiActionMenu.CommandAbilityMenu })
            {
                Assert.True(library.TryGetMenu(name, out var menu), name);
                foreach (var button in menu.Buttons.Where(StockUiMenuController.IsSelectable))
                {
                    Assert.True(StockUiMenuEntries.TryGet(name, button.ButtonId, out _), $"{name} button {button.ButtonId}");
                }
            }
            Assert.Equal(8, library.TryGetMenu(StockUiActionMenu.MagicCategoryMenu, out var categories) ? categories.Buttons.Count(StockUiMenuController.IsSelectable) : 0);
            foreach (string name in new[] { StockUiActionMenu.SpellListMenu, StockUiActionMenu.AbilityListMenu })
            {
                Assert.True(library.TryGetMenu(name, out var list), name);
                Assert.Equal((16, 96, 182, 200, UiAnchor.BottomLeft), (list.Frame.X, list.Frame.Y, list.Frame.Width, list.Frame.Height, list.Frame.Anchor));
                Assert.Equal(StockUiActionMenu.VisibleRows, list.Buttons.Count);
                var first = list.FindButton(1)!;
                Assert.Equal((16, 4, 132, 16), (first.X, first.Y, first.Width, first.Height));
                Assert.Equal(1, first.NavUp); // the end rows link to themselves: the controller scrolls
            }
        }

        [Fact]
        public void Router_SplitsActionNamesAndTargets()
        {
            Assert.Equal(("Cure IV", "<t>"), ChatCommandRouter.SplitActionArguments("Cure IV <t>"));
            Assert.Equal(("Cure IV", "<t>"), ChatCommandRouter.SplitActionArguments("\"Cure IV\" <t>"));
            Assert.Equal(("Shantotto", "<me>"), ChatCommandRouter.SplitActionArguments("\"Shantotto\" <me>"));
            Assert.Equal(("Cure IV", string.Empty), ChatCommandRouter.SplitActionArguments("Cure IV"));
            Assert.Equal(("1", "Goblin"), ChatCommandRouter.SplitActionArguments("1 Goblin"));
            var parsed = ChatCommandRouter.Parse("/ma Cure <stpc>");
            Assert.Equal((ChatCommandResultKind.CombatCast, "Cure", "<stpc>"), (parsed.Kind, parsed.Message, parsed.TargetName));
        }
    }
}
