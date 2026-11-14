# Logging

How GordianXI logs, where the files go, and how to turn detail up or down (#564). The logger is `GordianLog` in `src/Gordian.Core/Diagnostics`.

## Folders

Everything lives under the user data logs folder, `GordianStorage.LogsDirectory`. That is `logs/` next to the executable, or `%APPDATA%/GordianXI/logs` when the client runs from a protected folder such as Program Files. For the maintainer's Debug build it is `src/Gordian.App/bin/Debug/net10.0/logs`.

```
logs/
├── status/    status_2026-11-14_08-39-12.log …   one file per launch, kept 7 days
├── system/    gordian_2026-11-14.log, _2, _3 …   diagnostics, kept 7 days, rolls at 20 MB
├── network/   network_2026-11-14.log …           packet detail, opt-in, kept 7 days, rolls at 20 MB
└── chat/<server>/<character>/2026-11-14.log      chat logs (#565), never deleted
```

- **`system/`**: every category except status messages and packet detail. The file is named by the date a line is written, so a session that runs past midnight moves to the next day's file. When a file reaches the size cap the next lines go to `_2`, `_3`, and so on; a second launch on the same day continues the newest file.
- **`network/`**: the detail (Trace and Debug) of the network categories `NET`, `PARSER`, `CRYPTO` and `DISPATCHER`: datagram headers, sub-packets, unhandled packet ids, key exchange. Off by default. Their Info, Warn and Error lines (login steps, zone transitions) go to the system log as usual, and also here when the file is on, so it reads on its own.
- **`status/`**: the messages the user was shown in the Profiles and Launch status box (and the Controls tab's status line), one file per launch named by its start time. They are not repeated in the system log.
- **`chat/`**: the per-character chat logs (see [Chat logs](#chat-logs)).

At startup the writer thread deletes `system/`, `network/` and `status/` files last written more than 7 days ago. It never touches `chat/`, the pre-#564 files at the top of `logs/` (`gordian_system_<yyyyMMdd>.log`, kept by hand because some are cited as calibration evidence), or any other file.

## Chat logs

Every line a character's in-game log shows (say, shout, yell, tell, party, linkshell, unity, assist, emotes, NPC dialog, system, combat and battle messages, client notices and errors) is also written to `chat/<server>/<character>/<yyyy-MM-dd>.log` (#565):

```
[21:05:09] [Tell] Gemini>> meet at the Home Point
[21:05:12] [Combat] Gemini hits the Wild Rabbit for 12 points of damage.
```

- **`<server>`** is `Retail` for a retail profile and the server host for a LandSandBoat profile (`AccountProfile.ChatLogServerName`, set on `CharacterSession.ServerName`), so the same character name on two servers never shares a file. Server and character names keep their casing; characters a file system rejects become `_`, and an empty name becomes `Unknown` (`ChatLogFiles.FolderName`).
- **The date** is the local date the line was logged, so play past midnight moves to the next day's file. Chat files never roll by size and are never deleted.
- **The text** is the log line as plain text (`ChatLogFileWriter.PlainText`): element icons become their element words, line breaks become spaces, and the stock UI's drawing codes are dropped. Auto-translate phrases are written as the in-game log shows them; resolving them to words is #388.
- **How:** `ChatLogFileWriter` (`src/Gordian.Engine/Ui`), built by `CharacterSession`, listens to `StockUiChatLog.LineAdded` and hands each formatted line to `GordianLog.Chat`; the logger's writer thread opens and appends the files, so the network thread never waits on disk, and `Shutdown` flushes them.
- **Setting:** `"ChatLog": false` in `logging.json` turns it off (on by default).

## Line format

```
[08:39:14.512] [INFO] [NET] [Gemini] First datagram from the new map server accepted (Seq=3); zone transition complete.
```

Time, level, category, then the character tag when the line was logged inside a character's scope (see below). An error with an exception carries the full `ToString()`, stack trace included, on the following lines. Status lines are shorter: `[08:39:14] [Gemini] message`.

## Levels and categories

Levels, lowest first: `Trace`, `Debug`, `Info`, `Warn`, `Error`, plus `Status` for user-facing messages (never filtered). Each category has a minimum level; a line below it is dropped before its interpolated message is formatted (`GordianLog` takes `$"..."` through interpolated string handlers, so a disabled line costs one lookup and no allocation).

The default minimum is `Debug` in Debug builds or with `GORDIAN_DEBUG=1`, otherwise `Info`. Built-in overrides for the categories that flooded the log (measured from the maintainer's 2026-11-13 session log: 680,057 lines, 108 MB, of which `DEBUG NET` 437k, `INFO Locomotion` 149k, `DEBUG Graphics` 50k, `DEBUG ENTITY` 19k, `DEBUG GPU_TEX` 6.5k):

| Category | Built-in minimum | Why |
|---|---|---|
| `LOCOMOTION` | Warn | an Info line per remote entity movement packet |
| `GRAPHICS` | Info | per-frame and per-draw detail |
| `ENTITY` | Info | per-entity update detail |
| `GPU_TEX` | Info | per-texture upload detail |
| `NET`, `PARSER`, `CRYPTO`, `DISPATCHER` | Debug only into `network/` | packet detail |

The bounded wire trace (the first 20 map datagrams each way after login and each zone line, headers and sub-packets) uses its own `WIRE` category, which is not a network category, so it stays in the system log at Debug for login debugging without the network file.

Category names are `UPPER_SNAKE` constants in `LogCategories` (`src/Gordian.Core/Diagnostics/LogCategories.cs`). Add a constant there instead of passing a new string literal. Categories that only the logger and shell use: `APP` (startup, shutdown, unhandled exceptions), `AVALONIA` (Avalonia's own warnings and errors, such as binding errors, forwarded by `AvaloniaLogSink`), `LOGGING` (retention, dropped records), `PROFILE` (profile storage), `STATUS` (status messages).

## Settings

`logging.json` in `GordianStorage.UserSettingsDirectory` (`%LOCALAPPDATA%/GordianXI` on Windows). Every field is optional; a missing one keeps the built-in default, so the file never freezes an old default. Read at startup; the Logs tab (#566) will edit it and apply it live through `GordianLog.ApplySettings`.

```json
{
  "DefaultLevel": "Info",
  "Categories": { "LOCOMOTION": "Info", "GRAPHICS": "Debug" },
  "NetworkFile": true,
  "ChatLog": true,
  "RetentionDays": 7,
  "MaxFileBytes": 20971520
}
```

Turning `NetworkFile` on lowers the network categories to `Debug` unless `Categories` gives one its own level.

Environment overrides, for development:

| Variable | Effect |
|---|---|
| `GORDIAN_DEBUG=1` | default level `Debug` in a Release build |
| `GORDIAN_LOG_NETWORK_DISK=1` | network file on, whatever `logging.json` says |
| `GORDIAN_LOG_CONSOLE=1` | also write every line to standard output |

`GORDIAN_LOG_TO_DISK` is gone: the system log is always written.

## Character scope

Multi-box sessions share one process, so lines carry the character they belong to. `SessionNetworkManager.LogScope` (also `CharacterSession.LogScope`) is named from `CharacterName`.

- The network loops (inbound read, outbound flush, the 0x00A retransmit tasks) call `GordianLog.SetFlowScope(LogScope)` once at the top, so everything they run, including every packet handler in Core and the engine, logs under the character.
- Synchronous per-character work on a shared thread wraps itself in `using (GordianLog.BeginScope(session.LogScope))`; `MainWindow`'s input loop does this around `Locomotion.Update`. A thread scope does not survive an `await`.

## Writing

Records go onto a bounded queue (65,536) and one background thread writes them, flushing whenever the queue is empty. If the queue is ever full the record is dropped and counted, and the writer logs `N log record(s) dropped` under `LOGGING`. `GordianLog.Flush()` waits until everything logged so far is on disk; the shell calls `Shutdown()` on exit and flushes from the unhandled-exception handler. Files are opened shared, so they can be read and tailed while the client writes them.

Before `GordianLog.Initialize` runs nothing is logged, so tests and tools that use Core never write into a real logs folder.

## Status messages

`MainWindowViewModel.StatusMessage` and `ControlsInputViewModel.StatusMessage` write every non-empty message to the status file as it is set (`GordianLog.Status`), including a repeat of the previous one. A failure the user has to act on is a status message and also leaves a `Warn` or `Error` with the technical detail in the system log, since the system log does not repeat status messages.

## Verification

```bash
dotnet test tests/Gordian.Core.Tests --filter "FullyQualifiedName~Gordian.Core.Tests.Diagnostics"
```

`GordianLogTests` covers level gating (a disabled interpolated line is not formatted), routing of each category to its file, status files per launch, the character tag and full exceptions; `LogFilesTests` covers rolling, write-date naming, retention (chat is never deleted) and `logging.json`; `ChatLogFilesTests` covers chat folder naming.

```bash
dotnet test tests/Gordian.Engine.Tests --filter "FullyQualifiedName~ChatLogFileWriter"
```

`ChatLogFileWriterTests` covers the line format and plain text, per-day files across midnight, separate files per character, the setting and logout.
