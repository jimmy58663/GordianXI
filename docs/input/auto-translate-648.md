# Chat auto-translate (#648; partial scope of #388)

> **Independent-candidate scope:** This branch is based on upstream main without
> PR #646 or PR #647. Older native/GPU and owner results below describe the
> original stacked candidate, not this separated revision. See
> [independent review and test boundaries](independent-648.md).


Tab in an open **chat** field searches a fixed-phrase dictionary instead of cycling
targets. A unique prefix match is inserted immediately, without submitting. Outside
chat, target bindings are unchanged. Ctrl+Tab remains a viewport
shortcut; stock macro/name and numeric fields do not open completion.

## Supported interaction

- A typed prefix searches the longest matching word-boundary suffix before the
  caret, including multi-word and double-quoted prefixes. Matching ignores case.
- Empty input, or a caret after whitespace, browses categories. Enter on a category
  opens its phrases. Tab/Shift+Tab and Down/Up cycle; eight rows are visible.
- One prefix match: Tab inserts it immediately through the same acceptance path as
  Enter, preserving identity, caret and suffix. Chat stays open; it never auto-submits.
- Multiple matches and category browsing: Enter selects **without sending**.
  A subsequent Enter submits the draft.
  Escape dismisses the menu without losing the draft; a second Escape closes chat.
  No-match Enter does not submit. Other edits dismiss the menu.
- A selected phrase has explicit identity spans and green braces in the input.
  Typing inside or deleting any part converts that phrase to ordinary text;
  edits before it move its span. History preserves identities. Typing the same
  label or braces manually does **not** produce an auto-translate token.
- The existing 120-display-character editor cap remains. The complete encoded
  message is checked separately against 127 bytes after placeholder expansion (one of the 128 field bytes is
  reserved for the terminating NUL).

