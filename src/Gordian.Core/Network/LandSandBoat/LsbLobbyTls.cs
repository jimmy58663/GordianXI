// src/Gordian.Core/Network/LandSandBoat/LsbLobbyTls.cs
using System;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// How a LandSandBoat profile reaches the lobby's data (xi_data) and view (xi_view) servers. <see cref="Auto"/>
    /// follows the xiloader version the auth server accepted (<see cref="LsbLobbyTls.UsesTls"/>); the other two are a
    /// per-profile override for a server the version does not describe, such as a build old enough to accept any version.
    /// </summary>
    public enum LsbLobbyTransport
    {
        /// <summary>TLS when the auth server accepted xiloader 3.x or later, plain TCP before that.</summary>
        Auto,

        /// <summary>Always TLS (LandSandBoat since 778b74294a, xiloader 3.0).</summary>
        Tls,

        /// <summary>Always plain TCP (LandSandBoat before 778b74294a).</summary>
        Plain,
    }

    /// <summary>
    /// A successful LandSandBoat auth login: the account id and session hash the lobby needs, the xiloader version the
    /// server accepted, the server's key fingerprint (<see cref="LsbLobbyTls.KeyFingerprint"/>) and whether the lobby
    /// connections use TLS. Deconstructs to <c>(AccountId, SessionHash)</c>.
    /// </summary>
    public sealed record LsbAuthentication(
        uint AccountId,
        byte[] SessionHash,
        (byte Major, byte Minor, byte Patch) XiloaderVersion,
        string ServerKeyFingerprint,
        bool LobbyUsesTls)
    {
        /// <summary>The fingerprint the data and view servers must present, or null when the lobby is plain TCP.</summary>
        public string? LobbyServerKey => LobbyUsesTls ? ServerKeyFingerprint : null;

        public void Deconstruct(out uint accountId, out byte[] sessionHash)
        {
            accountId = AccountId;
            sessionHash = SessionHash;
        }
    }

    /// <summary>Thrown when a lobby server's TLS key is not the auth server's: the lobby was not opened.</summary>
    public sealed class LobbyServerKeyMismatchException : AuthenticationException
    {
        public LobbyServerKeyMismatchException(string message) : base(message) { }
    }

    /// <summary>
    /// TLS on LandSandBoat's lobby data (54230) and view (54001) connections. Since LandSandBoat 778b74294a ("Serve lobby
    /// data and view over TLS", Nov 2026, with xiloader 3.0 required from 9b6df7c7e2) both use the same TLS 1.3 server
    /// context and certificate as the auth server (xi_connect, 54231), and their packets are unchanged inside the tunnel.
    /// xiloader 3.0 accepts a lobby connection only when the server presents the key the auth server presented, so the
    /// lobby needs no trust prompt of its own; GordianXI does the same with <see cref="KeyFingerprint"/>.
    /// <para>
    /// Protocol behaviour referenced from LandSandBoat (https://github.com/LandSandBoat/server, src/login/handler_session.cpp
    /// and src/login/auth_session.h) and xiloader (https://github.com/LandSandBoat/xiloader, v3.0.0 release notes and
    /// src/net/tls_client_config.cpp for the fingerprint format).
    /// </para>
    /// </summary>
    public static class LsbLobbyTls
    {
        /// <summary>The first xiloader major version whose servers run the lobby over TLS.</summary>
        public const byte FirstTlsMajorVersion = 3;

        /// <summary>What a failed lobby handshake suggests: the server may predate the TLS lobby.</summary>
        private const string PlainServerHint =
            "A LandSandBoat server older than xiloader 3.0 serves the lobby without TLS: set the profile's lobby encryption to Plain.";

        /// <summary>
        /// Whether the lobby uses TLS for a server that accepted <paramref name="acceptedVersion"/>, given the profile's
        /// <paramref name="transport"/> choice.
        /// </summary>
        public static bool UsesTls((byte Major, byte Minor, byte Patch) acceptedVersion, LsbLobbyTransport transport) => transport switch
        {
            LsbLobbyTransport.Tls => true,
            LsbLobbyTransport.Plain => false,
            _ => acceptedVersion.Major >= FirstTlsMajorVersion,
        };

        /// <summary>
        /// The certificate's public key fingerprint as xiloader 3.0 shows it: <c>SHA256:</c> and the base64 (no padding)
        /// SHA-256 of the DER SubjectPublicKeyInfo. It names the key, not the certificate, so a renewed certificate for
        /// the same key keeps it, and operators can publish one value for xiloader and GordianXI alike.
        /// </summary>
        public static string KeyFingerprint(X509Certificate certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            var cert = certificate as X509Certificate2;
            using var copy = cert == null ? new X509Certificate2(certificate) : null;
            cert ??= copy!;
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo(), hash);
            return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
        }

        /// <summary>
        /// Runs the TLS 1.3 client handshake for a lobby connection over <paramref name="inner"/> and returns the
        /// encrypted stream, which owns <paramref name="inner"/>. The server must present the key with
        /// <paramref name="expectedKey"/> (the auth server's, <see cref="LsbAuthentication.ServerKeyFingerprint"/>);
        /// anything else fails with <see cref="LobbyServerKeyMismatchException"/>.
        /// </summary>
        public static async Task<Stream> AuthenticateAsync(Stream inner, string host, int port, string channel, string expectedKey,
            TimeSpan timeout, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentException.ThrowIfNullOrEmpty(expectedKey);
            string? presented = null;
            bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
            {
                if (certificate == null) return false;
                presented = KeyFingerprint(certificate);
                return string.Equals(presented, expectedKey, StringComparison.Ordinal);
            }

            var ssl = new SslStream(inner, leaveInnerStreamOpen: false, Validate);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.Tls13,
                }, cts.Token).ConfigureAwait(false);
            }
            catch (AuthenticationException) when (presented != null && presented != expectedKey)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw new LobbyServerKeyMismatchException(
                    $"The lobby {channel} server {host}:{port} presented key {presented}, not the login server's {expectedKey}. The lobby was not opened.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw new TimeoutException($"The TLS handshake with the lobby {channel} server {host}:{port} timed out after {timeout.TotalSeconds:0.#} s. " +
                    PlainServerHint);
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw new IOException($"The TLS handshake with the lobby {channel} server {host}:{port} failed ({ex.Message}). {PlainServerHint}", ex);
            }
            catch
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            GordianLog.Info(LogCategories.LsbLogin, $"Lobby {channel} connection to {host}:{port} secured ({ssl.SslProtocol}, server key {presented}).");
            return ssl;
        }
    }
}
