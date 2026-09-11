// src/Gordian.Core/Network/Crypto/IPacketCryptoSuite.cs
using System;

namespace Gordian.Core.Network.Crypto
{
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
        /// Decrypts and authenticates an incoming packet payload slice in-place.
        /// </summary>
        /// <param name="packetData">The full raw datagram buffer (including the 28-byte FFXI header).</param>
        /// <param name="headerSize">The length of the unencrypted FFXI envelope header (standard 28 bytes).</param>
        /// <param name="decryptedPayloadLength">Outputs the length of valid decrypted payload data.</param>
        /// <returns>True if decryption and checksum verification succeeded; false if corrupted or forged.</returns>
        bool TryDecryptAndVerify(Span<byte> packetData, int headerSize, out int decryptedPayloadLength);

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
