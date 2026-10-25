// src/Gordian.Core/Network/LandSandBoat/ServerCertificateTrust.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>What <see cref="ServerCertificateTrust.Evaluate"/> decided about an auth server's certificate.</summary>
    public enum CertificateTrustDecision
    {
        /// <summary>The certificate verifies against the system's trusted roots and names the host, or it is the one the user pinned.</summary>
        Trusted,

        /// <summary>The certificate does not verify (a self-signed LandSandBoat <c>login.cert</c>, say) and nothing is pinned for this server.</summary>
        Unknown,

        /// <summary>The certificate does not verify and is not the one pinned for this server: it changed, or someone is intercepting.</summary>
        Changed
    }

    /// <summary>
    /// An auth server certificate the client did not trust, as shown to the user to approve or refuse.
    /// <see cref="PreviousFingerprint"/> is set when the server had a different certificate pinned.
    /// </summary>
    public sealed record ServerCertificateInfo(
        string Host,
        int Port,
        string Sha256Fingerprint,
        string Subject,
        string Issuer,
        DateTime NotBefore,
        DateTime NotAfter,
        SslPolicyErrors PolicyErrors,
        string? PreviousFingerprint)
    {
        /// <summary>Whether this server had another certificate pinned (see <see cref="CertificateTrustDecision.Changed"/>).</summary>
        public bool IsChanged => PreviousFingerprint != null;

        public static ServerCertificateInfo From(string host, int port, X509Certificate certificate, SslPolicyErrors errors, string? previousFingerprint)
        {
            // SslStream hands over an X509Certificate2 it owns; only a copy made here is disposed here.
            var cert = certificate as X509Certificate2;
            using var copy = cert == null ? new X509Certificate2(certificate) : null;
            cert ??= copy!;
            return new ServerCertificateInfo(host, port, ServerCertificateTrust.Fingerprint(cert), cert.Subject, cert.Issuer,
                cert.NotBefore, cert.NotAfter, errors, previousFingerprint);
        }
    }

    /// <summary>
    /// Thrown when an auth server's certificate is not trusted and nobody approved it. The TLS handshake was refused,
    /// so no credentials were sent.
    /// </summary>
    public sealed class UntrustedServerCertificateException : AuthenticationException
    {
        public ServerCertificateInfo Certificate { get; }

        public UntrustedServerCertificateException(ServerCertificateInfo certificate)
            : base(certificate.IsChanged
                ? $"The TLS certificate of {certificate.Host}:{certificate.Port} has changed since it was trusted (was {certificate.PreviousFingerprint}, now {certificate.Sha256Fingerprint}). The login was stopped before sending credentials."
                : $"The TLS certificate of {certificate.Host}:{certificate.Port} is not trusted (SHA-256 {certificate.Sha256Fingerprint}). The login was stopped before sending credentials.")
        {
            Certificate = certificate;
        }
    }

    /// <summary>
    /// Trust on first use for LandSandBoat auth servers (#445). LandSandBoat servers usually present a self-signed
    /// <c>login.cert</c>, so a certificate that verifies normally is trusted, and one that does not is trusted only when
    /// the user approved its SHA-256 fingerprint for that server before (<see cref="KnownServerCertificateStore"/>).
    /// </summary>
    public static class ServerCertificateTrust
    {
        /// <summary>The certificate's SHA-256 fingerprint as colon-separated upper-case hex (<c>AB:CD:...</c>).</summary>
        public static string Fingerprint(X509Certificate certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(certificate.GetRawCertData(), hash);
            return string.Join(':', Convert.ToHexString(hash).Chunk(2).Select(pair => new string(pair)));
        }

        /// <summary>
        /// The decision for a certificate with <paramref name="errors"/> and <paramref name="fingerprint"/>, given the
        /// fingerprint pinned for its server (<see langword="null"/> when none). A certificate that verifies normally wins
        /// over the pin, so a server that moves to a CA-issued certificate keeps working.
        /// </summary>
        public static CertificateTrustDecision Evaluate(SslPolicyErrors errors, string fingerprint, string? pinnedFingerprint)
        {
            if (errors == SslPolicyErrors.None) return CertificateTrustDecision.Trusted;
            if (pinnedFingerprint == null) return CertificateTrustDecision.Unknown;
            return string.Equals(fingerprint, pinnedFingerprint, StringComparison.OrdinalIgnoreCase)
                ? CertificateTrustDecision.Trusted
                : CertificateTrustDecision.Changed;
        }
    }

    /// <summary>
    /// The auth server certificates the user approved, one SHA-256 fingerprint per server (<c>host:port</c>), kept in
    /// <c>known_servers.json</c> under <see cref="GordianStorage.UserSettingsDirectory"/> so every profile on the same
    /// server shares the approval. The pins are kept in memory and read again whenever the file's last-write time or
    /// size changed since it was read or written here, so a hand edit or delete made while GordianXI runs takes effect
    /// at the next login and is not overwritten by the next approval (#450).
    /// </summary>
    public sealed class KnownServerCertificateStore
    {
        private const string FileName = "known_servers.json";
        private static readonly Lazy<KnownServerCertificateStore> s_default =
            new(() => new KnownServerCertificateStore(Path.Combine(GordianStorage.UserSettingsDirectory, FileName)));

        private readonly string _filePath;
        private readonly object _gate = new();
        private Dictionary<string, string>? _pins;
        private (bool Exists, DateTime LastWriteUtc, long Length) _loadedStamp;

        /// <summary>The store in the user's settings folder.</summary>
        public static KnownServerCertificateStore Default => s_default.Value;

        public KnownServerCertificateStore(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            _filePath = filePath;
        }

        private static string Key(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

        /// <summary>The fingerprint pinned for <paramref name="host"/>:<paramref name="port"/>, or <see langword="null"/>.</summary>
        public string? Get(string host, int port)
        {
            lock (_gate)
            {
                return Load().TryGetValue(Key(host, port), out var fingerprint) ? fingerprint : null;
            }
        }

        /// <summary>Pins <paramref name="fingerprint"/> for <paramref name="host"/>:<paramref name="port"/>, replacing any earlier pin, and saves.</summary>
        public void Set(string host, int port, string fingerprint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
            lock (_gate)
            {
                var pins = Load();
                pins[Key(host, port)] = fingerprint;
                try
                {
                    string? directory = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(pins, new JsonSerializerOptions { WriteIndented = true }));
                    _loadedStamp = Stamp();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The pin holds in memory until the file changes or GordianXI restarts; then the user is asked again.
                    GordianLog.Warning("LSB_LOGIN", $"Could not save the trusted server certificate to {_filePath}: {ex.Message}");
                }
            }
        }

        /// <summary>The file's existence, last-write time and size: what tells a changed file from the one in memory.</summary>
        private (bool Exists, DateTime LastWriteUtc, long Length) Stamp()
        {
            try
            {
                var info = new FileInfo(_filePath);
                return info.Exists ? (true, info.LastWriteTimeUtc, info.Length) : default;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return default;
            }
        }

        /// <summary>The pins, read from the file the first time and again whenever the file changed on disk.</summary>
        private Dictionary<string, string> Load()
        {
            var stamp = Stamp();
            if (_pins != null && stamp == _loadedStamp) return _pins;
            _pins = new Dictionary<string, string>(StringComparer.Ordinal);
            _loadedStamp = stamp;
            try
            {
                if (stamp.Exists)
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath));
                    if (saved != null)
                    {
                        foreach (var (key, value) in saved) _pins[key.ToLowerInvariant()] = value;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable file trusts nothing: every server is asked about again.
                GordianLog.Warning("LSB_LOGIN", $"Could not read the trusted server certificates in {_filePath}: {ex.Message}");
            }
            return _pins;
        }
    }
}
