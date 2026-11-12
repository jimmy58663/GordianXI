# Retail Login Protocol Specification

## 1. Header

- **Version**: 2.1.25 (Clean-room specification. 2.1.25, map transport header fields identified (§4.6.3): `+0x04` is the milliseconds part (0–999) of the send time whose seconds are at `+0x08`, and `State` word 2 (`+0x14`) is the client's public IPv4 address from the `RoutingToken`, in network byte order (every client datagram from the second on, all eight captured map logins). No capture contains a map retransmission or a dropped datagram. The `0x00A` `Language` values for the other regions are recorded from the implementation (§4.6.1). 2.1.24, from the first live map-server attempt: the server answered the `0x00A` but kept resending its first datagram and logged the client out after 1.4 s. Map datagrams in captures #5, #9 and `trace_session_20261110_231055` were decrypted, decompressed and parsed. **§4.6.2 was missing the payload compression**: every encrypted datagram in both directions carries `0x01` ‖ a prefix-code bitstream ‖ `LE32(bit count)`, and the MD5 trailer covers those bytes including the bit count. Also new: the transport header fields (§4.6.3; the first datagram's `+0x04..+0x1B` are memory residue, not constants), retail's first map exchange (§4.6.4; the client answers the first server datagram with `0x011`, not `0x00C`), Test Vector 19, and the map-key zero-byte rule is now verified (capture `231055`: the key has `0x00` at index 10, and both the cipher and the `0x00A` ticket use the truncated key). 2.1.23, from the second live lobby attempt, which still got `3301` with a `D_28` built exactly per §9 item 8. **The `:51220` `0x04/0x05` key-request payload in §4.3.5 was wrong.** Decrypting retail's requests in the three hooked sessions (`trace_session_20261110_220551`, `_230908`, `_231055`) shows the lobby request carries selector `00 00 00 01` and `01 00 05 00 00 01` (little-endian fields), where the old table gave `ServiceType = 0x00` and big-endian `00 01 00 05 00 01`, which has the `00` selector of the Viewer's request. The server answers either form with a valid key, so the failure surfaced only at the lobby. The lobby request's `+0x20` word is also never zero: it is whatever encrypts to four zero bytes on the wire. §4.3.5 rewritten from the decrypted wire, Test Vector 18 added, new §4.3.7 lists the six other `:51220` requests retail sends after the Square Enix login (the spec never mentioned them), the `0x26` trailing 12 bytes are corrected (never zero in any capture), the `K_8` order is confirmed, and §9 item 8 is reordered. 2.1.22, operational detail for the first live lobby attempt, which got `0x26` → `0x04` lobby error `301` (message `3301`, "communication error with the FFXI server"). `3301` is a session-correlation failure, **not** a bad password (that is `3302`): the lobby could not validate the `AuthCode` against the live PlayOnline presence session. No spec field changed; the lobby-login wire sequence, its ordering and timing, the Core-IRC keepalive requirement, and the digest-protected `AuthCode` inputs to check are now stated explicitly (new §4.5.1.1, §9 item 8), and the lobby error codes `3300`–`3302` are named (§4.5.10). 2.1.21, from the implementation's first Square Enix login, which got `0x83` `Status = 0x6F` on an account without an authenticator: **the Square Enix password digest was missing a step** (§5.4, O17). The Viewer does not pass `SHA-1(SE_PASS)` to the time digest; it passes `H_1 = SHA-1(lowerhex(SHA-1(SE_PASS)) ‖ "playonline")`. The time step (§5.4 steps 3–5) is now verified: from the `H_1` the Viewer handed over in captures #2 and #3, it reproduces both payload digests. Test Vector 3 corrected, Test Vector 17 (a whole `0x04/0x07` payload) added, §6.1 and §9 item 2 updated (nothing else is sent between the Core welcome and `0x04/0x07`; `0x6F` covers more than a wrong ID). Harness `tools/verify_se_auth.py`. 2.1.20, from the implementation's first live runs (2026-11-11), which got no answer from the version servers or from the Core IRC host after `USER`: two framing errors corrected, both verified on every capture. (a) The `POLP` `Checksum` is `MD5(frame[0x08..end])[0..3]`, not CRC-32 (§4.1.1, O15); request bytes, timing and server behaviour added (§4.1.5); an outdated install is signalled by `CurrentVersion`, not `Status`. (b) Every Core IRC token line ends in a 4-character `LineChecksum` suffix (§5.10, O16); the `USER` token is a 43-character body plus that suffix, and the "4 trailing bytes" of earlier versions were the decoded suffix (§4.2.2 step 2, Test Vector 9). The redirect suffix is resolved by the same rule. Observed modulus size and `USER` timing added. Harness `tools/verify_line_checksums.py`. 2.1.19, from captures #10 (second account; one failed and one successful login) and the hooked captures `trace_session_20261110_220551`, `_230908`, `_231055`: **every login input is now derived and verified.** (a) `NICK`: the middle field is `PassResponse = md5hex(GreetingToken[0..39] ‖ POL_PASS)`, not `md5hex(POL_PASS)` (§4.2.2, Test Vector 2); the ClientMeta body bytes `16..23` are `MD5(00×64 ‖ PolIdToken ‖ 00×10)[0..7]`, derived from the POL ID and verified on both accounts (§4.2.2, Test Vector 16), with byte `5` documented as the presumed `Region`; the suffix sums 20 little-endian `u32` words, not bytes; the POL ID encoding is confirmed on a real login. (b) `:51220`: the request-header digest is `MD5(POL_ID ‖ POL_PASS ‖ LE32(seed))` (§4.3.3, Test Vector 15); `AccountToken` is 12 zero bytes; there is no `SessionSecret`. (c) Core IRC: the `001` payload carries nothing the client uses; the `RoutingToken` comes from the greeting token (§4.2.2 step 6); a redirect can point at a `:51240` host. (d) Errors and disconnects: failed Square Enix login (`0x83` `Status = 0x6F`), immediate re-login after a killed client, and lobby error `3307` (§9 item 2). (e) `0x26` `ClientCode`, `+0x83` and `ExpansionMask` re-described from public documentation (the mask is installed content). (f) Credentials redacted from Test Vectors 1 and 10.2. 2.1.18: clean-room pass. Every statement about the internals of the retail binaries (code addresses, how individual routines fill buffers, disassembly provenance, internal tables) is removed from the spec and its appendix and, where it mattered, restated as a wire-observable fact; the appendix's research-only address class is removed. The `:51220` Hello field at `+0x0C` is renamed `Unused0C`. POL ID input format pinned to 4 letters + 4 digits (§4.2.2, §6.1). 2.1.17: AuthCode block-cipher steps 1 and 5 corrected, Test Vector 1 intermediates corrected, §5.4 clock decided, AuthCode `SessionKey` = `LobbySessionKey` verified, and several contradictions removed. Earlier history is in the repository log.)
- **Date**: 2026-11-11
- **Provenance**: *Derived by an independent research process; no POL code was consulted by the implementer.*
- **Source**: In-house protocol observation and clean-room specification (`gordian-pol-research`).

---

## 1.1 Verification Status (READ FIRST)

