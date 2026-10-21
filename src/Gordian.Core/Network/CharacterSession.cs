// src/Gordian.Core/Network/CharacterSession.cs
using System;
using System.Linq;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Represents an active, in-memory character session connection, encapsulating
    /// character identity, connection state, and the dedicated network pipeline.
    /// </summary>
    public sealed partial class CharacterSession : IDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public string CharacterName { get; }
        public uint CharacterId { get; }
        public string AccountUsername { get; }

        /// <summary>
        /// Gets or sets the name of the launch profile that started this session (empty for sessions that did not come
        /// from a profile, such as the retail handoff). Online status keys on this, not on the account, so the other
        /// profiles of an account do not show Online when one of its characters is.
        /// </summary>
        public string ProfileName { get; set; } = string.Empty;
        public SessionNetworkManager NetworkManager { get; }
        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Gets or sets whether this character session is the primary active client rendering 3D graphics.
        /// In native single-process multi-boxing, only the primary rendering client accepts direct gamepad inputs;
        /// background headless sessions do not receive broadcast gamepad polling.
        /// </summary>
        public bool IsRendering3D { get; set; } = true;

        /// <summary>
        /// Gets the current operational lifecycle state of this character session.
        /// </summary>
        public SessionState State => NetworkManager.CurrentState;

        /// <summary>
        /// Gets real-time datagram throughput, packet rates, and memory telemetry tracker for this session.
        /// </summary>
        public SessionPerformanceTracker Performance => NetworkManager.Performance;

        /// <summary>
        /// Gets the thread-safe active game world state.
        /// </summary>
        public WorldState World => NetworkManager.World;

        /// <summary>
        /// Gets the active character statistics and vitals state.
        /// </summary>
        public LocalPlayerState LocalPlayer => NetworkManager.LocalPlayer;

        /// <summary>
        /// Gets the entity packet handling module.
        /// </summary>
        public EntityPacketModule EntityModule => NetworkManager.EntityModule;

        /// <summary>
        /// Gets the communication and chat packet handling module.
        /// </summary>
        public ChatPacketModule ChatModule => NetworkManager.ChatModule;

        /// <summary>
        /// Gets the active party and alliance state model.
        /// </summary>
        public PartyState Party => NetworkManager.Party;

        /// <summary>
        /// Gets the party packet handling module.
        /// </summary>
        public PartyPacketModule PartyModule => NetworkManager.PartyModule;

        /// <summary>
        /// Gets the active story progression, quest, merit, and minigame state model.
        /// </summary>
        public ProgressionState Progression => NetworkManager.Progression;

        /// <summary>
        /// Gets the progression, quest, cutscene, and mog house packet handling module.
        /// </summary>
        public ProgressionPacketModule ProgressionModule => NetworkManager.ProgressionModule;

        /// <summary>
        /// Gets the active multi-container inventory, currency, and trade state model.
        /// </summary>
        public InventoryState Inventory => NetworkManager.Inventory;

        /// <summary>
        /// Gets the inventory, trade, shop, and bazaar packet handling module.
        /// </summary>
        public InventoryPacketModule InventoryModule => NetworkManager.InventoryModule;

        /// <summary>
        /// Gets the treasure pool: the 10 slots and the lots on them.
        /// </summary>
        public TreasurePoolState Treasure => NetworkManager.Treasure;

        /// <summary>
        /// Gets the treasure pool packet handling module (lot and pass).
        /// </summary>
        public TreasurePacketModule TreasureModule => NetworkManager.TreasureModule;

        /// <summary>Gets the crafting state (S2C 0x06F, 0x070, 0x031).</summary>
        public CraftingState Crafting => NetworkManager.Crafting;

        /// <summary>Gets the crafting packet handling module (synthesis and recipe requests).</summary>
        public CraftingPacketModule CraftingModule => NetworkManager.CraftingModule;

        /// <summary>Gets the actor and magic schedulers the server asked to play (S2C 0x038, 0x03A); decode only.</summary>
        public SchedulerState Scheduler => NetworkManager.Scheduler;

        /// <summary>Gets the delivery box state (S2C 0x04B).</summary>
        public DeliveryBoxState Delivery => NetworkManager.Delivery;

        /// <summary>Gets the character's blacklist (S2C 0x041 / 0x042).</summary>
        public BlacklistState Blacklist => NetworkManager.Blacklist;

        /// <summary>Gets the world pass, item search, party group id, party map position and concierge state.</summary>
        public SocialState Social => NetworkManager.Social;

        /// <summary>Gets the social packet module (delivery box, blacklist, item search, linkshell items, party id and map positions).</summary>
        public SocialPacketModule SocialModule => NetworkManager.SocialModule;

        /// <summary>Gets the search (cache) server service: Auction House lists and histories, <c>/sea</c>, party and linkshell member lists.</summary>
        public Search.SearchService Search => NetworkManager.Search;

        /// <summary>Gets the state behind the everyday commands: the emote list, wide scan and proposals.</summary>
        public PlayerCommandState Commands => NetworkManager.Commands;

        /// <summary>Gets the everyday command packet module (<c>/heal</c>, <c>/sit</c>, <c>/random</c>, wide scan, votes).</summary>
        public PlayerCommandPacketModule CommandModule => NetworkManager.CommandModule;

        /// <summary>Gets the local player's personal pet (S2C 0x068).</summary>
        public LocalPetState Pet => NetworkManager.Pet;

        /// <summary>Gets the login-time data module (mounts, Maze Mongers, Alter Ego points, extended job data).</summary>
        public LoginDataPacketModule LoginDataModule => NetworkManager.LoginDataModule;

        /// <summary>
        /// Gets the active session combat, targeting, recast, and action history state model.
        /// </summary>
        public CombatState Combat => NetworkManager.Combat;

        /// <summary>
        /// Gets the combat, spell casting, ability, and emote packet handling module.
        /// </summary>
        public CombatPacketModule CombatModule => NetworkManager.CombatModule;

        /// <summary>Bridge from S2C 0x028 actions to entity animation (swings, casts, hit reactions).</summary>
        public Animation.ActionPlaybackQueue ActionPlayback => NetworkManager.ActionPlayback;

        /// <summary>
        /// Gets the unified player action coordinator service.
        /// </summary>
        public Actions.PlayerActionService ActionService => NetworkManager.ActionService;

        /// <summary>
        /// Gets the instance-level input state tracking active keys, mouse buttons, and actions.
        /// </summary>
        public Input.InputState InputState { get; } = new Input.InputState();

        /// <summary>
        /// Gets the real-time player locomotion and camera controller.
        /// </summary>
        public Input.PlayerLocomotionController Locomotion { get; }

        /// <summary>The character's macros, palettes and macro runner (#339), kept per character (<see cref="Gordian.Core.Macros.MacroStore"/>).</summary>
        public Gordian.Core.Macros.MacroController Macros { get; }

        private int _serverMessageRequested;

        /// <summary>
        /// Applies the server-stored config menu's Mouse/Cam. axis rows (only the choices the player has made) to this
        /// session's own copy of the input profile: the third-person rows set the mouse inversion, the first-person rows
        /// the separate first-person inversion (#306). Choosing a first-person row turns the separate first-person set
        /// on; its other values start from the third-person ones, so nothing else changes.
        /// </summary>
        private void ApplyCameraSettings() => ApplyCameraSettings(Locomotion.Profile, ActionService.UiSettings);

        /// <summary>Applies the Mouse/Cam. axis rows the player has chosen in <paramref name="settings"/> to <paramref name="profile"/>.</summary>
        public static void ApplyCameraSettings(Input.InputProfile profile, Ui.StockUiSettings settings)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(settings);
            if (settings.HasValue(Ui.StockUiSettingKey.ThirdPersonInvertX)) profile.InvertMouseX = settings.IsOn(Ui.StockUiSettingKey.ThirdPersonInvertX);
            if (settings.HasValue(Ui.StockUiSettingKey.ThirdPersonInvertY)) profile.InvertMouseY = settings.IsOn(Ui.StockUiSettingKey.ThirdPersonInvertY);

            bool firstX = settings.HasValue(Ui.StockUiSettingKey.FirstPersonInvertX), firstY = settings.HasValue(Ui.StockUiSettingKey.FirstPersonInvertY);
            if (!firstX && !firstY) return;
            if (!profile.SeparateFirstPersonMouse)
            {
                profile.SeparateFirstPersonMouse = true;
                profile.FirstPersonMouseSensitivityX = profile.MouseSensitivityX;
                profile.FirstPersonMouseSensitivityY = profile.MouseSensitivityY;
                profile.FirstPersonInvertMouseX = profile.InvertMouseX;
                profile.FirstPersonInvertMouseY = profile.InvertMouseY;
            }
            if (firstX) profile.FirstPersonInvertMouseX = settings.IsOn(Ui.StockUiSettingKey.FirstPersonInvertX);
            if (firstY) profile.FirstPersonInvertMouseY = settings.IsOn(Ui.StockUiSettingKey.FirstPersonInvertY);
        }

        /// <summary>The shared input profile this session copies (#314).</summary>
        private readonly Input.InputProfileHub _inputProfiles;

        /// <summary>The user's input profile changed: take a fresh copy and apply this character's config choices again.</summary>
        private void OnSharedInputProfileChanged(object? source)
        {
            Locomotion.Profile = _inputProfiles.CreateSessionCopy();
            ApplyCameraSettings();
        }

        public CharacterSession(
            string characterName,
            uint characterId,
            string accountUsername,
            SessionNetworkManager networkManager,
            Input.InputProfileHub? inputProfiles = null)
        {
            _inputProfiles = inputProfiles ?? Input.InputProfileHub.Default;
            CharacterName = characterName ?? throw new ArgumentNullException(nameof(characterName));
            CharacterId = characterId;
            AccountUsername = accountUsername ?? string.Empty;
            NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            ActionService.UiLayout = Ui.StockUiLayoutStore.GetForCharacter(characterName);
            ActionService.UiSettings = Ui.StockUiSettingsStore.GetForCharacter(characterName);
            Macros = new Gordian.Core.Macros.MacroController(Gordian.Core.Macros.MacroStore.GetForCharacter(characterName));
            // Every character runs on its own copy of the user's input profile (#314); see InputProfileHub.
            Locomotion = new Input.PlayerLocomotionController(
                InputState,
                _inputProfiles.CreateSessionCopy(),
                World,
                LocalPlayer,
                ActionService
            );
            // The config menu's Mouse/Camera page (third-person axes) drives the session's input profile; only
            // choices the player has made are applied, so an untouched page leaves the profile's own settings alone.
            ApplyCameraSettings();
            ActionService.UiSettings.Changed += (key, _) =>
            {
                if (key is Ui.StockUiSettingKey.ThirdPersonInvertX or Ui.StockUiSettingKey.ThirdPersonInvertY
                    or Ui.StockUiSettingKey.FirstPersonInvertX or Ui.StockUiSettingKey.FirstPersonInvertY) ApplyCameraSettings();
            };
            _inputProfiles.Changed += OnSharedInputProfileChanged;
            // A knockback in an action result slides the player (the server trusts the position it then reports), unless
            // Anchor is on and the server allows it.
            ActionPlayback.HitLanded += (target, reaction) =>
            {
                if (reaction.KnockbackLevel == 0 || target.ServerId != LocalPlayer.ServerId) return;
                if (ActionService.Knockback.IsAnchored(ActionService.Profile)) return;
                Locomotion.ApplyKnockback(new System.Numerics.Vector2(reaction.PushDirectionX, reaction.PushDirectionZ), reaction.KnockbackLevel);
            };
            Locomotion.LocomotionUpdated += (pos, dir, speed) =>
            {
                NetworkManager.NotifyLocomotionChanged(pos, dir, speed);
            };

            Chat.CharacterName = () => CharacterName;
            Chat.LocalPlayerId = () => LocalPlayer.ServerId;
            Chat.StatusIds = () => LocalPlayer.GetStatusEffectIds();
            Chat.CharacterId = () => CharacterId;
            Chat.Settings = () => ActionService.UiSettings;
            Chat.CancelStatus = id => _ = ActionService.CancelBuffAsync(id);
            Chat.Log.Window2Types = () => (uint)ActionService.UiSettings.GetValue(Ui.StockUiSettingKey.LogWindow2Types);
            Chat.ClientChatFilters = () => (uint)ActionService.UiSettings.GetValue(Ui.StockUiSettingKey.ClientChatFilters);
            Chat.PageSize = window => ActionService.UiSettings.GetValue(
                window == 2 ? Ui.StockUiSettingKey.Window2MaxLines : Ui.StockUiSettingKey.Window1MaxLines);
            Chat.Execute = (line, kind) => ActionService.ExecuteCommandAsync(line, kind);
            // Battle messages (file 7027) put "the" before a monster's name: "The Wild Rabbit hits Gemini ...". Named monsters (NamedFlag, #334) take none.
            Chat.Attach(ChatModule, Party, Combat, ActionService.Menus, ResolveEntityName,
                id => World.TryGetByServerId(id, out var e) && e is { TakesArticle: true });
            // The server message (S2C 0x04D, #117): asked for once, when the first zone-in after login completes; /servmes
            // asks again.
            NetworkManager.Parser.HandshakeCompleted += () =>
            {
                if (System.Threading.Interlocked.Exchange(ref _serverMessageRequested, 1) == 0) _ = ChatModule.RequestServerMessageAsync();
            };
            // The command menu's chat-mode list (Tier 2 chunk 6b): picks the default chat mode, shows the last tell
            // partner, and greys the linkshell modes until the server has shown a linkshell in that slot.
            ActionService.Menus.ChatModeSelected = mode => Chat.OpenInputInMode(mode);
            ActionService.Menus.TellTarget = () => Chat.Input.TellTarget;
            ActionService.Menus.TellCandidates = () =>
            {
                // The last tell partner first, then the players around you (yourself included), nearest first.
                var names = new System.Collections.Generic.List<string>();
                string last = Chat.Input.TellTarget;
                if (last.Length > 0) names.Add(last);
                foreach (string name in ActionService.NearbyPlayerNames())
                {
                    if (!names.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
                }
                return names;
            };
            ActionService.Menus.TellTargetSelected = name => Chat.Input.TellTarget = name;
            ActionService.Menus.HasLinkshell = slot => Party.HasLinkshell(slot);
            // Unity membership (#65): the Unity leader (1-11) in S2C 0x061's unity_info; 0 = not in a Unity.
            ActionService.Menus.HasUnity = () => LocalPlayer.UnityFaction != 0;
            ChatModule.LinkshellMessageReceived += msg =>
            {
                if (!string.IsNullOrEmpty(msg.LinkshellName)) Party.SetLinkshellEquipped(msg.Slot == Packets.LinkshellSlot.LS1 ? 1 : 2, true);
            };
            Locomotion.Chat = Chat;
            // Macros (#339, #362): the character's own books, run through the chat's submit so each line acts as typed.
            ActionService.Macros = Macros.Library;
            ActionService.Menus.Macros = Macros.Library;
            ActionService.PetSnapshot = () => Pet.Current;
            Macros.Runner.SubmitLine = line => Chat.SubmitAsync(line, Chat.Input.Mode);
            Macros.Runner.IsSubTargeting = () => ActionService.SubTargeting != null;
            Locomotion.Macros = Macros;
            Chat.ExpandTokens = ActionService.ExpandChatTokens;
            Chat.Macros = () => Macros.Library;
            Events.Attach(NetworkManager.Progression, ProgressionModule, World, LocalPlayer, Chat, ActionService.Menus, () => CharacterName,
                Party, index => _ = EntityModule.RequestEntityInfoAsync(index));
            Locomotion.Events = Events;
            // Event opcodes 0xA6 / 0x75 send the sub-map packets (C2S 0x0EB / 0x0F2) through the zone module.
            Events.Lifecycle = NetworkManager.Parser.LifecycleModule;
            // System messages (S2C 0x053) and everyone's emotes (S2C 0x05A, ours included): text from the client's own
            // message tables, the emote motion on the caster.
            Messages.Attach(Commands, World, LocalPlayer, Chat, () => CharacterName);
            // The NPC shop (Tier 2 chunk 6c): S2C 0x03E / 0x03C / 0x03D drive the shop windows through the inventory
            // state; the windows send 0x083 (buy) and 0x084 + 0x085 (appraise, sell). A zone change ends the shop.
            // The gamepad key-assignment list edits the user's shared profile, so every character gets the change (#21, #314).
            ActionService.Menus.GamepadProfile = () => _inputProfiles.Profile;
            ActionService.Menus.GamepadProfileEdited = edit => _inputProfiles.Edit(edit, ActionService.Menus);
            ActionService.Menus.Inventory = Inventory;
            ActionService.Menus.ShopBuy = (count, shopNo, index) => InventoryModule.BuyShopItemAsync(count, shopNo, index, 0);
            ActionService.Menus.ShopAppraise = (count, itemId, slot) => InventoryModule.AppraiseShopItemAsync(count, itemId, slot);
            ActionService.Menus.ShopSellConfirm = () => InventoryModule.ConfirmShopSaleAsync();
            Inventory.ShopChanged += ActionService.Menus.OnShopChanged;
            Inventory.ShopPurchased += (index, count) =>
            {
                // Retail prints the purchase itself on 0x03F ("You buy 12 Ronfaure chestnuts from the shop.").
                foreach (var item in Inventory.SnapshotShopItems())
                {
                    if (item.ShopIndex != index) continue;
                    Chat.Log.Add(Ui.ChatLogChannel.System, Ui.StockUiShop.BuyMessage(Ui.StockUiShop.Lookup(ActionService.Menus.ItemLookup, item.ItemId), count));
                    return;
                }
            };
            Inventory.ItemChanged += (_, _, _) => ActionService.Menus.OnInventoryChanged();
            // The Items and Equipment windows (#351, #352): use (0x037; LandSandBoat requires the count 0), drop (0x028),
            // equip (0x050), auto-sort (0x03A). Equipment and container sizes refresh the open lists.
            ActionService.Menus.ItemUse = (id, index, container, slot) => InventoryModule.UseItemAsync(id, index, 0, slot, container);
            ActionService.Menus.ItemDrop = (container, slot, count) => InventoryModule.DropItemAsync(container, slot, count);
            ActionService.Menus.ItemEquip = (slot, equipSlot, container) => InventoryModule.EquipItemAsync(slot, equipSlot, container);
            ActionService.Menus.ItemSort = container => InventoryModule.SortContainerAsync(container);
            ActionService.Menus.ItemMove = (count, from, fromSlot, to, toSlot) => InventoryModule.MoveItemAsync(from, fromSlot, to, toSlot, count);
            ActionService.Menus.CharacterStats = CharacterStats;
            Inventory.EquipChanged += (_, _, _) => ActionService.Menus.OnItemsChanged();
            Inventory.ContainerSizesChanged += (_, _, _) => ActionService.Menus.OnItemsChanged();
            // A style lock that could not use some items (S2C 0x11C) prints one error line per item.
            InventoryModule.LockstyleFailed += items =>
            {
                foreach (string line in Ui.LockstyleLog.FormatErrors(items, id => ActionService.Menus.ItemLookup?.Invoke(id))) Chat.Log.Add(Ui.ChatLogChannel.Error, line);
            };
            // A player check (#64): S2C 0x0C9's general block, sent last, opens the check window.
            Commands.Equipment.Completed += info =>
            {
                string name = ResolveEntityName(info.ServerId) is { Length: > 0 } known ? known : info.Message?.Name ?? string.Empty;
                // Items for sale: the entity update's BazaarFlag (0x00D), not 0x0CA's, which LandSandBoat always sets.
                bool bazaar = World.TryGetByServerId(info.ServerId, out var entity) && entity is PlayerEntity { HasBazaar: true };
                ActionService.Menus.OpenCheck(new Ui.StockUiCheckData(name, info, bazaar));
            };
            // Treasure pool events print to the message log as the retail client does; a zone change empties the pool
            // (the server sends it again for a party that is still in it).
            // The Treasure Pool window (#143): the command menus' Treasure opens it; Cast Lot / Pass send 0x041 / 0x042.
            ActionService.Menus.TreasurePool = Treasure;
            ActionService.Menus.TreasureLot = slot => TreasureModule.SendLotAsync(slot);
            ActionService.Menus.TreasurePass = slot => TreasureModule.SendPassAsync(slot);
            Treasure.Changed += ActionService.Menus.OnTreasureChanged;
            var treasureLog = new Ui.StockUiTreasure(id => ActionService.Menus.ItemLookup?.Invoke(id), ResolveEntityName, () => LocalPlayer.ServerId);
            Treasure.Found += found =>
            {
                foreach (string line in treasureLog.FormatFound(found)) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            Treasure.Solved += solution =>
            {
                foreach (string line in treasureLog.FormatSolution(solution)) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            // Synthesis results print to the message log as the retail client does.
            Crafting.SynthesisCompleted += outcome =>
            {
                foreach (string line in Ui.CraftingLog.FormatOwn(outcome, id => ActionService.Menus.ItemLookup?.Invoke(id), Messages)) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            Crafting.OtherSynthesisCompleted += outcome =>
            {
                foreach (string line in Ui.CraftingLog.FormatOther(outcome, id => ActionService.Menus.ItemLookup?.Invoke(id), Messages)) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            // The synthesis animation (S2C 0x030) plays on the crafters. The guild shop's purchases and sales print to the log;
            // its lists and the open status print only for /guild while the guild shop window (#330) is not up.
            SynthesisAnimation.Attach(Combat, Crafting, World, LocalPlayer);
            Inventory.GuildTransactionReceived += t =>
            {
                foreach (string line in Ui.GuildShopLog.FormatTransaction(t, id => ActionService.Menus.ItemLookup?.Invoke(id), Messages)) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            Inventory.GuildListCompleted += (sells, items) =>
            {
                if (ActionService.Menus.IsGuildShopOpen) return;
                foreach (string line in Ui.GuildShopLog.FormatList(sells, items, id => ActionService.Menus.ItemLookup?.Invoke(id))) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            Inventory.GuildStatusReceived += (status, hours) =>
            {
                if (status != Packets.ShopOpenStatus.Open) Chat.Log.Add(Ui.ChatLogChannel.System, Ui.GuildShopLog.FormatStatus(status, hours));
            };
            World.ZoneChanged += _ =>
            {
                if (Inventory.IsShopOpen) Inventory.CloseShop();
                // LandSandBoat empties the recycle bin whenever the character is loaded, i.e. at every zone-in
                // (charutils::EmptyRecycleBin), without telling the client; retail's help text says the same (#351).
                Inventory.ClearContainer(Packets.ContainerId.RecycleBin);
                Treasure.Clear();
                // The pet, the wide scan list and the tracked entity do not survive a zone change; the server sends the
                // pet again (S2C 0x068) when it comes along.
                Pet.Clear();
                Commands.OnZoneChanged();
            };
            // Wide scan lists and proposals print to the message log; no window for them exists yet.
            Commands.WideScan.ListCompleted += entries =>
            {
                foreach (string line in Ui.StockUiPlayerCommands.FormatWideScan(entries, ResolveEntityNameByIndex)) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            Commands.WideScan.ListFailed += state =>
                Chat.Log.Add(Ui.ChatLogChannel.System, state == TrackingListState.Error ? "Wide Scan is not available." : "Wide Scan ended.");
            Commands.Votes.Changed += proposal =>
            {
                var lines = proposal.Closed ? Ui.StockUiPlayerCommands.FormatProposalResult(proposal)
                    : proposal.Votes.All(v => v == 0) ? Ui.StockUiPlayerCommands.FormatProposalStart(proposal)
                    : Enumerable.Empty<string>();
                foreach (string line in lines) Chat.Log.Add(Ui.ChatLogChannel.System, line);
            };
            // The character stops while the input line has the keyboard (keys held when it opened are released).
            Chat.Input.OpenChanged += open =>
            {
                if (open) InputState.Reset();
            };
            AttachKeyItemsAndEquipmentSets();
            AttachSynthesisAndGuildShop();
        }

        /// <summary>
        /// The stock chat: log windows and the chat input line (Tier 2 chunk 5).
        /// </summary>
        public Ui.StockUiChat Chat { get; } = new();

        /// <summary>Event dialog: NPC talk, choice menus and zone messages (Tier 2 chunk 6).</summary>
        public Events.EventDialogController Events { get; } = new();

        /// <summary>System messages and emotes from the client's own message tables (S2C 0x053, 0x05A).</summary>
        public Events.ClientMessageController Messages { get; } = new();

        /// <summary>Plays the synthesis animation on the crafters (S2C 0x030).</summary>
        public Animation.SynthesisAnimationController SynthesisAnimation { get; } = new();

        private string? ResolveEntityName(uint id)
        {
            if (id == LocalPlayer.ServerId || id == CharacterId) return CharacterName;
            return World.TryGetByServerId(id, out var entity) && !string.IsNullOrEmpty(entity?.Name) ? entity.Name : null;
        }

        /// <summary>
        /// The Equipment window's stats side (#352), as the maintainer's retail capture (2026-10-11) lays it out: the name,
        /// "Lv99 Bard", "Lv49 White Mage", "ILv 119/Su 0", HP and MP current / max, TP, the base stats with their equipment
        /// bonus, Attack, Defense and the elemental resistances (S2C 0x061 / 0x01B, as <see cref="LocalPlayerState"/> keeps them).
        /// </summary>
        private Ui.StockUiCharacterStats CharacterStats()
        {
            var p = LocalPlayer;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string main = p.MainJob == Packets.JobId.None ? string.Empty : string.Create(inv, $"Lv{p.MainJobLevel} {Ui.StockUiCheck.JobName((byte)p.MainJob)}");
            string sub = p.SubJob == Packets.JobId.None ? string.Empty : string.Create(inv, $"Lv{p.SubJobLevel} {Ui.StockUiCheck.JobName((byte)p.SubJob)}");
            string itemLevel = string.Create(inv, $"ILv {p.ItemLevel}/Su {p.SuperiorLevel}");
            var bases = new int[7];
            var bonuses = new int[7];
            for (int i = 0; i < 7; i++)
            {
                bases[i] = p.BaseStats[i];
                bonuses[i] = p.StatModifiers[i];
            }
            var resistances = new int[8];
            for (int i = 0; i < 8; i++) resistances[i] = p.ElementalResistances[i];
            return new Ui.StockUiCharacterStats(CharacterName, main, sub, itemLevel, p.CurrentHp, p.MaxHp, p.CurrentMp, p.MaxMp, p.CurrentTp,
                bases, bonuses, p.Attack, p.Defense, resistances);
        }

        private string? ResolveEntityNameByIndex(ushort targetIndex)
            => World.TryGetByTargetIndex(targetIndex, out var entity) && !string.IsNullOrEmpty(entity?.Name) ? entity.Name : null;

        public void Disconnect()
        {
            NetworkManager.Disconnect();
        }

        public void Dispose()
        {
            _inputProfiles.Changed -= OnSharedInputProfileChanged;
            NetworkManager.Dispose();
        }

        public override string ToString()
        {
            return $"{CharacterName} (ID: {CharacterId}, State: {State})";
        }
    }
}
