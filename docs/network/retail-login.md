# Retail Login (Direct, POL-Bypassing)

> How GordianXI logs in to Square Enix's retail servers without `pol.exe`. It is written from the derived spec alone ([retail-login-spec.md](retail-login-spec.md) v1.5.0, clean-room policy in [retail-login-research.md](retail-login-research.md)). Status: the protocol pieces are built and unit-tested against the spec's vectors. The stages are not yet run in order, and nothing has been run against the live servers.

## Pieces (`src/Gordian.Core/Network/Retail`)

| Stage (spec section 2) | Class | Status |
|---|---|---|
| 1, 7. POLP version checks (`pt008.pol.com:54000` Viewer, `pc001W2U.pol.com:53001` FFXI) | `PolpPackets`, `PolpVersionChecker`, `PatchVersionFile` | Built. The Type 7 request, the Type 8 response with its CRC-32 checked, and the `patch.ver` version. Status 1 means up to date. The Viewer check (`ViewerVersion` `100020110829_E`) is only the Viewer's own updater; the login servers do not require it, so GordianXI skips it. |
| 2, 3. Core IRC: gatekeeper `ci000.pol.com:51240`, session host `:51241`-`:51244` | `PolCoreProtocol`, `PolCoreSession`, `PolClientMeta`, `PolCoreBase64` | Built. Redirect token (server time, port), Diffie-Hellman `300 *` / `USER` (the server's token is Base32 plus the tag; the client's is 47 characters of the polcore 6-bit encoding with no tag, a 61-byte line), encrypted `NICK` (86 bytes: POL ID, password MD5, 32-character client metadata, four tabs, CRLF), `001` ticket. Retail leaves the connection idle with no keepalives and the game never uses it, so it can be closed after the welcome. |
| 4. Square Enix account (`pp000.pol.com:51220`, `0x04/0x07`) | `PolPresencePackets`, `PolPresenceClient.AuthenticateAsync` | Built. Password or password + one-time code; the reply names the assigned `:51220` server and the hello echo bytes. |
| 5, 6, 10. Session keys (assigned `:51220`, `0x04/0x05`) | `PolPresenceClient.RequestSessionKeyAsync` | Built. `PolSessionKeyService.Pml` sends ServiceType 1, flags 1000; `Lobby` sends ServiceType 0, flags 1. The ClientNonce counts up from a random start. |
| 7. PML pages (`wh000.pol.com:51304`) | `PolHashes.PmlDigestResponse` | Digest only. The digest username is the decimal FFXI account id (or the Base32 POL ID before a character is chosen). The HTTP exchange is optional for the login and not built. |
| 8, 9, 11-13. Lobby `ffxi00.pol.com:54001` | `RetailLobbyPackets`, `RetailLobbyModifier` | Packets built ([packets.md](packets.md#retail-lobby-ffxi00polcom54001)), including the 72-byte `0x0B` with the map and search servers; the connection and the character select hookup are not. Character create, delete, name check and rename use LandSandBoat's opcodes and layouts. |
| Map server | `RetailLobbyPackets.MapRawKey`, `MapLoginTicket` | The map key is the `0x07` session hash, MD5(lobby key + LE32(M0+5)); `MapRawKey` gives the 20-byte key `LegacyBlowfishCryptoSuite` MD5s to it. The first packet's `Ticket` is MD5(account name + map key). The map cipher is FFXI's Blowfish (signed key bytes, key cut at the first zero byte), which the existing LSB cipher already is. The rest of the map session is the same as LSB's. |

Shared primitives:

- **`PolBase32`**: the 5-bit alphabet `0-5A-Z`, MSB-first, plus the POL ID's Base36 → 9-character form.
- **`PolCoreBase64`**: polcore's 6-bit alphabet, MSB-first, for the `NICK` client metadata and the `USER` token.
- **`PolStreamCipher`**: standard Blowfish in 64-bit OFB with the MD5 key expansion, IV `52 7E 58 C7 D4 C3 9A 32` and the CRLF rule. Encrypting and decrypting are the same operation; `TransformFrame` is a `reset_iv = 1` frame and `Transform` continues one.
- **`PolDiffieHellman`**: 256-bit, g = 67. The key is the secret's last 8 bytes reversed.
- **`PolHashes`**: CRC-32, the `:51220` additive checksum, the POL password MD5, the time-bound Square Enix SHA-1, the challenge MD5, the lobby session hash and x-MD5-pol.

The DH parameters, the IV and the version strings came from pol.exe research. The maintainer approved committing them on 2026-11-08 (AGENTS.md prohibition 10).

## Verification

`tests/Gordian.Core.Tests/Network/Retail`:

- **Spec vectors:** the worked vectors of spec v1.5.0 section 5.8 (POL ID, POL password, Square Enix digest, checksum, the M0+1 and M0+5 session hashes, the stream cipher key schedule, PML digest, map ticket). Vector 9 (`USER`, retail capture 3) is checked by decoding: the token decodes to the spec's 35 bytes; its layout is an open question. They were also recomputed independently with Python's `hashlib` before the code was written.
- **Client metadata:** the custom alphabet and the spec's observed `TTTTTAIS...` prefix.
- **Blowfish:** Schneier's published vectors, including the 26-byte variable-key vector.
- **Stream cipher:** checked against the standard key schedule, plus the CRLF rule and continuation.
- **End to end against loopback fakes:** the Core session (gatekeeper, key exchange, `NICK`, `001`, a refusal numeric) and the presence exchanges (account login, redirect echo, lobby key).

None of this has been run against the live servers.

## Open questions (after v1.5.0)

Answered by v1.3.0 to v1.5.0: the `NICK` metadata (including +0x05 = 1) and line ending, the `0x26` reserved block, the `0x0B` layout, the map key and cipher variant, the `0x00A` bounds and ticket formula, the character management opcodes, the lobby ServiceType, the ClientNonce, the PML username, the Viewer check and the Core socket's lifetime.

1. **`USER` token layout.** The encoding is settled: vector 9 is retail capture 3, and its token has `@`, `_`, lowercase letters and `6`-`9`, which Base32 lacks, so the client sends 47 characters of the polcore 6-bit encoding with no tag (section 4.2.2 step 2's 57 Base32 characters plus tag is out of date). What the 35 bytes hold is not:
   - The capture decodes to byte 0 = `0x95` and a trailer of `F8 4A`, not section 5.2's mode `0x02` and `00 00`. Neither is a sum, XOR, CRC-16, CRC-32 or MD5 of the other bytes.
   - The 47 characters carry 282 bits, and the capture's 2 spare bits are `10`, not zero padding, which is why it ends in `P` where zero bits give `i`.
   - GordianXI sends `02 + A + 00 00` with zero spare bits (PROVISIONAL). Capture 3's client public value A, with what byte 0, the trailer and the spare bits are, would settle it.
2. **Redirect token length.** Section 5.2 now says the gatekeeper token is 45 characters of Base32 (41 + the 4-character suffix), which is what the client reads, but 41 characters is still more than 24 bytes need (39). The client decodes the bytes it needs; this only matters if the extra bits carry data.
3. **The `0x00A` ticket's name.** v1.5.0 calls it the "character/account username (content ID name registered with the server)", with the example `"testuser"`. Retail logins have a POL ID, a Square Enix ID and a character name, so which one is it? The spec's table also puts an account name string at +0x04, where XiPackets has the checksum byte (`sAccunt` is at +0x31 and always empty).
4. **Map key wording.** Section 4.6 now names the map key MD5(lobby key + `MapToken`), but `0x0B` has no map token since v1.3.0, and section 5.6 still says the key is the `0x07` session hash. Test vector 8 uses `e18b4a50...`, the old map-token key, and calls it the key from vectors 5 and 6; vector 5's map key is `906fc2f1...`. GordianXI follows section 5.6 (the session hash). Section 5.6 also still says standard Blowfish where 4.6 says FFXI Blowfish.

## Next steps

- Nothing up to character select is blocked now; the map handoff waits on question 3 (the ticket's name). Next: a `RetailLoginClient` that runs the stages in order (with the version checks driven by the profile's game folder `patch.ver`), a retail lobby session behind the character select screen's lobby interface, and the App's retail launch in `MainWindowViewModel.LaunchProfiles` using the profile's POL ID, POL password, Square Enix ID, password and 2FA mode (#527).
- The old `ProxyStager` path is removed only once this login is proven in testing.
