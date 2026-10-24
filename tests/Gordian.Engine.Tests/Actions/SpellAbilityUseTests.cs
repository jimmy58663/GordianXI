// tests/Gordian.Engine.Tests/Actions/SpellAbilityUseTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Config;
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Engine.Tests.Actions
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
        public void SpellRows_MainMenuListsEveryLearnedSpellGreyedByJobLevelAndMp_CommandMenuOnlyCastable()
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

            // A White Mage 1 sees Cure (8 MP, its note-style small icon) under White Magic.
            var white = StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 20), 1, null);
            var cure = Assert.Single(white);
            Assert.Equal(("Cure", 8, 6), (cure.Name, cure.MpCost, cure.Icon));
            Assert.False(cure.IsGreyed);
            // Main menu: Fire is listed for the White Mage too, greyed (retail image-16: unusable spells greyed); command menu: not listed.
            Assert.Contains("current job and level", Assert.Single(StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 20), 2, null)).GreyedReason);
            Assert.Empty(StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 20), 2, null, usableOnly: true));
            // The command menu's Confirm: every castable spell but Trust.
            Assert.Equal(new[] { "Cure" }, StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 20), 0, null, usableOnly: true).Select(r => r.Name));

            // Too little MP, too low a level: greyed. A running recast is shown under "Next", not greyed.
            Assert.Contains("MP", StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 7), 1, null)[0].GreyedReason);
            Assert.Contains("level", StockUiActionMenu.SpellRows(catalog, State(JobId.RedMage, 2, 50), 1, null)[0].GreyedReason);
            var recast = StockUiActionMenu.SpellRows(catalog, State(JobId.WhiteMage, 1, 50, TimeSpan.FromSeconds(4.2)), 1, null)[0];
            Assert.False(recast.IsGreyed);
            Assert.True(recast.IsGreyedAt(now)); // spells in recast are greyed too (retail, round 2 check 2026-10-12)
            Assert.Equal("0:05", StockUiActionMenu.FormatRecast(recast.RecastLeft(now)));
            Assert.Equal("0:00", StockUiActionMenu.FormatRecast(TimeSpan.Zero));

            // Trust lists every learned trust whatever the job; from the command menu on yourself Fire is listed and not
            // greyed (the cursor then picks a valid target, retail note C5, 2026-10-12).
            Assert.Equal(new[] { "Shantotto" }, StockUiActionMenu.SpellRows(catalog, State(JobId.Warrior, 1, 0), 8, null).Select(r => r.Name));
            var self = new StockUiTargetContext(StockUiTargetKind.Self, SelfId, "Gemini");
            var blackOnSelf = StockUiActionMenu.SpellRows(catalog, State(JobId.BlackMage, 20, 50), 2, self, usableOnly: true);
            Assert.Null(Assert.Single(blackOnSelf).GreyedReason);
        }

        [Fact]
        public void AbilityRows_JobAbilitiesWeaponSkillsTraitsAndPetCommands()
        {
            var catalog = TestCatalog.Build();
            long now = 5_000_000;
            var state = new StockUiActionState
            {
                HasJobAbility = id => id is TestCatalog.Berserk or TestCatalog.Provoke or TestCatalog.CuringWaltz or 1 or TestCatalog.Fight,
                HasWeaponSkill = id => id == TestCatalog.FastBlade,
                HasTrait = id => id == TestCatalog.MagicDefBonus,
                HasPetAbility = id => id == 0, // Healing Ruby (command 1024)
                CurrentTp = 1500,
                AbilityRecast = recastId => recastId == 1 ? TimeSpan.FromSeconds(90) : TimeSpan.Zero,
                Now = now,
            };
            var abilities = StockUiActionMenu.JobAbilityRows(catalog, state, null);
            Assert.Equal(new[] { "Berserk", "Provoke", "Curing Waltz" }, abilities.Select(r => r.Name)); // the trait and Fight are elsewhere
            Assert.Equal("1:30", StockUiActionMenu.FormatRecast(abilities[0].RecastLeft(now)));
            Assert.Equal((361, 40), (abilities[0].Icon, abilities[0].SmallIcon)); // the command's picture (+0x04), small icon (+0x03)
            Assert.True(abilities[0].IsGreyedAt(now)); // in recast (retail note D2)
            Assert.False(abilities[0].IsGreyedAt(now + System.Diagnostics.Stopwatch.Frequency * 91));
            Assert.Null(abilities[2].GreyedReason); // 200 TP, 1500 held

            // Weapon skills are greyed out of combat even with TP (retail image-21), and under 1000 TP.
            var ws = StockUiActionMenu.WeaponSkillRows(catalog, state, null);
            Assert.Contains("engaged", Assert.Single(ws).GreyedReason);
            Assert.Null(StockUiActionMenu.WeaponSkillRows(catalog, new StockUiActionState { HasWeaponSkill = _ => true, CurrentTp = 1000, Engaged = true }, null)[0].GreyedReason);
            Assert.Contains("TP", StockUiActionMenu.WeaponSkillRows(catalog, new StockUiActionState { HasWeaponSkill = _ => true, CurrentTp = 999, Engaged = true }, null)[0].GreyedReason);

            var traits = StockUiActionMenu.TraitRows(catalog, state);
            Assert.Equal((StockUiActionType.Trait, "Magic Def. Bonus", 46), (traits[0].Type, Assert.Single(traits).Name, traits[0].Icon));
            var pets = StockUiActionMenu.PetCommandRows(catalog, state, null);
            Assert.Equal(new[] { "Fight" }, pets.Select(r => r.Name)); // pet commands are the type-2 abilities (retail image-40)
            Assert.Equal((3, 2, 0), (StockUiActionMenu.ReadyCharges(TimeSpan.Zero), StockUiActionMenu.ReadyCharges(TimeSpan.FromSeconds(12)), StockUiActionMenu.ReadyCharges(TimeSpan.FromSeconds(90))));
            var pacts = StockUiActionMenu.PetAbilityRows(catalog, state, 10); // Blood Pact: Ward's list
            Assert.Equal(("Healing Ruby", (ushort)512), (Assert.Single(pacts).Name, pacts[0].Id)); // used as job ability 512 + bit
            Assert.Empty(StockUiActionMenu.PetAbilityRows(catalog, state, 6));
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
                ListMenu(StockUiActionMenu.CommandMagicCategoryMenu, 7, 128, 176, 112, 120, UiAnchor.BottomLeft),
                ListMenu(StockUiActionMenu.AbilityMenu, 6, 384, 48, 112, 106, UiAnchor.TopRight),
                ListMenu(StockUiActionMenu.SpellListMenu, 12, 16, 96, 182, 200, UiAnchor.BottomLeft, 132, 4),
                ListMenu(StockUiActionMenu.AbilityListMenu, 12, 16, 96, 182, 200, UiAnchor.BottomLeft, 132, 4),
                ListMenu(StockUiActionMenu.MountListMenu, 12, 16, 96, 182, 200, UiAnchor.BottomLeft, 132, 4),
                ListMenu(StockUiActionMenu.OptionsMenu, 2, 384, 48, 112, 58, UiAnchor.TopRight, 88, 22),
                ListMenu(StockUiCommandMenu.Template, 10, 16, 136, 112, 160, UiAnchor.BottomLeft),
            });
        }

        [Fact]
        public async Task MagicWindow_TrustOpensTheSubTargetCursorAndTheListComesBackAfterTheCast()
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
            Assert.True(list.ShowsActionOptions);
            Assert.Equal("Shantotto", Assert.Single(list.ActionRows).Name);

            // Confirm: the sub-target cursor on yourself (a self-only spell still asks, retail video), the menus set aside.
            int before = SentCount;
            menus.Activate();
            for (int i = 0; i < 100 && _service.SubTargeting == null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(SelfId, _service.SubTargeting!.Current.ServerId);
            Assert.True(menus.Suspended);
            Assert.Equal(before, SentCount);
            Assert.True((await _service.ConfirmSubTargetAsync()).Success);
            Assert.Equal((SelfId, SelfIndex, CliActionId.CastMagic, (uint)TestCatalog.Shantotto), LastAction());
            Assert.False(menus.Suspended);
            Assert.Same(list, menus.Top); // the list is back where it was

            // + / Y: the Options window and back; Cancel on the cursor also brings the list back.
            Assert.True(menus.ToggleActionOptions());
            Assert.True(menus.Top!.IsActionOptions);
            Assert.True(menus.ToggleActionOptions());
            Assert.Same(list, menus.Top);
            menus.Activate();
            for (int i = 0; i < 100 && _service.SubTargeting == null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            _service.CancelSubTarget();
            Assert.False(menus.Suspended);
            Assert.Same(list, menus.Top);
        }

        [Fact]
        public async Task AbilitiesWindow_GreyedRowsPostWhyEmptyListsShowTheirTextAndAbilitiesAskForTheTarget()
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
            menus.Activate(); // not engaged: greyed
            Assert.Contains(_notices, n => n.Contains("engaged"));
            Assert.True(menus.IsOpen);

            menus.CloseTop();
            for (int i = 0; i < 3; i++) menus.Move(InputAction.MenuDown); // Mount (button 6 in these test windows)
            menus.Activate();
            Assert.Equal((StockUiActionList.Mounts, "No mounts available."), (menus.Top!.ActionList, menus.Top.EmptyText));
            menus.CloseTop();

            for (int i = 0; i < 4; i++) menus.Move(InputAction.MenuUp); // Job Abil.
            menus.Activate();
            Assert.Equal("Berserk", Assert.Single(menus.Top!.ActionRows).Name);
            menus.Activate();
            for (int i = 0; i < 100 && _service.SubTargeting == null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            await _service.ConfirmSubTargetAsync();
            Assert.Equal((SelfId, SelfIndex, CliActionId.JobAbility, (uint)TestCatalog.Berserk), LastAction());
        }

        [Fact]
        public async Task CommandMenu_MagicConfirmListsCastableMagic_RightOpensTheCategories_CursorStartsOnTheTarget()
        {
            LoadMenus();
            Learn(TestCatalog.Cure, TestCatalog.Fire, TestCatalog.Shantotto);
            var rows = StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Monster, HareId, "Forest_Hare"));
            var magic = rows.Single(r => r.Label.Text == "Magic").Entry;
            Assert.Equal((StockUiActionMenu.CommandMagicCategoryMenu, StockUiMenuCommand.SpellList, 0), (magic.Opens, magic.Command, magic.Argument));
            Assert.Equal(StockUiActionMenu.CommandAbilityMenu, rows.Single(r => r.Label.Text == "Abilities").Entry.Opens);
            var trust = rows.Single(r => r.Label.Text == "Trust").Entry;
            Assert.Equal((StockUiMenuCommand.SpellList, StockUiActionMenu.TrustKind), (trust.Command, trust.Argument));
            Assert.True(StockUiMenuEntries.TryGet(StockUiActionMenu.CommandMagicCategoryMenu, 3, out var songs));
            Assert.Equal(5, songs.Argument); // Songs are spell kind 5
            Assert.False(StockUiMenuEntries.TryGet(StockUiActionMenu.CommandMagicCategoryMenu, 8, out _)); // no Trust there

            // A White Mage 1 with MP (S2C 0x01B job info, vitals). The command menu on the hare: Magic (row 2 after
            // Attack) lists what can be cast: Cure, not Fire (Black Magic) nor Shantotto (Trust).
            var job = new byte[92];
            job[4] = (byte)JobId.WhiteMage;
            job[68 + (int)JobId.WhiteMage] = 1;
            _localPlayer.UpdateFromJobInfo(new S2C_0x01B_JobInfo(job));
            _localPlayer.UpdateVitals(100, 50, 0);
            var menus = _service.Menus;
            _service.SetTargetByName("Forest_Hare");
            Assert.True(_service.OpenTargetCommandMenu());
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            var list = menus.Top!;
            Assert.Equal((StockUiActionList.Spells, 0, false), (list.ActionList, list.ActionCategory, list.ShowsActionOptions));
            Assert.Equal(new[] { "Cure" }, list.ActionRows.Select(r => r.Name)); // no Fire (BLM), no Shantotto (Trust)
            menus.CloseTop();
            menus.Move(InputAction.MenuRight); // the categories
            Assert.Equal(StockUiActionMenu.CommandMagicCategoryMenu, menus.Top!.Name);
            menus.CloseTop();

            // A spell from the list: the cursor starts on the menu's target.
            menus.Activate();
            menus.Activate();
            for (int i = 0; i < 100 && _service.SubTargeting == null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(HareId, _service.SubTargeting!.Current.ServerId);
            _service.CancelSubTarget();
        }

        [Fact]
        public void ItemTargets_MapTheItemDatBitsToTheCursorFilter()
        {
            Assert.Equal(ActionTargetFlags.Self, ItemTargetFlags.ToActionTargets(0x01));
            Assert.Equal(ActionTargetFlags.Self | ActionTargetFlags.Party, ItemTargetFlags.ToActionTargets(0x03));
            Assert.Equal(ActionTargetFlags.Enemy, ItemTargetFlags.ToActionTargets(0x04));
            Assert.Equal(ActionTargetFlags.Alliance | ActionTargetFlags.Player | ActionTargetFlags.Npc, ItemTargetFlags.ToActionTargets(0x08 | 0x10 | 0x40));
            Assert.Equal(ActionTargetFlags.None, ItemTargetFlags.ToActionTargets(0x20)); // dead players: no cursor bit
        }

        [Fact]
        public async Task ItemUse_ThroughTheSubTargetCursor_SendsCountZeroWithContainerAndSlot()
        {
            _service.InventoryModule = new InventoryPacketModule(new InventoryState(), _localPlayer, (mem, _) =>
            {
                lock (_sent) _sent.Add(mem.ToArray());
                return Task.CompletedTask;
            });
            // An Echo Drops-like item (self or party) in inventory slot 7: the cursor starts on yourself; the hare is refused.
            Assert.True(_service.BeginItemSubTarget(ContainerId.Inventory, 7, 4151, 0x03));
            var picking = _service.SubTargeting!;
            Assert.Equal((ActionKind.Item, (ushort)4151, ContainerId.Inventory, (byte)7), (picking.Kind, picking.Id, picking.ItemContainer, picking.ItemSlot));
            Assert.Equal(SelfId, picking.Current.ServerId);
            Assert.False(_service.MoveSubTarget(HareId));
            Assert.True((await _service.ConfirmSubTargetAsync()).Success);

            byte[] packet;
            lock (_sent) packet = _sent.Last(p => (BinaryPrimitives.ReadUInt16LittleEndian(p) & 0x1FF) == 0x037);
            Assert.Equal(SelfId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8))); // count 0
            Assert.Equal(SelfIndex, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12)));
            Assert.Equal(7, packet[14]);
            Assert.Equal((uint)ContainerId.Inventory, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)));

            // An enemy-only item (0x04) starts on the preferred target; one with nothing allowed nearby does not start.
            Assert.True(_service.BeginItemSubTarget(ContainerId.Inventory, 8, 4400, 0x04, _world.TryGetByServerId(HareId, out var hare) ? hare : null));
            Assert.Equal(HareId, _service.SubTargeting!.Current.ServerId);
            _service.CancelSubTarget();
            Assert.False(_service.BeginItemSubTarget(ContainerId.Inventory, 9, 4401, 0x40)); // NPCs only: none here
            Assert.Null(_service.SubTargeting);
        }

        [Fact]
        public async Task SubTargetCursor_UnlocksTheLockOnAndRestoresIt_OnFinishedRunsOnce()
        {
            // Locked on to the hare: the cursor lets go of the lock while the player picks and takes it back after (C3).
            _service.SetTargetByName("Forest_Hare");
            _service.SetLockOn(true);
            int finished = 0;
            Assert.True(_service.BeginItemSubTarget(ContainerId.Inventory, 3, 4112, 0x01, onFinished: () => finished++));
            Assert.False(_service.IsLockedOn);
            Assert.Equal(HareId, _service.CurrentTarget!.ServerId); // the target and the fight stay
            _service.CancelSubTarget();
            Assert.True(_service.IsLockedOn);
            Assert.Equal(1, finished);

            // Confirm: onFinished after the use is sent (no inventory module here: a warning, still finished once).
            Assert.True(_service.BeginItemSubTarget(ContainerId.Inventory, 3, 4112, 0x01, onFinished: () => finished++));
            await _service.ConfirmSubTargetAsync();
            Assert.Equal(2, finished);
            _service.CancelSubTarget(); // nothing running: no third call
            Assert.Equal(2, finished);
            Assert.False(_service.BeginItemSubTarget(ContainerId.Inventory, 3, 4112, 0x40, onFinished: () => finished++)); // nothing allowed
            Assert.Equal(2, finished);
        }

        [Fact]
        public async Task PetCommand_BloodPactRageOpensItsListAndAbilitiesInRecastStayListed()
        {
            LoadMenus();
            var data = new byte[224];
            data[64 + (91 >> 3)] |= 1 << (91 & 7);   // Blood Pact: Rage (job ability bits)
            data[128 + (1 >> 3)] |= 1 << (1 & 7);    // Poison Nails (pet bit 1 = command 1025)
            _localPlayer.UpdateFromCommandData(new S2C_0x0AC_CommandData(data));
            var menus = _service.Menus;
            Assert.True(menus.Open(StockUiActionMenu.AbilityMenu));
            for (int i = 0; i < 4; i++) menus.Move(InputAction.MenuDown); // Pet Command (button 5 in these test windows)
            menus.Activate();
            var pet = menus.Top!;
            Assert.Equal(StockUiActionList.PetCommands, pet.ActionList);
            var rage = Assert.Single(pet.ActionRows);
            Assert.Equal((StockUiActionType.PetList, 6), (rage.Type, rage.OpensPetList));
            menus.Activate();
            Assert.Equal((StockUiActionList.PetAbilities, 6), (menus.Top!.ActionList, menus.Top.ActionCategory));
            Assert.Equal("Poison Nails", Assert.Single(menus.Top.ActionRows).Name);
            menus.Activate(); // a pact: the cursor on enemies
            for (int i = 0; i < 100 && _service.SubTargeting == null; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(HareId, _service.SubTargeting!.Current.ServerId);
            _service.CancelSubTarget();
        }

        [Fact]
        public void CommandMenu_TrustReleaseRow_CombatLock_AndTheMainMenuOverIt()
        {
            var trust = StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Trust, 0x01FFFF01, "Shantotto"));
            Assert.Equal(new[] { "Magic", "Abilities", "Trust", "Items", "Release" }, trust.Select(r => r.Label.Text)); // retail image-25
            Assert.Equal((668, StockUiMenuCommand.ReleaseTrust), (trust[^1].Label.Sprite, trust[^1].Entry.Command));
            Assert.Equal(ChatCommandResultKind.ReleaseTrust, ChatCommandRouter.Parse("/retr all").Kind);

            LoadMenus();
            var menus = _service.Menus;
            _service.SetTargetByName("Forest_Hare");
            Assert.True(_service.OpenTargetCommandMenu());
            // The main menu opens over the command menu, and Cancel goes back to it.
            Assert.True(menus.OpenMainMenu());
            Assert.Equal(2, menus.OpenMenus.Count);
            menus.CloseTop();
            Assert.True(menus.Top!.IsCommandMenu);
            // While engaged the command menu cannot be cancelled.
            _combat.Engage(HareId, HareIndex);
            var input = new InputState();
            var cancel = new InputProfile("t", "t");
            cancel.Bind(InputAction.Cancel, new InputChord(GordianKey.Escape));
            input.SetKeyDown(GordianKey.Escape);
            input.Update(cancel, TimeSpan.FromMilliseconds(16));
            menus.ProcessInput(input, TimeSpan.FromMilliseconds(16));
            Assert.True(menus.IsOpen);
            _combat.Disengage();
            input.SetKeyUp(GordianKey.Escape);
            input.Update(cancel, TimeSpan.FromMilliseconds(16));
            input.SetKeyDown(GordianKey.Escape);
            input.Update(cancel, TimeSpan.FromMilliseconds(16));
            menus.ProcessInput(input, TimeSpan.FromMilliseconds(16));
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public async Task TargetTokens_FromTheMacroPage()
        {
            // <r>: the tell partner; <ht>: a monster someone called for help on; <ft>, <focust>: not supported, nothing sent.
            _service.Menus.TellTarget = () => "Tarudrake";
            Assert.True((await _service.ExecuteCommandAsync("/ma Cure <r>")).Success);
            Assert.Equal(FriendId, LastAction().Target);
            _world.TryGetByServerId(HareId, out var hare);
            hare!.NamePlate |= NamePlateFlags.CalledForHelp;
            Assert.Equal(HareId, _service.ResolveTargetToken("ht", out _)!.ServerId);
            Assert.Null(_service.ResolveTargetToken("ft", out string? problem));
            Assert.Contains("not supported", problem);
            Assert.Null(_service.ResolveTargetToken("pet", out problem));
            Assert.Equal("You have no pet.", problem);
            Assert.Null(_service.ResolveTargetToken("a10", out problem)); // no alliance
        }

        [Fact]
        public void KeypadMinus_OpensTheMainMenuInEveryPreset()
        {
            var chord = new InputChord(GordianKey.NumPadSubtract);
            Assert.Contains(chord, InputProfile.CreateCompact().GetChords(InputAction.OpenMenu));
            Assert.Contains(chord, InputProfile.CreateFullNumpad().GetChords(InputAction.OpenMenu));
            var old = new InputProfile("old", "saved before");
            old.EnsureKeypadMinusMenuBinding();
            Assert.Contains(chord, old.GetChords(InputAction.OpenMenu));
            var taken = new InputProfile("taken", "minus used elsewhere");
            taken.Bind(InputAction.ToggleWalkRun, chord);
            taken.EnsureKeypadMinusMenuBinding();
            Assert.DoesNotContain(chord, taken.GetChords(InputAction.OpenMenu));
        }

        [Fact]
        public async Task SubTargetCursor_IsTheEntryPointForOtherCallers()
        {
            WorldEntity? picked = null;
            var started = _service.BeginSubTarget("Echo Drops", ActionTargetFlags.Self | ActionTargetFlags.Party,
                entity => { picked = entity; return Task.FromResult(PlayerActionResult.Ok("used")); });
            Assert.True(started.Success);
            Assert.Equal(SelfId, _service.SubTargeting!.Current.ServerId);
            Assert.False(_service.MoveSubTarget(HareId)); // not allowed
            var result = await _service.ConfirmSubTargetAsync();
            Assert.Equal("used", result.Message);
            Assert.Equal(SelfId, picked!.ServerId);
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
            // The Options windows and the info window over the target window (112 x 42, bottom right at 384,208).
            Assert.True(library.TryGetMenu(StockUiActionMenu.OptionsMenu, out _));
            Assert.True(library.TryGetMenu(StockUiActionMenu.BlueOptionsMenu, out _));
            Assert.True(library.TryGetMenu(StockUiActionMenu.InfoMenu, out var info));
            Assert.Equal((384, 208, 112, 42, UiAnchor.BottomRight), (info.Frame.X, info.Frame.Y, info.Frame.Width, info.Frame.Height, info.Frame.Anchor));
            // The list icons (ROM/280/15): the songs' notes are images 32-39, Soul Voice's picture 75.
            Assert.True(library.TryGetGroup(StockUiActionMenu.IconGroup, out var icons));
            Assert.Equal(677, icons.Images.Count);
            var catalog = Gordian.Engine.Actions.ActionCatalog.Load(rm)!;
            Assert.True(catalog.TryFindSpell("Victory March", out var march));
            Assert.Equal((420, 36, 784), (march.Id, march.Icon, march.MenuIndex));
            Assert.True(catalog.TryFindJobAbility("Soul Voice", out _, out var soulVoice));
            Assert.Equal(75, StockUiActionMenu.CommandIcon(soulVoice));
            // Job traits at command 1536 + id (LandSandBoat sql/traits.sql: Fencer 107), named in ROM/181/72.
            Assert.Equal("Magic Def. Bonus", catalog.JobAbilityName(StockUiActionMenu.TraitBase + 6 - 512));
            Assert.Equal("Fencer", catalog.JobAbilityName(StockUiActionMenu.TraitBase + 107 - 512));
            // Pet commands at command 1024 + the pet bit: Healing Ruby is LandSandBoat's ability 512.
            Assert.Equal("Healing Ruby", catalog.JobAbilityName(StockUiActionMenu.PetCommandBase - 512));
            // A blood pact's MP cost is +0x06 (retail image-46): Healing Ruby 6, Poison Nails 11, Shining Ruby 44.
            Assert.Equal((6, 11, 44), (catalog.Tables.GetCommand(1024)!.Charges, catalog.Tables.GetCommand(1025)!.Charges, catalog.Tables.GetCommand(1026)!.Charges));
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
