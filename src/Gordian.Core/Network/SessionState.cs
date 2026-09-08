// src/Gordian.Core/Network/SessionState.cs
using System;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Defines the precise network lifecycle and authentication phase states for an active client connection session.
    /// </summary>
    public enum SessionState
    {
        /// <summary>
        /// Sockets are closed and the session is completely idle.
        /// </summary>
        Disconnected,

        /// <summary>
        /// Establishing the initial TCP handshakes with the PlayOnline authentication gate.
        /// </summary>
        ConnectingToPol,

        /// <summary>
        /// Credentials and One-Time Passwords have been validated by the login server.
        /// </summary>
        PolAuthenticated,

        /// <summary>
        /// Redirecting the connection socket stream to the dedicated game world server address.
        /// </summary>
        ConnectingToGameServer,

        /// <summary>
        /// Handshaking symmetric encryption keys and building the local Blowfish-CBC state arrays.
        /// </summary>
        ExchangingCryptoKeys,

        /// <summary>
        /// Client has established secure communications and is rendering or selecting from the lobby character list.
        /// </summary>
        CharacterSelection,

        /// <summary>
        /// Downloading initial zone geometry maps, local objects, and player inventory registries from the server.
        /// </summary>
        LoadingWorldData,

        /// <summary>
        /// The character is fully spawned into the environment. Background thread engines are running active automation routines.
        /// </summary>
        ActiveInWorld,

        /// <summary>
        /// Processing logouts, character switches, or graceful packet termination cleanups.
        /// </summary>
        Disconnecting
    }
}
