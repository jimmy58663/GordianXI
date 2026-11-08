# Retail Login Research — Clean-Room Policy and Decision Record

> How GordianXI will implement a direct, POL-bypassing retail login without taking any code from Square Enix's `pol.exe` into this MIT repository. This is the governing policy; the binding rules it depends on are in [AGENTS.md](../../AGENTS.md) ("POL Login Research — Clean-Room Boundary" and Prohibitions #9/#10). Status: **on hold** pending an external spec (see below). This doc holds policy and decisions only — no reverse-engineering method.

## Goal

A direct login to retail FFXI servers that does not require the POL front end, analogous to how GordianXI logs into LandSandBoat today. Reaching retail's login requires understanding a protocol that POL encrypts, which cannot be recovered from a plain Wireshark capture. That research is done outside this repository; GordianXI implements only from a written spec.

## Why a clean-room split

Square Enix's `pol.exe` is copyrighted, GPL/AGPL-style reference projects do not implement POL's login, and there is no permissively-licensed reference for it. To keep GordianXI's MIT code free of derived code, the work is split into two sides that communicate **only through a written spec**:

| | Research side | Implementation side |
|---|---|---|
| Repo | `gordian-pol-research` (private, off-limits) | GordianXI (this repo) |
| Does | Hooking, captures, disassembly, writes the spec | Writes the C# login client from the spec |
| Sees | Never sees GordianXI's login code | Never sees the research, hooks, or dumps |
| Output | A versioned spec file, facts only | Idiomatic C#, authored from scratch, spec cited |

The implementer (the GordianXI agent/author) **must never** read `gordian-pol-research` or design its methods. This is enforced in AGENTS.md Prohibition #9 and the Workspace Access list. Specs cross as dated files dropped into `docs/network/`; nothing else crosses.

## Source of truth — preferred order

1. **External published spec (preferred).** An independent researcher has demonstrated a POL bypass publicly. If they publish a documented protocol spec under a reference-able license, that becomes the source of truth: cited in doc-comments, reimplemented from scratch, never copied. If they publish code instead, it is handled like any GPL/AGPL reference (read the wire format, reimplement; never translate — AGENTS.md Prohibition #7).
2. **In-house research (fallback).** If no external spec materializes on a workable timeline, the research is done in `gordian-pol-research` (for example by a separate AI agent with its own context, which the GordianXI implementer never shares), producing a spec in the format below.

Either way, the implementation side only ever consumes a spec that conforms to [retail-login-spec-format.md](retail-login-spec-format.md).

## Spec handoff rules

- Specs are **dated, versioned files** copied into `docs/network/`. The implementer builds only from the committed spec, never from pasted details, a verbal "how the function works," or the research repo.
- Each spec header states its provenance, e.g.: *"Derived by an independent research process; no POL code was consulted by the implementer."*
- Implementer questions go back as **questions**, answered by spec revisions — never by code excerpts pulled from the research side.
- The spec carries **facts only** (message order, field tables, named standard algorithms, key derivation, states, errors). No pseudo-code, decompiler output, or step-by-step routine walkthroughs. The implementer should reject a spec that reads like code and ask for a rewrite.

## What the clean room does and does not cover

- **Does:** keeps GordianXI's code free of `pol.exe`-derived code and documents every source, the same posture the project already holds for LSB and XiPackets.
- **Does not:** waive Square Enix's Terms of Service (a contract question, independent of how the code is structured), nor resolve anti-circumvention law (e.g. DMCA §1201; an interoperability exemption may apply but is not guaranteed). These are the maintainer's risk decisions, not engineering ones.

## Guardrails

- Reverse-engineering research runs against a **disposable trial account**, never a real account.
- Captures may contain passwords, one-time passwords, and session tokens. They stay out of both repositories and out of anything shared.
- **No secret extracted from `pol.exe` or a capture** (key, constant, token) is committed to GordianXI without the maintainer's explicit written sign-off (AGENTS.md Prohibition #10). This is the highest-risk item for a distributed build and is decided case by case.

## Current state (2026-11-08)

On hold. The maintainer has contacted the external researcher about a publication date. If a reference-able spec is published soon, it becomes the source of truth. Otherwise the in-house (`gordian-pol-research`) fallback applies. No implementation work begins until a conforming spec exists in `docs/network/`.
