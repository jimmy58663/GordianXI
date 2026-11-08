# Retail Login Spec Format

> The required shape of any derived retail-login protocol spec before GordianXI implements from it. It is the contract between the research side and the implementation side (see [retail-login-research.md](retail-login-research.md) and [AGENTS.md](../../AGENTS.md)). A spec that does not meet this format is sent back for rewrite, not implemented.

This document describes **what the spec must contain**, not how to obtain any of it. The research method is out of scope here and belongs only to the research side.

## Rules

- **Facts only.** Message order, byte layouts, named standard algorithms, key-derivation steps, state transitions, and error behaviour.
- **No code carried across.** No pseudo-code, no decompiler output, no "this routine does X then Y" walkthroughs, no copied source. Naming a standard algorithm ("MD5 of the following fields", "Blowfish-ECB with this key") is a fact; reproducing a routine is not.
- **Versioned and dated.** Each revision has a version and date in the header, plus a provenance line (e.g. *"Derived by an independent research process; no POL code was consulted by the implementer."*).
- **Self-contained.** The implementer builds from this file alone. Anything the implementation needs must be in the spec; open questions are listed, not left implicit.

## Required sections

1. **Header** — version, date, provenance line, and the source (external publication + its license, or in-house research).
2. **Endpoints and transport** — hosts, ports, and transport (TCP/UDP, TLS or custom), in connection order. Note which stages are plaintext vs encrypted.
3. **Session flow** — the ordered list of messages from first connect through successful login and handoff to the world/map connection, as a sequence or state diagram. Name each message and its direction (client→server / server→client).
4. **Message field tables** — for each message: a table of fields with offset, size, type, endianness, and meaning. Mark fixed/magic values, lengths, and reserved/unknown bytes explicitly.
5. **Cryptography** — named algorithm(s), mode, block/padding handling, and exactly how keys and any IV/salt are derived (which fields feed the derivation, in what order). State where each key comes from and its lifetime. Flag any value that originates inside `pol.exe` (these are sign-off-gated per AGENTS.md Prohibition #10).
6. **Authentication** — how credentials, tokens, and any one-time-password / second-factor step are presented and validated, as a flow. Do not include real credential values.
7. **States and errors** — success and failure states, retry/timeout behaviour, and the server responses that signal each (version mismatch, bad credentials, queue/full, maintenance, etc.).
8. **Handoff** — what GordianXI needs to carry from login into the existing world/map session path (session key, character/account identifiers, server address), and in what form.
9. **Open questions** — anything unverified or unknown, so the implementer asks rather than guesses.

## Field table example (shape only)

| Offset | Size | Type | Field | Notes |
|---|---|---|---|---|
| 0x00 | 2 | u16 LE | message id | fixed `0x####` |
| 0x02 | 2 | u16 LE | length | total message bytes |
| 0x04 | 16 | bytes | digest | MD5 of fields A..B (see Cryptography) |
| ... | | | | |

The implementer treats a conforming spec as the single source of truth, writes the login client from scratch in idiomatic C# (.NET 10: `Span<byte>`, `ref struct`, BCL crypto), cites the spec in XML doc-comments, and verifies behaviour against the maintainer's own trial-account captures.