The normative sections below are verified against live network captures (including the 2026-11-09 and 2026-11-10 retail updates) and assertable test harnesses across all protocol items O1–O17, except that O9's management opcodes `0x14`, `0x28`, `0x2B` and `0x04` are documented from public sources and have not been observed on a retail wire. Items an implementation still has to decide or confirm are listed in §9 item 7. None of them blocks a retail login: the `NICK` ClientMeta body (§4.2.2) and the `:51220` request-header digest (§4.3.3), both open earlier in 2.1.19, are now derived and verified. O13 makes every build-volatile input install-derived; the appendix classifies each constant as protocol-fixed, server-operational, or install-derived. O12 (the `NICK` POL-ID Base36 field encoding) is **RESOLVED**: the encoder reproduces the wire field (the encoder maps the capture #3 POL ID (redacted) to the wire field) and round-trips cleanly (Test Vector 1). O7 (the `0x26` AuthCode generation) is **RESOLVED**: the forward algorithm reproduces the wire `0x26` token byte-for-byte from the inputs logged by runtime instrumentation in capture #9, reproduces all four token-generation calls logged in that capture, and decrypts/round-trips wire tokens across captures #5, #6, #8 and #9, every decrypted plaintext satisfying $B[0]=H[13]\oplus H[3]$ and $B[1]=0$ (Test Vector 13). The 20-byte context block ($B_{40}[4..23]$): bytes 0..1 are `ProtocolVersion` (`0x0001`); bytes 2..7 are the client's public port (`u16_le`) and IPv4 address (`u32_le`) from the `RoutingToken` (Core IRC greeting token, §4.2.2 step 6) (identical to bytes `+0x04..+0x0B` of the 40-byte Hello on `:51220` in all four captures); bytes 8..19 vary between logins and match no session value (all zero in capture #8, which logged in, so zero-fill is accepted; whether the server checks them is not known). The four preimage words are the `ServerTimestamp` of the Core IRC greeting token (§4.2.2; exact match in capture #9) and the two little-endian halves of the Core IRC session key $K_8$ (§4.2.2). The $K_8$ identification is verified on the wire: $K_8$ assembled from capture #9's logged inputs reproduces all 46 PML `Authorization` digests in that capture (§5.7; 0 of 46 with the halves swapped). Every input is known to the client, so it can generate a token for any login. The four header-folding relations hold across captures #5, #6, #8, #9 ($S=0$); the trailing 12 bytes (`+0x68..+0x73`) vary per login and are not part of the token.

**Captures referenced:** #1–#3 cover the version check, the Core IRC / Ephemeral RSA handshake, the encrypted `NICK`, and the `:51220` presence exchange; #5 (`cap5_20261109_160208_lobby-charcreate-map`, taken after the 2026-11-09 viewer update) covers the full lobby flow, character creation, and the map-server datagram exchange; #6 (`cap6_20261109_215156_md5hook-zone-relogin`) adds a `SelectionMD5` preimage hook and a login → zone → logout → login → shutdown sequence; #7 (`cap7_20261109_230615_ffxi-update-maintenance`) covers an FFXI file update (patch to `30261102_0`) and an All-Worlds maintenance state that blocks play (§4.4); #8 (`cap8_20261110_062716_updated-build-login`) is a non-maintenance login on the updated `30261102_0` build (source of the `0x74F0058F` `ClientChecksum`); #9 (`cap9_20261110_110632_tokenhook-login`) is a non-maintenance login on the updated build taken with runtime instrumentation that logs the AuthCode generation inputs and output (O7); #10 (`trace_session_20261110_210031`) is a **second account** on the same install: a failed login (wrong Square Enix ID), then a successful login through character selection, with the same instrumentation as #9.

### Protocol Verification Matrix

| # | Item | Status | Resolution Summary |
|---|---|---|---|
| O1 | RSA key exchange (§4.2.2, §5.1) | **RESOLVED** | 256-bit ephemeral RSA with $e = 65535$ (`0xFFFF`). The client sends $N$ little-endian; the server returns $C$ **big-endian**; $M$ is a PKCS#1 v1.5 type-2 block whose last 8 bytes, reversed, are $K_8$. Verified by factoring both captures' $N$ (two 128-bit primes each): decryption gives valid padding in both and reproduces capture #3's independently captured $K_8$. Earlier drafts were wrong on three points: $e$ = 65537, $C$ read as little-endian, and $K_8$ taken from the top of $M$. |
| O2 | Session cipher key setup (§5.1) | **RESOLVED** | Algorithm in §5.1 step 2. The harness implementation (`testsessioncipher`) reproduces the capture #3 encrypted `NICK` byte for byte. The 2.0.0 prose did not match that implementation (buffer layout, S-box offset, chaining of the substitution passes); 2.0.1 rewrites it to match. |
| O3 | Session OFB IV (§5.1) | **RESOLVED** | Verified in harness. The 8-byte initial OFB IV is the first 8 bytes of the client RSA public modulus $N$ (`N[0..7]`). Keystream matches live captures byte-for-byte. |
| O4 | `NICK` `ClientMeta` encoder (§4.2.2) | **RESOLVED** | 36 characters: a 32-character body (24 bytes in the §5.2 Base64 alphabet; the `TTTTTAIS…` prefix decodes) and a 4-character suffix. The suffix is an additive checksum over the 80 preceding ASCII bytes of the NICK line, summed as 20 little-endian `u32` words (not over the 24 decoded payload bytes, which is why the 2.0.0 formula failed): its top four 6-bit slices, each biased by `0x3F`. Reproduces the capture #3 suffix (§4.2.2). |
| O5 | `Ticket` and `SelectionMD5` (§4.6, §4.5.8) | **RESOLVED** | `Ticket` = MD5(15-byte account field as sent, all zeros ‖ map key), verified against capture #5 (Test Vector 8). `SelectionMD5` = MD5 of a 92-byte preimage: AuthCode[64] ‖ SessionHash[16] ‖ u32(3) ‖ u32(ClientChecksum) ‖ u32(ContentID), verified against both captures' wire packets and MD5 hooks (Test Vector 11). The `+0x54` `ClientChecksum` changes with every client build (`0x43FA0718` in `30261006_0`, `0x74F0058F` in `30261102_0`, both recovered from wire `SelectionMD5`). It is computed from the installed `FFXiMain.dll`; see O13. |
| O6 | Map-server cipher (§5.6) | **RESOLVED** | Key = `SessionHash(Modifier used by 0x07 + 4)`. The cipher is the FFXI Blowfish variant also used by LandSandBoat: a modified round function, sign-extended key-byte loading, and the key truncated at its first zero byte. Capture #5 (character-management login, `0x07` at `M0+11` → key `M0+15`): all 977 datagrams decrypt. Capture #6 confirms the two previously-unobserved cases: a no-management login uses `0x07` at `M0+5` → key `M0+9`, and a zone change re-keys with `+2` → `M0+11`. Both the client's key-hash hook and datagram decryption (598/600 across two logins) agree. |
| O7 | `0x26` `AuthCode` generation (§4.5.2) | **RESOLVED** | The 52-byte token is built by `polcore.dll` and transferred via `CommonFunctionTable + 0xEA0`. Fully verified: (a) Forward generation reproduces the wire `0x26` token byte-for-byte from capture #9 logged inputs (`session_key = 65d35cede299b936360cd53a60a3b677`, `rand_lo = 0x6AF35D54`, `rand_hi = 0x711B6CB9`, `sequence = 1513561330`), and reproduces all four builder hook calls in capture #9 (hook-labelled `seed` `3`, `204`, `9`, `0`; that hook argument is not the token seed $S$, which is `0` in all four, Test Vector 13); the `SessionKey` input is the `LobbySessionKey` (verified 2.1.17, §4.5.2); (b) The 20-byte context block $B_{40}[4..23]$ is assembled from `ProtocolVersion` (`0x0001`) and the `RoutingToken` (Core IRC greeting token) (public port + IPv4), identical to bytes `+0x04..+0x0B` of the 40-byte Hello on `:51220` across captures #5, #6, #8, #9; bytes 8..19 vary between logins and carry no protocol data (all zero in #8, which logged in; server checking unknown); (c) Fixed key `CB 83 24 B7 BA 3E 9E 0A` and the proprietary 64-bit Feistel/S-box block cipher decrypt and round-trip wire tokens across captures #5, #6, #8, #9, satisfying the internal plaintext checksum and $B[0] = H[13] \oplus H[3]$ relationship; (d) The four header-folding relations ($\text{Token}[0]=\text{Token}[14]\oplus S$, $[1]=[12]\oplus[6]$, $[2]=[10]\oplus[8]$, $[3]=[13]\oplus[9]$) hold on all captures with seed $S=0$; (e) Trailing 12 bytes (`+0x68..+0x73`) vary per login and are not a token field (§4.5.2 step 6); (f) `RandLo` = Core IRC greeting `ServerTimestamp` (capture #9). **O7a, resolved:** the remaining two words are $K_8$ (LE halves; capture #9's 46 PML digests reproduce from them). See §4.5.2 and Test Vector 13. |
| O8 | 44-char Core IRC greeting (§4.2.2) | **RESOLVED (revised 2.1.11)** | Same 25-byte layout as the redirect token (§4.2.1). It is **not** discardable: its `ServerTimestamp` (`+0x00`, BE) is the AuthCode preimage's `RandLo` (§4.5.2; capture #9), and its `+0x14` u16 (BE) is the client's public TCP port on the Core IRC connection (all of captures #5, #6, #8, #9 match the port in the `RoutingToken`). The earlier "read and discard" reading is withdrawn. |
| O9 | Lobby delete / rename / transfer / error reply (§4.5.10) | **RESOLVED (documented)** | `0x22`, `0x21` and the `0x03` ack are verified on the wire (capture #5). The `0x14`, `0x28`, `0x2B` and `0x04` layouts are now taken from the public XiPackets documentation, which showed the earlier binary-derived layouts were wrong: each request carries a world/character ID at `+0x20`, and the session hash is at `+0x24` or `+0x34`. Error codes and the name-taken codes (`313`/`322`/`335`) are listed. They match what the legacy client sends to LandSandBoat but have not been observed on a retail wire. |
| O10 | Code addresses | **RESOLVED** | Not part of this specification. Neither the spec nor its appendix contains binary code or data addresses; only the `CommonFunctionTable` byte offsets of the handoff contract (§8.2) are given. |
| O11 | Redirect Base32 token decode (§4.2.1) | **RESOLVED** | Encoded with Square Enix's custom 32-character permuted Base32 alphabet `N43OVHBJ1Y2C0WSXED5QFILRZMUTAPGK` using standard RFC 4648 5-bit MSB-first packing (40 characters = 25 decoded bytes). Offset `+0x00..+0x03` gives the big-endian server Unix timestamp $t_{\text{server}}$; `+0x08..+0x0B` gives the Core IRC host IP; `+0x0C..+0x0D` gives the variable TCP port (`51241`, `51244`, `51247` observed). Verified by independent decode of all 5 login captures (IP matches the ASCII prefix, port matches the connect). |
| O13 | Build-volatile login inputs (§4.1.3, §4.4, §4.5.2, §4.5.8, §5.9) | **RESOLVED** | Every login input that changes with a retail patch is computed from the user's install, so a patch never requires a client release. (a) `ClientChecksum` = `ClientFileChecksum(<FFXI_ROOT>/FFXiMain.dll, 0x13)` over the on-disk file (§5.9). The computed value for the installed `30261102_0` file equals `0x74F0058F`, the value independently brute-forced from capture #8's wire `SelectionMD5`. (b) FFXI and Viewer version strings = the highest version tag in each product's `patch.cfg`, prefixed `"0001"` / `"1000"` for `POLP` (§4.1.3); matches captures #8–#9 on all three wire fields. (c) `X-POL-VIEWER-VERSION` = `"Ver."` + `<POL_ROOT>/version.dat` (§4.4); `1.18.15e` before the 2026-11-09 Viewer update, `1.18.15f` after, matching the file. The harness check (`verify`, O13) recomputes all of these from the install and compares with capture #9. Corrections made on the way: `patch.ver` is encrypted, not ASCII; the PML `User-Agent` is the fixed `PlayOnline-PML-Viewer/1.00 [en] (Windows XP)`, never the earlier `PlayOnlineViewer/1.18.14 (Win)`. |
| O14 | PML request headers and digest (§4.4, §5.7) | **RESOLVED** | The request headers in §4.4 are taken from the wire (capture #8): the earlier table's `X-PlayOnline-Agent`/`-Service` headers, `;level=2`, `main@wh000.pol.com` realm, `opaque`, `cnonce`/`nc`/`qop` and 8-hex `Want-Hello` were all wrong. The digest (§5.7) reproduces **every** wire `Authorization` response in captures #1, #3 and #9 (48 + 48 + 46 = 142, independent sessions, harness `verify` O14): `password` = POL-Base64 (§5.2) of the 8-byte Core IRC session key $K_8$ (§5.1), `HA1 = MD5(userName:realm:password)`, `HA2 = MD5("GET:" uri)`, `response = MD5(HA1:HA2:nonce)`, lowercase hex. `userName` = POL-Base64 of the ASCII string `"<a.b.c.d>:<port>"`, the client's address as seen by the server; the same address is the 6-byte `RoutingToken` from the Core IRC greeting token (§4.2.2 step 6, §4.3.1) and bytes `+0x04..+0x07` of the gatekeeper redirect token (§4.2.1). The `:51220` `0x04/0x05` exchange with service `1` / `1000` is **not** the digest key: the first `Authorization` is sent before that exchange completes. Its purpose is unverified. `X-PlayOnline-Hello` (the server's answer to `Want-Hello`) is not checked by this work. |
| O12 | `NICK` POL-ID field encoding (§4.2.2) | **RESOLVED** | The 9-character `<Encoded_POL_ID>` is constructed deterministically without session salt. The 8 ASCII characters of the POL ID are decoded as Base36 digits under custom alphabet `"EFKAOYMJVNGTDSWBQLPCIRHZXU6328401795"` into a 41-bit value, bits 46..47 are set to 1, an invertible 5-round bit diffusion is applied ($val \oplus= (val \gg 8) \ \& \ (0\text{xFF} \ll (8 \times a))$ for $a = 4 \dots 0$), bits 46..47 are cleared, and the value is re-encoded into 8 Base36 characters using the same alphabet, prepended with `'U'` (Test Vector 1). Reversible and verified: the encoder maps the capture #3 POL ID (redacted) to the wire field, matching Capture #3 byte-for-byte and round-tripping cleanly. |
| O15 | `POLP` header checksum (§4.1.1) | **RESOLVED (2.1.20)** | `Checksum` = `MD5(frame[0x08..end])[0..3]`. Verified on all 728 `POLP` frames in the captures, both directions. The CRC-32 in earlier versions was never checked and is wrong; both version servers close without replying to it (implementation live runs, 2026-11-11). |
| O17 | Square Enix password digest (§5.4) | **RESOLVED (2.1.21)** | The time digest's input is `H_1 = SHA-1(lowerhex(SHA-1(SE_PASS)) ‖ "playonline")`, not `SHA-1(SE_PASS)`. The time step reproduces the payload digests of captures #2 and #3 from the `H_1` logged there. The inner step is from the Viewer's own hashing; no capture holds a known Square Enix password to check it against. Earlier versions omitted it, and the server answers such a request with `Status = 0x6F` (implementation live run, 2026-11-11). |
| O16 | IRC line checksum suffix (§5.10) | **RESOLVED (2.1.20)** | One rule for every token line: little-endian `u32` sum of the line from its first byte, a 1–3-byte tail counted as `u32_le(zeros ‖ tail)`, four 6-bit slices + `0x3F`. Verified on all 51 distinct plaintext token lines (`USER`, redirect, greeting, ciphertext) and 7 decrypted `001`/`422`/`NICK` lines. Corrects the `USER` token: 43-character body + suffix, not a 36-byte buffer with free trailing bytes. |

---

## 2. Endpoints and Transport

Retail PlayOnline (`POL`) and Final Fantasy XI (`FFXI`) login uses **raw TCP sockets only** (no TLS/HTTPS is used at any stage, including Square Enix Account authentication).

| Stage | Host / Endpoint | Resolved IP (Observed) | Port (TCP) | Framing / Protocol | Purpose |
|---|---|---|---|---|---|
| 1 | `pt008.pol.com` | `124.150.156.107` | `54000` | `POLP` binary framing (16-byte header, all fields little-endian, §4.1.1) | PlayOnline Viewer client version check (`ProductCode = "W2U\0"`, `VersionString = "100020261022_0\0"`) |
| 2 | `ci000.pol.com` (`ci%03d.pol.com`) | `202.67.54.52` | `51240` | ASCII IRC line (`\r\n`) | Initial gatekeeper; returns `:<ip> 300 * <token>` redirecting to an active `:51241`–`:51247` session host |
| 3 | Redirected Core Session Host (`pol-10xx-512xx.pol.com`) | `202.67.54.132` | variable, read from the redirect token (`51241`, `51242`, `51244`, `51245`, `51246`, `51247` observed, and once `51240` on another host, capture #10; §4.2.1, §1.1 O11) | ASCII IRC (`300 *`, `USER`) $\rightarrow$ Blowfish-64 OFB + custom 6-bit Base64 IRC (`NICK`, `001`, `422`, `MODE`, `NOTICE`) | 256-bit Ephemeral RSA key exchange, POL ID (8 characters) + POL Password (up to 16 characters) authentication, and session token issuance |
| 4 | `pp000.pol.com` (`pp%03d.pol.com`) | `202.67.54.51` | `51220` | 40-byte hello $\rightarrow$ 24-byte `0x81`/`0x83` challenge $\rightarrow$ Blowfish-64 OFB + 32-bit additive checksum | Square Enix Account authentication (`SE_ID`, `SE_PASS`, and optional 6-digit `TOTP` via Category `0x04` / Command `0x07`) and redirect to assigned `:51220` session server |
| 5 | Redirected Presence / Key Server | `202.67.54.159` | `51220` | 40-byte hello $\rightarrow$ 24-byte `0x81` challenge $\rightarrow$ Blowfish-64 OFB + 32-bit additive checksum | Account and presence requests after the Square Enix login (`0x04/0x06`, `0x07/0x0C`, `0x02/0x03`, `0x01/0x03`, `0x00/0x09`, `0x03/0x03`, §4.3.7) and per-service 16-byte Session Key issuance (`0x04/0x05`, §4.3.5: the FFXI request gives the FFXI Lobby Session Key; the Viewer's request gives a different key that is not the PML digest key, §1.1 O14) |
| 6 | `wh000.pol.com` | `202.67.54.55` | `51304` (`51300`–`51307`) | HTTP/1.1 (`x-MD5-pol` Digest + `X-PlayOnline-Hello`) | PlayOnline PML navigation (`/pml/main/index.pml` $\rightarrow$ `/pml/game/ff11/index.pml`) |
| 7 | `pc001W2U.pol.com` | `124.150.156.107` | `53001` | `POLP` binary framing (16-byte header, all fields little-endian, §4.1.1) | Final Fantasy XI pre-launch version check (`ProductCode = "W20\0"`, `VersionString = "000130261006_0\0"`) |
| 8 | `ffxi00.pol.com` | `124.150.154.122` | `54001` | 28-byte `IXFF` LE header + 16-byte MD5 packet digest | Final Fantasy XI Lobby server (Opcodes `0x26` $\rightarrow$ `0x05`, `0x1F` $\rightarrow$ `0x20`, `0x24` $\rightarrow$ `0x23`, `0x07` $\rightarrow$ `0x0B`) |

---

## 3. Session Flow

```mermaid
sequenceDiagram
    participant Client as Client (POL / FFXI)
    participant Patch54000 as pt008.pol.com:54000
    participant Gate51240 as ci000.pol.com:51240
    participant Core5124x as Core IRC (:51241-51247)
    participant PpGate as pp000.pol.com:51220
    participant PpSession as Assigned :51220 Server
    participant PmlHttp as wh000.pol.com:51304
    participant FfxiPatch as pc001W2U.pol.com:53001
    participant Lobby as ffxi00.pol.com:54001
    participant MapServer as Map Server (UDP, port from 0x0B; 54240 observed)

    Client->>Patch54000: 1. Connect & send POLP Type 7 (88B, "W2U\0", "100020261022_0\0")
    Patch54000-->>Client: POLP Type 8 (103B, version OK) -> TCP Close
    Client->>Gate51240: 2. TCP connect :51240
    Gate51240-->>Client: ":<redirect_ip> 300 * <44-char Base32 token>\r\n" (port & server timestamp in token)
    Client->>Core5124x: 3. TCP connect <redirect_ip>:<51241..51247>
    Core5124x-->>Client: 3a. ":<ip> 300 * <44-char Base32 token>\r\n" (Server greeting & session parameters)
    Client->>Core5124x: 3b. "USER x 8 * :<47-char Base64 token>\r\n" (Client 256-bit RSA Modulus N, little-endian; e=65535)
    Core5124x-->>Client: 3c. ":<ip> 300 * <47-char Base64 token>\r\n" (Server 256-bit Ciphertext C, big-endian -> M = C^d mod N, PKCS#1 v1.5 -> K8 = last 8 bytes reversed -> Blowfish OFB)
    Client->>Core5124x: 4. Raw Blowfish-64 OFB "NICK <9-char 'U'+Base36 POL-ID>:<32-hex PassResponse>:<ClientMeta>\r\n"
    Core5124x-->>Client: Encrypted IRC replies (300, 001, 422, MODE, NOTICE)
    Client->>PpGate: 5. TCP connect pp000.pol.com:51220, send 40B Hello (RoutingToken)
    PpGate-->>Client: 24B Challenge (0x81, ChallengeSeed)
    Client->>PpGate: 40B Encrypted Header (Op 0x04/0x07) + 64B Encrypted Square Enix Auth (Mode, SE_ID, TOTP, SHA1)
    PpGate-->>Client: 24B Encrypted Response Header (0x83, RedirectIP, PayloadLength=8) -> TCP Close (no payload bytes sent)
    Client->>PpSession: 6. Connect RedirectIP:51220: 0x04/0x06, 0x07/0x0C, 0x02/0x03, 0x01/0x03, 0x00/0x09, 0x03/0x03 (§4.3.7), then Op 0x04/0x05 Viewer key (purpose unverified)
    Client->>PmlHttp: 7. GET /pml/main/index.pml (HTTP Digest x-MD5-pol keyed by Core IRC K8)
    PmlHttp-->>Client: 200 OK (main menu PML -> FFXI launch page)
    Client->>FfxiPatch: 8. Connect & send POLP Type 7 (88B, "W20\0", "000130261006_0\0")
    FfxiPatch-->>Client: POLP Type 8 (103B, version OK) -> TCP Close
    Note over Client,Lobby: Handoff to FFXiMain.dll (COM IFFXiEntry -> IGameMain -> CommonFunctionTable)
    Client->>Lobby: 9. Resolve ffxi00.pol.com & TCP connect :54001
    Client->>PpSession: 10. Op 0x04/0x05 FFXI key request (§4.3.5) -> 32B response (first 16B = FFXI Lobby Session Key)
    Client->>Lobby: 11. Send IXFF Opcode 0x26 (152B: ClientCode + AuthCode[64] + ClientVersion + ExpansionMask)
    Lobby-->>Client: IXFF Opcode 0x05 (40B: KeyByteModifier seed + ExpansionMask + FeatureFlags)
    Client->>Lobby: 12. Send IXFF Opcode 0x1F (44B: MD5(LobbySessionKey || LE32(Modifier+1)))
    Lobby-->>Client: IXFF Opcode 0x20 (452B: Character List)
    Client->>Lobby: 13. Send IXFF Opcode 0x24 (44B: MD5(LobbySessionKey || LE32(Modifier+3)))
    Lobby-->>Client: IXFF Opcode 0x23 (272B: World List)
    Client->>Lobby: 14. Send IXFF Opcode 0x07 (88B: Select Character, SessionHash(Modifier+5), SelectionMD5)
    Lobby-->>Client: IXFF Opcode 0x0B (72B: MapServer & SearchServer endpoints) -> TCP Close
    Client->>MapServer: 15. UDP Packet 0x00A (92B, Ticket = MD5(15 zero bytes || MapKey))
```

---

## 4. Message Field Tables

### 4.1 Version Check Framing (`POLP` on `pt008.pol.com:54000` and `pc001W2U.pol.com:53001`)

Used unauthenticated over plain TCP on `pt008.pol.com:54000` (PlayOnline Viewer check) and `pc001W2U.pol.com:53001` (Final Fantasy XI pre-launch check).

> **Protocol Note (Type 5 & 6 Legacy Framing)**: In early versions of the PlayOnline protocol, clients preceded the version check with a 16-byte Type 5 / Type 6 handshake. In modern retail (empirically confirmed in Captures #1–#3), the client sends Type 7 immediately upon TCP connect and the server responds directly with Type 8, followed by TCP teardown.

> **Version strings are volatile; a patch sub-protocol exists.** The version strings in the tables below (`"100020261022_0"`, `"000130261006_0"`, client `"30261006_0"`) are **examples from specific builds** and change with every PlayOnline/FFXI update — e.g. capture #7 (`cap7_20261109_230615_ffxi-update-maintenance`) shows FFXI updating to `30261102_0`. When the installed version is behind, the client runs a full patch/download sub-protocol on `:53001` (POLP frames beyond Type 7/8: a manifest frame then `…/Indirect/patch.txt` file transfers). Patching is **out of scope** for this login spec — it is handled by the PlayOnline Viewer and gated by the PML `$patch_enable` flag (§4.4) — but implementers should treat the version strings as inputs read from the install (§4.1.3), not constants, and expect the `:53001` exchange to be large when an update is pending.

#### 4.1.1 Common `POLP` Frame Header (16 bytes / `0x10` bytes)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 4 | `u32` | LE | `PacketSize` | Total packet length in bytes, including this 16-byte header (`0x00000058` = 88 on request; `0x00000067` = 103 on response). |
| `+0x04` | 4 | `u8[4]` | — | `Checksum` | The first 4 bytes of the standard MD5 digest of the frame from `+0x08` (`Magic`) to the end: `MD5(frame[0x08 .. PacketSize−1])[0..3]`, copied as is. **Not CRC-32** (earlier versions said CRC-32 over `+0x10..`; never checked, and wrong). Verified on all 728 `POLP` frames in the captures, both directions, including the patch-transfer frames (harness `tools/verify_line_checksums.py`). The server closes the connection without replying to a request with a wrong checksum (§4.1.5). |
| `+0x08` | 4 | `char[4]` | ASCII | `Magic` | Fixed ASCII string `"POLP"` (`50 4F 4C 50`). |
| `+0x0C` | 4 | `u32` | LE | `PacketType` | `0x00000007` = Client Version Request (88 bytes total); `0x00000008` = Server Version Response (103 bytes total). |

#### 4.1.2 `POLP` Type `7` — Client Version Request (88 bytes total: 16B header + 72B payload)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 16 | `Header` | LE | `Header` | `PacketSize = 0x00000058` (88), `Checksum`, `Magic = "POLP"`, `PacketType = 0x00000007`. |
| `+0x10` | 4 | `char[4]` | ASCII | `ProductCode` | `"W2U\0"` (`57 32 55 00`) for POL Viewer (`:54000`); `"W20\0"` (`57 32 30 00`) for FFXI US Windows (`:53001`). |
| `+0x14` | 68 | `char[68]` | ASCII | `VersionString` | Null-terminated ASCII version string, zero-padded to 68 bytes. **Install-derived** (§4.1.3): product ID ‖ installed version, e.g. `"100020261022_0"` on `:54000`; `"000130261006_0"` / `"000130261102_0"` on `:53001`. |

#### 4.1.3 Installed Version on Disk (`patch.cfg`, verified §1.1 O13)
The installed version of each product is read from the plain-text patch manifest in its root directory (install roots: §9 item 5):
- **PlayOnline Viewer**: `<POL_ROOT>/patch.cfg`
- **Final Fantasy XI**: `<FFXI_ROOT>/patch.cfg`

`patch.cfg` lists every patched file as `file <path> {` followed by one line per release, each starting with a version tag (`YYYYMMDD_N` for the Viewer, `3YYYMMDD_N` for FFXI; e.g. `30261102_0 2905680 … 30261102_0/Direct/FFXiMain.dll.slc …`). The installed version is the **highest version tag in the file** (plain string comparison):

| Wire field | Value |
|---|---|
| `POLP` `:54000` `VersionString` | `"1000"` ‖ Viewer version (e.g. `"100020261022_0"`) |
| `POLP` `:53001` `VersionString` | `"0001"` ‖ FFXI version (e.g. `"000130261102_0"`) |
| Lobby Opcode `0x26` `ClientVersion` | FFXI version (e.g. `"30261102_0"`) |

The prefixes `"1000"` and `"0001"` are the product IDs, the same strings as the value names under the `InstallFolder` registry key. The local installation is outdated when the Type 8 `CurrentVersion` differs from the installed version; `Status` stays `1` in that case (capture #7, §4.1.5).

> **`patch.ver` is not readable.** Earlier versions of this spec said `patch.ver` (288 bytes) stores the version as null-terminated ASCII at `+0x00`. On the current install both products' `patch.ver` files contain encrypted or obfuscated data with no readable version string. Do not use `patch.ver`.

#### 4.1.4 `POLP` Type `8` — Server Version Response (103 bytes total: 16B header + 87B payload)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 16 | `Header` | LE | `Header` | `PacketSize = 0x00000067` (103), `Checksum`, `Magic = "POLP"`, `PacketType = 0x00000008`. |
| `+0x10` | 4 | `u32` | LE | `NonceEcho` | Client/session tracking nonce echoed by server. |
| `+0x14` | 4 | `u32` | LE | `Status` | `0x00000001` = `"registered"`. Returned even when the installed version is outdated (§4.1.5); no other value has been observed (§9 item 3). |
| `+0x18` | 11 | `char[11]` | ASCII | `StatusString` | Null-terminated ASCII status string (`"registered\0"`). |
| `+0x23` | 16 | `char[16]` | ASCII | `ServerIP` | Null-terminated ASCII server IP address (`"124.150.156.107\0"`). |
| `+0x33` | 2 | `char[2]` | ASCII | `Flag` | `"0\0"`. |
| `+0x35` | 35 | `u8[35]` | Raw | `Padding` | Zero bytes (`0x00` $\times 35$). |
| `+0x58` | 4 | `u32` | LE | `VersionLength` | `0x0000000B` (11 bytes). |
| `+0x5C` | 11 | `char[11]` | ASCII | `CurrentVersion` | Null-terminated ASCII current version: `"20261022_0\0"` on `:54000`; `"30261006_0\0"` on `:53001`. |

#### 4.1.5 Exchange, Timing and Server Behaviour
- **Complete request (capture #8, `:54000`, 88 bytes)**: `58000000 174B6746 504F4C50 07000000 57325500 31303030 32303236 31303232 5F300000` followed by 52 zero bytes (`"W2U\0"`, `"100020261022_0"`). The capture #8/#9 `:53001` request is `58000000 7E3427FC 504F4C50 07000000 57323000 30303031 33303236 31313032 5F300000` + 52 zero bytes (`"W20\0"`, `"000130261102_0"`). Every byte except `Checksum` is the §4.1.2 layout; the harness rebuilds both from their inputs.
- **Sequence**: the client sends the 88 bytes in **one** `send` call, about 130–240 ms after starting the connect, and nothing else. The server answers within about 150 ms with the 103-byte Type 8 frame (retail reads the 4-byte `PacketSize`, then the frame). The **client** closes the socket about 100 ms later.
- **Bad checksum**: in the implementation's live runs on 2026-11-11, both servers closed the connection cleanly (FIN) within about 0.4 s, with no bytes sent, for requests whose layout was right but whose `Checksum` was a CRC-32. Treat a close with no reply as "request rejected".
- **Old version**: captures #1–#4 sent Viewer version `"100020110829_E"` and still received `Status = 1`, `"registered"`, with `CurrentVersion` echoing `"20110829_E"`. So `:54000` does not reject an old Viewer version with a status code. Likewise capture #7 sent the outdated FFXI version `"000130261006_0"` to `:53001` and got `Status = 1` with `CurrentVersion = "30261102_0"`: an outdated install shows up as `CurrentVersion` differing from the installed version. No non-`1` status has been observed (§9 item 3).

---

### 4.2 PlayOnline Core Gatekeeper & IRC Session (`ci000.pol.com:51240` $\rightarrow$ `:51241`–`:51247`)

#### 4.2.1 Gatekeeper Redirect (`ci000.pol.com:51240` Server $\rightarrow$ Client)
Upon TCP connection to `ci000.pol.com:51240`, the server immediately sends a single CRLF-terminated ASCII line and closes the connection:
```text
:<redirect_ipv4> 300 * <44-char-token>\r\n
```
- `<redirect_ipv4>`: Dotted-quad IPv4 address of the assigned Core IRC session server (e.g., `202.67.54.125`), transmitted directly as an ASCII string in the IRC message prefix.
- `<token>` (the part after `300 * `): A 44-character ASCII string consisting of a **40-character body** and a **4-character checksum suffix** (verified across Captures #1, #5, and #6; Test Vector 12).
  - **Alphabet**: Encoded using PlayOnline's custom 32-character permuted Base32 alphabet:
    ```text
    N43OVHBJ1Y2C0WSXED5QFILRZMUTAPGK
    ```
    | Index Range | Characters | Description |
    |---|---|---|
    | `0..31` | `N43OVHBJ1Y2C0WSXED5QFILRZMUTAPGK` | 32-character custom permuted 5-bit Base32 alphabet. |
  - **Decoding Procedure**: Standard RFC 4648 5-bit MSB-first bit packing:
    - 8 Base32 characters $c_0 \dots c_7$ decode into 5 binary bytes $b_0 \dots b_4$:
      - $k_i = \text{AlphabetIndex}(c_i) \in [0, 31]$
      - $b_0 = (k_0 \ll 3) \mid (k_1 \gg 2)$
      - $b_1 = ((k_1 \ \& \ 3) \ll 6) \mid (k_2 \ll 1) \mid (k_3 \gg 4)$
      - $b_2 = ((k_3 \ \& \ 15) \ll 4) \mid (k_4 \gg 1)$
      - $b_3 = ((k_4 \ \& \ 1) \ll 7) \mid (k_5 \ll 2) \mid (k_6 \gg 3)$
      - $b_4 = ((k_6 \ \& \ 7) \ll 5) \mid k_7$
    - The 40-character body ($5 \times 8$ chars) produces an exact **25-byte payload**.
  - **4-Character Suffix**: `LineChecksum` (§5.10) of the whole line before the suffix, from the leading `:` (resolved 2.1.20; the earlier mismatch came from padding the 61-byte line on the wrong side). A client may verify it; nothing it needs depends on it.

**Decoded 25-Byte Redirect Payload Layout**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 4 | `u32` | **BE** | `ServerTimestamp` | Server Unix timestamp $t_{\text{server}}$ at redirection (e.g., `0x6AF25117` = 1794265367 in Cap #5; `0x6AF2A30B` = 1794286347 in Cap #6). Anchors the server clock for the Square Enix password time digest (§5.4). |
| `+0x04` | 4 | `u32` | **BE** | `ClientPublicIPv4` | The client's IPv4 address as seen by the gatekeeper (e.g. `C0 00 02 0A` = `192.0.2.10`). Earlier versions called this `ServerInstanceID`. The same address appears in the `RoutingToken` (§4.2.2) and the PML `userName` (§5.7). |
| `+0x08` | 4 | `u32` | **BE** | `RedirectIPv4` | Redirect Core IRC host IPv4 address (`CA 43 36 7D` = `202.67.54.125`). |
| `+0x0C` | 2 | `u16` | **BE** | `RedirectPort` | **Assigned TCP port** for the next connection (`0xC829` = `51241` in Cap #5, `0xC82F` = `51247` in Cap #6, `0xC82C` = `51244` in Cap #1). Connect to exactly this host and port. It is not always in `51241`–`51247`: in capture #10 the `ci000` token named `202.67.54.131:51240`, and that host sent a Core IRC greeting and ran the whole Core session (RSA, `NICK`) on that connection. |
| `+0x0E` | 2 | `u16` | — | `Reserved1` | Zeroes (`0x0000`). |
| `+0x10` | 4 | `u32` | — | `Reserved2` | Zeroes (`0x00000000`). |
| `+0x14` | 2 | `u16` | **BE** | `ClientPort` | The client's public TCP port for this connection, as seen by the server. In the greeting token (§4.2.2) it equals the port in the `RoutingToken` on all of captures #5, #6, #8, #9. In the gatekeeper redirect it differs, consistent with the port of the separate gatekeeper connection (not independently verified). Earlier versions read `+0x14..+0x17` as a 32-bit `RoutingTag`. |
| `+0x16` | 2 | `u8[2]` | — | `Marker` | `01 01` on every observed token (captures #1–#3, #5–#9). |
| `+0x18` | 1 | `u8` | — | `Terminator` | `0x00`. |

#### 4.2.2 Core IRC Ephemeral RSA Handshake (`:51241`–`:51247`)
The Stage 3 connection executes an exact 3-line plaintext exchange to establish symmetric encryption before credentials are sent:

1. **Server Line 1 — Greeting & Challenge (`Server -> Client`, State 7)**:
   Upon connecting to `<redirect_ipv4>:<assigned_port>`, the server immediately sends an initial unencrypted IRC line:
   ```text
   :<server_ipv4> 300 * <44-char-base32-token>\r\n
   ```
   - Observed in Capture #1: `:pol-1044-51244.pol.com 300 * WCZSXHSNNN4NI55OBMG0ECZNNNNNNNNNQWHN3N1NfAEq\r\n`
   - Observed in Capture #3: `:202.67.54.126 300 * WCZ5HGSNNN4NI55OBMK0E2NNNNNNNNNNMEJN3N1NwhBK\r\n`
   - In both lines above, decoded bytes `+0x04..+0x07` (the client's public IPv4, as in the redirect token, §4.2.1) are replaced with `192.0.2.10` and the body re-encoded. The 4-character suffixes are as captured.
   - `<44-char-base32-token>`: 44-character PlayOnline 5-bit Base32 token. It has the same 25-byte layout as the redirect token (§4.2.1). Keep it: its 40-character body is the input to the `NICK` `PassResponse` (step 5); its `+0x04` address and `+0x14` port form the `RoutingToken` (step 6); and its `ServerTimestamp` is the `RandLo` input to the `0x26` AuthCode (§4.5.2).

2. **Client Line — Ephemeral RSA Modulus (`Client -> Server`, State 8)**:
   The client generates an ephemeral RSA keypair $(N, d)$ with fixed public exponent $e = 65535$ (`0xFFFF`; verified, §1.1 O1), where $N = p \times q$ fits in 32 bytes and is the product of two 128-bit primes. $e$ must be coprime to $(p-1)(q-1)$. Because 65535 = 3 × 5 × 17 × 257, reject any prime $p$ or $q$ for which $p - 1$ is divisible by 3, 5, 17 or 257. **Observed size:** all 12 retail moduli in the captures are 255 bits, top byte `0x41`–`0x4C`, and the four factored primes have top byte `0x83`–`0x8E`; this is what two 128-bit primes with the top bit set give. Whether the server rejects other sizes is not known; generating 128-bit primes with the top bit set reproduces the retail range. $N$ is written as 32 bytes, little-endian (`N[0]` is LSB, `N[31]` is MSB).

   The `USER` token is **47 characters: a 43-character body and a 4-character checksum suffix** (corrected 2.1.20):
   - **Body (43 characters)**: the 32 bytes of $N$ (little-endian) in the §5.2 Base64 encoding. 256 bits need 42⅔ characters, so the 43rd character holds the last 4 bits of `N[31]` followed by **two zero bits**.
   - **Suffix (4 characters)**: `LineChecksum` (§5.10) of the 55 bytes `"USER x 8 * :" ‖ Body`. The 55-byte length is not a multiple of 4, so the last 3 bytes count as `u32_le(00 ‖ tail)`.

   Earlier versions described a 36-byte buffer (N plus "4 varying trailing bytes") encoded to 48 characters and cut to 47, and said zero trailing bytes were safe. That was wrong: the "trailing bytes" are the decoded checksum suffix. A line built that way carries a wrong suffix, and the server does not answer it (implementation live runs, 2026-11-11: no reply in 15 s, connection left open).

   The client transmits the ASCII command:
   ```text
   USER x 8 * :<43-char-base64-modulus><4-char-suffix>\r\n
   ```
   - Observed in Capture #1: `USER x 8 * :8yu_X78A6fDPuyYfeZAIFVxxuhY0A7AlNnfogoe9Pd5UDWB\r\n`
   - Observed in Capture #3: `USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP\r\n` (body ends `…WZ@lIA`; suffix `KuIP`)
   - **Timing and order (all captures)**: the client sends `USER` 31–59 ms after the greeting arrives, and sends nothing else on the connection before the server's ciphertext line, which arrives 148–279 ms after `USER`. `NICK` follows 35–160 ms after that. A 6 ms gap is not known to matter; the absent reply in the live runs is explained by the suffix.

3. **Server Line 2 — RSA Ciphertext C (`Server -> Client`, State 8)**:
   The server builds a 32-byte **PKCS#1 v1.5 (block type 2)** message $M$ around a random 8-byte key $K$:
   $$M_{\text{BE}} = \texttt{00}\ \texttt{02}\ \|\ PS_{21}\ \|\ \texttt{00}\ \|\ K_8^{\text{BE}}$$
   $PS$ is 21 random **non-zero** bytes. The server then encrypts with $e = 65535$:
   $$C = M^e \bmod N$$
   and responds with an unencrypted confirmation line delivering the 256-bit ciphertext $C$:
   ```text
   :<server_ipv4> 300 * <47-char-base64-server-ciphertext>\r\n
   ```
   - Observed in Capture #1: `:pol-1044-51244.pol.com 300 * OVfQuRJX05g@gOVwNJfiQTFGD6qHOLzU5BhYusDSWFYCiQM\r\n`
   - Observed in Capture #3: `:202.67.54.126 300 * OlyyM2LoViSrkkvTTuyCUsDQPvhUHOKSbQImR@QqeX3n]E|\r\n`
   - `<47-char-base64-server-ciphertext>`: 47-character custom 6-bit Base64 token. Decode the first 43 characters (the body) into 32 bytes, which hold Server Ciphertext $C$ in **big-endian** byte order (Byte 0 = MSB, Byte 31 = LSB). This is the opposite of the client's $N$, which is little-endian; both orders were verified by decrypting captures #1 and #3. Only characters `0..42` affect those 32 bytes. The line has the same shape as `USER`: a 43-character body and a 4-character `LineChecksum` suffix (§5.10) over the whole line from its leading `:` (capture #3's suffix is `n]E|`). Decode the 43-character body only; the suffix characters are often outside the §5.2 alphabet.

4. **Decryption & Symmetric Key Extraction**:
   The client decrypts the ciphertext using its private exponent $d$:
   $$M = C^d \bmod N$$
   Write $M$ as 32 big-endian bytes $M_{\text{BE}}[0..31]$. Check the PKCS#1 v1.5 structure: $M_{\text{BE}}[0] = \texttt{00}$, $M_{\text{BE}}[1] = \texttt{02}$, bytes `2..22` non-zero, $M_{\text{BE}}[23] = \texttt{00}$. Treat a mismatch as a failed handshake.

   The session key $K_8$ is the last 8 bytes **reversed**, which is the same as the low 8 bytes of $M$ in little-endian order:
   $$K_8 = [M_{\text{BE}}[31], M_{\text{BE}}[30], \dots, M_{\text{BE}}[24]]$$
   It is used as two 32-bit little-endian words, `key_lo = u32_le(K8[0..3])` and `key_hi = u32_le(K8[4..7])`.

   Capture #3: $M_{\text{BE}}$ = `00 02 EA 29 … BB AC 00 05 6B 71 45 74 D3 1D 31`, so $K_8$ = `31 1D D3 74 45 71 6B 05`, `key_lo = 0x74D31D31`, `key_hi = 0x056B7145`. This matches the key captured independently at cipher initialisation (Test Vector 10.2).
   
   $K_8$ initializes the non-standard **Blowfish-64 OFB** cipher (detailed in Section 5.1). The initial 8-byte OFB IV is the first 8 bytes of the client RSA public modulus $N$: `N[0..7]`. All subsequent IRC lines on this socket are transmitted directly on the wire encrypted with **Blowfish-64 OFB** (`reset_iv = 1` for each line), preserving `\r\n` (`0x0D 0x0A`) via the stream cipher's CRLF preservation rule (Section 5.1).

5. **Encrypted `NICK` Authentication (`Client -> Server`)**:
   The client formats the plaintext command line (verified against the decrypted `NICK` in capture #3; **no trailing tabs**):
   ```text
   NICK <Encoded_POL_ID>:<PassResponse>:<Encoded_ClientMeta>\r\n
   ```
   - `<Encoded_POL_ID>`: **9-character string** derived from the 8-character POL ID without session salt (**RESOLVED, §1.1 O12**).
     1. **Base36 Decoding**: The 8 ASCII characters of the POL ID are mapped to integer digit values $0 \dots 35$ via the custom 36-character alphabet `EFKAOYMJVNGTDSWBQLPCIRHZXU6328401795` (Appendix §1.2 `CONST_BASE36_ALPHABET_POL_ID`), accumulating an unsigned integer (use at least 64-bit arithmetic):
        $$val = \sum_{i=0}^7 \text{Index}(c_i) \times 36^{7 - i}$$
        **Range:** POL IDs are issued by Square Enix as 4 uppercase letters followed by 4 digits (§6.1), so the first character is a letter (alphabet index $\le 25$) and $val < 26 \times 36^7 < 2^{41}$: the value always fits the 41-bit mask in step 2 and the encoding is reversible. Reject a POL ID that is not in that form.
     2. **Bit Initialization**: Set bits 46 and 47:
        $$val \gets (val \ \& \ \text{0x1FFFFFFFFFF}) \;\mid\; (3\text{L} \ll 46)$$
     3. **5-Round Bit Diffusion**: For round index $a = 4, 3, 2, 1, 0$:
        $$mask = \text{0xFF} \ll (8 \times a)$$
        $$val \gets val \oplus \big((val \gg 8) \ \& \ mask\big)$$
     4. **Bit Cleanup**: Clear bits 46 and 47:
        $$val \gets val \ \& \ \sim(3\text{L} \ll 46)$$
     5. **Base36 Re-Encoding**: Convert $val$ back into 8 Base36 characters using the same alphabet (extracting least significant digits first: $c_i = \text{alphabet}[val \bmod 36], val \gets \lfloor val / 36 \rfloor$ for $i = 7 \dots 0$).
     6. **Prefix**: Prepend the ASCII character `'U'` (`0x55`), yielding the final 9-character string (`'U'` + 8 characters).
     - Verified against Capture #3 (POL ID redacted), matching the wire decrypted NICK line byte-for-byte and round-tripping cleanly upon inverting the diffusion rounds (Test Vector 1).
   - `<PassResponse>`: **32-character lowercase hexadecimal MD5** (`"%02x"` × 16) of the **40-character body of this connection's greeting token**, as ASCII text, followed by the plaintext POL password: `PassResponse = md5hex(GreetingToken[0..39] ‖ POL_PASS)`. `GreetingToken[0..39]` is the Base32 body of the `300 *` line the server sent at the start of this connection (step 1), without its 4-character suffix and not decoded. Because the greeting changes on every connection, so does this field. **Verified (2.1.19)** against the decrypted `NICK` of the hooked capture `trace_session_20261110_220551` (harness `tools/verify_nick.py --search`, run with the account's credentials); it also explains why account A's field differs between captures #1, #3 and #9. Earlier versions said `md5hex(POL_PASS)`; that was wrong (never checked against a real password). Test Vector 2.
   - `<Encoded_ClientMeta>`: **36-character client metadata string** composed of a 32-character Base64 payload body followed by a 4-character additive checksum suffix:
     1. **Payload Body (24 bytes $\rightarrow$ 32 characters) — RESOLVED (2.1.19)**: the §5.2 Base64 encoding of 24 bytes:

        | Bytes | Value | Status |
        |---|---|---|
        | `0..4` | `00 00 00 02 01` | Identical in every capture, both accounts, before and after the 2026-11-09 Viewer update. Send as is. |
        | `5` | `01` | **`Region` (presumed, unconfirmed):** the client's region / language code, `01` = US / English (North American client). `01` in every capture, all from one US English install. Reported by runtime analysis to come from the installed client's area code; not yet observed with a non-US client, so other values are unknown. Send `01` for a US English install. |
        | `6..7` | `00 00` | Zero in every capture. |
        | `8..15` | 8 zero bytes | Zero in every capture. Send zeros. |
        | `16..23` | `MD5(00 × 64 ‖ PolIdToken ‖ 00 × 10)[0..7]` | Standard MD5 over an 82-byte buffer: 64 zero bytes, the 8-byte `PolIdToken` below, 10 zero bytes. Per account, stable across sessions. |

        **`PolIdToken`** (8 bytes) from the 8 ASCII characters of the POL ID, `b[0..7]`, all arithmetic unsigned:
        1. *Diffusion*, in place, for `i = 1..7` in order: `b[i] = b[i] XOR b[i-1]` (using the already-updated `b[i-1]`); then if `b[i] & 0x02`, set `b[i] = b[i] OR 0x80`. `b[0]` is unchanged.
        2. *Multiply-accumulate* (64-bit, mod 2⁶⁴): `sum = 1`, `mult = 0x00007048860DDF79`; for `i = 1..7`: `sum = sum + b[i]`; `mult = mult × (b[i-1] × b[i])`.
        3. *LCG* (32-bit, mod 2³²): `lcg = 0`; for `i = 0..7`, if `b[i] > 0x30`, apply `lcg = lcg × 0x425F0CBD + 0x7F4F` exactly `b[i] − 0x30` times. (`b` is the diffused array from step 1.)
        4. *Combine*: `total = (sum + mult) mod 2⁶⁴`; `lo = (total mod 2³²) XOR lcg`; `hi = total >> 32`; `PolIdToken = LE32(lo) ‖ LE32(hi)`.

        **Verified**: the hooked captures `trace_session_20261110_230908` / `_231055` log the 82-byte buffer, and its MD5 equals the logged digest and the wire bytes (account A). Independently, account B's POL ID run through the same steps reproduces its wire body (redacted by GordianXI: the body is derived from the POL ID) (capture #10, recorded before the hooks existed, so the rule was not fitted to it). Account A's body (redacted) is the same for every session of that account. A client computes the body from the POL ID alone; nothing is stored or issued by the server. Test Vector 16.
     2. **Suffix (4 characters) — RESOLVED (§1.1 O4)**: a checksum over the **80 ASCII bytes of the NICK line that precede the suffix**, i.e. `"NICK " ‖ POL_ID(9) ‖ ":" ‖ PassResponse(32) ‖ ":" ‖ MetaBody(32)` (5 + 9 + 1 + 32 + 1 + 32 = 80).
        - Read the 80 bytes as **20 little-endian `u32` words** and add them: `sum = (Σ_{w=0}^{19} u32_le(Line[4w .. 4w+3])) & 0xFFFFFFFF`. (Adding the 80 bytes individually is wrong: a byte sum is below `0x2800` and leaves the top slices zero.)
        - Emit four characters from the top of `sum`, bits 31..8, six bits at a time, each biased by `0x3F`:
          $$c_k = \big((\text{sum} \gg (26 - 6k)) \,\&\, \text{0x3F}\big) + \text{0x3F}, \quad k = 0..3$$
          (shifts 26, 20, 14, 8). The `+0x3F` bias maps each 6-bit slice into printable ASCII `0x3F`–`0x7E`, which is why the suffix can contain characters (e.g. the backtick `0x60`) outside the §5.2 Base64 alphabet.
        - Synthetic example (POL ID `ABCD1234` → `US77G53VV` per Test Vector 1; `PassResponse` from Test Vector 2; ClientMeta body from Test Vector 16): the 80-byte line `NICK US77G53VV:fcd51ab80c161c6c3636c9d2bf5f2bc7:TTTTTAISTTTTTTTTTTTTTHBy5pjMnc0G` gives `sum = 0xC68B4957`, slices `0x31 0x28 0x2D 0x09` → `pglH`. The rule also reproduces the suffix of the real `NICK` in captures #3 and `trace_session_20261110_220551` (that text is not published because it holds the account's POL ID and password response).
   - **Wire Transmission**: Encrypted with the session Blowfish-64 OFB cipher (`reset_iv = 1`). `\r\n` is preserved in plaintext per the CRLF rule. Total encrypted frame length = **86 bytes** (5 + 9 + 1 + 32 + 1 + 36 + 2).

6. **Encrypted Server Welcome (`300`, `001`, `422`, `MODE`, `NOTICE`; `Server -> Client`)**:
   The server replies with IRC lines encrypted directly with Blowfish-64 OFB (`reset_iv = 1` per line). When decrypted, these yield standard numeric IRC lines:
   ```text
   :<server> 300 * <44-char Base32 token>\r\n
   :<server> 001 <Base64 payload>\r\n
   :<server> 422 <Encoded_POL_ID> :<suffix>\r\n
   :<Encoded_POL_ID> MODE <Encoded_POL_ID> :+i<…>\r\n
   :<peer>!~x@ NOTICE <Encoded_POL_ID> :<Base64 payload>\r\n   (zero or more; presence data)
   PING :<token>\r\n   (later; answer with PONG, §9 item 6)
   ```
   This is the sequence decrypted from captures #9 and #10. Earlier versions showed `376 … End of /MOTD` and a Base32 `001`; neither appears in these captures.
   - **`001` payload (corrected).** In captures #9 and #10 the `001` argument is a long §5.2 Base64 string that decodes to `00 00 00 01` followed by zero bytes, plus a 4-character suffix. It does **not** carry the fields earlier versions listed (`RoutingToken`, `AccountToken`, `SessionSecret`, `FFXIAccountID`). What is known about those values:

| Value | Where it actually comes from |
|---|---|
| `RoutingToken` (6 bytes) | The Core IRC **greeting** token (§4.2.2 step 1, layout §4.2.1): `LE16(ClientPort at +0x14)` ‖ `LE32(ClientPublicIPv4 at +0x04)`, i.e. both fields byte-reversed from big-endian. Verified in capture #10 against the `:51220` Hello and in captures #5, #6, #8, #9 against the AuthCode context block. Used in the `:51220` Hello (§4.3.1), the AuthCode context (§4.5.2) and, as `"<a.b.c.d>:<port>"`, the PML `userName` (§5.7). |
| `AccountToken` (12 bytes) | 12 zero bytes in every decrypted `:51220` request header of captures #9 and #10 (both accounts). Send zeros. |
| `SessionSecret` (8 bytes) | Not a protocol value. Nothing in the login needs one: the `:51220` header digest uses the POL ID and password (§4.3.3). |
| `FFXIAccountID` (4 bytes) | Read by `FFXiMain.dll` through `CommonFunctionTable + 0xEEC` (§8.2). Its wire source is not established; it is not needed in any packet this spec builds. |

---

### 4.3 Presence & Square Enix Account Server (`pp000.pol.com:51220` and Redirected `:51220`)

Every TCP connection to a `:51220` server performs a 4-frame exchange over a single socket:
1. **Client Plaintext Hello** (40 bytes)
2. **Server Plaintext Challenge Greeting** (24 bytes)
3. **Client Encrypted Request Header** (40 bytes, `reset_iv = 1`) + **Client Encrypted Request Payload** (`N` bytes, `reset_iv = 0`)
4. **Server Encrypted Response Header** (24 bytes, `reset_iv = 1`) + **Server Encrypted Response Payload** (`M` bytes, `reset_iv = 0`)

Both `pp000.pol.com:51220` and the redirected `:51220` server reuse the **exact same Blowfish-64 OFB cipher instance negotiated during the Stage 3 Ephemeral RSA key exchange** (key setup §5.1; OFB IV = the first 8 bytes of the client RSA public modulus $N$).

#### 4.3.1 Frame 1 — Client Connection Hello (40 bytes / `0x28` bytes, Plaintext)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 4 | `u32` | BE | `Reserved0` | `0x00000000`. |
| `+0x04` | 2 | `u16` | LE | `ProtocolVersion` | `0x0001` (bytes `01 00`). Bytes `+0x04..+0x0B` are the same 8 bytes as the start of the AuthCode context block (§4.5.2). |
| `+0x06` | 6 | `u8[6]` | Raw | `RoutingToken` | The 6-byte `RoutingToken` (§4.2.2 step 6): the client's server-observed port (LE) and IPv4 (LE) from the Core IRC greeting token, e.g. `50 C3 0A 02 00 C0` = `192.0.2.10:50000`. |
| `+0x0C` | 12 | `u8[12]` | Raw | `Unused0C` | **Not a protocol field.** Send 12 zero bytes. Observed in capture #9: the first connection to `pp000.pol.com:51220` carried 12 zero bytes here and the server answered normally (`0x81` challenge → `0x83` redirect); later connections from the same client carried leftover data equal to the **encrypted** bytes at `+0x0C` of that client's previous request header (the zero `AccountToken` after encryption, so it changed after a relogin with the new session key; capture #10 shows bytes left over from the previous login's session). The server does not depend on the value. Earlier versions called this `EncryptedAccountEcho` and said it was copied from the `0x83` header; that was wrong. |
| `+0x18` | 16 | `u8[16]` | Raw | `Padding` | Zeroes (`0x00` $\times 16$). |

#### 4.3.2 Frame 2 — Server Challenge Greeting (24 bytes / `0x18` bytes, Plaintext)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 1 | `u8` | — | `GreetingType` | `0x81`. |
| `+0x01` | 19 | `u8[19]` | — | `Reserved` | Zeroes (`0x00` $\times 19$). |
| `+0x14` | 4 | `u32` | LE | `ChallengeSeed` | 32-bit server challenge nonce $C_{\text{seed}}$ (e.g., `0x6AF0F350`). |

#### 4.3.3 Frame 3a — Client Request Header (40 bytes / `0x28` bytes, Encrypted with `reset_iv = 1`)
Before encrypting all 40 bytes with the session's Blowfish-64 OFB cipher (`reset_iv = 1`, OFB stream bytes `0..39`), the plaintext 40-byte request header has the layout:

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 1 | `u8` | — | `FrameType` | Fixed value `0x02`. |
| `+0x01` | 1 | `u8` | — | `ServiceCategory` | `0x04` for authentication and session key operations (`0x00`, `0x05`, `0x06`, `0x07` for presence/friends). |
| `+0x02` | 1 | `u8` | — | `Command` | `0x07` = Square Enix Account Auth (`64B` payload); `0x05` = Request 16-Byte Session Key (`40B` payload); `0x06` = Session Status (`8B` payload); `0x03` = Account Info (`416B` payload). |
| `+0x03` | 1 | `u8` | — | `Reserved0` | `0x00`. |
| `+0x04` | 4 | `u32` | LE | `PayloadLength` | Byte length of the encrypted payload frame following this header (`0x00000040` = 64, `0x00000028` = 40, `0x000001A0` = 416, etc.). |
| `+0x08` | 4 | `u32` | LE | `Reserved1` | `0x00000000`. |
| `+0x0C` | 12 | `u8[12]` | Raw | `AccountToken` | 12 zero bytes in every decrypted request header (captures #9, #10). |
| `+0x18` | 16 | `u8[16]` | Raw | `ChallengeMD5` | **Verified (2.1.19):** `MD5(POL_ID[8] ‖ POL_PASS ‖ LE32(ChallengeSeed))`, standard RFC 1321 MD5, where `POL_ID` is the 8 ASCII characters of the PlayOnline ID, `POL_PASS` the plaintext PlayOnline password bytes as typed (no terminator, no padding), and `ChallengeSeed` the `0x81` greeting's `+0x14` value of **this** connection. The same digest is therefore sent in every request header on a connection, whatever the category or command. Reproduces all 11 request headers of the hooked capture `trace_session_20261110_220551` (harness check `tools/verify_challenge_md5.py`, run with the account's credentials). Test Vector 15. Earlier versions gave `MD5(SessionSecret ‖ AccountToken ‖ seed)`, which was never verified and is wrong. |

#### 4.3.4 Frame 3b — Client Request Payload: Category `0x04` / Command `0x07` — Square Enix Account Auth (64 bytes / `0x40` bytes, Encrypted with `reset_iv = 0`)
Sent on `pp000.pol.com:51220` in Core Login State `28` (`0x1C`) immediately after the 40-byte Request Header, encrypted with `reset_iv = 0` (OFB stream bytes `40..103`):

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 1 | `u8` | — | `AuthMode` | `0x00` = No Square Enix Account; `0x01` = Square Enix ID + Password only (no OTP); `0x02` = Square Enix ID + Password + 6-digit One-Time Password (`TOTP`). |
| `+0x01` | 17 | `char[17]` | ASCII | `SquareEnixID` | Null-terminated ASCII Square Enix ID (up to 16 characters, zero-padded). |
| `+0x12` | 14 | `char[14]` | ASCII | `OneTimePassword` | When `AuthMode == 0x02`: 6-digit ASCII One-Time Password at `+0x12..+0x17` followed by zero padding (`+0x18..+0x1F`). When `AuthMode == 0x01`: all 14 bytes are `0x00`. |
| `+0x20` | 20 | `u8[20]` | Raw | `PasswordTimeDigest` | 20-byte raw SHA-1 digest of the time-bound Square Enix Password hash (see Section 5.4). |
| `+0x34` | 8 | `u8[8]` | — | `Reserved` | Zeroes (`0x00`). |
| `+0x3C` | 4 | `u32` | LE | `Checksum` | 32-bit PlayOnline additive checksum (Section 5.3) computed over plaintext bytes `+0x00..+0x3B`. |

#### 4.3.5 Frame 3b — Client Request Payload: Category `0x04` / Command `0x05` — Session Key Exchange (40 bytes / `0x28` bytes, Encrypted with `reset_iv = 0`)
**Corrected 2.1.23.** The layout below is read from retail's decrypted requests in the three hooked sessions `trace_session_20261110_220551`, `_230908` and `_231055` (every request kind agrees across all three). Versions up to 2.1.22 gave a `ServiceType` byte at `+0x13` (`0` for FFXI) and big-endian fields at `+0x14`, `+0x16` and `+0x18`. That form puts `00` where the Viewer's request has its selector, so the server issues a valid key of the wrong kind and the lobby later rejects the `AuthCode` with `3301` (§9 item 8).

Retail sends three kinds of key request on the assigned `:51220`:
1. **Viewer key**: once, about 1 s after the last §4.3.7 request, as the first PML connection opens. The 16-byte key it returns is **not** the PML digest key (§1.1 O14); its use is unverified.
2. **FFXI key**: by `FFXiMain.dll` in lobby state 11 (`CommonFunctionTable + 0x30C`), after the `:54001` connect and before `0x26` (§4.5.1.1). Its 16-byte key is the **FFXI Lobby Session Key** that feeds the AuthCode (§4.5.2), every lobby `SessionHash` (§5.5) and the map key (§5.6). Retail sends it again later in the session (capture `231055`, about 50 s after the lobby closed), with the same bytes.
3. **Third key**: once, about 0.5 s after the lobby connection closes (after `0x07` → `0x0B`, i.e. after the character is chosen). Its use is not known; it is not needed for the lobby login.

The 40-byte plaintext payload:

| Offset | Size | Type | Endianness | Field Name | Viewer key | FFXI key | Third key |
|---|---|---|---|---|---|---|---|
| `+0x00` | 16 | `u8[16]` | — | `Reserved` | zeros | zeros | zeros |
| `+0x10` | 4 | `u8[4]` | Raw | `KeySelector` | `00 00 00 00` | **`00 00 00 01`** | `00 01 00 01` |
| `+0x14` | 2 | `u16` | **LE** | `Param` | `0x03E8` (`E8 03`) | **`0x0001` (`01 00`)** | `0x0001` (`01 00`) |
| `+0x16` | 2 | `u16` | **LE** | `SubCommand` | `0x0005` (`05 00`) | `0x0005` (`05 00`) | `0x0005` (`05 00`) |
| `+0x18` | 2 | `u8[2]` | Raw | `Fixed18` | `00 01` | `00 01` | `00 01` |
| `+0x1A` | 6 | `u8[6]` | — | `Padding` | zeros | zeros | zeros |
| `+0x20` | 4 | `u8[4]` | Raw | `WireZeroWord` | `00 00 00 00` | **see below** | `00 00 00 00` |
| `+0x24` | 4 | `u32` | LE | `Checksum` | `0x000504E8` | see below | `0x01050201` |

- **`+0x20` in the FFXI key request.** It is never zero in the plaintext. In all three sessions (and in the second FFXI request of `231055`) it equals the session cipher's OFB keystream bytes for that position, so the four bytes go on the wire as **`00 00 00 00`**. (Up to 2.1.22 this field was called `ClientNonce`, said to be non-zero on one of capture #9's four requests; that one was the FFXI request.) To reproduce it: the payload is encrypted with `reset_iv = 0` after the 40-byte header, so payload `+0x20` is OFB stream bytes `72..75`; set the plaintext there to those keystream bytes (for example, encrypt 40 zero bytes as the payload and copy ciphertext bytes `+0x20..+0x23`), then compute the checksum over the plaintext. Whether the server checks this word is not known; match it.
- **`Checksum`**: the §5.3 additive checksum over plaintext `+0x00..+0x23` (the sum of nine LE `u32` words), including the `+0x20` word.
- Field names are descriptive only. The byte values are the protocol; send them exactly.
- **Response** (32 bytes after the `0x83` header, §4.3.6): `SessionKey[16]`, 12 zero bytes, then a §5.3 checksum over the first 28 bytes.

#### 4.3.6 Frame 4a — Server Response Header (24 bytes / `0x18` bytes, Decrypted with `reset_iv = 1`)
Decrypted with the session's Blowfish-64 OFB cipher (`reset_iv = 1`, OFB stream bytes `0..23`):

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 1 | `u8` | — | `ResponseType` | `0x83` (response to Category `0x04` requests). |
| `+0x01` | 1 | `u8` | — | `Status` | `0x00` = Success; non-zero = error code. |
| `+0x02` | 2 | `u16` | — | `Reserved0` | `0x0000`. |
| `+0x04` | 4 | `u32` | LE | `PayloadLength` | Byte length of the encrypted response payload following this 24-byte header (`0x00000020` = 32 for `0x04/0x05` Session Key; `0x00000008` = 8 for `0x04/0x07` Auth redirect). |
| `+0x08` | 4 | `u32` | LE | `RedirectIPv4` | On `pp000.pol.com:51220` authentication response (`0x04/0x07`), contains the **32-bit IPv4 address of the assigned `:51220` session server**. The 4 raw bytes on the wire are **last octet first** (e.g. `A5 36 43 CA`, where Byte 0 = `0xA5` [165], Byte 1 = `0x36` [54], Byte 2 = `0x43` [67], Byte 3 = `0xCA` [202]). Reversing the 4 bytes gives the dotted-quad address `202.67.54.165`. On subsequent `:51220` queries, contains server sequence/transaction ID. |
| `+0x0C` | 12 | `u8[12]` | Raw | `AccountToken` | Echo of the 12-byte `AccountToken`. |

> **Operational Note (8-byte result on 0x83 Auth redirect)**: When `pp000.pol.com` replies with the `0x83` redirect header, `PayloadLength` says 8 bytes, but the server sends only the 24-byte header and closes the TCP connection (capture #9: one 24-byte receive, then close). The client must not wait for the 8 bytes; it reads the header, then connects to `RedirectIPv4:51220`. If a server does send them, ignore them.

#### 4.3.7 Frame 4b — Server Response Payload: Category `0x04` / Command `0x05` (32 bytes / `0x20` bytes, Decrypted with `reset_iv = 0`)
Decrypted with `reset_iv = 0` (OFB stream bytes `24..55`):

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 16 | `u8[16]` | Raw | `SessionKey` | **16-byte Session Key** (stored by `polcore.dll` and returned to `FFXiMain.dll` via `CommonFunctionTable + 0xFAC`). |
| `+0x10` | 12 | `u8[12]` | Raw | `ZeroPadding` | 12 zero bytes (`0x00` $\times 12$). |
| `+0x1C` | 4 | `u32` | LE | `Checksum` | 32-bit PlayOnline additive checksum over plaintext bytes `+0x00..+0x1B`. |

---

#### 4.3.7 Other Requests on the Assigned `:51220` After the Square Enix Login (observed, 2.1.23)
Earlier versions did not list these. Retail sends all of them in every hooked session (`220551`, `230908`, `231055`), and capture #8 (build `30261102_0`, same as the live client) shows the same connection pattern: eight `:51220` connections between the Square Enix login and the first PML connection. Each runs as its own `:51220` connection with the normal hello, `0x81`, header and payload framing (§4.3.1–§4.3.3); every reply had `Status = 0`. Timing from capture `231055`, measured from the `0x04/0x07` reply (`+0.0 s`):

| Time | Category / Command | Payload sent | Reply payload | Notes |
|---|---|---|---|---|
| `+1.2 s` | `0x04` / `0x06` | 24 zero bytes | 128 bytes | Session status. The reply begins with 8 bytes such as `00 01 00 01 01 00 05 00` or `00 00 00 01 01 00 05 00`, two recent server timestamps, then `0x20` and zeros; the last 4 bytes are a checksum. |
| `+2.0 s` | `0x07` / `0x0C` | none (length 0) | 16 bytes | Sent with the next three in parallel, on four sockets. |
| `+2.1 s` | `0x02` / `0x03` | none | 16 bytes | |
| `+2.1 s` | `0x01` / `0x03` | none | 328 bytes | Reply holds the account's character records (names in ASCII). |
| `+2.1 s` | `0x00` / `0x09` | none | 152 bytes | Reply holds account records (names in ASCII). |
| `+3.2 s` | `0x03` / `0x03` | 416 bytes | 12 bytes | Status record. In the first one, bytes `+0x08..+0x13` are a 12-byte value constant across all three sessions of the same account (source not established; not published here), bytes `+0x02..+0x07` and `+0x14..+0x27` equal the **ciphertext** of that connection's own request header at the same offsets (buffer residue), bytes `+0x00..+0x01` and `+0x28..+0x19B` are zero, and `+0x19C` is a §5.3 checksum over `+0x00..+0x19B`. Retail sends it again right after the lobby's `0x05` reply (0.2 s after `0x26`), that time with most bytes non-zero. |
| `+5.2 s` | `0x04` / `0x05` | Viewer key request | 32 bytes | §4.3.5. The first PML connection opens at the same moment. |

**Status.** None of these replies is needed to build any later packet. Whether the lobby or the presence server needs any of them to have been sent (for example, `0x03/0x03` registering the client's status) is **not known**. A client that still gets `3301` after the §4.3.5 correction should send the five requests whose payloads are fully known (`0x04/0x06` and the four empty ones) in this order before PML. `0x03/0x03` cannot yet be built exactly.

### 4.4 PlayOnline PML HTTP Protocol (`wh000.pol.com:51304`)

PML navigation uses HTTP/1.1 over plain TCP port `51304` with HTTP Digest authentication (`algorithm=x-MD5-pol`).

Request headers below are taken from capture #8, in wire order (§1.1 O13, O14):

| Header Name | Direction | Format / Value |
|---|---|---|
| `Host` | Client $\rightarrow$ Server | `wh000.pol.com` |
| `User-Agent` | Client $\rightarrow$ Server | `PlayOnline-PML-Viewer/1.00 [en] (Windows XP)`. Fixed literal, identical before and after the 2026-11-09 Viewer update. |
| `X-POL-VIEWER-VERSION` | Client $\rightarrow$ Server | `Ver.` ‖ the ASCII contents of `<POL_ROOT>/version.dat` (e.g. `Ver.1.18.15f`). **Install-derived; changes with Viewer patches** (`1.18.15e` → `1.18.15f` on 2026-11-09). |
| `Accept` | Client $\rightarrow$ Server | `text/x-playonline-pml, image/x-playonline-ang, image/png, image/jpeg, */*` |
| `Accept-Language` | Client $\rightarrow$ Server | `en-US` |
| `Connection` | Client $\rightarrow$ Server | `Keep-Alive` |
| `If-Modified-Since` | Client $\rightarrow$ Server | Cache date, on the unauthenticated first request (e.g. `Thu, 16 Sep 2014 02:19:19 GMT`). |
| `X-PlayOnline-Want-Hello` | Client $\rightarrow$ Server | 11 characters: `B64(LE32(t) ‖ LE32(t))` (POL Base64, §5.2), where `t` is the current Unix time in seconds when the connection's first request is built; the same value is sent on that connection's later requests. Verified in captures #1–#3 and #5–#9: every value decodes to two equal 32-bit words, each within about 1 s of the request's send time. The captures cannot tell the local clock from server time, since the two agreed to within a second. Example: `1ULayDsBNfi` = `E6 1B F3 6A E6 1B F3 6A`, `t = 0x6AF31BE6`. |
| `X-PlayOnline-Hello` | Server $\rightarrow$ Client | Server signature over the `Want-Hello` challenge. Format not verified. |
| `WWW-Authenticate` | Server $\rightarrow$ Client | `Digest realm="POL", nonce="<nonce>", algorithm="x-MD5-pol", domain="/pml/", qop="auth"` |
| `Authorization` | Client $\rightarrow$ Server | `Digest userName="<userName>", realm="<realm>", nonce="<nonce>", uri="<request-uri>", response="<32-hex-md5>", algorithm="x-MD5-pol"`, in that order, with no `cnonce`, `nc` or `qop`. `userName` and `response` per §5.7 (verified, O14). The same header is sent on every later request on the connection, reusing the nonce; only `uri` and `response` change. |

- **Traffic Parity & Behavioral Telemetry (Recommended)**:
  For behavioural parity with the retail PlayOnline Viewer, the client SHOULD perform the standard PML navigation sequence:
  1. (Retail also runs the §4.3.7 requests and the Viewer key request (§4.3.5) here. They are not needed for the digest; include them for traffic parity.)
  2. Connect TCP `wh000.pol.com:51304`, send `GET /pml/main/index.pml`, answer the HTTP 401 Digest challenge per §5.7 (keyed by the Core IRC $K_8$), and receive the authenticated 200 OK.
  3. Send `GET /pml/game/ff11/index.pml?SC=0&PF=WIN` over the same keep-alive TCP socket (`Connection: Keep-Alive`).
  4. Discard the received PML response bodies without rendering.
  *Timing Impact*: In live network captures, this entire 2-request exchange over a single persistent TCP connection adds only **300–400 ms** total, incurring no meaningful delay while ensuring the network signature is identical to retail.

- **Launch gating & maintenance (observed in capture #7, `cap7_20261109_230615_ffxi-update-maintenance`)**:
  The PML page returned by the Viewer tier carries the **launch-decision flags** — the retail Viewer, not the login servers, decides whether FFXI may start. The relevant `<define>` fields are:
  | PML field | Meaning |
  |---|---|
  | `$playbutton_enable` | `"1"` = Play allowed; `"0"` = Play blocked (e.g. maintenance). |
  | `$patch_enable` | `"1"` = the FFXI patch/updater may run. |
  | `$status` | Human-readable status string shown to the user. |
  During an **All-Worlds maintenance**, capture #7 showed the version checks (`:54000`, `:53001`) still returning `Status = 1` ("registered") and the POL login (`:51240`→`:51241`, `:51220`) completing **normally** — the only block was `$playbutton_enable = "0"` with `$status = "The game is unavailable for play during maintenance."` (`$patch_enable` was `"1"`, so patching still proceeded). The Viewer therefore never launched `FFXiMain.dll`, and **no FFXI Lobby (`:54001`) connection was attempted**.
  - **Implication for a clean-room client**: maintenance is **not** signalled by any wire-level error on the login path — those layers all succeed. A client that skips the PML tier will not learn it is maintenance and will instead proceed to the Lobby and fail at the TCP/connection level. To detect maintenance cleanly, either parse `$playbutton_enable` / `$status` from `GET /pml/game/ff11/index.pml`, or treat a failed Lobby connect after a fully successful login as a probable maintenance/outage and surface it as such.

---

### 4.5 Final Fantasy XI Lobby Protocol (`ffxi00.pol.com:54001`)

> **Compatibility note**: Live capture on `124.150.154.122:54001` confirms that the retail FFXI Lobby protocol uses the 28-byte (`0x1C`) `IXFF` header, MD5 packet integrity hashing and MD5 session-key salting (`SessionHash`) described below.

#### 4.5.1.1 Lobby Login Wire Sequence, Ordering and Timing (observed)

The exact order retail follows from the Play click to the lobby login, verified in captures #1 and #5. Nothing on the Core IRC connection carries the transition; the only wire traffic between the FFXI version check and `0x26` is the `:54001` TCP connect and one FFXI key request (§4.3.5).

1. **Play clicked.** The client closes the PML keep-alive socket (`wh000.pol.com:51304`) and immediately runs the **FFXI `POLP` version check** on `pc001W2U.pol.com:53001` (§4.1). It sends **nothing** to `pp000`/the assigned `:51220`, nothing on Core IRC, and no further PML request as part of the click. (The §4.3.7 requests, the Viewer key and both PML pages came earlier, before and during PML browsing, not here.)
2. **`FFXiMain.dll` launches** (lobby states `1 → 10`; several seconds of local DLL init, no wire traffic).
3. **State 10 — connect the lobby.** TCP connect to `ffxi00.pol.com:54001`. The socket is opened here, before the key fetch, but no lobby packet is sent yet.
4. **States 11–12 — fetch the FFXI key.** Connect to the **assigned** `:51220` (the `RedirectIPv4` from the `0x04/0x07` reply, §4.3.6 — **not** `pp000`), send the `0x04/0x05` **FFXI key request** exactly as in §4.3.5 (selector `00 00 00 01`, wire-zero `+0x20` word), and read the 32-byte reply. Its 16-byte `SessionKey` is the `LobbySessionKey`. Close `:51220`.
5. **State 14 — send `0x26`** on the `:54001` socket, built from the key just fetched.
6. The server replies `0x05` (40 bytes, §4.5.3) on success.

**Ordering (Q): the lobby connect comes first, then the key fetch, then `0x26`.** The FFXI key fetch and the `:54001` connect are on different sockets and the fetch does not depend on the lobby socket, but retail always has the `:54001` socket open before it fetches the key, and sends `0x26` only after the key arrives.

**Timing.** Capture #5: FFXI `POLP` check done at `+0.0 s`; `:54001` connect `+8.1 s` (the gap is local `FFXiMain.dll` init); FFXI key received `+9.3 s`; `0x26` sent `+9.5 s`. From the FFXI key reply to `0x26` is ~170 ms; from the `:54001` connect to `0x26` is ~1.3 s. Send `0x26` promptly after the key: the `AuthCode` is time-bound and the presence session must still be current (the `3301` message tells the user to "wait several minutes," i.e. an expired/again-attempted session is rejected).

**Core IRC stays open.** The Core IRC socket (`:51241`–`:51247`, §4.2.2) opened in stage 3 **remains open** through the version check, the key fetch and the lobby login, and for the whole PlayOnline session; it only carries periodic `PING`/`PONG` (§9 item 6). The lobby validates `0x26` against this live presence session, so a client that closed or reconnected Core before `0x26` will fail with `3301`. Keep it open.


#### 4.5.1 Common Lobby Packet Header (28 bytes / `0x1C` bytes)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 4 | `u32` | LE | `PacketSize` | Total packet length in bytes, including this 28-byte header (`0x1C` to `0xFC0`). |
| `+0x04` | 4 | `char[4]` | ASCII | `Magic` | Fixed ASCII string `"IXFF"` (`49 58 46 46`, or `0x46465849` as `u32_le`). |
| `+0x08` | 4 | `u32` | LE | `Command` | Lobby opcode. Client: `0x26`, `0x1F`, `0x24`, `0x07`, `0x22`, `0x21`, `0x14`, `0x28`, `0x2B`. Server: `0x05`, `0x20`, `0x23`, `0x0B`, `0x03`, `0x04`. |
| `+0x0C` | 16 | `u8[16]` | Raw | `PacketMD5` | MD5 digest computed over the entire packet (`0 .. PacketSize - 1`) with header bytes `+0x0C .. +0x1B` set to `0x00` prior to hashing. |

#### 4.5.2 Client Opcode `0x26` — Login & Client Version Check (152 bytes / `0x98` bytes, Sent Once in State 14)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000098` (152), `Magic = "IXFF"`, `Command = 0x00000026`, `PacketMD5`. |
| `+0x1C` | 16 | `char[16]` | ASCII | `PolAccount` | Deprecated legacy field; zeroes when `AuthCode` is present. |
| `+0x2C` | 8 | `u8[8]` | Raw | `ClientCode` | `05 00 00 00 00 00 00 00` in every captured `0x26` (English client). Not a fixed platform identifier: public documentation (XiPackets `C2S_0x0026_RequestLobbyLogin`) says the client sets it to `3` or `5` from its language setting and a graphics setting chosen at first launch, without saying which combination gives which. Send `5` (§9 item 7). |
| `+0x34` | 64 | `u8[64]` | Raw | `AuthCode` | 64-byte authentication block. Bytes `+0x34..+0x67` (52 bytes) are the AuthCode token generated by the POL runtime (`polcore.dll`) and delivered via `CommonFunctionTable + 0xEA0` (§1.1 O7). Bytes `+0x68..+0x73` (12 bytes) vary per login in retail and look like stack residue: on build `30261102_0` they are `09 00 00 00` followed by two LE `u32` values whose difference is the constant `0x00399771` (captures #8, #9 and all four hooked sessions); on build `30261006_0` they had a different, also pointer-like shape. **No capture sends zeros here** (2.1.23 correction: capture #8's zeros are the token's context bytes `8..19`, not these). They are outside the encrypted token, so the server is unlikely to check them, but zeros are untested. All 64 bytes, as sent, are an input to `SelectionMD5` (§4.5.8). |
| `+0x74` | 15 | `char[15]` | ASCII | `ClientVersion` | Null-terminated ASCII client version string, zero-padded to 15 bytes. **Install-derived**: the FFXI version from `patch.cfg` (§4.1.3), e.g. `"30261006_0"` (captures #1–#7), `"30261102_0"` (captures #8–#9). |
| `+0x83` | 1 | `u8` | — | `VersionTail` | `0xFF` in every captured `0x26`. Public documentation treats `+0x74..+0x83` as one 16-byte version buffer with no separate terminator field, so this is the buffer's last byte, not a protocol terminator. Send `0xFF`. |
| `+0x84` | 4 | `u32` | LE | `ExpansionMask` | Expansions **installed** on the client (XiPackets `excode_client`): bit 0 base game, bits 1–11 Rise of the Zilart … Seekers of Adoulin, in release order. A current full retail install has all twelve, so the value is `0x00000FFF` (every capture). This is not the account's entitlement: the server reports that separately in `0x05` `ExpansionMask` (§4.5.3; `0x00000001` for the trial account all captures used). |
| `+0x88` | 16 | `u8[16]` | — | `Padding` | Zeroes (`0x00` $\times 16$). |

**AuthCode 52-Byte Token Generation Procedure (§1.1 O7, VERIFIED)**

> **Verification status (§1.1 O7)**: The generation algorithm is fully verified against live captures (#5, #6, #8, #9) and an assertable harness, and independently re-implemented from scratch (byte-for-byte match). The forward algorithm reproduces the wire `0x26` token from the logged inputs in capture #9 and reproduces all four builder-hook calls (differing session keys). The proprietary 64-bit block cipher under key `CB 83 24 B7 BA 3E 9E 0A` decrypts and round-trips the wire tokens of all four captures, and every decrypted plaintext satisfies $B[0] = H[13] \oplus H[3]$ and $B[1] = 0$ — confirming the cipher and key universally, not just for capture #9. The earlier observation that token bytes `12..27` were identical across calls with differing session keys is explained in Test Vector 13 ("Resolution of the Preimage Artifact"): the context block is the same for every call in a login, so plaintext blocks 1 and 2 encrypt identically, and token bytes `12..27` then differ only by the XOR of `Token[11]`.
>
> **Context block characterization ($B[4..23]$)**: Decrypting the wire tokens of all four captures yields four context blocks (address bytes `2..7` shown as the synthetic `50C30A0200C0`; the real values differ per login): `#5 010050C30A0200C000000000CDCCCC3E00000000`, `#6 010050C30A0200C0E92470FACEA16CC8E57045BB`, `#8 010050C30A0200C0000000000000000000000000`, `#9 010050C30A0200C0CDCCCC3E0000000000000000`. The 20-byte block: bytes `0..1` are `ProtocolVersion` (`0x0001` LE), bytes `2..3` are the client's public port (`u16_le`), and bytes `4..7` are the client's public IPv4 address (`u32_le`) from the `RoutingToken` (Core IRC greeting token, §4.2.2 step 6) (identical to bytes `+0x04..+0x0B` of the 40-byte Hello sent to `pp000.pol.com:51220`, verified across all four captures). Bytes `8..19` vary between logins and match no session value. Capture #8 logged in successfully with all twelve bytes zero, so zero-fill is accepted; whether the server checks these bytes at all is not known. A clean-room client assembles the 20-byte block from `0x0001` + `RoutingToken` + 12 zero bytes.

The 52-byte token is constructed in `polcore.dll` and handed across the COM boundary via `CommonFunctionTable + 0xEA0` to `FFXiMain.dll`:

1. **28-byte Preimage Digest**:
   - Assemble the 28-byte buffer $D_{28}$:
     $$D_{28} = \text{PresenceSessionKey}_{16} \;\|\; \text{LE32}(\text{ServerTimestamp}) \;\|\; K_8[0..7]$$
   - Compute 16-byte MD5 digest $H_{16} = \text{MD5}(D_{28})$.

   | Offset | Size | Type | Field Name | Description |
   |---|---|---|---|---|
   | `+0x00` | 16 | `u8[16]` | `SessionKey` | The 16-byte `LobbySessionKey` (`:51220` `0x04/0x05` service 0, §4.3.5, §5.5), the same key used for the lobby `SessionHash`. **Verified (2.1.17)** in capture #9: the key in the wire token's builder call equals the key `FFXiMain.dll` read through `CommonFunctionTable + 0xFAC`, and that key reproduces the wire `0x1F` (`M0+1`) and `0x24` (`M0+3`) `SessionHash` values. The builder runs once per received service key (four times in capture #9); the token sent is the one built from the key current when `0x26` is assembled. |
   | `+0x10` | 4 | `u32` | `ServerTimestamp` | Formerly `RandLo`. The `ServerTimestamp` (`+0x00`, BE) of the Core IRC greeting token (§4.2.2). Capture #9: greeting `6A F3 5D 54`, `RandLo` `0x6AF35D54`. Not random. |
   | `+0x14` | 4 | `u32` | `K8Lo` | $K_8[0..3]$, the first half of the Core IRC session key (§4.2.2), as `u32` LE. Earlier versions and the capture #9 hook called it `RandHi`. Capture #9: `0x711B6CB9`. |
   | `+0x18` | 4 | `u32` | `K8Hi` | $K_8[4..7]$, as `u32` LE. Earlier versions and the capture #9 hook called it `Sequence`; it is not a counter. Capture #9: `0x5A371CF2`. Bytes `+0x14..+0x1B` together are exactly $K_8$. |

2. **40-byte Plaintext Block Assembly ($B_{40}$)**:
   - Assemble $B_{40}$:
     - $B[0] = H_{16}[13] \oplus H_{16}[3]$
     - $B[1] = S$ (seed byte; always `0x00` in retail logins)
     - $B[2..3] = \text{0x00}, \text{0x00}$
     - $B[4..23]$ (20 bytes): Context Block assembled from session state:
       - $B[4..5] = \text{LE16}(1)$ (`ProtocolVersion = 0x0001`)
       - $B[6..7] = \text{LE16}(\text{ClientPort})$ (from the `RoutingToken`, §4.2.2 step 6)
       - $B[8..11] = \text{LE32}(\text{ClientIPv4})$ (from the `RoutingToken`, §4.2.2 step 6)
       - $B[12..23] = \text{0x00} \times 12$ (clean-room value; varies per login in retail and carries no protocol data)
     - $B[24..39]$ (16 bytes): $H_{16}$

   | Offset | Size | Type | Field Name | Description |
   |---|---|---|---|---|
   | `+0x00` | 1 | `u8` | `DigestFold` | $H_{16}[13] \oplus H_{16}[3]$. |
   | `+0x01` | 1 | `u8` | `Seed` | Seed $S$ (`0x00` in retail). |
   | `+0x02` | 2 | `u8[2]` | `ZeroPad` | `0x00, 0x00`. |
   | `+0x04` | 2 | `u16` | `ProtocolVersion` | `0x0001` (`01 00`). |
   | `+0x06` | 2 | `u16` | `ClientPort` | Client public port as observed by server, from the `RoutingToken`, §4.2.2 step 6. |
   | `+0x08` | 4 | `u32` | `ClientIPv4` | Client public IPv4 address as observed by server, from the `RoutingToken`, §4.2.2 step 6. |
   | `+0x0C` | 12 | `u8[12]` | `Unused` | Varies per login in retail; carries no protocol data. Zero-fill (`0x00` $\times 12$; accepted in capture #8). |
   | `+0x18` | 16 | `u8[16]` | `Digest` | Full 16-byte MD5 digest $H_{16}$. |

3. **Proprietary 64-Bit Block Cipher & Key Schedule**:
   - **Key**: Fixed 8-byte key (Appendix §1.1 `CONST_AUTH_CODE_KEY`):
     $$K = \text{CB 83 24 B7 BA 3E 9E 0A}$$
   - **Key Expansion**:
     Generates 32 64-bit subkeys $K_0 \dots K_{31}$.
     Let key halves be 32-bit little-endian words $k_0 = \text{u32\_le}(K[0..3])$, $k_1 = \text{u32\_le}(K[4..7])$.
     Rotate words: $k_0 = (k_0 \ll 8) \mid (k_0 \gg 24)$, $k_1 = (k_1 \gg 16) \mid (k_1 \ll 16)$.
     Populate an 8-byte buffer: $\text{ctx}[0..3] = k_1$, $\text{ctx}[4..7] = k_0$.
     Initial byte transform: $\text{ctx}[0] = (\text{ctx}[0] + 0\text{x45}) \pmod{256}$.
     For $i = 1 \dots 7$:
     $$\text{ctx}[i] = ((\text{ctx}[i-1] \ll 2) \oplus (\text{ctx}[i] + \text{ctx}[i-1] - 0\text{x2C}) \oplus 0\text{x45}) \pmod{256}$$
     Set $K_0 = \text{LE64}(\text{ctx}[0..7])$.
     For rounds $r = 1 \dots 31$:
     $$K_r = (K_{r-1} \times 5) \pmod{2^{64}}$$

   - **Block Encryption Routine**:
     Processes an 8-byte block at byte counter $C$ using subkey $SK = K_{(C \gg 3) \bmod 32}$ (64-bit; $SK_{\text{lo}}$ = low 32 bits, $SK_{\text{hi}}$ = high 32 bits; $SK_{\text{byte}}[0..7] = \text{LE64}(SK)$):
     1. Read the input as two little-endian words $in_{\text{lo}} = \text{u32\_le}(in[0..3])$, $in_{\text{hi}} = \text{u32\_le}(in[4..7])$, and **swap** them: $(L, R) = (in_{\text{lo}}, in_{\text{hi}})$. $L$ is the high word of the 64-bit value in steps 3–4, so after this step the input's first four bytes occupy the high half.
     2. XOR with subkey: $(L, R) = (L \oplus SK_{\text{hi}}, R \oplus SK_{\text{lo}})$.
     3. XOR with the counter value (64-bit arithmetic):
        $$cVal = \big((C \ll 30) \mid (C \ll 20) \mid (C \ll 10) \mid C\big) + \text{0xA1652347} \pmod{2^{64}}$$
        $(L, R) = (L \oplus cVal_{\text{hi}}, R \oplus cVal_{\text{lo}})$.
     4. 64-bit addition with subkey:
        $$W = (((L \ll 32) \mid R) + SK) \pmod{2^{64}}$$
        Set $blkOut[0..7] = \text{LE64}(W)$ (bytes `0..3` are $W_{\text{lo}}$, bytes `4..7` are $W_{\text{hi}}$).
     5. Byte substitution with feedback:
        Let $dl = ((C \gg 3) \,\&\, \text{0xFF}) \oplus \text{0x45}$.
        For each byte $i = 0 \dots 7$, in order:
        - $cl = blkOut[i]$
        - For $j = 0 \dots 7$: $cl = (cl + SK_{\text{byte}}[j] + \text{0x88}) \bmod 256$ (additions only; nothing else happens inside this loop).
        - After the 8 additions, once per byte: $cl = cl \oplus dl$; $blkOut[i] = cl$; $dl = cl$.
     6. The output block is $blkOut[0..7]$.

     Test Vector 13 (capture #9, $B_{40}$ → 48-byte ciphertext) checks every step above; a reading of step 1 without the swap, or with the XOR inside the $j$ loop, does not reproduce it.

   - **Plaintext & Trailer Encryption**:
     - Encrypt blocks $0 \dots 4$ at counters $C = 0, 8, 16, 24, 32$.
     - Accumulate a 32-bit checksum over the 5 plaintext blocks, adding single **bytes** (unsigned), not words:
       $$\text{checksum} = \sum_{b=0}^4 (B_{40}[8b] + B_{40}[8b + 4]) \bmod 2^{32}$$
     - Encrypt 8-byte trailer block at counter $C = 40$:
       $$\text{Trailer} = \text{LE32}(40) \;\|\; \text{LE32}(\text{checksum})$$
     - Result is $(5 + 1) \times 8 = \mathbf{48}$ ciphertext bytes.
     - Store into token: $\text{Token}[4 \dots 51] = \text{Ciphertext}[0 \dots 47]$.

4. **Forward Chained CBC-Style XOR Diffusion**:
   - For $i = 5 \dots 51$:
     $$\text{Token}[i] = \text{Token}[i] \oplus \text{Token}[i - 1]$$

5. **Header Bytes Folding ($0 \dots 3$)**:
   - $\text{Token}[0] = \text{Token}[14] \oplus S$ ($S = 0$ in retail)
   - $\text{Token}[1] = \text{Token}[12] \oplus \text{Token}[6]$
   - $\text{Token}[2] = \text{Token}[10] \oplus \text{Token}[8]$
   - $\text{Token}[3] = \text{Token}[13] \oplus \text{Token}[9]$
   Total token length produced by `polcore.dll` is exactly **52 bytes** (`0x34`).

6. **Trailing bytes ($52 \dots 63$) — not a token field**:
   - The token is 52 bytes; the `0x26` `AuthCode` field is 64. The remaining 12 bytes (`+0x68..+0x73`) carry no protocol data and **vary per login**: Capture #5 `A0 E6 93 00 46 02 00 00 32 42 E2 00`, Capture #6 `50 EC 6F 00 46 02 00 00 32 42 8D 02`, Capture #8 `09 00 00 00 C0 0F E5 16 4F 78 AB 16`. A clean-room client sends 12 zero bytes and uses the same 64 bytes, as sent, in `SelectionMD5` (§4.5.8).

An emulator or clean-room client connecting to private servers (LandSandBoat) does not need this token as private servers ignore the field; connecting to retail requires generating valid tokens matching this procedure.

#### 4.5.3 Server Opcode `0x05` — Login Version Ack & Modifier Seed (40 bytes / `0x28` bytes)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000028` (40), `Magic = "IXFF"`, `Command = 0x00000005`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `ModifierSeed` | Initial 32-bit `KeyByteModifier` seed $M_0$ assigned by the Lobby server (e.g., `FA F1 0D 59` = `0x590DF1FA`). |
| `+0x20` | 4 | `u32` | LE | `ExpansionMask` | Enabled account expansion bitmask (e.g., `0x00000001` on trial account). |
| `+0x24` | 4 | `u32` | LE | `FeatureFlags` | Enabled account feature flags (e.g., `0x00000002`). |

#### 4.5.4 Client Opcode `0x1F` — Request Character List (44 bytes / `0x2C` bytes)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x0000002C` (44), `Magic = "IXFF"`, `Command = 0x0000001F`, `PacketMD5`. |
| `+0x1C` | 16 | `u8[16]` | Raw | `SessionHash` | `MD5(SessionKey[16] || LE32(ModifierSeed + 1))` (see Section 5.5). |

#### 4.5.5 Server Opcode `0x20` — Character List Response (452 bytes / `0x1C4` bytes observed)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x000001C4` (452), `Magic = "IXFF"`, `Command = 0x00000020`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `ContentIDCount` | Number of character/content slots returned (`0x00000003` observed; each slot is 140 / `0x8C` bytes starting at `+0x20`). |
| `+0x20` | `140 * N` | `Slot[N]` | LE | `CharacterSlots` | Array of 140-byte character slot records (`FFXIContentID` at `+0x00`, `CharacterID` at `+0x04`, `Status` at `+0x08`, `CharacterName[16]` at `+0x0C`, `CharInfo` structure at `+0x2C`). |

#### 4.5.6 Client Opcode `0x24` — Request World List (44 bytes / `0x2C` bytes)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x0000002C` (44), `Magic = "IXFF"`, `Command = 0x00000024`, `PacketMD5`. |
| `+0x1C` | 16 | `u8[16]` | Raw | `SessionHash` | `MD5(SessionKey[16] || LE32(ModifierSeed + 3))` (incremented by `+2` after `0x1F`/`0x20`). |

#### 4.5.7 Server Opcode `0x23` — World List Response (272 bytes / `0x110` bytes observed)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000110` (272), `Magic = "IXFF"`, `Command = 0x00000023`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `WorldCount` | Number of active worlds returned (e.g., `0x0000000C` = 12 worlds). |
| `+0x20` | `20 * N` | `World[N]` | LE | `WorldEntries` | Array of 20-byte (`0x14`) world records: `+0x00` = `u32_le WorldID`, `+0x04` = `char[16] WorldName` (null-terminated ASCII). |

#### 4.5.8 Client Opcode `0x07` — Select Character (88 bytes / `0x58` bytes)
| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000058` (88), `Magic = "IXFF"`, `Command = 0x00000007`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Selected character's 32-bit FFXI Content ID. |
| `+0x20` | 4 | `u32` | LE | `WorldCharacterID` | The character's in-world server ID: low 16 bits of the list slot's ID, zero-extended (capture #5: slot has `5E 91 65 00`, this field is `5E 91 00 00`). |
| `+0x24` | 16 | `char[16]` | ASCII | `CharacterName` | Selected character's 15-byte null-terminated ASCII name. |
| `+0x34` | 16 | `u8[16]` | Raw | `SessionHash` | `MD5(LobbySessionKey ‖ LE32(Modifier))` with the next modifier in the schedule (§5.5): `M0+5` if only `0x1F` and `0x24` came before, `M0+11` in capture #5. Verified. |
| `+0x44` | 4 | `u32` | LE | `DllTableIndex` | Always `0x00000003`. Public documentation (XiPackets) identifies it as an index into the client's table of `FFXi.dll` / `FFXiMain.dll` checksums, sent where a checksum was probably intended. |
| `+0x48` | 16 | `u8[16]` | Raw | `SelectionMD5` | `MD5` of the 92-byte preimage below. Verified against capture #6's MD5 hook and wire value (Test Vector 11). |

**`SelectionMD5` preimage (92 bytes / `0x5C`, verified)** — captured directly from the client's MD5 call in capture #6 (`cap6_20261109_215156_md5hook-zone-relogin`); `MD5(preimage)` equals the wire `SelectionMD5` for both of that capture's logins:

| Offset | Size | Field | Source |
|---|---|---|---|
| `+0x00` | 64 | `AuthCode` | The 64-byte block sent in Opcode `0x26` `+0x34..+0x73` (§4.5.2), byte-for-byte. |
| `+0x40` | 16 | `SessionHash` | Opcode `0x07` `+0x34..+0x43`. |
| `+0x50` | 4 | `DllTableIndex` | `0x00000003` (same value as `0x07` `+0x44`). |
| `+0x54` | 4 | `ClientChecksum` | `ClientFileChecksum(<FFXI_ROOT>/FFXiMain.dll, 0x13)` (§5.9). **Install-derived; changes with every build** (`0x43FA0718` in `30261006_0`, `0x74F0058F` in `30261102_0`). See note below. |
| `+0x58` | 4 | `FFXIContentID` | Opcode `0x07` `+0x1C`. |

> **`ClientChecksum` (`+0x54`) note (§1.1 O13)**: The value is `ClientFileChecksum` (§5.9) over every byte of `<FFXI_ROOT>/FFXiMain.dll` as stored on disk, with period `0x13`. The constant `3` at `+0x44` / `+0x50` does not depend on the build (§4.5.8 `DllTableIndex`).
>
> Verified values, each recovered independently from a wire `SelectionMD5` where it is the only unknown in the 92-byte preimage:
> - `30261006_0` → `0x43FA0718` (capture #6).
> - `30261102_0` → `0x74F0058F` (capture #8). `ClientFileChecksum` of the installed `30261102_0` `FFXiMain.dll` gives exactly this value.
>
> A clean-room client computes the value from the user's own `FFXiMain.dll` at login. It must not ship a value, because the next patch changes it. The file must be the one matching the `ClientVersion` sent in `0x26`. LandSandBoat ignores the field entirely.

#### 4.5.9 Server Opcode `0x0B` — Character Select Ack & Map / Search Server Handoff (72 bytes / `0x48` bytes)
Layout verified against capture #5. The server closes the lobby connection after sending it.

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000048` (72), `Magic = "IXFF"`, `Command = 0x0000000B`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Echo of the selected content ID (`84 69 6B 01`). |
| `+0x20` | 2 | `u16` | LE | `CharacterIDLow` | Low 16 bits of the character ID (`5E 91`). |
| `+0x22` | 2 | `u16` | LE | `Unknown22` | `0x000A` observed. The 4 bytes `+0x20..+0x23` (`5E 91 0A 00`) are sent back unchanged in the first map packet (§4.6 `UniqueNo`). |
| `+0x24` | 16 | `char[16]` | ASCII | `CharacterName` | Null-terminated character name. |
| `+0x34` | 4 | `u32` | LE | `Unknown34` | `0x00000002` observed. |
| `+0x38` | 4 | `u8[4]` | **first octet first** | `MapServerIPv4` | `7C 96 98 42` = `124.150.152.66`. The client then sent UDP to exactly that address. |
| `+0x3C` | 4 | `u32` | LE | `MapServerPort` | `E0 D3 00 00` = `54240` (UDP). |
| `+0x40` | 4 | `u8[4]` | **first octet first** | `SearchServerIPv4` | `7C 96 9A 3E` = `124.150.154.62`. |
| `+0x44` | 4 | `u32` | LE | `SearchServerPort` | `F2 D2 00 00` = `54002`. |

> Note: this is the opposite byte order to the presence `RedirectIPv4` field in §4.3.6, which is last octet first on the wire.

#### 4.5.10 Character Management & Lobby Acknowledgments

Lobby management operations (creation, name validation, deletion, renaming, and world transfer) follow a uniform request-reply structure using the `SessionHash` modifier schedule (§5.5).

> **Verification status (§1.1 O9)**: `0x22`, `0x21` and the `0x03` success ack are verified against capture #5. The `0x14`, `0x28`, `0x2B` and `0x04` layouts below follow the public XiPackets documentation, which corrected the binary-derived layouts in drafts ≤ 2.0.3. They have not been observed on a retail wire; the legacy client produces the same packets against LandSandBoat.

##### Client Management Opcodes

**Client Opcode `0x22` — Name / World Check (96 bytes / `0x60` bytes)**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x60`, `Command = 0x00000022`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Content ID of the slot being evaluated. |
| `+0x20` | 16 | `char[16]` | ASCII | `CharacterName` | Null-terminated proposed character name. |
| `+0x30` | 16 | `u8[16]` | Raw | `SessionHash` | Next modifier in the schedule (`MD5(LobbySessionKey || LE32(Modifier))`). |
| `+0x40` | 16 | `char[16]` | ASCII | `WorldName` | Null-terminated target world name (`"Shiva"`). |
| `+0x50` | 16 | `u8[16]` | Raw | `Unknown50` | `00`, then `FF` × 14, then `00`. |

**Client Opcode `0x21` — Create Character (144 bytes / `0x90` bytes)**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x90`, `Command = 0x00000021`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Content ID of the slot. |
| `+0x20` | 16 | `u8[16]` | Raw | `SessionHash` | Next modifier in the schedule. |
| `+0x30` | 96 | `u8[96]` | Raw | `CharInfo` | Appearance / race / job / nation structure (echoed back in refreshed `0x20` list slot offset `+0x2C`). |

**Client Opcode `0x14` — Delete Character (52 bytes / `0x34` bytes)**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x34`, `Command = 0x00000014`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Content ID of character to delete. |
| `+0x20` | 4 | `u32` | LE | `WorldCharacterID` | The character's in-world server ID (as in `0x07` `+0x20`). |
| `+0x24` | 16 | `u8[16]` | Raw | `SessionHash` | Next modifier in the schedule. |

**Client Opcode `0x28` — Rename Character (68 bytes / `0x44` bytes)**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x44`, `Command = 0x00000028`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Content ID of character to rename. |
| `+0x20` | 4 | `u32` | LE | `WorldCharacterID` | The character's in-world server ID. |
| `+0x24` | 16 | `char[16]` | ASCII | `NewName` | Null-terminated new character name. |
| `+0x34` | 16 | `u8[16]` | Raw | `SessionHash` | Next modifier in the schedule. |

**Client Opcode `0x2B` — World Transfer (68 bytes / `0x44` bytes)**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x44`, `Command = 0x0000002B`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `FFXIContentID` | Content ID of character to transfer. |
| `+0x20` | 4 | `u32` | LE | `TargetWorldID` | 32-bit ID of the destination world. |
| `+0x24` | 16 | `char[16]` | ASCII | `NewName` | Null-terminated character name on the destination world. |
| `+0x34` | 16 | `u8[16]` | Raw | `SessionHash` | Next modifier in the schedule. |

##### Server Acknowledgment & Error Opcodes

The server dispatches responses through a unified acknowledgment mechanism:

1. **Server Opcode `0x03` — Success Ack (32 bytes / `0x20` bytes)**:
   Returned upon successful execution of character operations (`0x14` Delete, `0x21` Create, `0x22` Name Check, `0x28` Rename, `0x2B` World Transfer):

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000020` (32), `Magic = "IXFF"`, `Command = 0x00000003`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `Status` | `0x0000000C` observed on success. |

2. **Server Opcode `0x04` — Failure / Error Ack (36 bytes / `0x24` bytes)**:
   Returned when any lobby character operation fails (e.g. name taken, prohibited characters, world full, slot locked):

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 28 | `Header` | LE | `Header` | `PacketSize = 0x00000024` (36), `Magic = "IXFF"`, `Command = 0x00000004`, `PacketMD5`. |
| `+0x1C` | 4 | `u32` | LE | `Unknown1C` | Not used by the client. XiPackets shows `0x10` in its example; the 2026-11-11 live `3301` carried `0x000115E9` (71145). No captured login contains a lobby `0x04`, so its meaning (possibly a server-side ID or sub-reason) is not known. |
| `+0x20` | 4 | `u32` | LE | `ErrorCode` | Lobby error code. The client adds 3000 and looks up the message (for example `101` → `3101`). Login-relevant codes (XiPackets `lobby/Protocol.md`): `300`→`3300` "This protocol is not supported"; **`301`→`3301` "There has been a communication error with the FINAL FANTASY XI server. Please log out from PlayOnline and wait several minutes before logging in again"** — the generic session/`AuthCode`-validation failure, returned when the lobby cannot correlate `0x26` with a live PlayOnline presence session (see §9 item 8), **not** a wrong password; `302`→`3302` "Authentication failed. Password incorrect." is the distinct wrong-credential code; `307`→`3307` "Same character is already logged in"; `214`/`215`→`3214`/`3215` expansion-area not registered/installed. Name-already-in-use codes: `313`, `322`, `335`. |

   The error ack replaces the normal reply to any failing request: create, name check, select or delete.

**Character list slot after creation** (140 bytes, in `0x20`): `+0x00` content ID; `+0x04` `u16` character ID low; `+0x06` `u16` world ID (`0x0065` = Shiva, matching the world list); `+0x08` 4 bytes (`01 00 00 0A`); `+0x0C` `char[16]` name; `+0x1C` `char[16]` world name; `+0x2C` `CharInfo`. Empty slots carry a single space (`0x20`) in both name fields.

---

### 4.6 Map Server Initial Handshake & Datagram Framing

After lobby character selection completes (`0x07` $\rightarrow$ `0x0B`), the client initiates communication with the Map Server over UDP to `MapServerIPv4:MapServerPort`.

#### 4.6.1 First UDP Datagram (136 bytes, Unencrypted)
The first datagram is 136 bytes: a 28-byte transport header, one 92-byte packet `0x00A`, and a 16-byte trailer. It is **not** encrypted.

**Datagram layout**

| Offset | Size | Field | Description |
|---|---|---|---|
| `+0x00` | 28 | `TransportHeader` | `+0x00` `u16` LE = `1` (client sequence), `+0x02` `u16` = `0` (ack). Bytes `+0x04..+0x1B` are **memory residue that differs per login** (corrected 2.1.24; earlier versions printed capture #5's bytes as if they were constants): capture #5 has `C0 02 00 00`, `77 00 00 00`, 12 zero bytes, `24 E8 FE 76`; captures #9 and `231055` have `+0x04` = `0x1EF` / `0x1F2`, `+0x08` = `0x2A` / `0x12`, and the ASCII text fragment `\0turer coupon\0\0\0` at `+0x0C..+0x1B`. They are never repeated in later datagrams (§4.6.3). A clean-room client may send zeros; whether the server reads them is not known. |
| `+0x1C` | 92 | `Packet00A` | Handshake packet payload (see below). |
| `+0x78` | 16 | `TrailerMD5` | `MD5` of the 92 packet bytes `+0x1C..+0x77`. Verified. |

**Packet `0x00A` (offsets relative to datagram `+0x1C`)**

| Offset | Size | Type | Endianness | Field Name | Description |
|---|---|---|---|---|---|
| `+0x00` | 2 | `u16` | LE | `IdAndSize` | `0A 2E` = `0x2E0A`: low 9 bits are the packet ID (`0x00A`); high 7 bits (`0x17`) are size in 4-byte units ($23 \times 4 = 92$). |
| `+0x02` | 2 | `u16` | LE | `Sync` | `0x0001`. |
| `+0x04` | 1 | `u8` | — | `Checksum` | Low 8 bits of the sum of packet bytes `+0x08..+0x5B` (`0xBF`; full sum `0xBBF`). Verified. |
| `+0x05` | 7 | `u8[7]` | — | `Zero` | Zero. |
| `+0x0C` | 4 | `u8[4]` | Raw | `UniqueNo` | `5E 91 0A 00` — bytes `+0x20..+0x23` of lobby `0x0B`, unchanged. |
| `+0x10` | 18 | `u8[18]` | — | `Zero` | Zero. |
| `+0x22` | 15 | `char[15]` | ASCII | `Name` | A single space then zeros (`20 00 …`). The character name is **not** sent here. |
| `+0x31` | 15 | `char[15]` | ASCII | `Account` | Zero. |
| `+0x40` | 16 | `u8[16]` | Raw | `Ticket` | $\text{MD5}(\text{Account}_{15} \,\|\, \text{MapKey}_{16})$, where $\text{Account}_{15}$ is the 15-byte field at `+0x31` exactly as sent (all zeros) and MapKey is the §5.6 key **after the zero-byte preparation of §5.6 item 2** (every byte from the first `0x00` on set to `0x00`; length stays 16). Verified on all eight captured map logins (captures #5, #6 ×3, #9, and the hooked sessions `210031`, `220551`, `230908`, `231055`). In `231055` the key `35 43 6F 04 5F 99 26 08 C7 7D 00 2A 4C 88 6A 58` has `0x00` at index 10: the wire ticket matches MD5 over the truncated key `…C7 7D 00 00 00 00 00 00` and **not** over the raw key, and its datagrams decrypt only under the truncated key. Capture #5's ticket: `27 67 3E A2 22 A8 29 98 95 83 11 8D F9 DE 80 B3`. No capture shows how the server treats a wrong ticket. |
| `+0x50` | 4 | `u32` | LE | `Ver` | `0`. |
| `+0x54` | 4 | `char[4]` | ASCII | `Platform` | `"WIN\0"`. |
| `+0x58` | 2 | `u16` | LE | `Language` | Client region/language: `0x0000` = Japanese (JP), `0x0001` = English (US), `0x0002` = European (EU). Every capture is a US client and sends `0x0001`; the `0` and `2` values were reported by the implementation (2.1.25) and are not in any capture. |
| `+0x5A` | 2 | `u16` | LE | `Unknown5A` | `0xFFFF`. |

#### 4.6.2 Subsequent Datagrams (Encrypted Map Traffic)
Every datagram after the client's first, in both directions, has this layout (corrected 2.1.24: earlier versions left out the compression and the bit count). Verified on all 978 map datagrams of capture #5 and on captures #9 and `231055`: each decrypts, passes the MD5 check, and decompresses to a whole number of game packets.

| Part | Size | Content |
|---|---|---|
| `TransportHeader` | 28 | Plaintext, §4.6.3. |
| `Compressed` | `C` | `0x01`, then the game packets (each with its own `id`/`size`/`sync` header, concatenated) encoded with the prefix code below. |
| `BitCount` | 4 | `u32` LE: the number of bits in `Compressed` that carry data, **counting the 8 bits of the leading `0x01`**: `8 + Σ code lengths`. |
| `Trailer` | 16 | `MD5(Compressed ‖ BitCount)`. |

- **Prefix code.** Each payload byte is replaced by a variable-length code (a fixed table of 256 code/length pairs, one per byte value). Codes are packed **least-significant bit first**: the first code starts at bit 0 of the byte after the `0x01`, each code's bit 0 goes in first. The last byte's unused high bits are ignored: retail leaves leftover data there in about a third of datagrams (both directions); zeros are the natural choice. The table is the one LandSandBoat ships as `res/compress.dat` (code for signed byte `b` at `u32` index `b + 0x80`, its length at `b + 0x180`); it decodes every retail datagram in the captures above, and re-encoding the decoded packets reproduces every datagram's `Compressed` and `BitCount` apart from those unused bits (harness `tools/verify_map_datagrams.py`; Test Vector 19). Its location inside `FFXiMain.dll` was not traced.
- **Empty payload.** A datagram that carries no game packets (retail's acknowledgment-only datagram) has `Compressed` = `01`, `BitCount` = `8`: 49 bytes in all.
- **Encryption** (§5.6 item 4): ECB over $\lfloor (\text{len} - 28) / 8 \rfloor$ 8-byte blocks starting at byte 28, covering `Compressed`, `BitCount` and the trailer as far as whole blocks reach; the last `(len − 28) mod 8` bytes are sent unencrypted. Key: `SessionHash(Modifier used by 0x07 + 4)` (§5.6).
- **Packet `sync`.** Every game packet inside a client datagram carries, at its `+0x02`, the datagram's own sequence number (§4.6.3 `+0x00`). Server packets carry the server's datagram sequence number.

#### 4.6.3 Map Transport Header (28 bytes, plaintext, observed 2.1.24)
From captures #5, #9 and `231055` (about 1,180 datagrams). The first client datagram (§4.6.1) is the exception noted there.

| Offset | Size | Field | Client → server | Server → client |
|---|---|---|---|---|
| `+0x00` | 2 | `Sequence` (`u16` LE) | Own counter: `1` for the `0x00A` datagram, then `+1` per datagram sent. | Own counter, starting at `1`. |
| `+0x02` | 2 | `Ack` (`u16` LE) | The `Sequence` of the latest server datagram received. | The latest client `Sequence` received. |
| `+0x04` | 4 | `TimeMs` (`u32` LE) | Milliseconds part of the send time, `0..999` (identified 2.1.25). Never `1000` or above in any capture; `Time × 1000 + TimeMs` stays a near-constant offset from the capture clock in every capture (client: median offset +136 to +396 ms, spread about ±0.2 s; the client's clock is not the capture machine's wall clock). Not a checksum: it does not follow length, byte sums, the bit count or the trailer. | Same; the server's offset is steadier (spread about ±30 ms). |
| `+0x08` | 4 | `Time` (`u32` LE) | Unix time in seconds. | Same. |
| `+0x0C` | 16 | `State` | See below. | See below. |

**`State` (`+0x0C..+0x1B`).** In 91–99 % of datagrams each side copies these 16 bytes from the latest datagram it received from the other side, sometimes changing a byte. They change during zone entry and settle afterwards. The observed values, as four `u32` LE words:
- Server's first datagram: capture #5 `0x080338E0 0x0803549C 0x0803393B 0xFEE10000`; captures #9 and `231055` all zero.
- Client's reply (second datagram): word 0 = `0x01` in the low byte (capture #5 kept the server's upper three bytes: `0x08033801`), word 1 = `0`, word 2 = **the client's public IPv4 address as the server sees it, in network byte order**: the 4 IP bytes of the `RoutingToken` (§4.2.2), which carries them little-endian, reversed (identified 2.1.25: true in every client datagram from the second on, 1,357 datagrams in all eight captured map logins; values not published). Word 3 = copied from the server.
- Words 0, 1 and 3 are not identified, and whether the server checks any word is not known. A clean-room client should copy the server's last `State`, set word 0's low byte to `0x01`, and put its `RoutingToken` IPv4 in word 2 (network byte order).

#### 4.6.4 Retail's First Map Exchange (observed, captures #5 and `231055`)
Times from the client's `0x00A` datagram.

| Time | Direction | Seq / Ack | Game packets |
|---|---|---|---|
| `+0.00 s` | C → S | `1` / `0` | `0x00A` (plaintext datagram, §4.6.1) |
| `+0.31–0.38 s` | S → C | `1` / `0` | `0x04F`, `0x051`, 7 × `0x01C`, `0x00A` (the zone-in packet, 260 bytes) |
| `+0.03 s` later | C → S | `2` / `1` | **`0x011` only**: `11 04 02 00 02 00 00 00` (size 8, `sync` 2, then `02 00 00 00`) |
| `+0.17–0.20 s` later | S → C | `2` / `2` | 4 × `0x050` |
| `+2.1 s` later | C → S | `3` / `2` | `0x00C` (GameOK, `0C 06 03 00` + 8 zero bytes) with `0x061`, `0x118`, `0x11B`, `0x053`, `0x0E0`, `0x00F`, `0x0DB`, `0x112`, `0x05A` (`231055` also sends `0x04B`); about 2 s of local zone loading precede it |
| `+0.17–0.20 s` later | S → C | `3` / `3` | Includes **`0x008`**, `0x0B4`, `0x01C`, `0x051`, … |
| then | both | | The client sends roughly one datagram per server datagram; with nothing to send it sends the 49-byte empty datagram (§4.6.2) to acknowledge. |

So retail answers the server's first datagram within ~35 ms with `0x011`, not `0x00C`. `0x00C` comes about 2 s later, in a batch, and the server's `0x008` arrives only after that batch. A server that keeps resending its first datagram has not accepted any client datagram: check the §4.6.2 framing (leading `0x01`, `BitCount`, MD5 over `Compressed ‖ BitCount`), the key (§5.6 item 2), `Ack` = the server's `Sequence`, and each packet's `sync` = the datagram's `Sequence`.

**No capture shows a rejected datagram.** In all eight captured map logins (1,357 client and 1,329 server datagrams) no sequence number is skipped or repeated and the server never resends a datagram, so the captures cannot show which check a server applies or how it reacts when one fails.

---

## 5. Cryptography

### 5.1 PlayOnline 256-Bit Ephemeral RSA & Blowfish-64 OFB Cipher (`:51241`–`:51247` and `:51220`)
Both the Core IRC session (`:51241`–`:51247`) and all Presence/Key server (`:51220`) connections in a login session share a single **Blowfish-64 Output Feedback (OFB)** cipher instance initialized from the Stage 3 Ephemeral RSA key exchange:

1. **256-Bit Ephemeral RSA Representation & Parameters**:
   - **Byte orders on the wire**: the client's modulus $N$ is sent **little-endian**; the server's ciphertext $C$ comes back **big-endian** (§4.2.2).
   - **Public Exponent**: Fixed at $e = 65535$ (`0xFFFF`). Verified: with each capture's factored $N$, $e = 65535$ decrypts both captures to valid PKCS#1 v1.5 blocks and gives capture #3's independently captured $K_8$. $e = 65537$ gives random noise.
   - The client generates an ephemeral 256-bit RSA keypair $(N, d)$: $N = p \times q$ with 128-bit primes, $\gcd(e, (p-1)(q-1)) = 1$, $d = e^{-1} \bmod (p-1)(q-1)$.
   - The server returns $C = M^e \bmod N$ where $M$ is a PKCS#1 v1.5 type-2 block: `00 02` ‖ 21 non-zero random bytes ‖ `00` ‖ 8-byte key.
   - The client computes $M = C^d \bmod N$, checks the padding, and sets $K_8$ = the last 8 bytes of the big-endian $M$, reversed. Equivalently, $K_8$ is the low 8 bytes of $M$ in little-endian order. `key_lo = u32_le(K8[0..3])`, `key_hi = u32_le(K8[4..7])`.
   - Capture #3: $K_8$ = `31 1D D3 74 45 71 6B 05` (`key_lo = 0x74D31D31`, `key_hi = 0x056B7145`).

2. **Non-Standard Blowfish Key Setup (from $K_8$)**:
   The session cipher builds its P-array and S-boxes from a 4096-byte buffer `buf` derived from $K_8$. The standard fractional-$\pi$ Blowfish tables are **not** used. The round function and block encryption are standard 16-round Blowfish (`BlowfishEncrypt` below, using the current P and S).
   - **Step 1: Buffer expansion (`buf[0..4095]`)**:
     1. `buf[0..7] = K8[0..7]`; set `pos = 8`.
     2. While `pos < 4096`: compute a 16-byte digest over **the entire filled prefix** `buf[0 .. pos-1]`, copy `min(16, 4096 - pos)` digest bytes to `buf[pos..]`, and advance `pos` by that count. (The last step copies 8 bytes.)
     3. The **first** digest (over the 8 key bytes) is standard RFC 1321 MD5. **Every later digest** is MD5 with one change: the chaining state $(A, B, C, D)$ starts at $(0, 0, 0, 0)$ instead of the RFC 1321 constants. Padding, the length field and the compression function are unchanged.
   - **Step 2: P-array load (1-byte overlapping stride, big-endian)**:
     $$P[i] = \text{u32\_be}(\text{buf}[i .. i+3]), \quad i = 0..17$$
   - **Step 3: S-box load (4-byte stride from offset 0, big-endian)**:
     $$S[b, j] = \text{u32\_be}(\text{buf}[4(256b + j) .. 4(256b + j) + 3]), \quad b = 0..3,\; j = 0..255$$
     The S-boxes use all 4096 bytes, so they overlap the bytes used in steps 2 and 4.
   - **Step 4: P XOR pass**:
     $$P[i] \gets P[i] \oplus \text{u32\_be}(\text{buf}[4i .. 4i+3]), \quad i = 0..17$$
   - **Step 5: Substitution passes (input is always the zero block, not chained)**:
     $$\text{For } i = 0..8: \quad (L, R) \gets \text{BlowfishEncrypt}(0, 0); \quad P[2i] \gets L,\; P[2i+1] \gets R$$
     $$\text{For } k = 0..511: \quad (L, R) \gets \text{BlowfishEncrypt}(0, 0); \quad S_{\text{flat}}[2k] \gets L,\; S_{\text{flat}}[2k+1] \gets R$$
     where $S_{\text{flat}}[n] = S[\lfloor n/256 \rfloor, n \bmod 256]$. Each call uses the tables as updated so far. Unlike standard Blowfish, the previous output is **not** fed back as the next input. Test Vector 6 checks steps 1–5.

3. **64-Bit Output Feedback (OFB) Mode & Initial IV**:
   - **Initial 8-byte IV**: The first 8 bytes of the client RSA public modulus $N$: `N[0..7]` in little-endian byte order (`iv_lo = u32_le(N[0..3])`, `iv_hi = u32_le(N[4..7])`). Capture #3: `95 C6 21 6A 8D E5 13 84`. The identical IV is reused on all `:51220` frames.
   - When `reset_iv != 0`, set `(cur_lo, cur_hi) = (iv_lo, iv_hi)` and `byte_offset = 0`.
   - For each byte $i$:
     1. When `byte_offset & 7 == 0`, advance the 64-bit feedback register using one 16-round Blowfish block encryption: `(cur_lo, cur_hi) = BlowfishEncrypt(cur_lo, cur_hi)`.
     2. Extract keystream byte `ks = ((byte_offset & 4) ? cur_hi : cur_lo) >> (8 * (byte_offset & 3)) & 0xFF` and increment `byte_offset`.
     3. Compute `out_byte = in_byte ^ ks`.
     4. **CRLF Preservation Rule**: If `in_byte == 0x0A` (`\n`), `in_byte == 0x0D` (`\r`), `out_byte == 0x0A`, or `out_byte == 0x0D`, write `in_byte` unchanged (`out_byte = in_byte`); otherwise write `out_byte`.
     
     > **Universal Scope of CRLF Preservation**: This preservation logic is active on **every** call to the session Blowfish cipher instance, including both ASCII IRC lines on `:51241`–`:51247` and binary request/response frames on `:51220`.

### 5.2 PlayOnline Custom 6-Bit Base64 Alphabet
Core IRC token encoding (`USER` RSA token, server ciphertext token, `NICK` client metadata body) uses a custom 64-character Base64 alphabet:

```text
TSG8IncW3HFKokOg79qzeCmZs2yBYEQVAUxR5rbwi4P@jMDLtpvad0f_J1hlN6uX
```

| Index Range | Characters | Description |
|---|---|---|
| `0..63` | `TSG8IncW3HFKokOg79qzeCmZs2yBYEQVAUxR5rbwi4P@jMDLtpvad0f_J1hlN6uX` | 64-entry ASCII character lookup table. |

**Encoding Procedure**:
1. Binary bytes are processed in 3-byte blocks ($24\text{ bits}$).
2. Each 3-byte block `[b0, b1, b2]` is packed big-endian into a 24-bit integer: `val = (b0 << 16) | (b1 << 8) | b2`.
3. Four 6-bit indices are extracted MSB-first:
   - $c_0 = \text{table}[(val \gg 18) \ \& \ 0\text{x}3\text{F}]$
   - $c_1 = \text{table}[(val \gg 12) \ \& \ 0\text{x}3\text{F}]$
   - $c_2 = \text{table}[(val \gg 6) \ \& \ 0\text{x}3\text{F}]$
   - $c_3 = \text{table}[val \ \& \ 0\text{x}3\text{F}]$
4. If the input length $L$ is not a multiple of 3, the final block is padded with zero bits and emits $\lceil (L \times 8) / 6 \rceil$ characters.

**Token Length Derivations**:
- **Stage 3 `USER` RSA Public Modulus Token** and the server ciphertext token: 32 input bytes → **43 characters** (the last one carries 4 data bits and 2 zero bits), followed by a 4-character `LineChecksum` suffix (§5.10), 47 characters in all. (Test Vector 9.)
- **Stage 3 `NICK` ClientMeta Token**: 24 input bytes. $24 \times 8 = 192\text{ bits} \rightarrow 192 / 6 = \mathbf{32}\text{ characters}$. A 4-character suffix (Section 4.2.2, §1.1 O4) makes 36 characters total.
- **Stage 2 Gatekeeper Redirect Token**: 44 characters (40 characters of permuted 5-bit Base32 encoding 25 bytes, followed by a 4-character checksum suffix; §4.2.1, verified).

### 5.3 PlayOnline 32-Bit Additive Checksum (`:51220` Payloads)
Every `:51220` payload ends with a 4-byte little-endian checksum over the preceding $N - 4$ plaintext bytes (`len = N - 4`):
1. Sum all 32-bit little-endian words `u32_le` in `buf[0 .. (len & ~3) - 1]` into a 32-bit accumulator `acc`.
2. If `len & 3` is non-zero, fold the remaining $1..3$ bytes into a zero-padded little-endian word and add to `acc`.
3. Write `LE32(acc)` into the final 4 bytes (`buf[N - 4 .. N - 1]`).

### 5.4 Square Enix Account Password Time-Bound SHA-1 Digest (`:51220` Op `0x04/0x07`)
In `:51220` Category `0x04` / Command `0x07` (64-byte payload), bytes `+0x20..+0x33` contain a 20-byte raw SHA-1 digest computed as follows (corrected 2.1.21; §1.1 O17). All SHA-1 is standard FIPS 180 SHA-1; all hex is lowercase (`"%02x"` per byte).
1. Take the password bytes $P$: one byte per typed character, its low 8 bits. For an ASCII password this is the password exactly as typed: no case change, no trimming, no terminator.
2. Compute the 20-byte **password key**:
   $$H_1 = \text{SHA-1}\!\left(\text{LowercaseHex}_{40}(\text{SHA-1}(P)) \;\|\; \texttt{"playonline"}\right)$$
   `"playonline"` is 10 ASCII bytes with no terminator (`CONST_SE_PASS_SALT`). The Viewer computes $H_1$ once, when the user submits the login form, and hands only $H_1$ to the protocol layer. **Earlier versions used $H_1 = \text{SHA-1}(P)$; that is wrong, and the server answers it with `Status = 0x6F`.**
3. Compute the current server time $t_{\text{server}} = \text{ServerTimestamp} + \Delta$, where `ServerTimestamp` is bytes `0..3` (BE) of the `:51240` Gatekeeper Redirect Token (§4.2.1) and $\Delta$ is the whole seconds elapsed on a local monotonic clock since that token was received. **This is the normative clean-room rule.** It does not depend on the user's wall clock being correct. (Whether retail uses this anchor or its local `time(NULL)` is not verified; in every capture the two agreed to within about a second, so both produce the same minute except within a second of a minute boundary.)
4. Round $t_{\text{server}}$ down to the minute and format it as a decimal ASCII string:
   $$T_{\text{str}} = \text{sprintf}\!\left(\text{"\%d"},\; \lfloor t_{\text{server}} / 60 \rfloor \times 60\right)$$
5. Concatenate the 40-character lowercase hex of $H_1$ and $T_{\text{str}}$ (50 ASCII bytes for 10-digit Unix timestamps) and hash:
   $$\text{PasswordTimeDigest}_{20} = \text{SHA-1}\!\left(\text{LowercaseHex}_{40}(H_1) \;\|\; T_{\text{str}}\right)$$

**Verification.** Steps 3–5: captures #2 (`AuthMode = 1`) and #3 (`AuthMode = 2`) log the 20-byte $H_1$ the Viewer passed in and the 64-byte payload built from it; steps 4–5 with the capture's own minute reproduce both `PasswordTimeDigest` values (`tools/verify_se_auth.py`). Steps 1–2: taken from the Viewer's own password hashing. No capture checks step 2 against a known password (the research `.env` holds a different account's credentials); the 2026-11-11 live `0x6F` with step 2 missing is consistent with it. Test Vectors 3 and 17.

### 5.5 FFXI Lobby Packet MD5 & `SessionHash` Salting (`:54001`)
1. **Lobby Packet Integrity Hash (`PacketMD5` at `+0x0C..+0x1B`)**:
   - Set bytes `+0x0C..+0x1B` of the `IXFF` packet to `0x00`.
   - Compute `MD5(packet[0 .. PacketSize - 1])` (RFC 1321) and write the 16-byte digest into `+0x0C..+0x1B`.
2. **Lobby `SessionHash`** (any client lobby request that carries one):
   - Given the 16-byte `LobbySessionKey` (from `:51220` Op `0x04/0x05`) and the 32-bit `ModifierSeed` $M_0$ (from `Opcode 0x05` offset `+0x1C`):
   - **Modifier schedule (verified in capture #5):** each client request that carries a `SessionHash` uses `Modifier = M_0 + 1 + 2n`, where `n` is the count of such requests already sent on the connection (first = `M_0 + 1`, second = `M_0 + 3`, …). The modifier advances by `+2` per hashed request **in send order**, regardless of opcode.
     - With no character creation in between, that makes `0x1F` → `M_0 + 1`, `0x24` → `M_0 + 3`, `0x07` → `M_0 + 5`.
     - In capture #5 (name-check `0x22`, create `0x21`, and a refresh `0x1F` preceded select), the order was `0x1F` +1, `0x24` +3, `0x22` +5, `0x21` +7, `0x1F` +9, `0x07` +11.
   - Form the 20-byte buffer `B = LobbySessionKey[0..15] || LE32(Modifier)` and compute `SessionHash = MD5(B)`. All packet MD5s and session hashes in capture #5 verify under this rule.

### 5.6 Final Fantasy XI Map Server Cipher

**Verified (§1.1 O6):** under this scheme, all 488 encrypted client datagrams and all 489 server datagrams in capture #5 decrypt and pass the MD5 trailer check (item 4). The scheme is the same one the LandSandBoat map server uses with the legacy client: both run the same `FFXiMain.dll`.

1. **Key**:
   $$\text{MapKey}_{16} = \text{MD5}(\text{LobbySessionKey}_{16} \;\|\; \text{LE32}(\text{Modifier}_{0x07} + 4))$$
   - $\text{Modifier}_{0x07}$ is the §5.5 modifier the client used for its Opcode `0x07` `SessionHash`. The lobby counter steps once per hashed request and once per reply. The `+4` covers the `0x07` request, the `0x0B` reply, and two steps at map connect.
   - Capture #5: `0x07` used $M_0 + 11$, so $\text{MapKey} = \text{SessionHash}(M_0 + 15)$ = `91 91 29 14 5B DE 35 AF C2 D0 2C EC 08 A5 32 7B`. Verified by decryption. Runtime instrumentation of the client's MD5 calls in that capture also shows it computing exactly $M_0 + 1, 3, 5, 7, 9, 11$ and then $M_0 + 15$.
   - A login with no character management sends `0x07` with $M_0 + 5$, giving map key $M_0 + 9$. Verified in capture #6 (both logins): the client's key hook computes $M_0 + 9$ and its map datagrams decrypt under it.
   - **Zone changes**: after the server sends map packet `0x00B`, both sides add 2 to the counter and derive a new key the same way. Verified in capture #6's zoning login: the key advances $M_0 + 9 \rightarrow M_0 + 11$, and post-zone datagrams decrypt only under $M_0 + 11$.
2. **Key preparation**: if the 16-byte key contains a `0x00` byte, set that byte and every later byte to `0x00`. The key length stays 16. **Verified (2.1.24)** in capture `231055`, whose map key has `0x00` at index 10: its datagrams decrypt under the truncated key and not under the raw key, and the `0x00A` ticket (§4.6.1) is computed over the truncated key too.
3. **Cipher**: a Blowfish variant. It differs from Schneier's Blowfish in the round function and the key loading:
   - **Initial tables**: the standard fractional-$\pi$ P-array (18 words) and S-boxes (4 × 256 words).
   - **Round function** for a 32-bit input $x$ with bytes $a = x \gg 24$, $b = (x \gg 16) \,\&\, \text{0xFF}$, $c = (x \gg 8) \,\&\, \text{0xFF}$, $d = x \,\&\, \text{0xFF}$, all sums mod $2^{32}$:
     $$F(x) = \big((S_1[c] \,\&\, 1) \oplus 32\big) + \big((S_3[a] \,\&\, 1) \oplus 32\big) + S_2[b] + S_0[d]$$
   - **Key loading**: for $i = 0..17$, build a 32-bit word from the next 4 key bytes, cycling through the 16 bytes, with `w = (w << 8) | sext32(byte)`. Each byte is **sign-extended** to 32 bits before the OR, so a byte ≥ `0x80` sets all higher bits of `w`. Then set $P[i] \mathrel{\oplus}= w$.
   - **Key expansion**: standard, chained. Start from $(L, R) = (0, 0)$, encrypt, and write each output into $P[0..17]$ and then $S_0..S_3$ in pairs, feeding each output into the next encryption.
   - **Block encryption**: the standard 16-round Feistel structure and final swap with $P[16]$/$P[17]$, using the $F$ above. Decryption uses the P-array in reverse order.
4. **Datagram framing**:
   - The 28-byte transport header is plaintext.
   - The bytes after the header are the compressed payload and its `BitCount` (§4.6.2). Before encryption, the sender appends a 16-byte trailer: MD5 of the plaintext bytes `28 .. len-17`, which are exactly `Compressed ‖ BitCount`.
   - Encryption is ECB over $\lfloor (\text{len} - 28) / 8 \rfloor$ 8-byte blocks starting at byte 28, trailer included. Each block is two `u32_le` words $(L, R)$. Any `(len - 28) mod 8` tail bytes stay plaintext.
   - **Validity check**: after decryption, recompute the trailer MD5. A mismatch means the wrong key, for example after a zone change.

### 5.7 PlayOnline PML Web Digest Authentication (`x-MD5-pol`)
**Verified (§1.1 O14)**: reproduces all 142 wire `Authorization` responses in captures #1, #3 and #9 (48 + 48 + 46).

PML requests on `wh000.pol.com:51304` use an HTTP Digest variant. It is **not** RFC 2617: there is no `cnonce`, `nc` or `qop`, and the nonce comes last in the response preimage. The key is the 8-byte Core IRC session key $K_8$ from the RSA handshake (§5.1), byte order as in Test Vector 6. All MD5 values are written as 32 lowercase hex characters. `B64` is the POL 6-bit Base64 (§5.2): MSB-first 6-bit groups, the last group zero-padded, no `=` padding.
1. **`userName`**:
   $$\text{userName} = \text{B64}\!\left(\text{ASCII}(\text{"}a.b.c.d\text{:}port\text{"})\right)$$
   where `a.b.c.d:port` is the client's server-observed address from the `RoutingToken` (§4.2.2), in decimal with no leading zeros.
2. **Password**:
   $$\text{Password} = \text{B64}(K_8)$$
   (11 characters).
3. **$HA1$**:
   $$HA1 = \text{MD5}\!\left(\text{userName} \;\|\; \text{":"} \;\|\; \text{Realm} \;\|\; \text{":"} \;\|\; \text{Password}\right)$$
   with `Realm` taken from `WWW-Authenticate` (`POL`).
4. **$HA2$**:
   $$HA2 = \text{MD5}\!\left(\text{"GET:"} \;\|\; \text{RequestURI}\right)$$
   `RequestURI` is exactly the `uri` sent, including any query string.
5. **Response**:
   $$\text{Response} = \text{MD5}\!\left(HA1 \;\|\; \text{":"} \;\|\; HA2 \;\|\; \text{":"} \;\|\; \text{Nonce}\right)$$

### 5.8 Cryptographic Test Vectors (Worked Examples)
The following throwaway test vectors allow verifying every cryptographic primitive in unit tests without live account access:

#### Test Vector 1: POL ID Base36 Encoding & Reversal (§4.2.2, §1.1 O12)
- **Base36 Alphabet**: `EFKAOYMJVNGTDSWBQLPCIRHZXU6328401795` (Appendix §1.2 `CONST_BASE36_ALPHABET_POL_ID`)
*(Redacted and replaced by GordianXI on 2026-11-10: the published vector used the capture account's real POL ID. The values below are a synthetic POL ID run through the same algorithm; the GordianXI implementation reproduces the redacted capture vector as well.)*
- **Input `POL_ID` (8 characters)**: `"ABCD1234"`
- **Decoded 41-bit Integer**: `0x3E9C91C1AA`
- **After Bits 46..47 Set**: `0xC03E9C91C1AA`
- **After 5-Round Bit Diffusion**:
  - $a = 4$: $val \oplus= (val \gg 8) \ \& \ \text{0xFF00000000} \rightarrow \text{0xC0FE9C91C1AA}$
  - $a = 3$: $val \oplus= (val \gg 8) \ \& \ \text{0x00FF000000} \rightarrow \text{0xC0FE6291C1AA}$
  - $a = 2$: $val \oplus= (val \gg 8) \ \& \ \text{0x0000FF0000} \rightarrow \text{0xC0FE62F3C1AA}$
  - $a = 1$: $val \oplus= (val \gg 8) \ \& \ \text{0x000000FF00} \rightarrow \text{0xC0FE62F332AA}$
  - $a = 0$: $val \oplus= (val \gg 8) \ \& \ \text{0x00000000FF} \rightarrow \text{0xC0FE62F33298}$
- **After Bits 46..47 Cleared**: `0x00FE62F33298`
- **Re-Encoded 8 Base36 Digits**: `"S77G53VV"`
- **Final Output with `'U'` Prefix (9 characters)**: `"US77G53VV"`
- **Round-Trip Decoding**: Decode `"S77G53VV"`, set bits 46..47, invert the rounds by applying the same XOR for $a = 0, 1, 2, 3, 4$ (reverse order), clear bits 46..47, encode back $\rightarrow$ `"ABCD1234"`.

*(Every value above was recomputed independently from the §4.2.2 algorithm.)*

#### Test Vector 2: `NICK` `PassResponse` (§4.2.2)
- **Greeting token body (40 characters, as ASCII)**: `"WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1N"` (the synthetic capture #5 token body from Test Vector 12)
- **POL password**: `"password"`
- **`PassResponse`** = `md5hex(body ‖ password)` = `"fcd51ab80c161c6c3636c9d2bf5f2bc7"`

#### Test Vector 3: Square Enix Password Time-Bound SHA-1 (corrected 2.1.21)
- **Input `SE_PASS`**: `"SecretPass123"`
- **`SHA-1(P)` hex**: `"aa15b298cd3043832456a42fb4f9910a8c96636b"`
- **Password key $H_1$** = `SHA-1("aa15b298…636b" ‖ "playonline")`: `2c47dceb3d437c6d414dfff87765f1b1fa72f880`
- **Timestamp String ($T_{\text{str}}$)**: `"1731110400"` ($1731110400 \bmod 60 = 0$)
- **Output 20-byte `PasswordTimeDigest`** = `SHA-1("2c47dceb…f880" ‖ "1731110400")`: `9bce4cfc21dbbaa94b003314cffebd562a4a60d5`
- (Versions before 2.1.21 gave `2e6a0284…bc56`, computed with $H_1 = \text{SHA-1}(P)$. Wrong.)

#### Test Vector 4: PlayOnline 32-Bit Additive Checksum
- **Input 36-byte Payload**: `01 02 03 04 05 06 ... 24`
- **Output 32-bit Checksum**: `0xB4ABA299` (`99 A2 AB B4` on wire, little-endian)

#### Test Vector 5: FFXI Lobby `SessionHash`
- **LobbySessionKey (16 bytes)**: `01 23 45 67 89 AB CD EF FE DC BA 98 76 54 32 10`
- **Modifier $M_0 + 1$ (`Opcode 0x1F` Request Character List)**: `0x590DF1FB` (`FB F1 0D 59` on wire, little-endian)
  - **Output `SessionHash` (16 bytes, Hex)**: `"a9b37731814689069446b8581bb145ea"`
- **Modifier $M_0 + 5$ (`Opcode 0x07` Character Select)**: `0x590DF1FF` (`FF F1 0D 59` on wire, little-endian)
  - **Output `SessionHash` (16 bytes, Hex)**: `"906fc2f1cc7f88ae1c06c215be8eb208"`
- **Modifier $M_0 + 15$ (map key when `0x07` used $M_0 + 11$, §5.6)**: `0x590DF209` (`09 F2 0D 59` on wire, little-endian)
  - **Output `SessionHash` (16 bytes, Hex)**: `"67d581eb124a444564343b0c9b4ff8d4"`

#### Test Vector 6: Blowfish-OFB Session Key Expansion (Verified against Capture #3)
- **Input 8-Byte RSA Session Key ($K_8$)**: `31 1D D3 74 45 71 6B 05` (`key_lo = 0x74D31D31`, `key_hi = 0x056B7145`)
- **Initial IV ($N[0..7]$)**: `95 C6 21 6A 8D E5 13 84` (`iv_lo = 0x6A21C695`, `iv_hi = 0x8413E58D`)
- **Post-Substitution P-Array ($P[0..3]$)**:
  `P[0] = 0x4314243A`, `P[1] = 0xAF81F8CD`, `P[2] = 0x73814FE5`, `P[3] = 0xA98502F8`
- **Post-Substitution S-Box ($S[0, 0..3]$)**:
  `S[0,0] = 0x561D0A90`, `S[0,1] = 0x08D37945`, `S[0,2] = 0x561D0A90`, `S[0,3] = 0x08D37945`
- **First 16 Keystream Bytes Generated**:
  `16 35 58 2F 2C 3E 21 03 2E 4E AE A5 78 A3 EF 84`
- **CRLF Preservation Rule**: Any input byte equal to `0x0D` (`\r`) or `0x0A` (`\n`), or any keystream XOR output equal to `0x0D` or `0x0A`, is emitted unmodified as `in_byte`.

#### Test Vector 7: PML `x-MD5-pol` HTTP Digest
Synthetic inputs (§5.7; the algorithm is verified against 142 real requests in captures #1, #3 and #9, harness `verify` O14):
- **Client address**: `"192.0.2.10:50000"` → **userName**: `"oz5vKRTDoxJpo8i0o8TtoT"`
- **$K_8$**: `11 22 33 44 55 66 77 88` → **Password**: `"Iq3a9nCbEJA"`
- **Realm**: `"POL"`, **URI**: `"/pml/main/index.pml"`
- **Nonce**: `"AAAAAA==0123456789abcdef0123456789abcdef01234567"`
- **HA1**: `"900b911b51ba972fd3cfe9c4731f3f31"`
- **HA2**: `"7a2b7979735964a76829475cf229c83e"`
- **Response**: `"346a5b8e4e59be3d3142f8ac592340af"`

#### Test Vector 8: Map Packet `0x00A` Ticket (capture #5)
- **MapKey**: `91 91 29 14 5B DE 35 AF C2 D0 2C EC 08 A5 32 7B` (`SessionHash(M0 + 15)`, §5.6)
- **Account field**: 15 × `00`
- **Ticket** = MD5(15 × `00` ‖ MapKey) = `27 67 3E A2 22 A8 29 98 95 83 11 8D F9 DE 80 B3`

#### Test Vector 9: Stage 3 `USER` Line (Body + `LineChecksum` Suffix, corrected 2.1.20)
- **Input**: Client RSA Public Modulus $N$, 32 bytes little-endian (capture #3): `95 C6 21 6A 8D E5 13 84 AA DB DF 6A A1 16 9B 1A 10 69 80 FE 43 FC EF 2C D8 E9 4D 58 75 EB EC 48`.
- **Body (43 characters, §5.2, last 2 bits zero)**: `rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIA`
- **Checksum input**: the 55 bytes `"USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIA"`; 13 full words plus the 3-byte tail `lIA` as `u32_le(00 6C 49 41)`; `sum = 0x33629167`; slices `0x0C 0x36 0x0A 0x11` → suffix `KuIP` (§5.10).
- **Wire Command Line**: `"USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP\r\n"` (61 bytes total), equal to capture #3. The harness `tools/verify_line_checksums.py` rebuilds it. Earlier versions gave a 36-byte input with "trailing bytes" `0B F8 4A 80`; those are just the suffix characters run through the Base64 decoder.

#### Test Vector 10: Stage 3 Ephemeral RSA Handshake & Derived Blowfish Session Key $K_8$
##### 10.1 Capture #1 Stage 3 Handshake (Alphabet-Clean Wire Tokens)
- **Line 1 (Server $\rightarrow$ Client)**: `:pol-1044-51244.pol.com 300 * WCZSXHSNNN4NI55OBMG0ECZNNNNNNNNNQWHN3N1NfAEq\r\n` (44 Base32 characters; client-address bytes synthetic, see §4.2.2)
- **Line 2 (Client $\rightarrow$ Server)**: `USER x 8 * :8yu_X78A6fDPuyYfeZAIFVxxuhY0A7AlNnfogoe9Pd5UDWB\r\n` (47 Base64 characters)
  - **Decoded Client RSA Modulus $N$ (32 bytes, little-endian)**:
    `0D AF B7 FD 00 E0 F7 6B AA F9 A7 36 51 78 04 29 F8 A2 FB A7 35 81 08 3B F0 5D 8C 3C C5 11 AB 49`
- **Line 3 (Server $\rightarrow$ Client)**: `:pol-1044-51244.pol.com 300 * OVfQuRJX05g@gOVwNJfiQTFGD6qHOLzU5BhYusDSWFYCiQM\r\n` (47 Base64 characters)
  - **Decoded Server Ciphertext $C$ (32 bytes, big-endian)**:
    `39 FD 9E FA 3E 3F D6 43 EB 3C E7 E7 F3 8D A8 78 02 82 BB D4 89 3A F4 E1 91 BE 9C F9 8B 81 1C A7`
  - **Factors of $N$** (decimal): $p$ = `176532316352809151093929119389468516639`, $q$ = `188753493582645005437843730230043414099`
  - **Decrypted $M_{\text{BE}}$** ($e = 65535$): `00 02 1E 28 74 C0 99 7C F4 88 65 DD 1E D0 2E 83 17 20 43 F4 D2 98 E9 00 33 C7 52 C5 0E 51 73 6A`
  - **$K_8$**: `6A 73 51 0E C5 52 C7 33`

##### 10.2 Capture #3 End-to-End Cryptography Vector (Handshake $\rightarrow$ Blowfish OFB Keystream $\rightarrow$ Encrypted NICK)
- **Line 1 (Server $\rightarrow$ Client)**: `:202.67.54.126 300 * WCZ5HGSNNN4NI55OBMK0E2NNNNNNNNNNMEJN3N1NwhBK\r\n` (44 Base32 characters; client-address bytes synthetic, see §4.2.2)
- **Line 2 (Client $\rightarrow$ Server)**: `USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP\r\n` (47 Base64 characters)
  - **Client RSA Public Modulus $N$ (32 bytes, little-endian)**:
    `95 C6 21 6A 8D E5 13 84 AA DB DF 6A A1 16 9B 1A 10 69 80 FE 43 FC EF 2C D8 E9 4D 58 75 EB EC 48`
- **Line 3 (Server $\rightarrow$ Client)**: `:202.67.54.126 300 * OlyyM2LoViSrkkvTTuyCUsDQPvhUHOKSbQImR@QqeX3n]E|\r\n`
  - *Note on wire trailer*: Decode the first 43 characters into the 32 bytes of $C$ (§4.2.2 step 3). The last four characters `n]E|` are the `LineChecksum` suffix (§5.10), not ciphertext.
  - **Decoded Server Ciphertext $C$ (first 32 bytes, big-endian)**:
    `3B B6 9A B5 9B CC 7E 80 65 34 DC 80 03 E6 95 85 8B 9E AB 2E A1 24 E2 C1 99 E1 16 8E B7 92 53 F2`
- **Factors of $N$** (decimal): $p$ = `174407169407696451849510974594705576147`, $q$ = `189127122418810726457008666384871172599`
- **Decrypted $M_{\text{BE}}$** ($e = 65535$): `00 02 EA 29 63 29 10 80 77 72 8B B9 CB 66 DC BF 47 81 47 8F C5 BB AC 00 05 6B 71 45 74 D3 1D 31`
- **Negotiated 64-Bit Blowfish Session Key $K_8$** (last 8 bytes of $M_{\text{BE}}$, reversed; matches the key captured at cipher initialisation):
  `31 1D D3 74 45 71 6B 05`
  - `key_lo = 0x74D31D31` (`31 1D D3 74` little-endian)
  - `key_hi = 0x056B7145` (`45 71 6B 05` little-endian)
- **Verified OFB Initial IV ($N[0..7]$)**: `95 C6 21 6A 8D E5 13 84`
- **First 16 Bytes of OFB Keystream** (verified in harness and against live capture):
  `16 35 58 2F 2C 3E 21 03 2E 4E AE A5 78 A3 EF 84`
- **Plaintext NICK Command (86 bytes)**: `"NICK <Encoded_POL_ID>:<PassResponse>:<ClientMeta body><suffix>\r\n"`
- **Encrypted NICK Command on Wire (86 bytes)**: *(Redacted by GordianXI on 2026-11-10, as in v1.7.0: the plaintext held the capture account's encoded POL ID and POL password MD5, and the wire bytes with the keystream above would reveal them. The GordianXI implementation reproduces the redacted wire bytes from the redacted plaintext byte for byte.)*

#### Test Vector 11: `SelectionMD5` Preimage (Opcode `0x07` `+0x48`)
Synthetic inputs (the real `AuthCode` is a per-login token; see §4.5.8 for the field sources):
- **AuthCode (64 bytes)**: `00 01 02 … 3F` (bytes `0x00`–`0x3F` in order)
- **SessionHash (16 bytes)**: `01 23 45 67 89 AB CD EF 01 23 45 67 89 AB CD EF`
- **DllTableIndex (`u32_le`)**: `0x00000003`
- **ClientChecksum (`u32_le`)**: `0x43FA0718`
- **FFXIContentID (`u32_le`)**: `0x016B6984`
- **Preimage**: the 92 bytes above concatenated in order.
- **Output `SelectionMD5`**: `32 79 22 48 BE C9 91 75 0B 20 50 FA BA 18 57 07`

*(Capture #6 verification, real values not reproduced here: `MD5(preimage)` = `FE E8 C3 FA 52 B1 24 78 AE 6B A1 1E D6 94 40 46`, matching the wire `0x07` `+0x48` and the client's own MD5-final hook.)*

#### Test Vector 12: Base32 Gatekeeper Redirect Token Decode & Port Extraction (§4.2.1, §1.1 O11)

*(Synthetic: the captured tokens are reproduced with bytes `+0x04..+0x07` (`ClientPublicIPv4`) replaced by `C0 00 02 0A` = `192.0.2.10` and the body re-encoded, so the real client address is not published. All other bytes are as captured. The 4-character suffixes are the captured ones and were not recomputed, since the suffix's input range is unknown (§4.2.1).)*

- **Input Line (Capture #5)**: `":202.67.54.125 300 * WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA\r\n"`
- **Token Body (40 chars)**: `"WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1N"`
- **Suffix (4 chars)**: `"vfrA"`
- **Decoded 25-byte Payload (Hex)**:
  `6A F2 51 17 C0 00 02 0A CA 43 36 7D C8 29 00 00 00 00 00 00 4B 27 01 01 00`
- **Extracted Fields**:
  - `ServerTimestamp` (`+0x00..+0x03`, BE): `0x6AF25117` ($1794265367$ Unix seconds)
  - `ClientPublicIPv4` (`+0x04..+0x07`, BE): `192.0.2.10` (`C0 00 02 0A`, synthetic)
  - `RedirectIPv4` (`+0x08..+0x0B`): `202.67.54.125` (`CA 43 36 7D`)
  - `RedirectPort` (`+0x0C..+0x0D`, BE): `51241` (`0xC829`)
- **Input Line (Capture #6)**: `":202.67.54.133 300 * WCM2B3GNNN4NI55OBU3AECZNNNNNNNNNXCVN3N1N}HNM\r\n"`
- **Token Body (40 chars)**: `"WCM2B3GNNN4NI55OBU3AECZNNNNNNNNNXCVN3N1N"`
- **Suffix (4 chars)**: `"}HNM"`
- **Decoded 25-byte Payload (Hex)**:
  `6A F2 A3 0B C0 00 02 0A CA 43 36 85 C8 2F 00 00 00 00 00 00 7A C8 01 01 00`
- **Extracted Fields**:
  - `ServerTimestamp` (`+0x00..+0x03`, BE): `0x6AF2A30B` ($1794286347$ Unix seconds)
  - `ClientPublicIPv4` (`+0x04..+0x07`, BE): `192.0.2.10` (`C0 00 02 0A`, synthetic)
  - `RedirectIPv4` (`+0x08..+0x0B`): `202.67.54.133` (`CA 43 36 85`)
  - `RedirectPort` (`+0x0C..+0x0D`, BE): `51247` (`0xC82F`)

#### Test Vector 13: AuthCode Token 52-Byte Generation & Header Folding (§4.5.2, §1.1 O7)

*(Synthetic address: every token and plaintext below is the captured one with the client address bytes $B[6..11]$ (the `RoutingToken`) set to `50 C3 0A 02 00 C0` = `192.0.2.10:50000`, re-encrypted and re-folded, so the published key cannot recover the real address. All other inputs are as captured. The harness checks the real tokens from the capture files and pins these synthetic values (§1.1 O7).)*

- **Fixed Cipher Key** ($K_{\text{auth}}$): `CB 83 24 B7 BA 3E 9E 0A` (Appendix §1.1 `CONST_AUTH_CODE_KEY`)
- **Capture #5 Token (52 bytes, synthetic address, `0x26` `+0x34..+0x67`)**:
  `BD 66 2E 08 4C B5 76 49 3E 50 10 77 10 58 BD EE D8 7F 2F EA 33 78 A2 64 4F 41 69 79 1D BD B5 CB 74 41 DF D7 68 4E B5 CB 6B 92 06 FA FA F1 15 68 E2 DB 0C A1`
  - **Header Equations Verified**:
    - $\text{Token}[1] = \text{Token}[12] \oplus \text{Token}[6] = \text{0x10} \oplus \text{0x76} = \mathbf{\text{0x66}}$ (matches `Token[1]`)
    - $\text{Token}[2] = \text{Token}[10] \oplus \text{Token}[8] = \text{0x10} \oplus \text{0x3E} = \mathbf{\text{0x2E}}$ (matches `Token[2]`)
    - $\text{Token}[3] = \text{Token}[13] \oplus \text{Token}[9] = \text{0x58} \oplus \text{0x50} = \mathbf{\text{0x08}}$ (matches `Token[3]`)
    - $\text{Token}[0] = \text{Token}[14] \oplus S = \text{0xBD} \oplus \text{0x00} = \mathbf{\text{0xBD}}$ (matches `Token[0]`, $S = 0$)
  - **Trailing Residue (12 bytes, `+0x68..+0x73`) — per-run-variable, not a token field**:
    `A0 E6 93 00 46 02 00 00 32 42 E2 00`
- **Capture #6 Token (52 bytes, synthetic address, `0x26` `+0x34..+0x67`)**:
  `81 8A 2E 2B 4C B5 76 49 C8 53 E6 74 FC 78 81 97 E4 06 13 93 6F 31 21 A4 CD E4 4B E1 89 7F AA 3F 98 F3 CC 77 DD 42 BF F3 DD 48 6F 2B 9C 27 73 BE 84 0D 6A 77`
  - **Header Equations Verified**:
    - $\text{Token}[1] = \text{Token}[12] \oplus \text{Token}[6] = \text{0xFC} \oplus \text{0x76} = \mathbf{\text{0x8A}}$ (matches `Token[1]`)
    - $\text{Token}[2] = \text{Token}[10] \oplus \text{Token}[8] = \text{0xE6} \oplus \text{0xC8} = \mathbf{\text{0x2E}}$ (matches `Token[2]`)
    - $\text{Token}[3] = \text{Token}[13] \oplus \text{Token}[9] = \text{0x78} \oplus \text{0x53} = \mathbf{\text{0x2B}}$ (matches `Token[3]`)
    - $\text{Token}[0] = \text{Token}[14] \oplus S = \text{0x81} \oplus \text{0x00} = \mathbf{\text{0x81}}$ (matches `Token[0]`, $S = 0$)
  - **Trailing Residue (12 bytes, `+0x68..+0x73`) — per-run-variable, not a token field**:
    `50 EC 6F 00 46 02 00 00 32 42 8D 02` (the `46 02 00 00` shared with Capture #5 is coincidence — both were captured back-to-back)
- **Capture #8 Token (52 bytes, synthetic address, `0x26` `+0x34..+0x67`, build `30261102_0`)**:
  `BD 66 2E 08 4C B5 76 49 52 50 7C 77 10 58 BD EE D8 7F 2F EA 33 78 A2 64 90 86 7A BB 02 C0 59 FD BA E1 41 77 E1 CB 6B C1 FE D8 F2 B7 B5 BC 5A 25 AD 96 43 EC`
  - **Header Equations Verified** (third independent capture, $S = 0$):
    - $\text{Token}[1] = \text{Token}[12] \oplus \text{Token}[6] = \text{0x10} \oplus \text{0x76} = \mathbf{\text{0x66}}$ (matches `Token[1]`)
    - $\text{Token}[2] = \text{Token}[10] \oplus \text{Token}[8] = \text{0x7C} \oplus \text{0x52} = \mathbf{\text{0x2E}}$ (matches `Token[2]`)
    - $\text{Token}[3] = \text{Token}[13] \oplus \text{Token}[9] = \text{0x58} \oplus \text{0x50} = \mathbf{\text{0x08}}$ (matches `Token[3]`)
    - $\text{Token}[0] = \text{Token}[14] \oplus S = \text{0xBD} \oplus \text{0x00} = \mathbf{\text{0xBD}}$ (matches `Token[0]`)
  - **Trailing Residue (12 bytes, `+0x68..+0x73`)**:
    `09 00 00 00 C0 0F E5 16 4F 78 AB 16` — bytes `56..59` are `C0 0F E5 16`, **not** `46 02 00 00`, so these 12 bytes are not a constant: they vary per login and carry no protocol data.
- **Capture #9 — Forward Generation Verified (build `30261102_0`, `cap9_20261110_110632_tokenhook-login`)**: the fixed hook on the builder logged four builder calls in one login (`sequence = 1513561330` = `0x5A371CF2`, `rand_lo = 0x6AF35D54`, `rand_hi = 0x711B6CB9`). The forward algorithm fed with these logged parameters reproduces each builder output and the wire token byte-for-byte.

  > **"Hook seed" is not $S$.** The hook also logged a per-call argument it labelled `seed` (`3`, `204`, `9`, `0` below). That argument does **not** enter the token: all four outputs have $B[1] = 0$ and $\text{Token}[0] = \text{Token}[14]$, i.e. $S = 0$. The labels below only identify the call. A client always uses $S = 0$.

  - **Inputs for Wire Login (hook call `seed 204`)**:
    - `SessionKey`: `65 D3 5C ED E2 99 B9 36 36 0C D5 3A 60 A3 B6 77`
    - `ServerTimestamp` (hook: `rand_lo`): `0x6AF35D54`; $K_8$ = `B9 6C 1B 71 F2 1C 37 5A` (hook: `rand_hi` `0x711B6CB9`, `sequence` `0x5A371CF2`, the LE halves of $K_8$)
    - `Seed`: `0` (retail header-folding seed)
    - `ContextBlock`: `01 00 50 C3 0A 02 00 C0 CD CC CC 3E 00 00 00 00 00 00 00 00` (`ProtocolVersion` `0x0001`, synthetic `RoutingToken` `50 C3 0A 02 00 C0` = `192.0.2.10:50000`, and the captured unused bytes `CD CC CC 3E` + 8 zeroes)
  - **Intermediate Values**:
    - $D_{28}$: `65 D3 5C ED E2 99 B9 36 36 0C D5 3A 60 A3 B6 77 54 5D F3 6A B9 6C 1B 71 F2 1C 37 5A`
    - $H_{16} = \text{MD5}(D_{28})$: `B6 89 C7 92 18 51 D7 0C EF AC CE 08 7D 45 F2 CE`
    - $B[0] = H_{16}[13] \oplus H_{16}[3] = \text{0x45} \oplus \text{0x92} = \text{0xD7}$
    - $B_{40}$ Plaintext: `D7 00 00 00 01 00 50 C3 0A 02 00 C0 CD CC CC 3E 00 00 00 00 00 00 00 00 B6 89 C7 92 18 51 D7 0C EF AC CE 08 7D 45 F2 CE`
    - Checksum accumulator: $B[0]+B[4] + B[8]+B[12] + B[16]+B[20] + B[24]+B[28] + B[32]+B[36] = \text{0xD7} + \text{0x01} + \text{0x0A} + \text{0xCD} + 0 + 0 + \text{0xB6} + \text{0x18} + \text{0xEF} + \text{0x7D} = \text{0x03E9}$
    - Ciphertext (48 bytes, before the forward XOR diffusion): `4C F9 C3 3F A6 BC 92 B5 B4 D0 39 8D E8 79 8E 1B D9 4B DA C6 F4 16 FC C1 0B CA E0 D5 66 91 7F C4 AB 3F 89 95 30 EB 76 F5 0D 07 E8 71 86 35 DB A1`
  - **`0x26` token (52 bytes; the real one matches the wire byte-for-byte)**:
    `29 B6 2E 43 4C B5 76 49 EF 53 C1 74 C0 10 29 A4 4C 35 BB A0 79 32 E8 2E DA CC 30 F1 FA 30 D0 05 63 F2 8D 49 E2 DD 54 C1 F1 1A 6C 99 94 93 7B 0A 8C B9 62 C3`
    - Header equations: $[0]=[14]=\text{0x29}$; $[1]=[12]\oplus[6]=\text{0xC0}\oplus\text{0x76}=\text{0xB6}$; $[2]=[10]\oplus[8]=\text{0xC1}\oplus\text{0xEF}=\text{0x2E}$; $[3]=[13]\oplus[9]=\text{0x10}\oplus\text{0x53}=\text{0x43}$. All match.
  - **Reproduction of All 4 Capture #9 Builder Calls**:
    - `seed 3`,   `session_key = e0c5f1b0afe100e336911696b3aed99c`:
      Output: `2A B5 2E 43 4C B5 76 49 54 50 7A 77 C3 13 2A A7 4F 36 B8 A3 7A 31 EB 2D D9 CF 33 F2 69 56 8A 3B 41 74 B1 FE E6 B0 45 9C 4E 74 FF 3C 47 36 A8 AF 5F 1C B1 66`
    - `seed 204`, `session_key = 65d35cede299b936360cd53a60a3b677`:
      Output: `29 B6 2E 43 4C B5 76 49 EF 53 C1 74 C0 10 29 A4 4C 35 BB A0 79 32 E8 2E DA CC 30 F1 FA 30 D0 05 63 F2 8D 49 E2 DD 54 C1 F1 1A 6C 99 94 93 7B 0A 8C B9 62 C3` (matches wire token)
    - `seed 9`,   `session_key = 37e09f966ef7fd1d461fd478e31e71a9`:
      Output: `2A B5 2E 43 4C B5 76 49 62 50 4C 77 C3 13 2A A7 4F 36 B8 A3 7A 31 EB 2D D9 CF 33 F2 6D 32 86 F7 93 88 94 6D BE 6D 17 68 B8 D3 2D D4 51 DF BE 46 49 F5 A7 8F`
    - `seed 0`,   `session_key = 5488d2bf36e3afa09fc4bac5b9beeda7`:
      Output: `2A B5 2E 43 4C B5 76 49 2C 50 02 77 C3 13 2A A7 4F 36 B8 A3 7A 31 EB 2D D9 CF 33 F2 88 11 54 A8 6E C3 6C 33 62 1F A8 96 A1 06 D2 7A D9 71 36 E8 C1 5B 2F 21`
  - **Resolution of the Preimage Artifact**:
    Plaintext blocks 1 and 2 ($B[8..23]$) hold the context block's address and unused bytes, which are the same in all four of this login's calls, so they encrypt to identical ciphertext blocks (`B4 D0 39 8D E8 79 8E 1B` and `D9 4B DA C6 F4 16 FC C1`) every time. The forward XOR diffusion makes each token byte the running XOR of the ciphertext bytes before it, so token bytes `12..27` differ between calls only by the XOR of `Token[11]`. `Token[11]` is `0x77` in seeds 3, 9 and 0, so bytes `12..27` are identical across those three calls; in seed 204 it is `0x74`, so those bytes differ by `0x03` (`C3 ^ 03 = C0`). The digest blocks (3 and 4) differ completely on each call. These specific ciphertext blocks depend on the login's `RoutingToken`, so they are not universal constants.

#### Test Vector 14: `ClientFileChecksum` (§5.9, §1.1 O13)
- **Input**: the 256 bytes `00 01 02 … FF` in order.
- **Period `0x13`** (as used for `FFXiMain.dll`): `0x159EE5E0`
- **Real file**: the installed `30261102_0` `FFXiMain.dll` (2,905,680 bytes) gives `0x74F0058F`, equal to the `ClientChecksum` recovered from capture #8's wire `SelectionMD5`.

#### Test Vector 16: `NICK` ClientMeta Body (§4.2.2)
- **POL ID**: `"ABCD1234"` (`41 42 43 44 31 32 33 34`)
- **After diffusion**: `41 83 C0 84 B5 87 B4 80`
- **sum** `0x438`; **mult** `0x31A0D748C8000000`; **lcg** `0x58E7FF18`; **total** `0x31A0D748C8000438`
- **PolIdToken**: `20 FB E7 90 48 D7 A0 31`
- **MD5(00×64 ‖ PolIdToken ‖ 00×10)**: `96 DA 93 1B 2D 14 6D 42 6B 37 5C 26 8B BF 74 86`
- **24-byte body**: `00 00 00 02 01 01` + 10 zero bytes + `96 DA 93 1B 2D 14 6D 42`
- **ClientMeta body (32 characters)**: `TTTTTAISTTTTTTTTTTTTTHBy5pjMnc0G`

#### Test Vector 15: `:51220` Request-Header Digest (§4.3.3)
- **POL ID**: `"ABCD1234"`; **POL password**: `"password"`; **ChallengeSeed**: `0x6AF3F7E9` (`E9 F7 F3 6A` on the wire)
- **Preimage (20 bytes)**: `41 42 43 44 31 32 33 34 70 61 73 73 77 6F 72 64 E9 F7 F3 6A`
- **Digest (request header `+0x18`)**: `58 9C FD 5B EA 27 F8 81 4B C2 B3 76 DC 5A A1 73`

### 5.9 Client File Checksum (`ClientChecksum`, verified §1.1 O13)
`ClientFileChecksum(file, period)` is a 32-bit shifted sum over every byte of a file as stored on disk:
1. Set `acc = 0` and `k = 0`.
2. For each byte `b` of the file, in order:
   - If `k > period`, set `k = 0`.
   - Sign-extend `b` to a 32-bit integer `s` (bytes `0x80`–`0xFF` become negative).
   - `acc = (acc + (s << ((4 * k) mod 32))) mod 2^32`.
   - `k = k + 1`.
3. The result is `acc`.

`k` runs `0, 1, …, period` and then wraps, so the shift cycles through `period + 1` positions. The shift amount is reduced mod 32, so `k = 8` shifts by 0 again.

| Use | File | Period |
|---|---|---|
| `SelectionMD5` `ClientChecksum` (§4.5.8) | `<FFXI_ROOT>/FFXiMain.dll` | `0x13` |

#### Test Vector 17: Whole `0x04/0x07` Payload, `AuthMode = 1` (§4.3.4, §5.4)
Made-up inputs. Check with `tools/verify_se_auth.py`.
- **`SE_ID`**: `"testuser01"` (10 characters); **`SE_PASS`**: `"SecretPass123"` ($H_1$ as in Test Vector 3: `2c47dceb3d437c6d414dfff87765f1b1fa72f880`)
- **Redirect `ServerTimestamp`**: `0x6AF25117` (1794265367); 7 s elapsed $\rightarrow$ $t_{\text{server}} = 1794265374$, $T_{\text{str}} =$ `"1794265320"`
- **`PasswordTimeDigest`**: `ecc6a8245206b0261c59e0dea7657fa1c852e8b8`
- **64-byte plaintext payload** (before the session cipher):
  ```
  +00  01 74 65 73 74 75 73 65 72 30 31 00 00 00 00 00   AuthMode 1, "testuser01", NUL padding to +0x11
  +10  00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00   +0x12..+0x1F OneTimePassword: 14 zeros
  +20  ec c6 a8 24 52 06 b0 26 1c 59 e0 de a7 65 7f a1   PasswordTimeDigest
  +30  c8 52 e8 b8 00 00 00 00 00 00 00 00 b0 f8 aa 5d   digest end, 8 zeros, Checksum 0x5DAAF8B0 (LE)
  ```
- For `AuthMode = 2` with code `"123456"`: byte `+0x00` = `02`, `+0x12..+0x17` = `31 32 33 34 35 36`, and the checksum changes accordingly (§5.3).
- **Request header** for this payload: `02 04 07 00 40 00 00 00` + 16 zero bytes + `ChallengeMD5` (§4.3.3, Test Vector 15). Retail's decrypted header in `trace_session_20261110_220551` has exactly this form; the success reply decrypted to `83 00 00 00 08 00 00 00 a6 36 43 ca` + 12 zero bytes.

### 5.10 IRC Line Checksum (`LineChecksum`, verified §1.1 O16)
Every Core IRC line that carries a token ends in a 4-character checksum suffix, placed directly before `\r\n`: the gatekeeper redirect (§4.2.1), the greeting, the client's `USER` line, the server's ciphertext line (§4.2.2 steps 1–3), and, inside the session cipher, `NICK`, `001` and `422`. The suffix is computed over **all bytes of the line before the suffix, starting at the line's first byte** (the leading `:` of a server line, the `U` of `USER`, the `N` of `NICK`), with no `\r\n`:

1. Read the bytes as little-endian `u32` words from the start and add them, mod 2³².
2. If 1–3 bytes are left over at the end, they form **one more word with zero bytes placed in front of them**: `u32_le(00 × (4 − r) ‖ tail)`, so the leftover bytes sit in the **high** end of that word. Add it. (Padding with zeros *after* the tail, the obvious choice, is wrong; it only gives the right answer when the length is a multiple of 4, which is why earlier versions could not explain the redirect suffix.)
3. Emit four characters from bits 31..8 of the sum: $c_k = ((\text{sum} \gg (26 - 6k)) \,\&\, \text{0x3F}) + \text{0x3F}$, $k = 0..3$. The characters are in `0x3F`–`0x7E` and are often outside the §5.2 alphabet (`?`, `]`, `\`, `{`, `|`, `` ` ``).

A receiver splits the suffix off as the last 4 characters before `\r\n`. Verified on all 51 distinct plaintext token lines in the captures (12 distinct `USER` lines, and the server's redirect, greeting and ciphertext lines, including lines with `pol-10xx-512xx.pol.com` prefixes) and on the 7 decrypted `001`, `422` and `NICK` lines the hooks logged; harness `tools/verify_line_checksums.py`. The `NICK` rule in §4.2.2 is this rule on an 80-byte line.

---

#### Test Vector 18: `0x04/0x05` Key Requests (§4.3.5)
Plaintext 40-byte payloads, before the session cipher.
- **Viewer key** (exact retail bytes):
  ```
  +00  00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
  +10  00 00 00 00 e8 03 05 00 00 01 00 00 00 00 00 00
  +20  00 00 00 00 e8 04 05 00                           checksum 0x000504E8
  ```
- **FFXI key**, with a made-up keystream word `K = 44 33 22 11` at stream bytes `72..75` (a real session uses its own keystream; the wire then carries `00 00 00 00` at `+0x20`):
  ```
  +00  00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
  +10  00 00 00 01 01 00 05 00 00 01 00 00 00 00 00 00
  +20  44 33 22 11 45 34 27 12                           +0x20 = K, checksum 0x12273445
  ```
  Checksum: `0x01000000 + 0x00050001 + 0x00000100 + 0x11223344 = 0x12273445`.
- **Third key** (exact retail bytes): `+0x10..+0x1F` = `00 01 00 01 01 00 05 00 00 01 00 00 00 00 00 00`, `+0x20` = `00 00 00 00`, checksum `0x01050201` (`01 02 05 01`).

#### Test Vector 19: Map Datagram Body (§4.6.2)
Plaintext bytes after the 28-byte header, before encryption. Both are retail's bytes (capture #5) and both rebuild exactly from the rules.
- **Empty (acknowledgment-only) datagram**: `Compressed` = `01`, `BitCount` = `08 00 00 00`, trailer = `MD5(01 08 00 00 00)` = `6f032100a78a9d7e5ddd0572dda1038d`. Body: `01 08000000 6f032100a78a9d7e5ddd0572dda1038d` (21 bytes; datagram 49 bytes; the first 16 body bytes are encrypted, the last 5 are not).
- **Retail's first reply, one `0x011` packet** `11 04 02 00 02 00 00 00`: `Compressed` = `01 88 4c 6e f7` (32 code bits), `BitCount` = `0x28` = 40, trailer `e3a5f1e438c1a0319d94ec6f76e6863e`. Body: `01884c6ef7 28000000 e3a5f1e438c1a0319d94ec6f76e6863e` (25 bytes; datagram 53 bytes).

## 6. Authentication

### 6.1 Credential Inputs
| Credential | Length / Format | Where Consumed |
|---|---|---|
| `POL_ID` | 8 ASCII characters issued by Square Enix: 4 uppercase letters followed by 4 digits (e.g. `ABCD1234`). Reject other input. | (1) Encoded into the 9-character `NICK` field (§4.2.2). (2) `PolIdToken`, which feeds the `NICK` ClientMeta body bytes `16..23` (§4.2.2). (3) Plaintext in the `:51220` request-header digest (§4.3.3). |
| `POL_PASS` | Up to 16 ASCII characters | Plaintext, in two digests: (1) the `NICK` `PassResponse` = `md5hex(GreetingToken[0..39] ‖ POL_PASS)` (§4.2.2); (2) the `:51220` request-header digest `MD5(POL_ID ‖ POL_PASS ‖ LE32(seed))` (§4.3.3). The client must keep the plaintext password for the session. |
| `SE_ID` | Up to 16 ASCII characters | Sent in Stage 4 (`pp000.pol.com:51220` Op `0x04/0x07`) at payload offset `+0x01..+0x11`, NUL-terminated, as the Viewer stored it. The send path changes neither case nor whitespace (2.1.21). Whether the Viewer's input box changes what the user types was not traced; send the ID as entered on the account website. |
| `SE_PASS` | ASCII string | Never sent. `H_1 = SHA-1(lowerhex(SHA-1(SE_PASS)) ‖ "playonline")`, then the time-bound `SHA-1` at payload offset `+0x20..+0x33` of `:51220` Op `0x04/0x07` (§5.4). No case change or trimming. A client may store `H_1` instead of the password. |
| `TOTP` (Optional) | 6 ASCII digits (`"000000"`–`"999999"`) | Sent in Stage 4 (`pp000.pol.com:51220` Op `0x04/0x07`) at payload offset `+0x12..+0x17` when `AuthMode` (`+0x00`) is `0x02` |

### 6.2 Phase 1 (No OTP) vs. Phase 2 (Software Token / TOTP)
Both flows use the **exact same 64-byte `:51220` Category `0x04` / Command `0x07` packet**, empirically verified across live captures:
- **Phase 1 (SE ID + SE Password only)** — *Verified in Capture #2*:
  - `payload[0x00] = 0x01` (`AuthMode = 1`)
  - `payload[0x01..0x11]` = Null-terminated `SE_ID` (up to 16 characters, zero-padded)
  - `payload[0x12..0x1F]` = All zeroes (`0x00` $\times 14$)
  - `payload[0x20..0x33]` = 20-byte `PasswordTimeDigest`
  - `payload[0x34..0x3B]` = All zeroes (`0x00` $\times 8$)
  - `payload[0x3C..0x3F]` = 32-bit additive checksum over `payload[0x00..0x3B]`
- **Phase 2 (SE ID + SE Password + 6-digit TOTP / Google Authenticator)** — *Verified in Capture #3*:
  - `payload[0x00] = 0x02` (`AuthMode = 2`)
  - `payload[0x01..0x11]` = Null-terminated `SE_ID` (up to 16 characters, zero-padded)
  - `payload[0x12..0x17]` = 6 ASCII digits of the One-Time Password (`TOTP`, e.g., `"300246"`), followed by zero padding at `0x18..0x1F` (`0x00` $\times 8$)
  - `payload[0x20..0x33]` = 20-byte `PasswordTimeDigest`
  - `payload[0x34..0x3B]` = All zeroes (`0x00` $\times 8$)
  - `payload[0x3C..0x3F]` = 32-bit additive checksum over `payload[0x00..0x3B]`

---

## 7. States and Errors

### 7.1 PlayOnline Core Login State Machine (Observed Steady-State Trajectory)
| State | Action | Next State |
|---|---|---|
| `0` $\rightarrow$ `17` | Pre-login initialization and optional `pt008.pol.com:54000` (`POLP` Type 7/8) PlayOnline Viewer version check. | $\rightarrow 18$ |
| `18` $\rightarrow$ `19` | Resolve `ci000.pol.com` and connect TCP `:51240` to receive redirect IP, the variable assigned port (§4.2.1, §1.1 O11), and server timestamp $t_{\text{server}}$. | $\rightarrow 21$ |
| `21` $\rightarrow$ `22` | Connect to redirected `:51241`–`:51247` server, exchange the 256-bit Ephemeral RSA handshake (receive `300 *` greeting; send `USER x 8 *` with modulus $N$; receive `300 *` Server Ciphertext $C$, decrypt $M$, derive $K_8$), and initialize Blowfish-64 OFB. | $\rightarrow 23$ |
| `23` | Send encrypted `NICK <Encoded_POL_ID>:<PassResponse>:<ClientMeta>` and read the encrypted replies (`001`, `422`, `MODE`, `NOTICE`; §4.2.2 step 6). | $\rightarrow 24$ |
| `24` $\rightarrow$ `26` $\rightarrow$ `27` | Resolve `pp000.pol.com` and connect TCP `:51220`; send 40-byte Hello and receive 24-byte `0x81` challenge (`ChallengeSeed`). | $\rightarrow 28$ |
| `28` $\rightarrow$ `29` $\rightarrow$ `30` | Build and send 40-byte encrypted Request Header + 64-byte Square Enix Account authentication payload (`Op 0x04/0x07`) and receive the 24-byte `0x83` response header (with redirected `:51220` server IP); the server then closes without sending the 8 payload bytes the header announces (§4.3.6). | Complete |

### 7.2 FFXI Lobby Client State Machine (`FFXiMain.dll`, Observed Steady-State Trajectory)
| State | Action | Next State |
|---|---|---|
| `0` $\rightarrow$ `1` | Resolve `"ffxi00.pol.com"` via `CommonFunctionTable + 0x374` / `+0x378` (`124.150.154.122`). | $\rightarrow 2$ |
| `2` $\rightarrow$ `10` | Allocate Lobby context and connect TCP socket to `124.150.154.122:54001`. | $\rightarrow 11$ |
| `11` $\rightarrow$ `12` | Invoke `CommonFunctionTable + 0x30C` (`(-2, -1, 0, -2, -1, 1)`) and poll `+0x310` until `:51220` `Op 0x04/0x05` returns the 16-byte Lobby Session Key. | $\rightarrow 13$ |
| `13` $\rightarrow$ `14` | Read 32-bit `FFXIAccountID` via `+0xEEC` and 16-byte `LobbySessionKey` via `+0xFAC`; send 152-byte `Opcode 0x26`. | $\rightarrow 15$ |
| `15` $\rightarrow$ `16` | Receive 40-byte `Opcode 0x05` (extract `ModifierSeed`, `ExpansionMask`, `FeatureFlags`); compute `SessionHash(ModifierSeed + 1)` and send 44-byte `Opcode 0x1F`. | $\rightarrow 17$ |
| `17` $\rightarrow$ `18` | Receive 452-byte `Opcode 0x20` (Character List); compute `SessionHash(ModifierSeed + 3)` and send 44-byte `Opcode 0x24` (Request World List). | $\rightarrow 19$ |
| `19` $\rightarrow$ `21` | Receive 272-byte `Opcode 0x23` (World List) and enter interactive Character Selection screen. | UI Ready |

---

## 8. Handoff (`pol.exe` / `polcore.dll` $\rightarrow$ `FFXi.dll` / `FFXiMain.dll`)

### 8.1 COM Activation Sequence
1. `pol.exe` instantiates `CLSID_POLCoreCom` (`{3501F5DD-7894-42DF-866A-A2B6527D8049}`, `IID_IPOLCoreCom` `{E0516654-EF77-435D-AA7D-50D2C069CE34}`).
2. When launching Final Fantasy XI, `app.dll` instantiates `CLSID_FFXiEntry` (`{989D790D-6236-11D4-80E9-00105A81E890}`, `IID_IFFXiEntry` `{989D790C-6236-11D4-80E9-00105A81E890}`) in `FFXi.dll` and invokes `IFFXiEntry::GameStart(pPol, pFFXiMessage)`.
3. `FFXi.dll` instantiates `CLSID_GameMain` (`{1027DC46-750D-4B1F-8834-1D25B8BEBAB8}`, `IID_IGameMain` `{493BF7B9-0C3A-43B5-BFA6-28FBEE251E3D}`) in `FFXiMain.dll` and invokes `IGameMain::FFXiGameMain(pPol, pFFXi)`.
4. `FFXiMain.dll` calls `IPOLCoreCom::GetCommonFunctionTable(&pTable)` (vtable index 7) to obtain a pointer to `polcore.dll`'s `CommonFunctionTable`.

### 8.2 `CommonFunctionTable` Handoff Contract Consumed by `FFXiMain.dll`
| Table Byte Offset | Index (`offset / 4`) | Signature | Purpose in Handoff |
|---|---|---|---|
| `+0x30C` | `195` | `int StartKeyExchange(int a0, int a1, int service, int a3, int a4, int param)` | Initiates `:51220` `Op 0x04/0x05` session key exchange (`(-2, -1, 0, -2, -1, 1)`). |
| `+0x310` | `196` | `int PollKeyExchange(int handle)` | Polls `:51220` `Op 0x04/0x05` completion (`1` = complete) and stores the 16-byte Lobby Session Key. |
| `+0x374` | `221` | `int StartDnsLookup(const char* hostname)` | Initiates async DNS lookup for `"ffxi00.pol.com"`. |
| `+0x378` | `222` | `int PollDnsLookup(int handle, void* outAddr)` | Polls async DNS resolution and writes resolved IPv4 address. |
| `+0xEA0` | `936` | `int GetReservedToken(uint8_t outToken[60])` | Delivers the 52-byte AuthCode token (§4.5.2) at `outToken[0..51]`; the rest of the buffer carries no protocol data. `FFXiMain.dll` places 64 bytes into Opcode `0x26` `+0x34..+0x73`, so wire bytes `52..63` vary per login (§4.5.2 step 6); a clean-room client sends zeros. The 64 wire bytes also feed `SelectionMD5`. |
| `+0xEEC` | `955` | `int GetFFXIAccountID(uint32_t* outAccountId)` | Writes the authenticated 32-bit FFXI Content/Account ID into `*outAccountId`. |
| `+0xFAC` | `1003` | `int GetLobbySessionKey(uint8_t outKey[16])` | Copies the 16-byte Lobby Session Key into `outKey[0..15]`. |

---

## 9. Open Questions and Operational Notes

1. **Legacy Hardware Security Token vs. Software Token**:
   - `AuthMode = 0x02` has been empirically verified with a registered Software Authenticator (Google Authenticator) transmitting 6 ASCII digits at payload offset `+0x12..+0x17`.
   - Legacy physical hardware security tokens (Square Enix Digipass) also generate 6-digit OTP codes and are expected to use the exact same `AuthMode = 0x02` frame layout. If a user utilizes a legacy hardware token, the implementation passes the 6 digits in the identical field.

2. **Specific Error Code Enumeration on Failed Authentication**:
   - Observed failure (capture #10, wrong Square Enix ID, `AuthMode = 1`): the `0x83` header on `pp000.pol.com:51220` returned `Status = 0x6F` (111), `PayloadLength = 0` and a `RedirectIPv4` value (`202.67.54.163`); the server then closed the connection. The retail client closed the Core IRC connection as well and restarted the whole login from `:54000` (new gatekeeper redirect, new Core session). Other `Status` values are not known.
   - **`0x6F` is not specific to a wrong ID (2.1.21).** The implementation's 2026-11-11 run got `0x6F` with a valid ID and password but a password digest missing the `"playonline"` step (§5.4), about 0.6 s after the request. So `0x6F` covers at least a wrong ID and a wrong password digest. No other status value has been seen; treat any non-zero `Status` as "Square Enix ID, password or one-time code rejected".
   - **Nothing else precedes `0x04/0x07` (2.1.21).** In captures #2 and `trace_session_20261110_220551`, after the last Core welcome line the client sends nothing on the Core connection; it resolves `pp000.pol.com`, connects to `:51220`, sends the 40-byte hello, receives the 24-byte `0x81`, and sends the `0x04/0x07` header and payload (about 0.4 s in all). The Core connection stays open.
   - Every other captured login used valid credentials and got `Status == 0x00`.
   - **Ungraceful disconnect (captures `trace_session_20261110_230908` → `_231055`).** The client was killed in the map zone (End Task); its last packet was a map datagram, with no logout or close traffic. A new login 23 s later completed normally through `:54000`, Core IRC, `:51220` and PML, so the PlayOnline layers do not block an immediate re-login. Character selection (`0x07`) 69 s after the kill was accepted (`0x0B`) and the map session worked. The maintainer observed the old character being dropped server-side after about 60 s. Selecting the same character **within** that window returns lobby error `3307` ("Same character is already logged in. Please wait a few minutes before trying to reconnect.") and the client stays on the character-select screen (maintainer-observed; not in a capture). A client should show that message and let the user retry after the timeout.
   - Any non-zero `Status` byte returned in the 24-byte response header on `pp000.pol.com:51220` (Frame 4a offset `+0x01`), or non-zero IRC numeric errors (`4xx`/`5xx`) on `:51241`–`:51247`, should be surfaced by the client as an authentication failure.

3. **Retail Version Check Discrepancy Action**:
   - On `pt008.pol.com:54000` and `pc001W2U.pol.com:53001`, `POLP` Type 8 returned `Status = 0x00000001` ("registered").
   - An outdated Viewer version did **not** produce a non-zero status on `:54000`: captures #1–#4 sent `"100020110829_E"` and got `Status = 1` (§4.1.5). On `:53001`, capture #7 sent the outdated FFXI version `"000130261006_0"` and got `Status = 1` with `CurrentVersion = "30261102_0"`, after which the client patched. **An outdated install is signalled by `CurrentVersion` differing from the installed version, not by `Status`.** Compare the two. As confirmed by user policy, GordianXI should check its version on disk against retail before permitting login; if retail is newer, the launcher should direct the user to update in PlayOnline Viewer or offer an optional admin-launched updater process rather than attempting to download patch files directly in the login flow.

4. **Credential Cache Excluded**:
   - The proprietary PlayOnline local encrypted credential file (`login_w.bin`) has been intentionally excluded from the specification. The implementer must manage user credentials directly within GordianXI's own secure account management rather than interacting with `login_w.bin`.

5. **PlayOnline Viewer Version Check (:54000) Registry Origin & Skippability**:
   - On Windows, PlayOnline installation paths are stored under `HKLM\SOFTWARE\WOW6432Node\PlayOnlineUS\InstallFolder`:
     - String value `"1000"`: PlayOnline Viewer root path (e.g. `C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\`).
     - String value `"0001"`: Final Fantasy XI root path (e.g. `C:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI\`).
   - **Skipping `:54000`**: The `POLP` version check on `pt008.pol.com:54000` is purely a local client auto-updater step for `pol.exe`. The core IRC gatekeeper (`ci000.pol.com:51240`) and account presence server (`pp000.pol.com:51220`) do not validate or require that a `:54000` check took place. If PlayOnline Viewer is not installed (e.g. running on Linux/macOS or in a custom launcher environment such as GordianXI or Horizon), the client can skip Step 1 and connect directly to `ci000.pol.com:51240`. This is **not verified**: no capture omits the `:54000` check, and the implementation's 2026-11-11 live runs failed at `USER` for an unrelated reason (the suffix, §4.2.2) both with and without the check, so they do not settle it either. Until a run that skips it completes a login, send the check (it costs one round trip). Without the Viewer's files the client also lacks `<POL_ROOT>/version.dat`, which the PML `X-POL-VIEWER-VERSION` header needs (§4.4); an implementation without a Viewer install must either skip the PML tier (losing the maintenance signal, §4.4) or supply that value another way.

6. **Core Session Socket Lifecycle & Keepalives**:
   - The Core IRC socket (`:51241`–`:51247`) stays open after stage 3 while `pol.exe` hosts `FFXiMain.dll`.
   - **Keepalive required.** In capture #5, roughly two minutes after login the server sent a short encrypted line (19 bytes) on this socket and the client replied (12 bytes). Decrypted under the session cipher (§5.1), this is a standard IRC `PING <token>` / `PONG <token>` exchange. An implementation must keep this socket open and answer the server's ping lines to prevent session termination.

7. **Open items for implementation (2.1.14 review)**. Everything else in this spec is either verified or derived from data the client has. These are not:
   - **`:51220` Hello `Unused0C` (`+0x0C`, §4.3.1) — RESOLVED.** Not a protocol field; the server proceeds with 12 zero bytes (capture #9). A clean-room client always sends 12 zero bytes.
   - **`0x04/0x05` `+0x20` word (§4.3.5) — RESOLVED (2.1.23).** Zero in the Viewer and third key requests; in the FFXI key request it is the OFB keystream for that position, so it is zero on the wire. The non-zero cap9 request was the FFXI request.
   - **Square Enix password digest (§5.4) — CORRECTED (2.1.21).** The time digest hashes `H_1 = SHA-1(lowerhex(SHA-1(SE_PASS)) ‖ "playonline")`, not `SHA-1(SE_PASS)`. Steps 3–5 verified on captures #2 and #3; step 2 has no capture with a known password and needs one live login to confirm.
   - **Clock for the Square Enix password digest (§5.4) — DECIDED (2.1.17).** The clean-room rule is redirect `ServerTimestamp` plus monotonic elapsed seconds. Retail's own source (that anchor or local `time(NULL)`) is still unverified; the two agreed to within a second in every capture.
   - **First map datagram transport header (§4.6.1).** Send the 28 bytes as observed (an observed constant, not a derivation); `C0 02`, `77` and `24 E8 FE 76` are not explained.
   - **`0x26` `ExpansionMask` (§4.5.2).** Send `0x00000FFF`. The field reports installed expansions, not the account's (the captures' trial account receives `0x00000001` in `0x05`), and a current full retail install has all twelve. How the client decides an expansion is installed is not documented publicly; a partial install would presumably send fewer bits, and the server's errors for this case are lobby codes `3214` / `3215` (§4.5.10).
   - **`NICK` ClientMeta body (§4.2.2) — RESOLVED (2.1.19).** Bytes `16..23` are `MD5(00×64 ‖ PolIdToken ‖ 00×10)[0..7]`, with `PolIdToken` computed from the POL ID; verified on both accounts. Still unverified: whether byte `5` (`Region`, presumed `01` = US / English) changes for non-US clients, and whether bytes `8..15` are ever non-zero.
   - **`:51220` `ChallengeMD5` (§4.3.3) — RESOLVED (2.1.19).** `MD5(POL_ID ‖ POL_PASS ‖ LE32(seed))`, verified 11/11 on a hooked capture. Not determined: whether retail changes the case of the POL ID or password first (the verifying account's credentials were already upper-case).
   - **Core IRC `001` payload (§4.2.2 step 6).** Decodes to `00 00 00 01` + zeros in captures #9 and #10; runtime analysis reports the client reads only the leading `1` and sets no state from it; it is not the source of the `:51220` values earlier versions attributed to it.
   - **RSA keypair reuse.** In capture #10 the retail client sent the identical `USER` modulus on both logins of one run. Generating a fresh keypair per login is assumed to be accepted (captures #1 and #3, from different runs, used different moduli).
   - **`0x26` `ClientCode` (§4.5.2).** Send `5`. It was `5` in every capture (English client); public documentation says `3` is also possible, depending on language and a first-launch graphics setting.
   - **Error paths (§9 items 2 and 3).** Capture #10 has one failed Square Enix login (`0x83` `Status = 0x6F`, item 2). No capture contains a `POLP` status other than `1`, even for an old Viewer version (§4.1.5). Rejected requests seen so far come from the implementation's live runs, not from retail: a `POLP` request with a bad checksum gets a clean close with no reply, and a `USER` line with a bad suffix gets no reply and the connection stays open.
   - **Server-side blocking.** Nothing in the captures shows rate limiting or blocking by IP. Retail logins on 2026-11-10 succeeded 23 s after a killed client (item 2) and on reruns within a minute. The hooks log Winsock calls, not packets, so TCP options and source ports were never captured; nothing suggests the servers check them, and no TLS is used anywhere (§2).
   - **`SessionKey` cross-check (§4.5.2) — RESOLVED (2.1.17).** In capture #9 the wire token's builder key equals the `LobbySessionKey` `FFXiMain.dll` read via `+0xFAC` (`65d35c…b677`), and that key reproduces the wire `0x1F` and `0x24` `SessionHash` values.
   - **`USER` trailing bytes — WITHDRAWN (2.1.20).** They were the decoded checksum suffix; the default of zeros was wrong and the server does not answer such a line (§4.2.2 step 2, §5.10).
   - **`POLP` checksum — CORRECTED (2.1.20).** MD5-based, not CRC-32; both version servers close without replying to a CRC-32 checksum (§4.1.1, §4.1.5).
   - **Unverified clean-room defaults.** The AuthCode context residue (12 zero bytes inside the token; one supporting login, capture #8), the `0x26` trailing 12 bytes (no capture sends zeros, §4.5.2), skipping the §4.3.7 requests, skipping `:54000` (item 5), the map transport `State` words 0, 1 and 3 (§4.6.3), and O9's management opcodes (§4.5.10). `X-PlayOnline-Hello` (§4.4) and the service-1 key (§4.3.5) are unchecked but not needed by the client.

8. **Lobby `0x26` → `0x04` error `3301` (first live lobby attempt, 2026-11-11).** The implementation reached the lobby with every earlier stage succeeding and got lobby error `301` (message `3301`). `3301` is "communication error … wait several minutes" — a **session-correlation failure**, not a bad password (which is the distinct code `3302`, §4.5.10). The lobby (`:54001`) validates the `0x26` `AuthCode` against the live PlayOnline presence session; `3301` means it could not. The `AuthCode` generation algorithm (§4.5.2) is verified byte-for-byte, so the cause is an **input** to it, or the session state it is validated against. The three digest-protected inputs — all session values that must match what the servers recorded for *this* login — and their common clean-room pitfalls, in order of likelihood:
   - **Update 2.1.23: the FFXI key request itself (most likely cause).** A second live attempt still got `3301`, with `D_28` = `SessionKey ‖ LE32(greeting ServerTimestamp) ‖ K_8`, exactly as below, and the reply took 5.1 s (retail's `0x05` arrives about 0.2 s after `0x26`). The fault was in the spec: §4.3.5 up to 2.1.22 described the `0x04/0x05` payload with `00` at `+0x13` and big-endian fields, so the selector bytes matched the Viewer's request, not the FFXI one. The server answers it with a valid key, but not the FFXI key the lobby checks against. Build the request exactly as §4.3.5 now gives it (selector `00 00 00 01`, LE `Param = 1`, LE `SubCommand = 5`, `00 01`, and a `+0x20` word that is zero on the wire; Test Vector 18). If `3301` persists after that, add the §4.3.7 requests before PML, then the retail-shaped `0x26` trailing bytes.
   - **`K_8` order — confirmed 2.1.23.** In all three hooked sessions the key logged at Core cipher initialisation (the RSA `K_8`, as in Test Vector 10.2) equals `LE32(rand_hi) ‖ LE32(sequence)` from the builder hook: `231055` cipher key `91 10 ED 1C 80 7F FD 2A` = `rand_hi 0x1CED1091`, `sequence 0x2AFD7F80`; likewise `220551` and `230908`. A `K_8` that works for the Core cipher and the PML digest is therefore the right `D_28` input.
   - **`ServerTimestamp` (`D_28` `+0x10`).** Must be the **Core IRC greeting** token's `+0x00` (BE), §4.2.2 (hook `rand_lo`; capture `231055`: `0x6AF40703`). It is **not** the `:51240` gatekeeper **redirect** token's timestamp (§4.2.1), which is the one used for the Square Enix password digest (§5.4). Using the redirect timestamp here is an easy and silent swap that yields `3301`.
   - **`SessionKey` (`D_28` `+0x00`, 16 bytes).** Must be the **FFXI `LobbySessionKey`** fetched from the **assigned** `:51220` in state 11–12, immediately before `0x26`, with the FFXI key request of §4.3.5. Not the Viewer's key (fetched during PML), not a key from `pp000`, not a stale key from an earlier fetch. Retail rebuilds the `AuthCode` on every service-0 key it receives and sends the one built from the **last** key (capture `231055`: four builder calls, the wire token is the one from `session_key = a242608e…ca26`, the key read back via `CommonFunctionTable + 0xFAC`).
   - **`K_8` (`D_28` `+0x14`, 8 bytes).** The live Core IRC session key (§4.2.2), as its two 32-bit LE halves: `K_8[0..3]` = hook `rand_hi`, `K_8[4..7]` = hook `sequence` (capture `231055`: `rand_hi = 0x1CED1091`, `sequence = 0x2AFD7F80`, so `K_8 = 91 10 ED 1C 80 7F FD 2A`). A swapped or wrong-endian half gives `3301`. `D_28 = SessionKey_16 ‖ LE32(ServerTimestamp) ‖ K_8[0..7]` in exactly that order; `ServerTimestamp` is **little-endian** inside `D_28` even though it is read big-endian from the token.
   - **Core IRC connection open.** The presence session lives on the Core IRC socket, which retail keeps open through the whole lobby login (verified capture #5: open at `0x26`, only `PING`/`PONG` after). A client that closed or reconnected Core before `0x26` loses the session the lobby checks against → `3301`.
   - **Context block (not digest-protected).** `AuthCode` plaintext `B[4..11]` carries `ProtocolVersion` + the client's **server-observed** public port and IPv4 from the `RoutingToken` (greeting token `+0x04`/`+0x14`, identical to the `:51220` Hello `+0x04..+0x0B`), §4.5.2. These are not in `H_16`, so they are the least likely cause, but a client that writes its **local** IP/port here instead of the `RoutingToken` values diverges from retail; use the `RoutingToken`. Bytes `B[12..23]` are accepted as zero (capture #8).
   - **To localise it:** log `D_28` (the 28 preimage bytes) and the resulting `H_16` on the failing attempt and compare the field sources against the list above; the token itself round-trips, so a `3301` with a correct `D_28` would instead point at session liveness (Core closed, or `0x26` sent too late).
