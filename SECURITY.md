# Security Policy

## Supported versions

GordianXI is before 1.0. Only the latest release gets security fixes. A fix ships in a new release; older releases are not patched. If you build from source, use the latest `main`.

| Version | Supported |
|---|---|
| Latest release | Yes |
| Older releases | No |

## Reporting a vulnerability

Please do not open a public issue, discussion or pull request for a security problem.

Report it privately through GitHub: go to the repository's **Security** tab and choose **Report a vulnerability** ([direct link](https://github.com/jimmy58663/GordianXI/security/advisories/new)). Only the maintainers can see the report.

Include what you can:
- the GordianXI version or commit, and your OS;
- what an attacker can do, and what they need first (a malicious addon, a malicious server, local access...);
- steps to reproduce, or a proof of concept;
- any fix you suggest.

## What to expect

- We confirm we got the report within 7 days.
- We tell you whether we accept it, and our plan, within 30 days.
- We fix accepted issues in a new release, then publish a GitHub security advisory. We credit you in the advisory unless you ask us not to.
- Please give us time to release the fix before you disclose the issue. If we cannot fix it within 90 days, we agree a date with you.

GordianXI is a volunteer project, so these times are targets, not promises. We will keep you updated if we run late.

## Scope

In scope: code in this repository and the release archives built from it. We are most interested in:
- **Lua addon sandbox escapes.** An addon reaching anything outside its curated API: CLR reflection, `io`/`os`, processes, files outside its own storage folder, or another addon's state.
- **The named-pipe handoff.** Another local process reading or injecting the session credentials passed over `GordianXI_Handoff`, or impersonating either end of the pipe.
- **Packet parsing.** A malicious or compromised game server causing a crash, memory corruption, code execution or file access through crafted packets.
- **Update and download integrity.** Anything that lets a tampered release, update, addon download or DAT file pass as genuine, or gets around the release attestations.
- **Proxy DLL staging.** `ProxyStager` swapping `FFXiMain.dll` in the game folder: DLL planting or hijacking, leaving the game folder modified, or writing outside it.
- Any other bug that lets untrusted input (server data, addons, DAT files, settings files) run code or read or change data it should not.

Out of scope:
- Game servers themselves (retail or private servers such as LandSandBoat). Report those to their operators.
- Third-party addons and scripts not shipped in this repository. Report those to their authors.
- Attacks that need an already compromised machine or administrator rights.
- Cheating or gameplay exploits with no security impact on the user.
- The reference projects (LandSandBoat, XiPackets, xi-tools and others). Report those upstream.
