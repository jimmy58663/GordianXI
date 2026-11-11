# Retail Login (Direct, POL-Bypassing)

> How GordianXI logs in to Square Enix's retail servers without `pol.exe`. It is written from the derived spec alone ([retail-login-spec.md](retail-login-spec.md) v2.1.19, clean-room policy in [retail-login-research.md](retail-login-research.md)). Status: the protocol pieces are built and unit-tested against the spec's vectors. The stages are not yet run in order, and nothing has been run against the live servers.

## Pieces (`src/Gordian.Core/Network/Retail`)

| Stage (spec section 2) | Class | Status |
|---|---|---|
| 1, 7. POLP version checks (`pt008.pol.com:54000` Viewer, `pc001W2U.pol.com:53001` FFXI) | `PolpPackets`, `PolpVersionChecker`, `PatchConfigFile` | Built. Type 7 request and Type 8 response (CRC-32 checked). The version string is the product id (`1000` Viewer, `0001` FFXI) and the highest version tag in that product's `patch.cfg`; `patch.ver` is encrypted and not read. Status 1 means up to date. The Viewer check is not required by the login servers (not verified). |
| 2, 3. Core IRC: gatekeeper `ci000.pol.com:51240`, session host `:51241`-`:51247` | `PolCoreProtocol`, `PolCoreSession`, `PolCoreToken`, `PolRsaKeyExchange`, `PolIdEncoding`, `PolClientMeta` | Built. The gatekeeper's and the session host's 44-character tokens share one 25-byte layout (server time, client address, redirect host and port, client port). The redirect can name any port, 51240 included. Ephemeral 256-bit RSA (e = 65535): `USER` carries the modulus N little-endian, the server returns C big-endian, and K8 is the last 8 bytes of the PKCS#1 v1.5 block reversed. Encrypted `NICK` (86 bytes, no tabs): the encoded POL ID, the password response MD5(greeting token body + POL password), the client metadata body computed from the POL ID, and its checksum suffix. The routing token (the client's port and address) comes from the greeting; the `001` payload carries nothing the client uses. The connection stays open and the session answers the server's `PING`s. |
| 4. Square Enix account (`pp000.pol.com:51220`, `0x04/0x07`) | `PolPresencePackets`, `PolPresenceClient.AuthenticateAsync` | Built. Password or password + one-time code; the reply's header names the assigned `:51220` server (last octet first), and the 8 payload bytes it announces never come. Status `0x6F` is a wrong Square Enix ID or password (the only error code captured). Every request header carries 12 zero bytes and MD5(POL ID + POL password + LE32(challenge seed)). |
| 5, 6, 10. Session keys (assigned `:51220`, `0x04/0x05`) | `PolPresenceClient.RequestSessionKeyAsync` | Built. `Lobby` sends ServiceType 0, flags 1; `Viewer` sends ServiceType 1, flags 1000 (not the PML key; retail asks for it, its use is unknown). ClientNonce 0. The hello sends zeros after the routing token. |
| 7. PML pages (`wh000.pol.com:51304`) | `PolHashes.PmlUserName`, `PmlDigestResponse` | Digest only: userName is the client's `a.b.c.d:port` (from the routing token) in POL Base64, the password is POL Base64 of K8, and the response is MD5(HA1:HA2:nonce). The HTTP exchange (and the maintenance flags it carries) is not built. |
| 8, 9, 11-13. Lobby `ffxi00.pol.com:54001` | `RetailLobbyPackets`, `RetailLobbyModifier`, `PolAuthCode`, `ClientFileChecksum` | Packets built ([packets.md](packets.md#retail-lobby-ffxi00polcom54001)); the connection and the character select hookup are not. `0x26` carries the AuthCode token (MD5 of the lobby key, the greeting's server time and K8, encrypted with a fixed-key block cipher). `0x07`'s selection digest includes `ClientFileChecksum` of the install's FFXiMain.dll. |
| Map server | `RetailLobbyPackets.MapKey` / `MapRawKey`, `RetailMapPackets` | The map key is SessionHash(the `0x07` modifier + 4); `MapRawKey` gives the 20-byte key `LegacyBlowfishCryptoSuite` MD5s to it. The first datagram (136 bytes, unencrypted) carries `Ticket` = MD5(15 zero bytes + map key). The map cipher is the FFXI Blowfish variant LandSandBoat's map session already uses; a zone change adds 2 to the key modifier. |

Shared primitives:

- **`PolBase32`**: the permuted 5-bit alphabet `N43OVHBJ1Y2C0WSXED5QFILRZMUTAPGK`, MSB-first. It encodes the Core IRC tokens and the `001` payload.
- **`PolCoreBase64`**: polcore's 6-bit alphabet, MSB-first, for the RSA tokens, the `NICK` metadata and the PML digest.
- **`PolStreamCipher`**: Blowfish rounds in 64-bit OFB, with the session's own key setup. A 4096-byte buffer is grown from K8 by digests of all that came before: the first is MD5, the rest are MD5 from a zero starting state (`ZeroStateMd5`). P and S load from it (P at a 1-byte stride), P is XORed with its first words, and P and S are refilled by encrypting the zero block without chaining. The IV is the first 8 bytes of N. The CRLF rule applies to every frame. `Fork` gives the keepalive its own keystream state.
- **`PolHashes`**: CRC-32, the `:51220` additive checksum, the POL password MD5, the time-bound Square Enix SHA-1, the challenge MD5, the lobby session hash and the PML digest.

The constants from pol.exe research (the three alphabets, the AuthCode key and its cipher's numbers, the POL ID token's numbers) come from the spec, under the maintainer's standing approval of 2026-11-08 to commit its pol.exe-derived constants (AGENTS.md prohibition 10). Capture values that identify the research accounts (their POL IDs, POL password hashes, and the `NICK` metadata bodies, which are derived from the POL ID) are redacted from the spec; GordianXI's code reproduced those capture vectors locally before they were removed.

## Verification

`tests/Gordian.Core.Tests/Network/Retail`:

- **Spec vectors (section 5.8):** 1 (synthetic POL ID; the capture vector is redacted), 2 (password response), 3-5, 6 (session key setup: P, S and the first 16 keystream bytes), 7 (PML digest), 8 (map ticket), 9 (`USER` token), 10 (both captures' RSA exchanges, replayed from the factored moduli), 11 (selection digest), 12 (Core tokens), 13 (all four AuthCode builder calls of capture #9), 14 (file checksum), 15 (request-header digest), 16 (metadata body) and the spec's synthetic `NICK` example. They were also recomputed independently in Python.
- **Checked locally, not committed:** capture #3's `NICK` line, which encrypts to the captured wire bytes byte for byte; `ClientFileChecksum` of the installed `30261102_0` FFXiMain.dll (`0x74F0058F`); and both installs' `patch.cfg` versions.
- **Blowfish:** Schneier's published vectors.
- **End to end against loopback fakes:** the Core session (gatekeeper, RSA exchange, `NICK`, the welcome without a usable `001`, a refusal numeric, a `PING`) and the presence exchanges (account login, lobby key).

None of this has been run against the live servers.

## Open items (spec section 9 item 7)

Unverified defaults GordianXI follows as the spec says: zeros in the `USER` buffer's 4 trailing bytes and in the AuthCode context's last 12 bytes, ClientNonce 0, the observed first map datagram header, ClientCode 5 and region byte 1 (US English client), expansion mask `0xFFF` (all expansions installed), skipping the Viewer check, an untruncated map key in the ticket, and the POL ID upper case in the `:51220` digest. The only error codes captured are `0x83` status `0x6F` (wrong Square Enix ID) and the lobby's 3307 (the character is still logged in, about a minute after a client was killed); the lobby session should show 3307's message and let the user retry.

## Next steps

- A `RetailLoginClient` that runs the stages in order (version checks from the profile's game folders), a retail lobby session behind the character select screen's lobby interface, and the retail map session start (first datagram, then LandSandBoat's map session with the retail key). Then the App's retail launch in `MainWindowViewModel.LaunchProfiles` with the profile's POL ID, POL password, Square Enix ID, password and 2FA mode (#527).
- Optional: the PML pages, for traffic parity and to read the maintenance flags before connecting to the lobby.
- The old `ProxyStager` path is removed only once this login is proven in testing.
