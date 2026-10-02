# Distribution, Docs Site & Registries

> Preliminary plan for how GordianXI reaches users: the public docs site, client auto-updates, the addon registry, and patch-diff tracking. Nothing here is started or final; each section records the preferred route and the questions to settle when the work begins. The client updater and addon registry belong to Phases 9 and 6 ([post-mvp.md](post-mvp.md)); the docs site and patch diffs can start whenever there is time.

## Domains

Both domains are registered through Cloudflare; `gordianxi.com` redirects to `gordianxi.org`.

| Host | Purpose |
|---|---|
| `gordianxi.org` | Landing page (can later redirect to or link the docs) |
| `docs.gordianxi.org` | Public docs site |
| `addons.gordianxi.org` *(optional)* | Cached copy of the addon registry index, if raw GitHub URLs are not enough |
| `patches.gordianxi.org` *(optional)* | Patch-diff reports, kept apart from the docs (see [Patch diffs](#patch-diffs)) |

## Public docs site

**Audience:** players and addon/automation authors. `docs/` stays what it is today, internal technical notes (protocol specs, calibrations, reverse-engineering findings), and is **not** published.

**Tooling:** DocFX (a .NET tool, no Node.js toolchain to maintain) builds the site in GitHub Actions; the output is uploaded to Cloudflare as a static site. Uploading a prebuilt site means Cloudflare's own build limits do not apply.

**Source layout** (proposed folder `site/`, next to `docs/`):

```text
site/
  docfx.json
  index.md
  user/          install, first launch, bootloader setup, account profiles, settings, troubleshooting
  addons/        getting started, manifest and permissions, API reference (generated), publishing to the registry
  automation/    gambit profiles, conditions/targets/actions, multi-box coordination, server restrictions
```

**Addon API reference.** DocFX's built-in API docs render C# signatures (`public void Send(string text)`), which is not what an addon author calls. The addon API is implemented in C# and injected into each addon's Lua environment, so there is no Lua source for a Lua doc tool such as LDoc to read either. Preferred route: mark each Lua-exposed member in `Gordian.Addons` with an attribute carrying its Lua name (e.g. `gordian.chat.send`), keep the description in its XML doc comment, and add a small generator that emits:

1. **Markdown pages** for `site/addons/api/`, showing Lua call syntax, parameters, return values, required manifest capability and an example; DocFX renders them like any other page.
2. **A Lua Language Server stub file** (`---@meta` annotations), shipped to addon authors so VS Code and other editors autocomplete and type-check the `gordian.*` API.

Both come from the same attributes, so the reference and the editor hints cannot drift. Once the stub file exists, LDoc could document it, but the generator already produces the Markdown, so LDoc adds a toolchain (Lua, LuaRocks) for no gain.

**Automation reference.** Gambit condition/target/action types can use the same attribute-and-generator approach when Phase 7 defines them.

**Open questions**
- Cloudflare Pages vs Workers static assets: Cloudflare has been steering new static sites to Workers. Check the current recommendation and free-tier limits when setting this up; the deploy step is either `cloudflare/wrangler-action` (runs Wrangler, an npm tool, on the CI runner only) or the Cloudflare REST API.
- Publish on merge to `main` only, or also versioned docs per client release?
- Publish the reference registries ([docs/README.md](../README.md#reference-registries): event opcodes, packets, DAT files, flags) as a protocol reference section for other client, server and tool authors? They are written to stand alone, cite their sources, and mark findings beyond the public references.
- The Cloudflare API token is stored as a GitHub Actions secret, limited to the one Pages/Workers project.

## Client auto-updater

**Behaviour:** check for a new version at startup and from a "Check for updates" button; if one exists, show the changelog, download it, and apply it on restart.

**Preferred route:** [Velopack](https://github.com/velopack/velopack) (MIT). It covers Windows, macOS and Linux, uses GitHub Releases as the update source, and handles the parts that are hard to build ourselves: replacing a running app, restart, rollback and delta downloads. Its release tooling (`vpk`) runs in the release workflow and attaches packages and a release manifest to each GitHub Release. Add it to `THIRD_PARTY_NOTICES.md` when adopted.

**Constraints**
- Never apply an update while a game session is active or while the `FFXiMain.dll` proxy is staged; apply only after `ProxyStager` has restored the original DLL (normally at shutdown or the next startup).
- Update downloads are integrity-checked (Velopack's manifest carries hashes). The update check must not use the GitHub REST API, which limits unauthenticated clients to 60 requests/hour per IP; `releases/latest/download/...` asset URLs are not subject to that limit.

**Open questions**
- Code signing: without a certificate, Windows SmartScreen warns on first run and macOS Gatekeeper blocks unsigned apps unless notarized. Certificates cost money; decide before the first public release.
- Release channels (stable / prerelease)?
- Packaging per OS (Windows installer, macOS `.app`, Linux AppImage) comes with Velopack; see Phase 9 in [post-mvp.md](post-mvp.md).

## Addon registry

**Purpose:** the in-client Addon Browser lists available addons, installs them, and keeps them updated. Addons live in their authors' own repositories; the registry only describes them.

**Repository:** a separate repo (e.g. `GordianXI/addon-registry`) so authors can submit or update their entries by PR without touching the client repo. One file per addon:

```jsonc
// addons/<addon-id>.json  (illustrative)
{
  "id": "autotranslate-plus",
  "name": "AutoTranslate+",
  "author": "someone",
  "repo": "https://github.com/someone/autotranslate-plus",
  "description": "...",
  "versions": [
    {
      "version": "1.2.0",
      "tag": "v1.2.0",
      "url": "https://github.com/someone/autotranslate-plus/releases/download/v1.2.0/autotranslate-plus.zip",
      "sha256": "…",
      "minClientVersion": "0.9.0",
      "capabilities": ["chat.read", "ui.draw"]
    }
  ]
}
```

CI on the registry repo validates every PR (schema, the release URL downloads, the hash matches the download, the capabilities are known) and, on merge, builds a single `index.json` that the client fetches (from raw GitHub or a Cloudflare-cached copy).

**Trust model:** every installable version is pinned by tag and SHA-256 in the registry, so nothing reaches users that did not pass review; the client refuses any download whose hash does not match. Installing from branch archives (`/archive/refs/heads/main.zip`) is not supported for registry addons, because a branch can change after review. Removing or yanking a version from the registry stops new installs of it. The post-MVP plan also calls for signed registry packages, which would add a check against a compromised registry host.

**Open questions**
- Does an auto-update install silently, or ask when the new version requests more capabilities than the installed one? (Proposed: silent unless capabilities grow.)
- Review load: every addon update is a PR the maintainer merges. If that becomes a bottleneck, consider trusted authors whose PRs auto-merge once CI passes.
- Dependencies between addons, and addons that are not in the registry (local development, private-server addons): see "Custom Source Support" in [post-mvp.md](post-mvp.md).

## Patch diffs

**Goal:** publish what changed in each retail FFXI patch (items, text, other game content) faster than existing sites such as BG-Wiki and FFXIAH.

**Status: needs more discussion before building, mainly on the legal side.**

**Legal considerations**
- Diff reports of game content reproduce Square Enix's copyrighted data. Fan sites have done this for years and it is broadly tolerated, but publishing it on the GordianXI site ties a replacement-client project to data mining, which makes both a more visible target. Keep patch reports on their own host (e.g. `patches.gordianxi.org`) and possibly their own repo, so a takedown of one never affects the client, docs or registry.
- Downloading patches with an unofficial tool, or on third-party infrastructure (GitHub-hosted runners), goes further than using the official updater on a machine you own. Avoid both.
- Consider publishing structured change summaries (what changed and in which file) rather than dumping whole files.

**Route**
1. **Now (manual):** after patching normally with the official updater, run a local diff tool that compares a snapshot of the DAT files from before the patch with the patched files, decodes changed content with the existing Phase 4 decoders, writes Markdown, and publishes it to the reports site.
2. **Later (automated):** a scheduled job on a machine you own that notices a new patch (for example, a changed version file in the installed game), runs the official updater, then the diff tool, then publishes. How to detect and apply patches without someone at the machine is the main research item; driving the official updater unattended may not be practical.

A side benefit: the same diff shows which file formats a patch touched, which tells us when a GordianXI decoder may need updating.
