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

`--profile` takes retail and private server profiles alike, with the same checks as the Launch button; a retail profile logs in to Square Enix's live servers. Profile passwords are encrypted with the per-user key in `~/.gordian_key`, so a shared folder works from every build under the same user. Selection state for the launch list is not changed.

## Status box and Logs tab (#566)

The **status box** under the Profiles and Launch tab lists this launch's latest status messages (the last 50, 13 px monospace, newest at the bottom, `MainWindowViewModel.RecentStatusLines`) instead of only the newest one. It is a fixed five lines tall and scrolls; it does not grow as messages arrive. **View full log** opens the Logs tab on this launch's status file.

The **Logs** tab is the second tab (`MainWindowViewModel.LogsTabIndex`):

- **Left: the logs folder.** **Open logs folder** opens `GordianLog.LogsDirectory` in the OS file manager through Avalonia's `Launcher.LaunchDirectoryInfoAsync`: the folder the logger writes to, next to the exe or in the user's application data folder when the exe is in a protected folder such as Program Files (`GordianStorage.ResolveRootDataDirectory`). It is never a path built from `%LOCALAPPDATA%` or `%APPDATA%`. **Status (this session)** is pinned above the tree. The tree (`LogTreeBuilder`) lists `status`, `system`, `network` and `chat` first, then other folders, then loose files (the pre-#564 logs); files are newest first. A `FileSystemWatcher` marks it for rebuild when files appear or go, keeping expanded folders and the highlighted file; **Refresh** rebuilds it by hand.
- **Right: a terminal-style viewer.** 13 px monospace on near-black, virtualized. A file loads off the UI thread (`LogFileReader`); one larger than 32 MB loads only its last 32 MB, which the header line says. While the file grows (this launch's status file, today's system or chat file) the view's half-second timer calls `LogsViewModel.Poll`, which reads only the appended lines; **Follow** keeps the newest in view. Search filters lines as you type and highlights the matching text (`LogLineSegments`, drawn by `LogLineTextBlock`); the level filter (system and network files) keeps a stack trace with the error above it. Each line is coloured per token: timestamp sage green, level by severity, category purple, character or channel teal, message by level. A newly shown file or filter starts at the top, then follows to the newest line, so a short file always sits at the top. **Copy** copies the selected lines, or all shown lines when none is selected.
- **Logging settings** (expander): the default level, per-category levels (each shows its built-in default), the network packet log and chat logs. **Save and apply** writes `logging.json` with only what differs from the defaults and applies it to the running logger at once (`LoggingSettingsViewModel`); retention and file size keep the file's values and apply at the next launch. Details: [logging.md](logging.md).

## Character config folders (CharConfig)

Everything GordianXI keeps for one character is in one folder under `GordianStorage.RootDataDirectory` (#639): server, then world, then character:

```
CharConfig/
  127.0.0.1/            (a LandSandBoat profile's server host)
    Gordian/            (the world the server names)
      Akaden/
        macros.json             MacroStore
        equipment_sets.json     EquipmentSetStore
        ui_layout.json          StockUiLayoutStore
        ui_settings.json        StockUiSettingsStore
        synthesis_history.json  SynthesisHistoryStore
  Retail/
    Asura/
      Akaden/
        ...
```

- **The folder key** is `CharacterFolderKey(Server, World, Character)`. `CharacterSession.Folder` builds it, and the stores and the chat logs (`logs/chat/<server>/<world>/<character>/`, [logging.md](logging.md)) both use it, so a character's config and chat logs sit under matching folders. The same character name on two servers or two worlds never shares macros, sets or layouts.
- **The server** is the launching profile's `AccountProfile.ServerFolderName`: `Retail`, or a LandSandBoat profile's server host in lower case (host names ignore case, so `Play.Example.com` and `play.example.com` share a folder on Linux too). The port is not part of it, so two servers on one host still share a folder.
- **The world** is the one the lobby's character list gives the character (`LobbyCharacter.WorldName`): a retail world such as Asura, or the name a LandSandBoat server gives its own. Both ticket builders (`XiloaderLobbyHandoff.CreateTicket`, `PolLobbyHandoff.CreateTicket`) copy it to `MapSessionTicket.WorldName`, and the launcher passes it to `CharacterSession` as `worldName`. Every server type gets the world folder, retail and LandSandBoat alike.
- **Names** go through `GordianStorage.SafeFolderName`: casing is kept, characters a file system rejects become `_`, an empty name becomes `Unknown`, and a Windows device name gets a trailing `_` on every platform (a character named Con keeps its files in `Con_/`, since Windows can't make a `Con` folder). `CharacterConfigStorage` builds every path with `Path.Combine`.
- **Older builds** kept each kind of file in its own flat folder named by character only (`macros/<name>.json`, `equipment_sets/`, `ui_layouts/`, `ui_settings/`, `synthesis_history/`). Those files carry no server or world, so the first character of that name to log in, anywhere, takes them: `CharacterConfigStorage.Resolve` moves the file into that character's folder when the character has none there yet, logs `STORAGE: Moved '<old>' to '<new>'.`, and removes the old folder once it is empty. A same-named character elsewhere then starts empty. If the move fails, the character keeps using the old file until the app closes (logged as a warning), and the move is tried again at the next launch. Tested in `CharacterConfigStorageTests` (the paths and the move), `CharacterConfigMigrationTests` (each store taking an old file) and `CharacterConfigStoresTests` (the separate folders per server and world). Each login logs `STORAGE: '<character>' keeps its config and chat logs under '<server>/<world>/<character>'.`, and a warning when the server or world name is missing.

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
- **Default FFXI install** (the Profiles and Launch tab, above the list; `ProfileStoreDocument.DefaultGameDirectory` in `profiles.json`, #652): the install new profiles start with and the one a profile without a folder uses (`GameDirectoryDetector.DetectGameDirectory`: this folder, else the registry's). Off Windows there is no registry, so this is where the install is set once; empty shows the registry's install as the placeholder. A Linux player with retail's client and a private server's own client (Horizon) sets the retail one here and the other on that server's profiles.
- **Game Folder** starts as the default install in a new profile; change it for a separate install, such as a private server's own client: Horizon ships one with changed DATs applied through XIPivot, and it can lag behind retail. Empty means the default install. Save checks that the folder has `VTABLE.DAT` and `FTABLE.DAT`, and a 📦 on the card marks a profile whose folder differs from the default. The LandSandBoat lobby login reports this install's FFXI version (`patch.cfg`), and a launch stops if it can't be read ([session-and-packets.md](../network/session-and-packets.md#lobby-over-tls-xiloader-30-656)). One install is loaded at a time (`AppResourceManager`): the zone, model and UI caches and the static DAT loaders are shared by all sessions, so two installs are never mixed. A launch loads the profile's folder (or the default install when it has none), and opening the viewport with no lobby or character loads the default install. The install switches without a restart (#665), but only while nothing uses the loaded one: no viewport window open (the lobby reads the DATs too), no character logged in (collision and events read them with every window closed) and no launch still logging in (`AppResourceManager.LaunchHold`, so one batch never switches under itself). Otherwise the profile is not launched, with a message naming both installs: "Close the game windows and log out of the characters on '<A>' to switch." Switching only happens when an install is about to be used: changing a profile's folder or the default install in the settings switches nothing until the next launch or empty viewport. A switch resets the shared DAT caches (`ResourceManager.ResetSharedCaches`, the same reset `/reloaddats` runs), points item names and the sound library at the new install, disposes the old manager and logs `RES: install switched '<A>' -> '<B>'`. Cache generations are unique across the process, so everything keyed by `CacheGeneration` (GPU model keys, the stock UI library, item icons, footwear) rebuilds as after a VFS reload. The GPU texture, model and zone caches live on the shared graphics device, which is disposed with the last viewport window, and a switch waits until it is gone. Two installs at the same time need a second portable copy of GordianXI (#567). Tested with two fake installs (`AppResourceManagerSwitchTests`).
- **Retail launch.** A retail profile logs in directly ([network/retail-login.md](../network/retail-login.md)). No bootloader is started and the game folder is not changed.
- **Older profiles** load without the new fields and keep working:
  - Without a server type, a profile is LandSandBoat when it names a server host and retail otherwise.
  - The removed `BootloaderPath` and `Arguments` fields are ignored when a file loads and dropped when it is saved. They are not migrated: the bootloader launch was never distributed.
  - A stored key means the authenticator-key mode.
  - The old Folder Group, Target Bootloader and Custom Arguments fields are gone from the editor.

## LSB Accounts tab (#652)

The second tab (`LsbAccountsViewModel`) manages LandSandBoat accounts as xiloader's menus do, over the xi_connect account commands ([session-and-packets.md](../network/session-and-packets.md#account-management-652)). Retail accounts are managed on Square Enix's account website and are not here.

- **Server and account.** Host, auth port and account name, typed or filled from a saved LandSandBoat profile (which also fills its password). The tab lists the saved profiles of this account on this server (same host, auth port and account name, case-insensitive).
- **Create account.** Password and confirmation (6-32 printable ASCII characters; names 1-16). On success the editor opens on the Profiles tab with a new profile filled in: name (numbered if taken), server, ports, account, password and the default install (`MainWindowViewModel.OpenPrefilledProfile`); nothing is saved until Save. With **Set up two-factor for the new account** ticked, the two-factor setup below runs first (QR code and key, Fill code, Confirm); the new profile gets the key in Authenticator key mode, and the editor opens from **Continue to the new profile** under the recovery code, so the code is seen before the tab changes. Cancel, or a setup the server refuses, opens the profile without two-factor.
- **Change password.** Current password, new password and confirmation, and the two-factor code if the account has one. With **Update saved profiles of this account** on (the default), the new password is saved to those profiles.
- **Two-factor.** **Set Up Two-factor** shows the server's key as a QR code (QRCoder, rebuilt `otpauth://` link) and as text in groups of four with **Copy key**; **Fill code** makes the current code from that key (or from a saved profile's key), and **Confirm Two-factor** turns it on. The login is unchanged until it is confirmed; Cancel leaves it off. On confirmation the recovery code is shown once (with **Copy recovery code**; never saved), and with Update saved profiles on the profiles get the key in **Authenticator key (automatic)** mode, so GordianXI makes their codes. **Remove Two-factor** and **New Recovery Code** take a current code or the recovery code; removal also clears the profiles' key. When no code is typed, a saved profile's key makes one.
- Passwords and keys are stored only in profiles (encrypted, as the editor stores them). The server's certificate is trusted on first use with the same prompt and `known_servers.json` as the login.

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