Prefix/category browsing and edit-to-plain semantics follow the
[official PlayOnline guide](https://www.playonline.com/ff11us/guide/system/dictionary01.html?pageID=system).
The precise arrow/Shift+Tab cycling and longest-suffix policy are Gordian choices,
not a claim of exhaustive retail keyboard parity.

## Data and protocol

`AutoTranslateCatalog` reads installed English `ROM/168/25.DAT` through the normal
ResourceManager/VFS. No phrase assets are bundled. **Differs from xi-tools:** the
observed category header is 76 bytes: four-byte key, two 32-byte CP932 names,
little-endian count at 68 and payload size at 72. Entries have a four-byte key,
one-byte length and CP932 text including a trailing NUL. Bounds, count/size,
identity, uniqueness and terminators are validated; invalid data fails closed.

References (schemas only; independently authored C#, no source copied):
- [xi-tools format notes](https://github.com/vekien/xi-tools/blob/main/docs/dats/ROM_168_25.md)
- [Windower ResourceExtractor linked-name schema](https://github.com/Windower/ResourceExtractor/blob/master/Program.cs)
- [Independent dictionary format description](https://github.com/ejouanchicot/ffxi-macro-editor/blob/main/docs/FORMAT.md)
- [#388 six-byte wire requirement](https://github.com/jimmy58663/GordianXI/issues/388)
- LandSandBoat C2S `0x0b5_chat_std.h`, `0x0b6_chat_name.h` (existing packet builders)

`@J`, `@A`, `@C`, `@Y` plus a hexadecimal index resolve respectively through the
existing Jobs, ZoneNames, Spells and Abilities DMsg tables. Read-only installed-data
verification consumed 36,798 bytes: 42 categories, 1,254 literal entries, 1,283
resolved references, 148 blanks; **2,537 selectable labels**, no unresolved or
non-ASCII labels in that install. Blank and unresolved rows are excluded, never
shown as fake names. Missing dictionaries/references produce menu notices. Installed
content is not evidence of a private server's gameplay availability.

`ChatText` keeps plain display text separate from immutable phrase spans. Only
explicit spans become `FD type language group id FD`; CP932 text cannot inject
an FD via a private-use Unicode character. Encoding validates before writing and
never splits/truncates a marker or multibyte character. Span packet builders retain
the allocation-free plain-text path. ChatPacketModule sends the same reliable
0x0B5/0x0B6 packets as before; synthetic loopback covers its outbound builder and
incoming dispatcher. Incoming fixed phrases resolve into the local English label;
unknown and malformed markers remain visible placeholders, not dropped bytes.

Chat routing supports default channels, channel slash aliases, tells (including
last partner) and local `/echo`. The router's message-body suffix is projected back
onto the identity spans; phrase identities in the recipient are rejected.
A complete leading slash-command phrase is consumed as syntax **only** when its
identity resolves in the installed catalog to the exact displayed local English
label, and the entire verb ends at whitespace or end-of-line. On explicit submission
it uses the ordinary command dispatcher and its existing policy; Tab never executes
it. No Unicode/braces stripping, guessed aliases, `!` phrase commands, partial verbs
or arbitrary recipient/action-argument conversion is performed. A command phrase
inside `/party`, `/tell`, `/say`, `/echo` or ordinary text remains a binary body token.
Use `/say ` explicitly to send a leading command phrase as conversation. Unknown or
mismatched identities do not gain execution authority.

Placeholder expansion transforms only plain rich-body segments, not phrase labels,
and cannot reparse an expanded rich body as a command. Other commands with remaining
rich arguments are refused explicitly, rather than silently flattened or executed
with altered syntax. This does **not** implement `/ma` or `/ja` phrase arguments.
Read-only installed-catalog inspection confirmed `/shutdown` and `/logout` in the
Text Commands category. The official guide documents selected phrases and macro
usage, but does not specify every command-position rule; this narrow routing policy
is a Gordian choice, not a claim of complete retail parity.

## Limits and acceptance gates

This is the installed **English fixed dictionary**, not all of #388: no dynamic
recent-item/key-item catalog, macro editing/import, completion of `/ma`/`/ja` command
arguments, non-English UI/IME, mouse/controller completion or settings for alternative
Tab behavior. Existing ASCII editor/font limits remain. The catalog is a per-resource
snapshot attached when a viewport session is added; live dictionary replacement is
not an acceptance claim. Ordinary command handling and server restriction gates are
unchanged. No actual server login or public message was used for testing.

Tests include synthetic DAT fixtures, malformed data, identity editing/history,
quoted prefixes, category/cancellation behavior, plain-text nonconversion, chat/tell
packet bytes, incoming synthetic loopback, multibyte/expanded byte budgets and CP932
marker-injection rejection. Negative controls remove rendering, change FD to FE,
and restore the old native Tab route; each fails its corresponding assertion.

Use the upstream package sources (no private graphics dependency), then run:

```sh
# Use an absolute operator-owned TMPDIR (shared /tmp/gordian-tests may be foreign-owned).
dotnet test GordianXI.slnx
dotnet build tests/Gordian.InputProbe
python3 tests/Gordian.InputProbe/run-probe.py
python3 tests/Gordian.InputProbe/run-probe.py --input-layout de
```

Select Xvfb as described in
[the independent input guide](independent-648.md). The runner isolates
HOME/XDG/TMP, display authorization and settings; it does not borrow an owner session.
For **optional read-only installed-art** testing set `GORDIAN_COMPLETION_GAME_ROOT`
to the actual FFXI DAT root. The input probe then draws the production `DrawInput`
completion menu to an offscreen software-Vulkan framebuffer with the installed stock
font/frame and compares GPU readback against the closed menu. This is separate from
its real XTEST → Avalonia → production viewport input path; it is not an in-game
render or server acceptance test. Without that variable it explicitly reports the
art test not run. No game assets are copied into tests.

Native US/German Compact/Full input and strict software Vulkan validation pass,
including prefix/category selection, edit/cancel, Ctrl+Tab, macro/number exclusion
and outside-chat targeting. The original viewport lifecycle probe also passes on
software Vulkan. Windows, owner compositor/controller and contributor in-game
acceptance remain pending. Do not close #388 or call this full retail parity.

AI assistance: implementation, tests and this guide prepared with Hermes Agent.

## Single-match / command follow-up evidence

Starting at `9aec550cd68c491ceb1eaa4cf5ea72942385cf92`, the Linux solution passes
**5,044 tests** (5,019 baseline plus 25 cases), zero failures/skips. Existing compiler
and xUnit analyzer warnings remain. Native XTEST US/German probes pass on both
software Vulkan and NVIDIA with strict validation and production completion-menu
GPU readback from read-only installed art. Tests never authenticate or send to a
server: native command probes replace dispatch with fixture sinks, while CPU tests
use the real router, action service and lifecycle packet builder with an in-memory
packet sink. Existing binary chat/tell and incoming-loopback regressions also pass.

Independent old-source controls prove both changes: restoring only the old input
source fails unique native Tab acceptance; restoring only the old chat router fails
native explicit-Enter phrase-command dispatch. With the old chat source, all four
plain `/shutdown`/`/logout` lifecycle cases still pass and all four phrase cases fail.
Restoring the candidate returns the full suite and native probe to GREEN.

**Plain `/shutdown` was investigated separately, not claimed fixed.** It already
parses and reaches the lifecycle dispatcher without a confirmation prompt, emitting
synthetic bytes `E7 04 00 00 00 00 03 00` (C2S `0x0E7`, Toggle, Shutdown), recording
pending shutdown and printing `Shutdown requested.`. `on` emits mode 3 and `off`
mode 2; logout uses kind 1. The menu route separately asks `Shut down?`, defaults to
No, cancels without action, and requests shutdown only after Yes. Those paths have
regressions. No host shutdown is involved. A successful outbound request does not
prove server acceptance or immediate exit: the existing lifecycle protocol waits
for the server response, and standing can cancel its countdown. We did not
observe the owner's state, server response or logs. If plain `/shutdown` still fails,
collect the exact displayed message (or absence) and whether it prints a countdown;
do not attribute it to typo, Unicode or this rich-phrase limitation without evidence.

Safe first owner check after a stopped-only, clean-tree update: type `/shut` in an
open chat field, press Tab once, verify the complete marked phrase and still-open
draft, then **Escape**, not Enter. Actual command/server and privately consenting
recipient acceptance remain owner-only gates. Never use `/shutdown` Enter as an
automatic deployment smoke test. No settings reset, camera/controller changes,
macro phrase support or graphics-lifecycle repair is part of this follow-up.


## Combined controls candidate

The #648 integration also retains per-character first-/third-person camera inversion
and Full/Compact 1/Compact 2 keyboard policy. The combined engine theory exercises
selection without submission, phrase editing, all keyboard modes, both camera axes
and views, independent characters, shared-profile refresh and fresh persisted-setting
loads. Disabling completion fails all four combined cases; the earlier camera and
keyboard negative controls remain separately recorded.

The combined Linux solution runs 4,921 test cases (4,845 prerequisite baseline plus
76 additions across the three tracks and integration), with zero reported failures
or skips. Native US/German input and production-menu GPU readback pass under both
software Vulkan and NVIDIA, including actual Ctrl+Tab switching between disconnected
characters, draft preservation, Full first-character-once and macro editor priority.
Both default strict graphics/lifecycle probes pass. These are isolated X11 results,
not owner compositor, physical controller, Windows or live-server acceptance. The
previously observed extended NVIDIA warm-retirement failure remains unresolved and
outside this input change; default-probe success does not supersede that failure.
