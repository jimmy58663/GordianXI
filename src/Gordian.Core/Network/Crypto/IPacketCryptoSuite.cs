// src/Gordian.Core/Network/Crypto/IPacketCryptoSuite.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Network.Crypto
{
    /// <summary>One session key the server may be using, with a label for logs (never the key itself).</summary>
    public sealed record SessionKeyCandidate(string Label, byte[] Key);

    /// <summary>
    /// Represents a pluggable, zero-allocation cryptographic engine for encrypting, decrypting,
    /// and authenticating FFXI network packet frames.
    /// </summary>
    public interface IPacketCryptoSuite : IDisposable
    {
        /// <summary>
        /// Gets the human-readable identifier of the cipher suite (e.g., "Blowfish-FFXI-ECB", "AES-256-GCM").
        /// </summary>
        string SuiteName { get; }

        /// <summary>
        /// Gets whether cryptographic keys have been established for the current session.
        /// </summary>
        bool IsKeyInitialized { get; }

        /// <summary>
        /// Derives the cryptographic state/subkeys from the server handshake key material.
        /// </summary>
        /// <param name="key">The session key bytes received during authentication.</param>
        void InitializeKey(ReadOnlySpan<byte> key);

        /// <summary>
        /// Starts a session whose key is one of <paramref name="candidates"/> (in order of preference) without knowing
        /// which: the first inbound datagram that verifies with a candidate locks that candidate in. Until then
        /// outbound packets use the first candidate. A suite without negotiation just uses the first candidate.
        /// </summary>
        void InitializeKeyCandidates(IReadOnlyList<SessionKeyCandidate> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            if (candidates.Count == 0) throw new ArgumentException("At least one key candidate is required.", nameof(candidates));
            InitializeKey(candidates[0].Key);
        }

        /// <summary>The label of the candidate key in use once one verified, or null while negotiating or for a single key.</summary>
        string? NegotiatedKeyLabel => null;

        /// <summary>True while several candidate keys are still being tried.</summary>
        bool IsNegotiatingKey => false;

        /// <summary>
        /// Advances the session cryptographic key when transitioning zones (LandSandBoat adds 2 to the fifth 32-bit key word).
        /// </summary>
        /// <returns>True if the key was successfully advanced; false if no underlying key material exists.</returns>
        bool AdvanceZoneKey();

        /// <summary>
        /// Decrypts and authenticates an incoming packet payload slice in-place.
        /// </summary>
        /// <param name="packetData">The full raw datagram buffer (including the 28-byte FFXI header).</param>
        /// <param name="headerSize">The length of the unencrypted FFXI envelope header (standard 28 bytes).</param>
        /// <param name="decryptedPayloadLength">Outputs the length of valid decrypted payload data.</param>
        /// <returns>True if decryption and checksum verification succeeded; false if corrupted or forged.</returns>
        bool TryDecryptAndVerify(Span<byte> packetData, int headerSize, out int decryptedPayloadLength);

        /// <summary>
        /// True when the most recent successful <see cref="TryDecryptAndVerify"/> only verified with the previous zone's
        /// key, i.e. the datagram is a straggler from the map server the session just left.
        /// </summary>
        bool LastDecryptUsedPreviousKey => false;

        /// <summary>
        /// Encrypts and appends integrity checks/signatures to an outbound packet in-place.
        /// </summary>
        /// <param name="datagramBuffer">The datagram buffer containing the header and payload data.</param>
        /// <param name="headerSize">The length of the unencrypted FFXI envelope header (standard 28 bytes).</param>
        /// <param name="payloadLength">The length of the unencrypted payload after the header.</param>
        /// <returns>The total length of the final datagram ready for wire transmission (header + encrypted payload + signature).</returns>
        int EncryptAndSign(Span<byte> datagramBuffer, int headerSize, int payloadLength);
    }
}
