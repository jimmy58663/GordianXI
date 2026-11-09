# Retail Login (Direct, POL-Bypassing)

> How GordianXI logs in to Square Enix's retail servers without `pol.exe`. It is written from the derived spec alone ([retail-login-spec.md](retail-login-spec.md) v1.4.0, clean-room policy in [retail-login-research.md](retail-login-research.md)). Status: the protocol pieces are built and unit-tested against the spec's vectors. The stages are not yet run in order, and nothing has been run against the live servers.

## Pieces (`src/Gordian.Core/Network/Retail`)

| Stage (spec section 2) | Class | Status |
|---|---|---|
| 1, 7. POLP version checks (`pt008.pol.com:54000` Viewer, `pc001W2U.pol.com:53001` FFXI) | `PolpPackets`, `PolpVersionChecker`, `PatchVersionFile` | Built. The Type 7 request, the Type 8 response with its CRC-32 checked, and the `patch.ver` version. Status 1 means up to date. The Viewer check (`ViewerVersion` `100020110829_E`) is only the Viewer's own updater; the login servers do not require it, so GordianXI skips it. |
| 2, 3. Core IRC: gatekeeper `ci000.pol.com:51240`, session host `:51241`-`:51244` | `PolCoreProtocol`, `PolCoreSession`, `PolClientMeta`, `PolCoreBase64` | Built. Redirect token (server time, port), Diffie-Hellman `300 *` / `USER`, encrypted `NICK` (86 bytes: POL ID, password MD5, 32-character client metadata, four tabs, CRLF), `001` ticket. Retail leaves the connection idle with no keepalives and the game never uses it, so it can be closed after the welcome. |
| 4. Square Enix account (`pp000.pol.com:51220`, `0x04/0x07`) | `PolPresencePackets`, `PolPresenceClient.AuthenticateAsync` | Built. Password or password + one-time code; the reply names the assigned `:51220` server and the hello echo bytes. |
| 5, 6, 10. Session keys (assigned `:51220`, `0x04/0x05`) | `PolPresenceClient.RequestSessionKeyAsync` | Built. `PolSessionKeyService.Pml` sends ServiceType 1, flags 1000; `Lobby` sends ServiceType 0, flags 1. The ClientNonce counts up from a random start. |
| 7. PML pages (`wh000.pol.com:51304`) | `PolHashes.PmlDigestResponse` | Digest only. The digest username is the decimal FFXI account id (or the Base32 POL ID before a character is chosen). The HTTP exchange is optional for the login and not built. |
| 8, 9, 11-13. Lobby `ffxi00.pol.com:54001` | `RetailLobbyPackets`, `RetailLobbyModifier` | Packets built ([packets.md](packets.md#retail-lobby-ffxi00polcom54001)), including the 72-byte `0x0B` with the map and search servers; the connection and the character select hookup are not. Character create, delete, name check and rename use LandSandBoat's opcodes and layouts. |
| Map server | `RetailLobbyPackets.MapRawKey`, `MapLoginTicket` | The map key is the `0x07` session hash, MD5(lobby key + LE32(M0+5)); `MapRawKey` gives the 20-byte key `LegacyBlowfishCryptoSuite` MD5s to it. The first packet's `Ticket` is MD5(account name + session hash). The rest of the map session is the same as LSB's. |

Shared primitives:

- **`PolBase32`**: the 5-bit alphabet `0-5A-Z`, MSB-first, plus the POL ID's Base36 → 9-character form.
- **`PolStreamCipher`**: standard Blowfish in 64-bit OFB with the MD5 key expansion, IV `52 7E 58 C7 D4 C3 9A 32` and the CRLF rule. Encrypting and decrypting are the same operation; `TransformFrame` is a `reset_iv = 1` frame and `Transform` continues one.
- **`PolDiffieHellman`**: 256-bit, g = 67. The key is the secret's last 8 bytes reversed.
- **`PolHashes`**: CRC-32, the `:51220` additive checksum, the POL password MD5, the time-bound Square Enix SHA-1, the challenge MD5, the lobby session hash and x-MD5-pol.

The DH parameters, the IV and the version strings came from pol.exe research. The maintainer approved committing them on 2026-11-08 (AGENTS.md prohibition 10).

## Verification

`tests/Gordian.Core.Tests/Network/Retail`:

- **Spec vectors:** all worked vectors of spec v1.4.0 section 5.8 (POL ID, POL password, Square Enix digest, checksum, the M0+1 and M0+5 session hashes, the stream cipher key schedule, PML digest). They were also recomputed independently with Python's `hashlib` before the code was written.
- **Client metadata:** the custom alphabet and the spec's observed `TTTTTAIS...` prefix.
- **Blowfish:** Schneier's published vectors, including the 26-byte variable-key vector.
- **Stream cipher:** checked against the standard key schedule, plus the CRLF rule and continuation.
- **End to end against loopback fakes:** the Core session (gatekeeper, key exchange, `NICK`, `001`, a refusal numeric) and the presence exchanges (account login, redirect echo, lobby key).

None of this has been run against the live servers.

## Open questions (after v1.4.0)

Answered by v1.3.0 and v1.4.0: the `NICK` metadata and line ending, the `0x26` reserved block, the `0x0B` layout, the map key, the character management opcodes, the lobby ServiceType, the ClientNonce, the PML username, the Viewer check and the Core socket's lifetime.

1. **Base32 token lengths.** The redirect token is 41 characters for 24 bytes and the `USER` token 57 for 35, longer than the bits need (39 and 56), while the client metadata uses the shortest form. The client decodes the bytes it needs and pads `USER` to 57 with zero characters. A worked `USER` token would confirm it.
2. **The `0x00A` ticket's account name.** Spec 4.6 says MD5(`AccountName` + session hash) without saying which name (POL ID, Square Enix ID, character name or the empty `sAccunt` field), its encoding, or whether a terminator is hashed. A worked ticket would settle it.
3. **`0x00A` past its end.** Spec 4.6 lists `ZoneID` at +0x5C, but the packet is 0x5C bytes. XiPackets has a 2-byte language at +0x58 and `dammyArea` (a client state value) at +0x5A. GordianXI keeps the existing builder (language 1, `dammyArea` 0).
4. **ClientMeta worked bits.** Spec 4.2.2 maps `02 01 00` to `TAST`; those bytes encode to `TAIT`. The observed `TTTTTAIS...` prefix needs +0x05 = 1, so GordianXI sends 1. Confirm +0x05 is always 1.
5. **Map cipher variant.** Spec 4.6 and 5.6 call the map cipher standard Blowfish keyed with the session hash. The existing map cipher, which works with LandSandBoat, truncates the MD5 key at its first zero byte and sign-extends key bytes when building the schedule, as FFXiMain.dll does. Retail uses the same FFXiMain.dll, so GordianXI keeps that cipher; confirm retail does not differ.

## Next steps

- Nothing up to character select is blocked now; the map handoff waits on question 2 (the ticket's account name). Next: a `RetailLoginClient` that runs the stages in order (with the version checks driven by the profile's game folder `patch.ver`), a retail lobby session behind the character select screen's lobby interface, and the App's retail launch in `MainWindowViewModel.LaunchProfiles` using the profile's POL ID, POL password, Square Enix ID, password and 2FA mode (#527).
- The old `ProxyStager` path is removed only once this login is proven in testing.
