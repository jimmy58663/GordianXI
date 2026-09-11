// src/Gordian.Core/Network/SessionRegistry.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Thread-safe central registry tracking all active in-memory character sessions.
    /// Provides the single source of truth for LaunchOrchestrator, Gordian.App (UI dashboards),
    /// and Gordian.Automation (team coordination).
    /// </summary>
    public sealed class SessionRegistry : IDisposable
    {
        private static readonly Lazy<SessionRegistry> _defaultInstance =
            new(() => new SessionRegistry());

        /// <summary>
        /// Gets the default shared SessionRegistry singleton instance.
        /// </summary>
        public static SessionRegistry Default => _defaultInstance.Value;

        private readonly ConcurrentDictionary<Guid, CharacterSession> _sessions = new();
        private readonly object _registrationLock = new();

        /// <summary>
        /// Fires when a new character session is registered.
        /// </summary>
        public event EventHandler<CharacterSession>? SessionRegistered;

        /// <summary>
        /// Fires when an existing character session is unregistered.
        /// </summary>
        public event EventHandler<CharacterSession>? SessionUnregistered;

        /// <summary>
        /// Gets an immutable snapshot of all active character sessions.
        /// </summary>
        public IReadOnlyList<CharacterSession> ActiveSessions => _sessions.Values.ToList();

        /// <summary>
        /// Determines if a character is currently active in memory by character name.
        /// </summary>
        public bool IsCharacterActive(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName)) return false;

            return _sessions.Values.Any(s =>
                string.Equals(s.CharacterName, characterName, StringComparison.OrdinalIgnoreCase) &&
                s.State != SessionState.Disconnected);
        }

        /// <summary>
        /// Determines if an account is currently active in memory by account username.
        /// </summary>
        public bool IsAccountActive(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            return _sessions.Values.Any(s =>
                string.Equals(s.AccountUsername, username, StringComparison.OrdinalIgnoreCase) &&
                s.State != SessionState.Disconnected);
        }

        /// <summary>
        /// Registers an existing CharacterSession into the active registry.
        /// </summary>
        public void RegisterSession(CharacterSession session)
        {
            ArgumentNullException.ThrowIfNull(session);

            lock (_registrationLock)
            {
                if (_sessions.TryAdd(session.SessionId, session))
                {
                    SessionRegistered?.Invoke(this, session);
                }
            }
        }

        /// <summary>
        /// Creates, registers, and initializes a new CharacterSession from proxy handoff parameters.
        /// </summary>
        public CharacterSession CreateAndRegisterSession(SessionHandoffArgs handoff, string? accountUsername = null)
        {
            ArgumentNullException.ThrowIfNull(handoff);

            lock (_registrationLock)
            {
                var networkManager = new SessionNetworkManager(handoff.ServerIp, handoff.ServerPort)
                {
                    CharacterId = handoff.CharacterId,
                    CharacterName = handoff.TargetCharacterName,
                    AccountName = accountUsername ?? string.Empty
                };

                // Initialize crypto key if base64 token provided
                if (!string.IsNullOrWhiteSpace(handoff.Base64SessionToken))
                {
                    try
                    {
                        byte[] key = Convert.FromBase64String(handoff.Base64SessionToken);
                        networkManager.Parser.InitializeSessionCrypto(key);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SessionRegistry] Failed to parse Base64SessionToken: {ex.Message}");
                    }
                }

                networkManager.CurrentState = SessionState.ConnectingToGameServer;

                var session = new CharacterSession(
                    handoff.TargetCharacterName,
                    handoff.CharacterId,
                    accountUsername ?? string.Empty,
                    networkManager
                );

                _sessions[session.SessionId] = session;
                SessionRegistered?.Invoke(this, session);
                return session;
            }
        }

        /// <summary>
        /// Attempts to retrieve a session by its unique Guid identifier.
        /// </summary>
        public bool TryGetSession(Guid sessionId, out CharacterSession? session)
        {
            return _sessions.TryGetValue(sessionId, out session);
        }

        /// <summary>
        /// Attempts to retrieve a session by character name.
        /// </summary>
        public bool TryGetSessionByCharacterName(string characterName, out CharacterSession? session)
        {
            session = _sessions.Values.FirstOrDefault(s =>
                string.Equals(s.CharacterName, characterName, StringComparison.OrdinalIgnoreCase));
            return session != null;
        }

        /// <summary>
        /// Unregisters and disconnects a character session.
        /// </summary>
        public void UnregisterSession(Guid sessionId)
        {
            lock (_registrationLock)
            {
                if (_sessions.TryRemove(sessionId, out var session))
                {
                    session.Disconnect();
                    SessionUnregistered?.Invoke(this, session);
                }
            }
        }

        /// <summary>
        /// Clears and disposes all registered sessions.
        /// </summary>
        public void Clear()
        {
            lock (_registrationLock)
            {
                foreach (var session in _sessions.Values)
                {
                    session.Disconnect();
                    SessionUnregistered?.Invoke(this, session);
                }
                _sessions.Clear();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
