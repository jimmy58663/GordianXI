# Desktop Shell & Account Profiles

> Phase 1 foundation of `Gordian.App`: the Avalonia control panel, account profiles and the packet inspector. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Foundation (Phase 1)

- Decoupled monorepo (`Gordian.Core`, `Gordian.Engine`, `Gordian.Automation`, `Gordian.Addons`, `Gordian.App`); layers in [architecture.md](../architecture.md)
- Clean-room reverse engineering boundaries enforced in `AGENTS.md`
- Modern dark HUD UI with Avalonia MVVM
- Encrypted profile storage with master AES key and defensive plaintext migration
- Account profile management (Add, Edit, Delete, Select for Launch)
- The profile editor lays its fields out in two columns (the server and accounts on the left; two-factor, login mode and game folder on the right) and drops to one column when the editor is narrower than 860 px (a `ContainerQuery` on the editor border). Save Profile and Cancel stay fixed under the scrolling form, like the launch bar under the profile list.
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

## Profile list and editor (#527)

The Profiles and Launch tab shows one of two views. The **launch list** fills the tab: folders, drag and drop, launch checkboxes, the **+ New Profile** button and the launch bar. **+ New Profile** or a card's **Edit** (or **Copy**) swaps it for the **editor**, and Save or Cancel swaps back (`MainWindowViewModel.IsEditorOpen` / `IsListVisible`). A new profile goes in the folder highlighted in the list (or the folder of the highlighted profile), the root otherwise. After that, dragging moves it: the editor has no folder field.

The editor's **Server** toggle (`AccountProfile.ServerType`) decides which fields show. Save keeps only the chosen type's fields.

| Field | Retail | LandSandBoat | Stored as |
|---|---|---|---|
| Profile Name | ✓ | ✓ | `ProfileName` |
| Server Host and Port | | ✓ (required) | `ServerHost`, `ServerPort` (54231) |
| Advanced: data and view ports | | ✓ | `DataPort`, `ViewPort` (0 = 54230 / 54001) |
| POL ID, POL Password | ✓ (8 and up to 16 characters) | | `PolId`, `PolPassword` (encrypted) |
| Square Enix ID / Account Name, Password | ✓ (ID up to 16 characters) | ✓ | `Username`, `Password` (encrypted) |
| Two-factor authentication | ✓ | ✓ | `TwoFactor`, `OtpSeed` (encrypted) |
| Login: Character Select / Auto-Login | ✓ | ✓ | `LoginMode`, `CharacterName`, `CharacterSlot` |
| Game Folder | ✓ | ✓ | `GameDirectory` |

The retail field limits come from [retail-login-spec.md](../network/retail-login-spec.md) section 6.1. **Show passwords** reveals the password fields and the authenticator key.

- **Two-factor modes** (`TwoFactorMode`):
  - **None**: no code is sent.
  - **Authenticator key (automatic)**: the Base32 key from the authenticator setup is stored, and the code is computed at every login (`TotpEngine`). The key must be at least 16 Base32 characters; spaces, dashes and `=` padding are ignored. The card shows the current code.
  - **Enter code at launch**: the card gets a 6-digit code box. Launch uses the code once and clears it (`AccountProfile.EnteredOneTimeCode`, never saved), so a later login, such as the return to character select after Log Out, needs a new code typed on the card. A profile in this mode with no valid code is not launched.
  - Switching modes keeps a stored key, because an authenticator key is hard to get again.
- **Game Folder** is empty for the install the registry names. Fill it in only for a separate install, such as a private server's own client: Horizon ships one with changed DATs applied through XIPivot, and it can lag behind retail. Save checks that the folder has `VTABLE.DAT` and `FTABLE.DAT`, and a 📦 on the card marks a profile with its own folder. One install is loaded per GordianXI process (`AppResourceManager`). The first launch loads the profile's folder, or the registry install when the profile has none or the viewport opened first. A later profile that names a different install is not launched ("Restart GordianXI to switch installs"): the zone, model and UI caches and the static DAT loaders are shared by all sessions.
- **Retail launch.** Direct retail login is not built yet, so a retail profile is refused with "direct retail login is not available yet". The exception is an older profile that still has a bootloader path: it goes through the old pol.exe and `ProxyStager` path, which stays until direct retail login is proven in testing.
- **Older profiles** load without the new fields and keep working:
  - The server type follows their fields: a server host, an xiloader bootloader, or a `--server`, `127.0.0.1` or `localhost` argument makes a LandSandBoat profile; anything else is retail.
  - Their host and ports come from the old bootloader arguments (`--server` / `-s`, `--authport`, `--dataport`, `--viewport`; xiloader with no `--server` means 127.0.0.1) until the profile is saved, which writes them to the new fields.
  - A stored key means the authenticator-key mode.
  - The old Folder Group, Target Bootloader and Custom Arguments fields are gone from the editor. `BootloaderPath` and `Arguments` stay in the file for the old retail path.

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
