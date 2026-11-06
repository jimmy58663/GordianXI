# Desktop Shell & Account Profiles

> Phase 1 foundation of `Gordian.App`: the Avalonia control panel, account profiles and the packet inspector. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Foundation (Phase 1)

- Decoupled monorepo (`Gordian.Core`, `Gordian.Engine`, `Gordian.Automation`, `Gordian.Addons`, `Gordian.App`); layers in [architecture.md](../architecture.md)
- Clean-room reverse engineering boundaries enforced in `AGENTS.md`
- Modern dark HUD UI with Avalonia MVVM
- Encrypted profile storage with master AES key and defensive plaintext migration
- Account profile management (Add, Edit, Delete, Select for Launch)
- Profile & Global session termination commands (`TerminateCommand`, `TerminateAllCommand`)
- Live Packet Inspector UI with Hex/ASCII view, pause/clear, and real-time capture

## Developer launch options

`--profiles-dir <path>` reads profiles from that folder instead of the `profiles` folder beside the build, and `--profile <name>` logs that profile in when the main window opens. Together they let a script start any build, including a worktree's, on a known test character:

```bash
dotnet run --project src/Gordian.App/Gordian.App.csproj -- --profiles-dir <path> --profile <name>
```

`--profile` accepts only private server profiles. Profile passwords are encrypted with the per-user key in `~/.gordian_key`, so a shared folder works from every build under the same user. Selection state for the launch list is not changed.

## Screenshots

`/screenshot [hide] [name]` saves the viewport's next frame as a PNG in the `screenshots` folder under `GordianStorage.RootDataDirectory`, and the chat log prints the saved path.

- **`hide`** saves the 3D scene without the stock UI and overlays, which also leaves out name plates.
- **File names** follow Windower: `img_yyyyMMdd_HHmmss.png`. A name may use letters, digits, `-`, `_` and `.`, so it can't leave the folder. An existing file is kept, and the new one takes a numbered name (`shot-2.png`).
- **No stall:** `ViewportScreenshot` copies the swapchain image to a staging texture before presenting and reads it on the next frame, when the GPU has finished. `PngEncoder` then runs on a worker thread.
- **The frame is exact:** it is read inside the client, so it has no mouse cursor and doesn't depend on window position. Outside tools can't do this reliably: `ffmpeg`'s window capture returns black frames for the viewport.

Checked on Vulkan and Direct3D 11 (Valkurm Dunes, 2026-11-06). Retail's `/screenshot` under Windower gave no reply and no file we could find, so this follows Windower's own screenshot naming.

## Profile character and online status (#179, #180)

- **Choosing the character (LandSandBoat).** A profile picks its character by `CharacterName` and/or `CharacterSlot` (editor drop-down "Character Slot": "Not set" or 1-16, stored as 0-16; it and the name field show only for Auto-Login). The slot is the 1-based position in the xi_view `0x20` list as the select screen shows it, counting free slots, so a character stays in slot 3 when slot 2 is empty (`LsbLoginClient.ParseCharacterSlotList`, `LsbCharacterSlot`). `ChooseCharacter` order: name (any case), then slot, then character id, then the first character (with a warning). A slot that is empty or past the list **throws** ("Character slot N is empty or not on this account") instead of logging in as another character. `CharacterSlot` is stored in the profile JSON next to the name; a profile saved before it existed has no key and loads as 0, so old profiles behave as before. The log line `LSB_LOGIN: Character choice: requested name=... slot=... -> slot N 'Name' (ID ...)` shows what was chosen. The editor's login mode radio picks "Character Select screen" or "Auto-Login" (`LoginMode`, saved with the profile; Auto-Login needs a name or a slot). A profile saved without the field auto-logs in when it names a character and opens the character select screen otherwise ([character-lobby.md](../design/character-lobby.md#launch), #32); before #32 such a profile logged in as the first character. Not done: listing the account's characters in the editor.
- **Online status** is per profile, not per account. `CharacterSession.ProfileName` is set when a profile launches the session; `SessionRegistry.IsProfileOnline(profileName, characterName)` matches that tag, or, for sessions without a tag (retail handoff), the profile's character name (or profile name). The account is never matched, so two characters of one account (multi-box) each show their own state in Profiles and Launch. Terminate on a profile ends only its own session. A launch is skipped when the profile is already online, and again after login if the resolved character id is already in the game (`IsCharacterIdActive`), which covers two profiles that resolve to the same character.

- **Trusting a server's certificate (#445).** The first LandSandBoat login to a server whose TLS certificate does not verify (usually a self-signed `login.cert`) shows a modal over the main window: "Trust the certificate of host:port?" with its SHA-256 fingerprint, subject, issuer and validity (selectable, so it can be compared with what the operator publishes). "Trust this certificate" pins it for that server and the login continues; "Don't connect" stops the login ("Login stopped: the server's certificate was not trusted."). A pinned server is not asked about again. If its certificate changes, the modal comes back with a red border, "Certificate of host:port changed", and the old and new fingerprints. The password is never sent before the server is trusted. Mechanism: [session-and-packets.md](../network/session-and-packets.md#auth-handshake-xi_connect).
- **One session per account.** LandSandBoat (like retail) denies a second login on an account and drops the first, so `MainWindowViewModel.GetLaunchSkipReason` refuses to launch a profile whose account already has a live session (`SessionRegistry.TryGetActiveAccountSession`): "Account 'X' already has a character in game (Knot). Skipped.". This is the only launch path (Launch selected). Online status and Terminate stay per profile. Detecting the first session being dropped by the server is not handled yet (separate issue).
- **Saving keeps the position.** Editing a profile without changing its folder puts it back at its old index in its folder (or the tree root) and in `Profiles`; a folder change appends it to the new folder.

## Viewport window shell (#319, #320, #302, #380)

- **Graphics backend:** chosen once, when the first viewport window creates the shared device. Settings > Display and the floating pill's **Graphics** drop-down change the saved choice; **Reopen viewport to apply** (shown only when the choice differs from the running backend) closes and reopens the main window and the pop-outs with the sessions connected. Details: [viewport-and-terrain.md](../rendering/viewport-and-terrain.md) (backend selection).
- **Floating pill:** its header has minimise and close buttons for the viewport window (useful in borderless and full-screen modes, which have no title bar). Closing leaves the sessions connected; Settings > Open Viewport Window reopens it.
- **PiP deck:** live thumbnails of the background characters ([viewport-and-terrain.md](../rendering/viewport-and-terrain.md#live-pip-thumbnails-302)).
- **ImGui input:** interactive Tier 3 overlays get the mouse and keyboard when visible; Ctrl+Shift+F11 shows a test window.
