# Desktop Shell & Account Profiles

> Phase 1 foundation of `Gordian.App`: the Avalonia control panel, account profiles and the packet inspector. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Foundation (Phase 1)

- Four-tier decoupled monorepo (`Gordian.Core`, `Gordian.Automation`, `Gordian.Addons`, `Gordian.App`)
- Clean-room reverse engineering boundaries enforced in `AGENTS.md`
- Modern dark HUD UI with Avalonia MVVM
- Encrypted profile storage with master AES key and defensive plaintext migration
- Account profile management (Add, Edit, Delete, Select for Launch)
- Profile & Global session termination commands (`TerminateCommand`, `TerminateAllCommand`)
- Live Packet Inspector UI with Hex/ASCII view, pause/clear, and real-time capture

## Profile character and online status (#179, #180)

- **Choosing the character (LandSandBoat).** A profile picks its character by `CharacterName` and/or `CharacterSlot` (editor field "Character Slot", 1-16, 0 = not set). The slot is the 1-based position in the xi_view `0x20` list as the select screen shows it, counting free slots, so a character stays in slot 3 when slot 2 is empty (`LsbLoginClient.ParseCharacterSlotList`, `LsbCharacterSlot`). `ChooseCharacter` order: name (any case), then slot, then character id, then the first character (with a warning). A slot that is empty or past the list **throws** ("Character slot N is empty or not on this account") instead of logging in as another character. `CharacterSlot` is stored in the profile JSON next to the name; a profile saved before it existed has no key and loads as 0, so old profiles behave as before. The log line `LSB_LOGIN: Character choice: requested name=... slot=... -> slot N 'Name' (ID ...)` shows what was chosen. A profile with neither a name nor a slot opens the character select screen instead ([character-lobby.md](../design/character-lobby.md#launch), #32); before #32 it logged in as the first character. Not done: listing the account's characters in the editor.
- **Online status** is per profile, not per account. `CharacterSession.ProfileName` is set when a profile launches the session; `SessionRegistry.IsProfileOnline(profileName, characterName)` matches that tag, or, for sessions without a tag (retail handoff), the profile's character name (or profile name). The account is never matched, so two characters of one account (multi-box) each show their own state in Profiles and Launch. Terminate on a profile ends only its own session. A launch is skipped when the profile is already online, and again after login if the resolved character id is already in the game (`IsCharacterIdActive`), which covers two profiles that resolve to the same character.

- **One session per account.** LandSandBoat (like retail) denies a second login on an account and drops the first, so `MainWindowViewModel.GetLaunchSkipReason` refuses to launch a profile whose account already has a live session (`SessionRegistry.TryGetActiveAccountSession`): "Account 'X' already has a character in game (Knot). Skipped.". This is the only launch path (Launch selected). Online status and Terminate stay per profile. Detecting the first session being dropped by the server is not handled yet (separate issue).
- **Saving keeps the position.** Editing a profile without changing its folder puts it back at its old index in its folder (or the tree root) and in `Profiles`; a folder change appends it to the new folder.
