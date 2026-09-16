// src/Gordian.Core/Network/CharacterSession.cs
using System;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Represents an active, in-memory character session connection, encapsulating
    /// character identity, connection state, and the dedicated network pipeline.
    /// </summary>
    public sealed class CharacterSession : IDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public string CharacterName { get; }
        public uint CharacterId { get; }
        public string AccountUsername { get; }
        public SessionNetworkManager NetworkManager { get; }
        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

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
        /// Gets the active session combat, targeting, recast, and action history state model.
        /// </summary>
        public CombatState Combat => NetworkManager.Combat;

        /// <summary>
        /// Gets the combat, spell casting, ability, and emote packet handling module.
        /// </summary>
        public CombatPacketModule CombatModule => NetworkManager.CombatModule;

        /// <summary>
        /// Gets the unified player action coordinator service.
        /// </summary>
        public Actions.PlayerActionService ActionService => NetworkManager.ActionService;

        public CharacterSession(
            string characterName,
            uint characterId,
            string accountUsername,
            SessionNetworkManager networkManager)
        {
            CharacterName = characterName ?? throw new ArgumentNullException(nameof(characterName));
            CharacterId = characterId;
            AccountUsername = accountUsername ?? string.Empty;
            NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        public void Disconnect()
        {
            NetworkManager.Disconnect();
        }

        public void Dispose()
        {
            NetworkManager.Dispose();
        }

        public override string ToString()
        {
            return $"{CharacterName} (ID: {CharacterId}, State: {State})";
        }
    }
}
