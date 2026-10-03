# Event Opcodes

Every opcode of the retail event VM (0x00-0xD9), what it does, and what GordianXI does with it. Written 2026-10-01 from the XiEvents opcode notes (https://github.com/atom0s/XiEvents, `OpCodes/`), the PS2-era names and typed layouts in xi-tools (https://github.com/vekien/xi-tools, `docs/events/`), GordianXI's `EventVm` and the retail event DATs; everything is restated in our own words. How the VM runs (request stacks, operands, waits, cutscene staging) is in [vm.md](vm.md); how often retail uses each opcode and sub-case is in [opcode-usage.md](opcode-usage.md).

Where a finding goes beyond or disagrees with a reference it is marked **Beyond <source>:** or **Differs from <source>:**, so it can be shared back.

**Status** (checked against the case labels of `EventVm.Step` and the helpers they call):
- `runs`: GordianXI implements it.
- `partial`: some sub-cases or side effects only; the detail section says which.
- `stepped`: skipped by its length (`EventOpcodeTable`) and reported through `IEventVmHost.OnSkippedOpcode` (EVENT, Debug).
- `ignored`: skipped, or passed at once, by design and without a diagnostic.
- `ends`: GordianXI ends the request when it meets it.
- `no length`: the length table has no length, so the request ends there.

**Events** is the number of retail event entries that use the opcode, from [opcode-usage.md](opcode-usage.md) (see its caveats: script tables walked as code inflate a few rows). **Operands** in the detail sections: `work` is a 16-bit work reference (bit 15 set = immediate value), `actor:u32` an actor id as [vm.md](vm.md#entities-and-request-stacks) describes.

When a change makes an opcode run, update its row and detail section in the same PR, and regenerate [opcode-usage.md](opcode-usage.md) if `EventOpcodeTable` changed. `EventOpcodeCensusTests.OpcodesDoc_HasEveryOpcode` fails when an opcode has no row here.

## Summary

| Op | Name | Bytes | Status | Events | What it does |
|---|---|---|---|---:|---|
| 0x00 | | 1 | runs | 207,922 | Frees the running request stack; the event ends once no entity that carries it has a stack left. |
| 0x01 | | 3 | runs | 21,496 | Jumps to an absolute code offset (a goto: nothing is pushed). |
| 0x02 | `CodeIF` | 8 | runs | 20,872 | Compares two work values by one of eleven tests and jumps to an absolute offset when the test holds. |
| 0x03 | | 5 | runs | 23,916 | Copies the second work value into the first. |
| 0x04 | | 3 | ignored | 707 | Does nothing in retail; stepped over by its length without a diagnostic. |
| 0x05 | | 3 | runs | 4,761 | Sets a work value to 1. |
| 0x06 | | 3 | runs | 5,677 | Sets a work value to 0. |
| 0x07 | | 5 | runs | 3,623 | Adds the second work value to the first. |
| 0x08 | | 5 | runs | 3,798 | Subtracts the second work value from the first. |
| 0x09 | | 5 | runs | 662 | Sets bit n (the second value) of the first work value. |
| 0x0A | | 5 | runs | 264 | Clears bit n (the second value) of the first work value. |
| 0x0B | | 3 | runs | 4,927 | Adds 1 to a work value. |
| 0x0C | | 3 | runs | 1,929 | Subtracts 1 from a work value. |
| 0x0D | | 5 | runs | 98 | Bitwise AND of two work values into the first. |
| 0x0E | | 5 | runs | 544 | Bitwise OR of two work values into the first. |
| 0x0F | | 5 | runs | 1,179 | Bitwise XOR of two work values into the first. |
| 0x10 | | 5 | runs | 1,143 | Shifts the first work value left by the second. |
| 0x11 | | 5 | runs | 158 | Shifts the first work value right by the second. |
| 0x12 | | 3 | runs | 28 | Stores a random number. |
| 0x13 | | 5 | runs | 1,084 | Stores a random number from 0 to the second work value. |
| 0x14 | | 5 | runs | 2,037 | Multiplies the first work value by the second. |
| 0x15 | | 5 | runs | 3,496 | Divides the first work value by the second (0 when either is 0). |
| 0x16 | | 7 | runs | 671 | Stores -r sin(angle) of a radius and a 4096-step angle. [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x17 | | 7 | runs | 1,239 | Stores r cos(angle) of a radius and a 4096-step angle. [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x18 | | 7 | runs | 63 | Stores the angle (atan2) of a vector, 8192 steps to a turn; every corpus hit is table data. [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x19 | | 5 | runs | 22 | Exchanges two work values. |
| 0x1A | | 3 | runs | 10,304 | Calls: pushes the return position on the 8-deep jump stack and jumps. |
| 0x1B | | 1 | runs | 4,980 | Returns from the last call; with nothing to return to it frees the running stack. |
| 0x1C | | 3 | runs | 28,161 | Waits a number of 60 Hz frames, yielding each frame. |
| 0x1D | | 3 | runs | 17,009 | Prints a dialog message with the event's own entity as the speaker. |
| 0x1E | | 5 | runs | 16,101 | Turns the event's entity toward an actor and has it look at the actor ([#174](https://github.com/jimmy58663/GordianXI/issues/174)). |
| 0x1F | `CodeMOVE` | by sub | runs | 25,020 | Stores a walk goal (sub 0), then walks the entity there on the ground one frame at a time (sub 1). |
| 0x20 | | 2 | runs | 15,139 | Locks or releases the player's control of the character. |
| 0x21 | | 1 | runs | 31,959 | Ends the whole event on every entity. |
| 0x22 | | 2 | runs | 10,789 | Sets or clears the event hide flag of the event's own entity. |
| 0x23 | `CodeMESWAIT` | 1 | runs | 33,114 | Waits while a printed message is open. |
| 0x24 | `CodeQUERY` | 7 | runs | 15,194 | Opens a choice menu from a message, a default option and a hidden-option mask. |
| 0x25 | `CodeQUERYWAIT` | 1 | runs | 15,207 | Waits for the choice; stores it in zone work value 0, or 254 and ends the event when cancelled. |
| 0x26 | | 1 | ends | 247 | Yields forever in retail; GordianXI frees the running stack instead. |
| 0x27 | | 7 | runs | 5,059 | Queues the event at a slot of another entity's own offset table and goes on. |
| 0x28 | `CodeREQSW` | 7 | runs | 244 | Queues the event at a slot of another entity and waits until the entity has started it. |
| 0x29 | `CodeREQEW` | 7 | runs | 10,036 | Queues the event at a slot of another entity and waits until the entity has finished it. |
| 0x2A | | 6 | runs | 2,716 | Waits until another entity has no request at or above a priority. |
| 0x2B | | 7 | runs | 6,344 | Prints a dialog message with a named actor as the speaker. |
| 0x2C | `CodeSCHEDULOR` | 13 | runs | 3,340 | Plays one of the actor's own motion routines toward a target ([#165](https://github.com/jimmy58663/GordianXI/issues/165)). |
| 0x2D | `CodeMAPSCHEDULOR` | 13 | stepped | 464 | Starts a zone scheduler action on two actors (doors and set pieces). |
| 0x2E | | 1 | stepped | 1,044 | Arms the event cancel data flag (and the cancel flag when cancelling is allowed). |
| 0x2F | | 6 | ignored | 10,668 | Sets or clears `Render.Flags0` bit 19 of an actor. |
| 0x30 | | 1 | stepped | 848 | Clears the client's "continue after control release" flag. |
| 0x31 | `CodeSMOVE` | by sub | stepped | 308 | A floor-following walk in three axes with a time operand. |
| 0x32 | | 3 | runs | 19,170 | Sets the entity's walk speed in tenths of a yalm per second. |
| 0x33 | | 2 | runs | 1,627 | Sets or clears `Render.Flags0` bit 21 of the event's own entity: the event keeps its placed height instead of the floor ([#192](https://github.com/jimmy58663/GordianXI/issues/192)). [#198](https://github.com/jimmy58663/GordianXI/issues/198). |
| 0x34 | | 3 | runs | 1,297 | Removes every actor and opens another zone for the scene ([#175](https://github.com/jimmy58663/GordianXI/issues/175)). |
| 0x35 | | 3 | runs | 882 | Like 0x34 without closing the current zone; the intros use it to return to their own zone. |
| 0x36 | | 7 | runs | 883 | Places the event's entity at x, y, height. |
| 0x37 | | 9 | runs | 12,635 | Places the event's entity and sets its heading. |
| 0x38 | | 3 | partial | 5,425 | Sets the low word of the event mode mask (`CliEventModeLocal`); GordianXI records it only. |
| 0x39 | | 3 | runs | 1,065 | Sets the event's entity heading (4096 steps per turn). |
| 0x3A | | 7 | runs | 588 | Reads an actor's heading into a work value. [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x3B | | 11 | runs | 783 | Reads an actor's position into three work values. [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x3C | | 7 | runs | 3,082 | Sets bit n of a run of work values, when n is inside the run. |
| 0x3D | | 7 | runs | 7,788 | Clears bit n of a run of work values, when n is inside the run. |
| 0x3E | | 7 | runs | 2,842 | Tests bit n of a run of work values and jumps when it is clear. |
| 0x3F | | 7 | runs | 944 | Stores the remainder of two work values (0 when either is 0). |
| 0x40 | `CodeSETBITWORK` | 9 | runs | 9,696 | Writes a value into a bit field (bits from..to) of a work value. |
| 0x41 | `CodeGETBITWORK` | 9 | runs | 3,796 | Reads a bit field (bits from..to) of a work value. |
| 0x42 | | 1 | ignored | 19,085 | Clears the event cancel data flag (and the cancel flag when cancelling is allowed). |
| 0x43 | | 2 | runs | 6,818 | Sends the event update (C2S 0x05B mode 1) or waits for the server's answer. |
| 0x44 | | 5 | runs | 74 | Jumps when the server id in a work value names no entity in the zone. |
| 0x45 | `CodeLOADSCHEDULER` | 17 | runs | 10,696 | Plays a routine of a scene resource DAT as a task on two actors: camera shots and fades ([#165](https://github.com/jimmy58663/GordianXI/issues/165)). |
| 0x46 | `CodeDEFCAMERA` | by sub | runs | 5,811 | Takes the camera from the player, gives it back, or reads who has it. |
| 0x47 | | by sub | runs | 1,931 | Sends a position update (C2S 0x05C) for the player or waits for the answer. |
| 0x48 | | 3 | runs | 12,944 | Prints a dialog message with no speaker (narration). |
| 0x49 | | 7 | runs | 52 | Prints a dialog message with no speaker prefix, an actor's name bound for the text. |
| 0x4A | `CodeDTURA` | 9 | partial | 8,753 | Turns one actor toward another and has it look at it. |
| 0x4B | | 7 | partial | 2,253 | Sets an actor's heading. |
| 0x4C | | 1 | runs | 884 | Opens the event's entity as a door (event status 8, `WorldEntity.EventStatus`, [#200](https://github.com/jimmy58663/GordianXI/issues/200)); the door opens through `DoorAnimator` ([#15](https://github.com/jimmy58663/GordianXI/issues/15)). |
| 0x4D | | 1 | runs | 777 | Closes the event's entity as a door (event status 9, [#200](https://github.com/jimmy58663/GordianXI/issues/200)). |
| 0x4E | | 6 | runs | 4,334 | Sets or clears the event hide flag of a named actor. |
| 0x4F | | 3 | stepped | 77 | Sets the event's entity event status to a value plus 18. |
| 0x50 | `CodeENDSCHEDULOR` | 13 | runs | 140 | Stops an actor's motion routine. |
| 0x51 | `CodeENDMAPSCHEDULOR` | 13 | stepped | 67 | Stops a zone scheduler action. |
| 0x52 | `CodeENDLOADSCHEDULER_Main` | 15 | runs | 5,071 | Stops a scene task of 0x45. |
| 0x53 | `CodeWAITSCHEDULOR` | 13 | runs | 23,558 | Waits while an actor's motion routine plays. |
| 0x54 | `CodeWAITMAPSCHEDULOR` | 13 | stepped | 82 | Waits while a zone scheduler action plays. |
| 0x55 | `CodeWAITLOADSCHEDULER_Main` | 15 | runs | 4,864 | Waits while a scene task of 0x45 runs. |
| 0x56 | | 5 | stepped | 6 | Reads an actor operand, does nothing with it and yields a frame. |
| 0x57 | | 3 | runs | 90 | Adds the frames since the last tick to a work value. |
| 0x58 | | 1 | runs | 5 | Yields one frame. |
| 0x59 | | by sub | partial | 5,279 | Sets turn speeds, the walk speed or a render flag of an actor, or waits for its emote. [#197](https://github.com/jimmy58663/GordianXI/issues/197), [#198](https://github.com/jimmy58663/GordianXI/issues/198). |
| 0x5A | `CodeMOVE2` | by sub | runs | 758 | Like 0x1F, but moves in all three axes without following the floor. |
| 0x5B | `CodeLOADEXTSCHEDULERMain` | 15 | runs | 21,066 | Loads an event motion DAT onto an actor and plays one of its routines. |
| 0x5C | | by sub | stepped | 2,888 | Sets the music of a music slot, or its volume ([#167](https://github.com/jimmy58663/GordianXI/issues/167)). |
| 0x5D | | 5 | stepped | 2,139 | Moves the music volume to a level over a time ([#167](https://github.com/jimmy58663/GordianXI/issues/167)). |
| 0x5E | | 5 | partial | 3,008 | Ends the event entity's action and returns it to an idle motion it names. |
| 0x5F | | by sub | stepped | 1,508 | A dispatcher: a render flag, or non-yielding forms of 0x5B / 0x66 / 0x53 / 0xC1. |
| 0x60 | | by sub | stepped | 50 | Sets `Render.Flags1` bit 30 (subs 0/1) or starts a zone scheduler action (sub 2). |
| 0x61 | | 2 | stepped | 327 | Sets or clears `Render.Flags2` bit 0 of the event's own entity. |
| 0x62 | `CodeLOADEVENTSCHEDULER` | 17 | runs | 1,701 | Like 0x45, from scheduler file 5012 + n: one effect package per file (warps `wp00` / `wp01`...). [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0x63 | | 3 | stepped | 0 | Plays an emote id on the event's entity and waits while one plays. |
| 0x64 | | 11 | stepped | 176 | Stores the 2D distance between two points held in work values. |
| 0x65 | `CodeGETDISTANCEAA` | 11 | stepped | 13 | Stores the 3D distance between two actors, in thousandths of a yalm. |
| 0x66 | `CodeLOADEXTSCHEDULERMain` | 15 | runs | 14,143 | Plays a routine from the actor's player-model event motion package. |
| 0x67 | | 5 | runs | 108 | Turns on the event message mode and hides the HUD and compass. |
| 0x68 | | 1 | runs | 122 | Turns the event message mode off and shows the HUD again. |
| 0x69 | | 4 | stepped | 102 | Mutes or unmutes sound categories (effects, system, zone, master, chat sounds) ([#167](https://github.com/jimmy58663/GordianXI/issues/167)). |
| 0x6A | | 7 | stepped | 191 | Moves the volume of sound categories to a level over a time ([#167](https://github.com/jimmy58663/GordianXI/issues/167)). |
| 0x6B | | 9 | partial | 1,145 | Like 0x5E for a named actor. |
| 0x6C | `CodeTRANSPAR` | 9 | runs | 14,433 | Fades an actor's alpha to a value over a number of frames, yielding until done. [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x6D |  | 7 | stepped | 2 | No-op in the current client. All three census hits are 0x9D table data, not code (see [0x9D](#0x9d)). |
| 0x6E | `CodeEMOT` | 7 | runs | 5,216 | An actor plays an emote; it waits while that actor still plays an action. |
| 0x6F |  | 1 | runs | 23,520 | Sleeps for 16 frames unless a wait is already running on the stack. |
| 0x70 |  | 1 | runs | 8,350 | Waits while the event's own entity is still turning. |
| 0x71 | `CodeOPENPASSWIN` | by sub | stepped | 2,163 | Text and number input windows (passwords, counts), the linkshell concierge window and a few unknown menus. |
| 0x72 | `CodeGETWEATER` | by sub | stepped | 244 | Reads the weather forecast file and writes a zone's forecast for a day into zone work values 2-4 ([#125](https://github.com/jimmy58663/GordianXI/issues/125)). |
| 0x73 | `CodeMAGICSCHEDULOR` | 11 | stepped | 947 | Starts a spell-casting task (tag `main`) from one actor toward another. |
| 0x74 |  | 2 | stepped | 12 | Sets or clears bit 31 of the event entity's `Render.Flags1`. |
| 0x75 | `CodeLOADROOM` | by sub | stepped | 951 | Opens an indoor room of the zone and reports the player's sub-region to the server (C2S 0x0F2). |
| 0x76 |  | 5 | runs | 4,173 | Waits while the named actor is still turning. |
| 0x77 |  | 5 | runs | 1,181 | Stops the clock at an hour and / or sets the weather for the event; 255 leaves either alone. |
| 0x78 |  | 1 | runs | 1,240 | Restarts the clock and gives the zone its own weather back. |
| 0x79 |  | by sub | runs | 3,925 | One actor looks at another (subs 0 / 1), or holds its head on a fixed axis (sub 2, [#188](https://github.com/jimmy58663/GordianXI/issues/188)). An unknown sub ends the request ([#201](https://github.com/jimmy58663/GordianXI/issues/201)). |
| 0x7A |  | by sub | stepped | 594 | Request-stack control on another entity: clear its whole VM or one slot, borrow or return its event data, copy or reset a request. |
| 0x7B |  | 5 | runs | 3,113 | An actor stops looking and talking. |
| 0x7C |  | 6 | ignored | 1,576 | Sets or clears `Render.Flags2` bit 17 of an actor. |
| 0x7D |  | 3 | stepped | 57 | Starts a scene task on the local player from file 5112 + n (rank-up scenes). |
| 0x7E | `CodeCHOCOBO` | by sub | stepped | 547 | Puts an actor on or off a chocobo or mount (event status, render flags, chocobo colours, mount id). |
| 0x7F | `CodeQUERYWAIT2` | 1 | runs | 6 | Waits for the open query like 0x25, but a cancel stores 255 and does not end the event. |
| 0x80 | `CodeLOADWAIT` | 5 | ignored | 7,553 | Waits until an actor's model and action resources have loaded. |
| 0x81 |  | 6 | runs | 2,179 | Turns an actor's eye blinking on or off (`EventRenderFlags.NoBlink`, [#198](https://github.com/jimmy58663/GordianXI/issues/198)). |
| 0x82 |  | 7 | partial | 9 | Branches on whether the event entity stands inside a named zone rectangle. |
| 0x83 |  | 3 | runs | 230 | Stores the current game time in a work value. |
| 0x84 |  | 1 | stepped | 134 | Sets `Render.Flags3` bit 0 of the event entity. |
| 0x85 |  | 1 | stepped | 7 | Opens the Mog House menu. |
| 0x86 |  | 6 | stepped | 194 | Sets or clears `Render.Flags3` bit 3 of an actor. |
| 0x87 |  | 2 | stepped | 10 | World pass request: sends C2S 0x01B and waits for the answer. |
| 0x88 |  | 2 | stepped | 15 | The other world pass request (C2S 0x01B with another mode). |
| 0x89 |  | 3 | stepped | 179 | Opens a zone map in the map window for the event. |
| 0x8A |  | 1 | stepped | 229 | Closes that map window ([#168](https://github.com/jimmy58663/GordianXI/issues/168)). |
| 0x8B | `CodeSETEVENTMARK` | 25 | stepped | 101 | Places a named marker on a zone map ([#168](https://github.com/jimmy58663/GordianXI/issues/168)). |
| 0x8C |  | by sub | stepped | 55 | Crafting requests (recipes, synthesis support) sent as C2S 0x058, and the wait for the answer. |
| 0x8D |  | 5 | stepped | 85 | Opens the map window on a map and sub-map without markers. |
| 0x8E |  | 1 | stepped | 27 | Sets the event entity's event status to 45. |
| 0x8F |  | 1 | stepped | 23 | Sets the event entity's event status to 46. |
| 0x90 |  | 1 | runs | 23 | Sets render flags on the event entity (`Flags0` bit 17, `Flags1` bit 12): hides it, as 0x22 01 ([#198](https://github.com/jimmy58663/GordianXI/issues/198)). |
| 0x91 |  | 3 | stepped | 7 | Sets the event entity's base walk speed. |
| 0x92 |  | 6 | runs | 8,349 | Sets or clears `Render.Flags3` bit 16 of an actor: no name plate for the event (#191; `WorldEntity.HidesEventName`). [#198](https://github.com/jimmy58663/GordianXI/issues/198). |
| 0x93 |  | 3 | stepped | 5,479 | Opens the item information window on an item id, or closes it for 0. |
| 0x94 |  | 6 | runs | 3,902 | Sets or clears `Render.Flags3` bit 17 of an actor; kept on the entity, effect unknown ([#198](https://github.com/jimmy58663/GordianXI/issues/198)); [#217](https://github.com/jimmy58663/GordianXI/issues/217). |
| 0x95 |  | 3 | stepped | 378 | Marks the event entity as an event NPC and clears its attachments. |
| 0x96 |  | 1 | stepped | 167 | Ends that event NPC state and clears the attachments again. |
| 0x97 |  | 5 | stepped | 165 | Saves the zone's wind values and sets new ones. |
| 0x98 |  | 1 | stepped | 15 | Waits while the zone is still reading extra data (a room). |
| 0x99 |  | 5 | runs | 5,220 | Yields one frame while an actor plays an action, then goes on. |
| 0x9A |  | 1 | stepped | 2,058 | Waits until the music server has read the current song ([#167](https://github.com/jimmy58663/GordianXI/issues/167)). |
| 0x9B |  | 1 | stepped | 130 | Waits while the event entity plays an action. |
| 0x9C |  | 3 | runs | 71 | Stores the client language (2 = English). |
| 0x9D |  | by sub | partial | 1,850 | Script tables inside the byte code: read, write, share and jump through them, plus string copies and compares. |
| 0x9E |  | 2 | stepped | 21 | Says whether a room load during the event should report the sub-region to the server. |
| 0x9F | `CodeLOADEVENTSCHEDULER2` | 17 | runs | 1,883 | Starts a scene task like 0x45, from file 51183 + n: effect routines ([#192](https://github.com/jimmy58663/GordianXI/issues/192)). |
| 0xA0 | `CodeWAITLOADSCHEDULER_Main` | 15 | runs | 27 | Waits for a scene task like 0x55, file base 5012 (the 0x62 tasks). [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xA1 | `CodeENDLOADSCHEDULER_Main` | 15 | runs | 0 | Stops a scene task exactly like 0x52 (base 30704, remapped), not a 0x62 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xA2 | `CodeWAITLOADSCHEDULER_Main` | 15 | runs | 18 | Waits for a 0x9F task ([#192](https://github.com/jimmy58663/GordianXI/issues/192)). |
| 0xA3 | `CodeENDLOADSCHEDULER_Main` | 15 | runs | 5 | Stops a 0x9F task ([#192](https://github.com/jimmy58663/GordianXI/issues/192)). |
| 0xA4 |  | 2 | stepped | 442 | Sets or clears `Render.Flags3` bit 26 of the event entity. |
| 0xA5 |  | 2 | stepped | 406 | Sets or clears `Render.Flags3` bit 11 of the event entity. |
| 0xA6 |  | by sub | stepped | 203 | Asks the server for the event map number (C2S 0x0EB, answer S2C 0x10E), waits, or reads the sub-map. Real use: one event (see Part 2). |
| 0xA7 |  | by sub | stepped | 55 | Battlefield registration handshake: waits for the server's answer and stores its result. |
| 0xA8 |  | 6 | stepped | 56 | Resets an NPC map marker to an empty entry, opening the map first if needed. |
| 0xA9 |  | 3 | stepped | 6 | Stops the clock at a time derived from a work value (minute 30). |
| 0xAA |  | 17 | stepped | 190 | Splits a game time value into Vana'diel year, month, day, weekday, hour, minute and moon phase. |
| 0xAB |  | by sub | partial | 2,130 | Sets or clears single render flags on the event entity (or an actor), and a few global flags. The entity flags are kept, the global ones stepped ([#198](https://github.com/jimmy58663/GordianXI/issues/198)); [#217](https://github.com/jimmy58663/GordianXI/issues/217). |
| 0xAC |  | by sub | stepped | 687 | Sets the event entity's server or event status, or render flags 6 / 7 on an actor (asking for it with C2S 0x016 when absent). |
| 0xAD |  | 12 | stepped | 420 | Scheduler actions (tag `main`) from one actor toward another; the kind is the low nibble of the sub byte. |
| 0xAE |  | by sub | stepped | 115 | Assorted: a weather change, an actor's name colour, its mouth clip set, actor-to-actor links, its environment area. |
| 0xAF |  | 8 | stepped | 0 | Stores the camera's eye (sub 0) or look-at point (sub 1). |
| 0xB0 |  | 12 | runs | 42 | Prints a message with a speaker and a listener actor. |
| 0xB1 |  | 4 | stepped | 28 | Stores bits of a client-wide flag value (always 128 in practice). |
| 0xB2 |  | by sub | stepped | 1 | Delivery box: wait a number of frames, or ask to open it. The one census hit is table data. |
| 0xB3 |  | by sub | stepped | 14 | Ranking boards (fishing and others): request a list, wait, read entries. |
| 0xB4 |  | by sub | stepped | 1,575 | Strings and small windows: copy text into work strings, target window, cast bar and event timer windows, chocobo race windows, map window update. |
| 0xB5 |  | 4 | stepped | 1,339 | Renames the event entity from a work string. |
| 0xB6 |  | by sub | stepped | 8,292 | Changes the event entity's look: race, single gear slots, the full look, model size and some render flags. |
| 0xB7 |  | by sub | stepped | 27 | Actor queries: write into another entity's VM, read an actor's name, server id or index. |
| 0xB8 |  | 27 | stepped | 36 | Adds a named marker on a map and shows it, opening the map if needed. |
| 0xB9 |  | 8 | stepped | 9 | Renames a map marker from the event read buffer. |
| 0xBA |  | 13 | runs | 3,560 | Places another actor of the event: position and heading. |
| 0xBB |  | 17 | runs | 478 | Starts a scene task like 0x45, from file 56685 + n. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xBC |  | 15 | runs | 55 | Waits for a 0xBB task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xBD |  | 15 | runs | 1 | Stops a 0xBB task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xBE |  | 3 | runs | 0 | Stores the server id of the entity that queued the running request. |
| 0xBF |  | by sub | stepped | 60 | Chocobo racing: reads race, chocobo, section or result parameters. |
| 0xC0 |  | 3 | runs | 1,073 | Sets or clears `Render.Flags3` bit 12 of the event entity from a work value; kept on the entity, effect unknown ([#198](https://github.com/jimmy58663/GordianXI/issues/198)); [#217](https://github.com/jimmy58663/GordianXI/issues/217). |
| 0xC1 |  | 5 | stepped | 1 | Once an actor's resources have loaded, ends its last action and releases a loaded resource set. |
| 0xC2 |  | by sub | stepped | 35 | Party Mog House visits: mask of members that can be visited, whether one's house is open. |
| 0xC3 |  | 7 | stepped | 54 | Copies a work string and a value into one of eight 20-byte slots. |
| 0xC4 |  | 12 | stepped | 206 | The 0x73 spell-casting task with three more kinds chosen by the sub byte. |
| 0xC5 |  | 17 | runs | 708 | Starts a scene task like 0x45, from file 67355 + n. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xC6 |  | 15 | runs | 47 | Waits for a 0xC5 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xC7 |  | 15 | runs | 4 | Stops a 0xC5 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xC8 |  | 7 | stepped | 29 | Opens the map window with given parameters (the intro map tutorial, [#168](https://github.com/jimmy58663/GordianXI/issues/168)). |
| 0xC9 |  | 1 | stepped | 16 | Restarts the clock (unlike 0x78 it leaves the weather). |
| 0xCA |  | ? | no length | 0 | No handler in the current client. |
| 0xCB |  | ? | no length | 8 | No handler in the current client; all eight census hits are 0x9D table data. |
| 0xCC |  | by sub | stepped | 2,689 | Item information windows, the zone search menu and the event item window. |
| 0xCD |  | 17 | runs | 1,039 | Starts a scene task like 0x45, from file 70435 + n. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xCE |  | 15 | runs | 71 | Waits for a 0xCD task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xCF |  | 15 | runs | 1 | Stops a 0xCD task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD0 |  | 17 | runs | 453 | Starts a scene task like 0x45, from file 70691 + n. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD1 |  | 15 | runs | 0 | Waits for a 0xD0 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD2 |  | 15 | runs | 0 | Stops a 0xD0 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD3 |  | 6 | stepped | 89 | Clears an actor's queued motions. |
| 0xD4 |  | by sub | partial | 655 | The query with the zone map behind it (home point lists) and the map marker data for it ([#168](https://github.com/jimmy58663/GordianXI/issues/168)). |
| 0xD5 |  | 17 | runs | 73 | Starts a scene task like 0x45, from file 102449 + n. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD6 |  | 15 | runs | 4 | Waits for a 0xD5 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD7 |  | 15 | runs | 1 | Stops a 0xD5 task. [#199](https://github.com/jimmy58663/GordianXI/issues/199). |
| 0xD8 |  | by sub | stepped | 46 | Sets another entity's event roll, heading or pitch, or copies them from its last position. |
| 0xD9 |  | 2 | stepped | 24 | Sets a sound-effect related flag (thought to lift the limit on sounds playing at once). |

## Details

### 0x02 `CodeIF`

- Layout: `02 a:work b:work kind:u8 target:u16`. The test is the low nibble of `kind`; `target` is an absolute code offset (XiEvents OpCodes/0x0002).
- Jump when:

| kind | jumps when |
|---|---|
| 0 | a != b |
| 1, 7 | a == b |
| 2 | a <= b |
| 3 | a >= b |
| 4 | a < b |
| 5 | a > b |
| 6, 9 | (a & b) == 0 |
| 8 | (a \| b) == 0 |
| 10 | (~a & b) == 0 |
| 11-15 | always |

- GordianXI: `EventVm.ExecIf`, the same table; otherwise it steps 8.
- **Differs from XiEvents:** the prose describes the else-offset as added to the program position; its pseudo code assigns it, and GordianXI treats it as absolute, like every other jump (0x01, 0x1A, 0x3E, 0x44).

### 0x16, 0x17, 0x18

- Layout: `16 out:work angle:work r:work` stores -r sin(angle); `17` the same with r cos(angle). The angle is in 4096 steps per turn (XiEvents OpCodes/0x0016, 0x0017). `18 out:work a:work b:work` stores atan2(-a, b) scaled by 4096 / pi (XiEvents OpCodes/0x0018).
- GordianXI (`EventVm.Step`, [#197](https://github.com/jimmy58663/GordianXI/issues/197)): the same, cut toward zero into the work value. Retail's angle step is 0.0015339355 = 6.283 / 4096 (the rounded 2π of 0x47), not 2π / 4096, so a half turn's cosine falls just short of -1: r = 1500 gives -1499, as here. The scripts use 0x16 / 0x17 in a shared routine that adds `r cos(a)` to x and `-r sin(a)` to the second position axis (Southern San d'Oria event 23 runs it).
- 0x18 keeps XiEvents' scale: 4096 / pi per radian is 8192 steps to a turn, twice the 4096 of 0x16 / 0x17 / 0x3A. **Beyond XiEvents:** no retail script runs 0x18. All 63 census entries that contain it are table data walked as code (their operands are table words such as `0x1880`), so the scale has no effect on any retail event (corpus probe, 2026-10-03).

### 0x19

- Layout: `19 a:work b:work`. Exchanges the two work values (XiEvents OpCodes/0x0019 pseudo code). GordianXI does the same.
- **Differs from XiEvents and xi-tools:** both describe it as an endian swap (xi-tools `docs/events/opcodes.md`: "byte-flipped"); the pseudo code swaps the two values and touches no bytes.

### 0x1A

- At depth 8 the call does not happen: retail sets the yield flag without moving on, so the request retries the call every frame. GordianXI does the same.
- **Differs from XiEvents:** the prose says the request is killed on a ninth call; the pseudo code only yields.

### 0x1F `CodeMOVE`

- Layout: `1F 00 x:work y:work height:work` (8 bytes), then `1F 01` (2 bytes), repeated by the script until the walk ends. Positions are thousandths of a yalm (XiEvents OpCodes/0x001F).

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00 | 8 | 24,548 | store the goal in the running stack (retail also cancels a turn) | runs |
| 0x01 | 2 | 24,545 | step toward the goal at the walk speed, face the way, yield; at the goal snap to it and go on | runs |
| 0x17 | - | 411 | not a sub-case: inline string data after `1F 17` ([vm.md](vm.md#event-files), corpus walk) | logged, steps 2 [#197](https://github.com/jimmy58663/GordianXI/issues/197). |
| 0x80, 0x81 | - | 59, 2 | no retail sub-case (retail neither moves on nor yields) | logged, steps 2 |

- GordianXI (`EventVm.ExecMove`): the distance is horizontal; the walk speed is 0x32's, else the entity's own, else 4.0. An entity whose place is unknown is put at the goal at once. The renderer draws the entity on the floor below its event position.
- **Differs from XiEvents:** retail sets the height to the goal's height on every frame and then snaps it to the floor. GordianXI moves the height with the walk instead: the Southern San d'Oria knights walk from height -2 to a goal at 0, and taking the goal height at once sank them into the floor (code note in `ExecMove`).
- **Beyond XiEvents:** in the pseudo code of 0x1F, 0x31, 0x36 and 0x37 the floor snap is skipped when the entity has `Render.Flags0` bit 20 or 21 or `Render.Flags2` bit 14 set. Bit 21 is the one 0x33 (own entity) and 0x59 sub 5 (another actor) set, so those opcodes are how a script keeps an actor off the floor. GordianXI runs both (`WorldEntity.KeepsEventHeight`, #192): the renderer then draws the entity at its placed height instead of the floor below. Port Jeuno 324 sets it with 0x59 sub 5 on its invisible marker 0x010F608F right before 0xBA places the marker about 50 yalms up, where the sky explosion plays; with the floor snap the explosion was buried in the street (in-game test, 2026-10-02).

### 0x20

- Layout: `20 flag:u8`, retail `CliEventUcFlag`.
- GordianXI holds the character for the whole event (`IsMovementLocked` in `EventDialogController`); the host honours only the release (`SetControlLock(false)`).

### 0x23 `CodeMESWAIT`

- GordianXI yields while the scene's message is open (`EventScene.IsWaitingForConfirm`) and steps 1 once it closes.
- Retail also ends the event as cancelled when it meets 0x23 while the message state is 2, and stops the speaker's mouth when it moves on (XiEvents OpCodes/0x0023). GordianXI does neither: its mouth clip plays once per line ([#185](https://github.com/jimmy58663/GordianXI/issues/185)).

### 0x26

- Retail sets the yield flag and never moves on, so the request spins until the event ends (XiEvents OpCodes/0x0026). GordianXI frees the running stack (`EndRequest`), since nothing after it would ever run.

### 0x27, 0x28 `CodeREQSW`, 0x29 `CodeREQEW`, 0x2A

- Layout: `27|28|29 priority:u8 actor:u32 slot:u8`, `2A priority:u8 actor:u32`.
- GordianXI: `EventVm.ExecRequest` and `ExecRequestLevelWait` on the target's VM in the same `EventScene` ([#85](https://github.com/jimmy58663/GordianXI/issues/85)); an actor outside the event is skipped. The wait state is kept in the running stack's request flag (1 = queued, 2 = started).
- **Beyond XiEvents:** `slot` indexes the target's own offset table; it is not an event id (xi-tools `docs/events/retail-events.md`, "The one rule").
- **Differs from XiEvents:** read literally, the decompiled status tests of 0x28 and 0x29 are inverted against their PS2 names (start wait, end wait) and an end wait would never release. GordianXI follows the names: 0x28 waits until the target has started the request, 0x29 until it has finished it.
- **Differs from xi-tools:** xi-tools `docs/events/opcodes.md` says 0x2A ends by calling `GetReqStatus`; XiEvents OpCodes/0x002A calls `GetReqLevel`, which is what GordianXI does.

### 0x2D `CodeMAPSCHEDULOR`

- Layout: `2D actor:u32 target:u32 routine:u32`; 0x51 stops it and 0x54 waits for it, same operands (XiEvents OpCodes/0x002D, 0x0051, 0x0054: `XiZone::SetAction`, `KillAction`, `IsMovingAction`). Retail runs it only when both actors' models are loaded (`Render.Flags0` bit 9).
- GordianXI steps over all three. S2C 0x039 now plays zone routines through `ZoneRoutinePlayer` ([#210](https://github.com/jimmy58663/GordianXI/issues/210)); these opcodes are not wired to it yet ([#109](https://github.com/jimmy58663/GordianXI/issues/109)); doors' own open / close routines run from their status ([#15](https://github.com/jimmy58663/GordianXI/issues/15)).

### 0x31 `CodeSMOVE`

- Layout: `31 00 x:work y:work height:work time:work` (10 bytes), then `31 01` (2 bytes) (XiEvents OpCodes/0x0031).

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00 | 10 | 297 | store the goal and a time (the operand times 0.001) in the running stack | stepped |
| 0x01 | 2 | 188 | step toward the goal in three axes while the time lasts, counting it down by the frame delay; snap to the floor | stepped |
| 0x80, 0x81 | ? | 10, 1 | no retail sub-case | no length: ends the request |

- **Differs from xi-tools:** xi-tools `docs/events/typed_opcodes.md` names the fourth operand `dir`; XiEvents reads it as the move time.

### 0x34, 0x35

- Layout: `34|35 zone:work`. In retail the first call removes every actor, a second marks the load, the third opens the zone (`XiZone::Open`) and goes on; 0x34 can instead close the event zone when an internal flag is set (XiEvents OpCodes/0x0034, 0x0035). xi-tools names both after `XiZone::Open` (`docs/reference/ps2_decomp_crosscheck.md` 4.4).
- GordianXI (`EventVm.ExecOpenZone`) treats both alike: it asks the host to draw the zone (`IEventVmHost.OpenEventZone`) and waits while it loads, at most 900 frames. Evidence and the Windurst intros: [vm.md](vm.md#another-zone-for-a-scene-175-2026-10-01).

### 0x36, 0x37

- Layout: `36 x:work y:work height:work`, `37 x:work y:work height:work heading:work` (thousandths of a yalm; 4096 heading steps per turn). Retail snaps the height to the floor (see 0x1F), copies the event pose to the entity and requests a hit check (XiEvents OpCodes/0x0036, 0x0037).
- GordianXI sets the event pose (`SetEventPosition`, `SetEventHeading` without a turn); the renderer draws on the floor below. No hit check.

### 0x38

- Layout: `38 mask:work`.
- GordianXI stores `(mask & 0xFF) | 0x2000` in `EventScene.EventModeLocal`; nothing reads it ([#176](https://github.com/jimmy58663/GordianXI/issues/176)). Values seen: 0x13 for the Southern San d'Oria aerial shots, 0x03 when the player walks in, 0x12 in Bastok Markets ([vm.md](vm.md#emotes-and-the-mode-mask-176-2026-10-01)).
- **Differs from xi-tools:** xi-tools `docs/events/event_mode_bits.md` (correction of 2026-07-06) reads the handler as taking the operand's high byte, `(mask >> 8) | 0x20`, so nearly every retail value would collapse to 0x20. GordianXI keeps the low byte under a 0x20 high byte, which matches XiEvents' own example (OpCodes/0x0038: Ailevia's camera tour in Southern San d'Oria sets the flag to 0x2003). The decompiled line is ambiguous between the two; not settled.

### 0x3A

- Layout: `3A actor:u32 out:work`. Stores the actor's heading in 4096 steps per turn (0 when the actor resolves to no entity) (XiEvents OpCodes/0x003A).
- GordianXI (`EventVm.Step`, [#197](https://github.com/jimmy58663/GordianXI/issues/197)): the named actor's event heading when it takes part in the event, else its world heading, scaled as the 0x7F03 fact. An actor code that names nobody leaves `out` unchanged; an actor not in the zone stores 0.
- **Differs from XiEvents and xi-tools:** both describe a single-byte yaw; the pseudo code scales by 4096 / 2 pi, the same unit as 0x39.

### 0x3B

- Layout: `3B actor:u32 x:work y:work height:work`. Stores the actor's position in thousandths of a yalm: its event position when it takes part in an event, else its world position (XiEvents OpCodes/0x003B).
- GordianXI (`EventVm.Step`, [#197](https://github.com/jimmy58663/GordianXI/issues/197)): the named actor's event position when it takes part in the event, else its world position, in the scripts' order (x, the other ground axis, height; the axes 0x36 / 0x37 take). An actor code that names nobody leaves the work values unchanged; an actor not in the zone stores zeros.
- Scripts read their own position with 0x3B / 0x3A and add 0x16 / 0x17 offsets to it. In Lower Jeuno event 70 all seven NPCs do this (`3B F8FFFF7F` / `3A F8FFFF7F`, the VM itself). With the two opcodes stepped, the NPCs stood at the origin of Ru'Lude Gardens, where the scene plays, and none showed on camera (in-game test, 2026-10-03).
- Over the corpus, 1,110 of the 2,914 reads name the VM itself and 1,744 another actor. For those, XiEvents' pseudo code reads the running VM's own event position when the actor is in an event (open question below); GordianXI reads the named actor's.

### 0x43

- Layout: `43 sub:u8`. Sub 0 sends C2S 0x05B mode 1 with zone work value 1 (the end parameter) and marks a reply pending (retail retries while the send fails); sub 1 yields until the pending flag clears (XiEvents OpCodes/0x0043).
- GordianXI: sub 0 `IEventVmHost.SendEventUpdate`, sub 1 waits on `ReceivePending` (cleared by S2C 0x052 mode 1). Checked against the maintainer's home point capture (`HomePointCaptureTests`, 2026-09-28; [vm.md](vm.md#checked-against-captures)).
- Subs 0x80 and 0x81 appear in the home point scripts. In XiEvents' pseudo code any sub other than 0 and 1 yields without moving on; GordianXI logs them and steps 2.

### 0x45 `CodeLOADSCHEDULER`

- Layout: `45 p:work actor:u32 target:u32 routine:u32 value:work`. The scene file is 30704 + p, with p from 300 shifted by 25937 and from 600 by 39643 (XiEvents OpCodes/0x0045; `EventSceneResource.GetFileId`).
- GordianXI: `EventVm.ExecStartTask`; the task's length is the routine's. Retail hands `value` to the scheduler; GordianXI does not use it (0 in every intro). Details: [vm.md](vm.md#cutscene-schedulers).

### 0x46 `CodeDEFCAMERA`

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00 | 2 | 5,565 | give the camera back: restore the view mode, end every camera task, re-centre on the player | runs: `IsCameraHeld = false`, `SetEventCamera(false)` |
| 0x01 | 2 | 5,496 | take the camera: view mode 0, no user camera, menu list drawing off | runs: `SetEventCamera(true)` |
| 0x02 | 4 | 101 | store 1 in the work value at +2 while the player has the camera, else 0 | runs |
| 0x03, 0x65, 0x80, 0x81 | 2 | 4, 4, 14, 1 | no retail sub-case; retail steps 2 | steps 2 without a diagnostic |

- Retail does all of this only when the local player's model is loaded (`Render.Flags0` bit 9); otherwise it steps 2 even for sub 2 (XiEvents OpCodes/0x0046).

### 0x47

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00 | 10 | 1,821 | send C2S 0x05C with x, y, height (work values, thousandths) and a 4096-step heading | runs: `SendEventUpdateXzy` |
| 0x01 | 2 | 1,812 | yield until the reply has come | runs: waits on `ReceivePending` |
| 0x80, 0x81 | 2 | 109, 1 | no retail sub-case (retail yields without moving on) | logged, steps 2 |

- Retail's sub 1 waits for two pending flags, the update's and the position update's (XiEvents OpCodes/0x0047); GordianXI has one.
- **Beyond XiEvents:** the 0x05C send does not exist in the 2003 PS2 client (xi-tools `docs/reference/ps2_decomp_crosscheck.md` 4.4).

### 0x48, 0x49

- Layout: `48 msg:work`, `49 actor:u32 msg:work`. 0x48 prints in message mode 919 with no names bound; 0x49 prints in mode 662 (as 0x1D / 0x2B) with the actor bound as the text's caster and target name, but no "Name : " prefix (XiEvents OpCodes/0x0048, 0x0049).
- GordianXI prints both with `EventSpeaker.None`; 0x49 passes the actor.
- **Differs from xi-tools:** xi-tools `docs/events/opcodes.md` puts 0x49's message operand at byte 1; it is at byte 5, after the actor (XiEvents, and the 0x2B layout).

### 0x4A `CodeDTURA`, 0x4B

- Layout: `4A actor:u32 target:u32`, `4B actor:u32 heading:work`.
- Retail turns an actor outside any event by setting its world heading (XiEvents OpCodes/0x004A, 0x004B). GordianXI turns only actors that take part in the event (`RequestTarget`), hence `partial`. 0x4A also sets the head look as 0x1E does ([#174](https://github.com/jimmy58663/GordianXI/issues/174)).
- **Differs from XiEvents:** its 0x4A pseudo code reads the second position from the first actor too, which would give a zero vector; GordianXI uses the target's position (taken as a transcription slip).

### 0x4C, 0x4D, 0x4F

- 0x4C sets the event entity's event status to 8 (open), 0x4D to 9 (close), `4F n:work` to n + 18, each only when `Render.Flags0` bit 2 is clear (XiEvents OpCodes/0x004C, 0x004D, 0x004F).
- GordianXI ([#200](https://github.com/jimmy58663/GordianXI/issues/200)): 0x4C / 0x4D set `WorldEntity.EventStatus` to 8 / 9 on the event's own entity while its `EventRenderFlags.Flags0Bit2` (0xAB sub 3) is clear; the status is cleared when the event ends, with the other event state (retail's `StatusEvent` reset at the event's end is *inference*). `DoorAnimator` plays the door's `open` / `clos` routines from it, and `ZoneDoors` blocks the doorway while it is not open ([#15](https://github.com/jimmy58663/GordianXI/issues/15), [collision-and-physics.md](../world/collision-and-physics.md)). 0x4F is stepped over.

### 0x53 `CodeWAITSCHEDULOR`, 0x54 `CodeWAITMAPSCHEDULOR`, 0x55 `CodeWAITLOADSCHEDULER_Main`

- GordianXI: 0x53 yields while `EventScene.IsEntityActionPlaying` for the actor and routine, 0x55 while `IsTaskRunning`; both step past once the motion or task is over, or when an actor is not in the zone. 0x54 is stepped over.
- **Differs from XiEvents:** as transcribed, the 0x54 and 0x55 pseudo code never moves past the opcode once the action has finished (it only steps past when an actor is missing), which would stall the request; 0x53 has the step. GordianXI steps past.

### 0x56

- Retail reads the actor operand and yields one frame (XiEvents OpCodes/0x0056). GordianXI steps over it without yielding (6 events).

### 0x59

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00 | 4 | 9 | body turn speed of the event's own entity | runs: `SetEntityTurnSpeed` (provisional unit) |
| 0x01 | 8 | 107 | body turn speed of a named actor | runs |
| 0x02 | 4 | 10 | head turn speed of the event's own entity | runs: `SetEntityHeadTurnSpeed` |
| 0x03 | 8 | 53 | head turn speed of a named actor | runs |
| 0x04 | 8 | 4,308 | walk speed (operand times 0.1) of the VM's own walks | runs |
| 0x05 | 7 | 755 | `Render.Flags0` bit 21 of a named actor (literal byte at +6) | runs: `SetEntityKeepsHeight` (Port Jeuno 324 keeps its sky marker 0x010F608F up, #192) |
| 0x06 | 6 | 24 | yield while the actor's emote action plays | runs (`EventScene.IsEntityActionPlaying` on the `emot` tag of 0x6E) |
| 0x07 | 4 | 0 | a movement flag (0 or 1) of the event's own entity | stepped |
| 0x08 | 8 | 1 | the same flag of a named actor | stepped |
| 0x80 | ? | 102 | no retail sub-case | no length: ends the request |

- Layout: `59 sub value:work` (subs 0, 2, 7), `59 sub actor:u32 value:work` (1, 3, 4, 8), `59 05 actor:u32 flag:u8`, `59 06 actor:u32` (XiEvents OpCodes/0x0059; xi-tools `docs/events/typed_opcodes.md`).
- Sub 4 is the most used. In XiEvents' pseudo code it sets the walk speed (`MainSpeed`) of the running VM, as 0x32 does, and the named actor only has to have a model. GordianXI does the same ([#197](https://github.com/jimmy58663/GordianXI/issues/197)). **Beyond XiEvents:** the scripts confirm the reading. Of the 4,684 sub 4 uses, 2,399 name the VM itself (0x7FFFFFF8) and 281 its own server id, and those that name another actor are still followed by a walk of the VM's own entity. Wajaom Woodlands actor 0x01033243 names three actors in turn (0x010332CD, 0x010332A6, 0x010332C4), each sub 4 followed by `1F 00` / `1F 01` and `00` (corpus probe, 2026-10-03).
- Subs 0 / 1 set retail's `TurnSpeed`. Its unit is not known; GordianXI reads it as 4096ths of a turn per 60 Hz frame (provisional). The scripts set 5 to 900, mostly 50 to 100, which at that unit turn a quarter turn in 10 to 20 frames, close to the turn ease used when no speed is set. With a speed, the event turn is timed at that rate (0x70 / 0x76 wait for it), and the renderer turns the body at that constant rate (`EventPoseSmoother`). Cleared when the event ends. See [reference/calibrations.md](../reference/calibrations.md).
- The head turn speed: [#188](https://github.com/jimmy58663/GordianXI/issues/188), [world/entities-and-animation.md](../world/entities-and-animation.md).

### 0x5A `CodeMOVE2`

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00 | 8 | 692 | store the goal | runs |
| 0x01 | 2 | 688 | step toward it in three axes, no floor snap; snap to the goal at once when no zone is loaded | runs |
| 0x80, 0x81 | - | 65, 1 | no retail sub-case | logged, steps 2 |

- **Differs from XiEvents:** as transcribed, the pseudo code adds the horizontal component of the step to the height axis and the height component to the horizontal one. GordianXI moves each axis by its own component; which retail does is not checked.

### 0x5B `CodeLOADEXTSCHEDULERMain`, 0x66 `CodeLOADEXTSCHEDULERMain`

- Layout: `5B|66 res:work actor:u32 target:u32 routine:u32` (15 bytes). Retail loads the motion resource onto the actor (0x5B: an event motion DAT; 0x66: the player-model package), yields until it has been read, ends the actor's last action and starts the routine, then yields one frame. It needs both actors in an event with their models loaded (XiEvents OpCodes/0x005B, 0x0066).
- GordianXI: `ExecEntityMotion` with `EventMotionSource.Bank` / `Package`, then yields a frame; routine 0 and `xxxx` start nothing. File ids and packages: [vm.md](vm.md#cutscene-schedulers). The race sets 9-69 and the "package -1" of [#193](https://github.com/jimmy58663/GordianXI/issues/193): [schedulers-and-motions.md](schedulers-and-motions.md#motion-resources).
- **Beyond XiEvents:** 0x66 packages 70-139, ten per player race, are file `61171 + n`, not the tables of 0-69 ([#209](https://github.com/jimmy58663/GordianXI/issues/209): Port Jeuno 324's player look up, `atp0` from package slot · 10 + 70); packages from 140 on are not located.
- **Beyond XiEvents:** XiEvents lists a 17-byte form of both. That form is reached only through 0x5F subs 5 and 6 (one more work value at +15, which retail hands to the actor); a top-level 0x5B or 0x66 is always 15 bytes (xi-tools `docs/events/opcodes.md`: no 17-byte instance in 67k and 82k decodes; the corpus walk agrees).

### 0x5C, 0x5D

- `5C sub song:work` (4 bytes) sets the song of music slot `sub` (0-7) at full start volume; `5C 8n song:work volume:work` (6 bytes) the same for slot n with a start volume; `5C A0|A1 volume:work time:work` moves the playing music's volume. `5D volume:work time:work` does the same as 0xA0 (XiEvents OpCodes/0x005C, 0x005D). Slots: 0 day idle, 1 night idle, 2 solo battle, 3 party battle, 4 mount, 5 death, 6 and 7 unknown.

| sub | bytes | events using | GordianXI |
|---|---|---|---|
| 0x00, 0x01 | 4 | 2,869, 2,869 | stepped |
| 0x02, 0x03 | 4 | 633, 632 | stepped |
| 0x06 | 4 | 10 | stepped |
| 0x80-0x83 | 6 | 49, 36, 22, 22 | stepped |
| 0xA0, 0xA1 | 6 | 90, 90 | stepped |
| 0xB0 | ? | 4 | no length: ends the request (retail has no such sub) |

- [#167](https://github.com/jimmy58663/GordianXI/issues/167) covers both.

### 0x5E, 0x6B

- Layout: `5E idle:u32`, `6B idle:u32 actor:u32`. Retail ends the actor's last action, makes the FourCC `idle` its default idle motion and plays it (XiEvents OpCodes/0x005E, 0x006B).
- GordianXI ends the event gesture (`ResetMotion`) and the entity's own idle plays; the named idle (`idl0`, `dft0`) is not used, hence `partial` ([#166](https://github.com/jimmy58663/GordianXI/issues/166)).

### 0x5F

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00, 0x01 | 2 | 15, 18 | set `Render.Flags1` bit 29 of the event's own entity to the sub | stepped |
| 0x02 | 6 | 41 | the body of 0xC1 at +1 | stepped |
| 0x03 | 16 | 404 | the body of 0x5B at +1, without the closing yield | stepped |
| 0x04 | 16 | 106 | the body of 0x66 at +1, without the closing yield | stepped |
| 0x05 | 18 | 463 | as 0x03 with a work value at +16 handed to the actor | stepped |
| 0x06 | 18 | 308 | as 0x04 with that work value | stepped |
| 0x07 | 14 | 768 | the body of 0x53 at +1, waiting while the motion plays | stepped |
| 0x80 | ? | 37 | no retail sub-case | no length: ends the request |

- Retail retries a sub-case from the 0x5F byte while its body cannot finish (motion resources still loading) (XiEvents OpCodes/0x005F). Lengths confirmed by the corpus walk (#73, [vm.md](vm.md#event-files)).
- Subs 3-7 start and wait for gestures like 0x5B / 0x66 / 0x53; stepping over them leaves those gestures out of 1,508 events. No issue covers this yet.

### 0x60

| sub | bytes | events using | meaning | GordianXI |
|---|---|---|---|---|
| 0x00, 0x01 | 4 | 0, 0 | set `Render.Flags1` bit 30 of the event's own entity to the sub | stepped |
| 0x02 | 6 | 49 | start zone scheduler action `routine:u32` (at +2) with no actors | stepped |
| 0x03-0xFF | 2 | 1 (0x80) | the old two-byte form, stepped in retail | stepped |

- **Differs from xi-tools:** xi-tools `docs/events/opcodes.md` lists 0x60 as deprecated; only the two-byte form is (XiEvents OpCodes/0x0060).

### 0x62 `CodeLOADEVENTSCHEDULER`

- Same layout as 0x45; the scene file is 5012 + p, with no 300 / 600 shift (XiEvents OpCodes/0x0062).
- Runs as an `EventScene` task on file `5012 + p` (#192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)). Each file 5013-5109 is a self-contained effect package (routines, generators, meshes, textures, sounds), such as the warp `wp00` / `wp01` of battlefield entry and exit; the files are listed in [schedulers-and-motions.md](schedulers-and-motions.md#the-5012-band-0x62--0xa0).

### 0x64, 0x65 `CodeGETDISTANCEAA`

- `64 out:work x1:work y1:work x2:work y2:work` stores the plain distance of the two points (same unit as the operands). `65 out:work actor:u32 target:u32` stores the 3D distance of two actors in thousandths of a yalm, from their event positions when in an event, 0 when either is missing (XiEvents OpCodes/0x0064, 0x0065).
- GordianXI steps over both; `out` keeps its old value.

### 0x69, 0x6A

- `69 mute:u8 kinds:work`: `kinds` bits 0x01 effects, 0x02 system, 0x04 zone, 0x08 master, 0x10 chat sounds; volume 0 when `mute` is nonzero, else 1. `6A volume:work time:work kinds:work` moves the volume of the first four kinds to `volume` / 1000 over `time` (XiEvents OpCodes/0x0069, 0x006A).
- GordianXI steps over both.
- **Differs from [#167](https://github.com/jimmy58663/GordianXI/issues/167):** the issue and xi-tools `docs/events/cutscenes.md` call these sound effect opcodes; per XiEvents they only set category volumes, they play nothing.

### 0x6C `CodeTRANSPAR`

- Layout: `6C actor:u32 alpha:work frames:work`. On the first call retail reads the actor's colour, takes the target alpha and the frame count (0 taken as 1); on each later call it steps the alpha toward the target and yields; when the time has run out it applies the target alpha and goes on. An actor that is missing or has no model is stepped over (XiEvents OpCodes/0x006C).
- GordianXI (`EventVm.ExecTransparency`, [#197](https://github.com/jimmy58663/GordianXI/issues/197)): the same, with the alpha kept on the entity (`WorldEntity.EventAlpha`, 0x80 = opaque, back to opaque when the event ends). The fade state is kept per VM, as retail keeps it in the VM's `ExtData`. An actor counts as missing when it is not in the zone. The renderer draws an entity under 0x80 after the others: first a depth-only pass, then a blended pass at alpha / 0x80 (`EntityRenderer`). An entity at 0 is not drawn. Values above 0x80 draw opaque.
- The alpha is half scale, like the other model colours: the scripts' targets are 0 (8,961 uses) and 128 (7,095), then 64, 127, 80 and 100. The frame count is 1 in 14,832 uses, which sets the alpha within one frame (corpus probe, 2026-10-03).
- **Differs from XiEvents:** the decompiled pseudo code writes the target alpha on every step and never uses the stepped `NowAlpha` it computes; the final write subtracts the alpha byte instead of adding it. GordianXI reads both as decompiler slips: it writes the stepped alpha, so the fade is gradual, and it sets the target at the end.
- Test scenes: Southern San d'Oria `!cs 686` fades NPC 0x010E60F5 in to about 0x48 and back out; `!cs 945` (Femitte's mithra dancer) sets 16 bystanders to 0x60 at once.

### 0x6E `CodeEMOT`

- Layout: `6E actor:u32 emote:work`. The work value's low byte is the emote id, its high byte a variant (XiEvents OpCodes/0x006E).
- GordianXI (`EventVm` case 0x6E): an actor that resolves to nobody steps over; while the actor plays an event action (`EventScene.IsEntityActing`) the opcode yields; otherwise it plays the race's emote motion (`IEventVmHost.PlayEntityEmote`) and records it under the scene tag `emot`. A fixed-model NPC has no emote motions and passes at once.
- Evidence ([events/vm.md](vm.md)): the low byte is the C2S 0x05D / LandSandBoat `Emote` id. Rahal's clap in the Southern San d'Oria intro is emote 13, right before "General Curilla, bravo."; the maintainer's retail recording (2026-10-01) shows his hands brought together at that moment.
- **Beyond XiEvents:** XiEvents names the operand only as the value passed to the emote call; the retail scripts and recording tie it to the protocol emote ids.

### 0x6F

- Layout: `6F`. Retail starts a 16-frame wait only when the stack's wait timer is free, so an interrupted 0x1C wait keeps its own time (XiEvents OpCodes/0x006F). GordianXI does the same on the running stack's `WaitTime`.

### 0x70, 0x76

- Layout: `70`; `76 actor:u32`. Retail yields while `Render.Flags3` bit 1 (a turn in progress) is set; 0x70 checks the event's own entity, 0x76 a named actor whose model is ready, and both cancel the turn when it is over (XiEvents OpCodes/0x0070, 0x0076).
- GordianXI: a turn (0x39 / 0x4B / 0x1E / 0x4A) marks the entity as turning for `ln(angle / 0.05) / 8` seconds, the viewport's ease time (`EventVm.SetEventHeading`, `EventScene.StartTurn`); 0x70 / 0x76 yield while `EventScene.IsTurning`. Retail's own turn rate is not measured ([events/vm.md](vm.md)).

### 0x71 `CodeOPENPASSWIN`

- Layout: `71 sub ...`. Open opcodes yield once after opening; the matching wait sub yields until the window closes.

| Sub | Bytes | Events | Meaning (XiEvents OpCodes/0x0071) | GordianXI |
|---|---|---|---|---|
| 0x00 | 2 | 21 | Open the text (password) window, 16 characters | stepped |
| 0x01 | 2 | 23 | Wait for the text, then send it with C2S 0x060 | stepped |
| 0x02 | 2 | 17 | Wait for the server's answer | stepped |
| 0x03 | 4 | 5 | Run the text through the vulgar-word filter, store the result | stepped |
| 0x10 | 4 | 254 | Open the number window (digits from a work value) | stepped |
| 0x11 | 4 | 254 | Wait for the number, store it | stepped |
| 0x12 | 6 | 1,855 | Open the number window with a size and type | stepped |
| 0x13 | 4 | 1,855 | As 0x11 | stepped |
| 0x20 | 16 | 22 | Open an input menu from seven work values (Moblin Maze Mongers) | stepped |
| 0x21 | 2 | 22 | Send that menu's choice (C2S 0x0D8) | stepped |
| 0x30 | 4 | 3 | Open a text window (quill count) | stepped |
| 0x31 | 4 | 11 | Wait for it, store the text in a work string | stepped |
| 0x32 | 6 | 8 | Open a text window with a size and type | stepped |
| 0x40 | 4 | 3 | Open the linkshell concierge window | stepped |
| 0x41 | 8 | 3 | Wait for its choice, store three values | stepped |
| 0x50-0x53 | 4 / 2 / 4 / 2 | 1 each | Unknown menu: create, destroy, update, wait | stepped |
| 0x54 | 10 | 1 | Unknown menu: size and place from four work values | stepped |
| 0x55 | 4 | 0 | Unknown menu call with one work value | stepped |

- The open and wait subs come in pairs in the corpus (0x10 / 0x11 and 0x12 / 0x13 have equal counts).
- Stepping means no input window opens and no 0x060 / 0x0D8 packet is sent, so the server never receives the player's input.
- `EventOpcodeTable` has XiEvents' 0x54 = 10 and 0x55 = 4 bytes ([#201](https://github.com/jimmy58663/GordianXI/issues/201), 2026-10-03); before, the one 0x54 event ended at that opcode. The corpus walk lands one more entry with them (207,658 of 209,144).

### 0x72 `CodeGETWEATER`

- Layout: `72 00 zone:work` (4), `72 01 zone:work day:work` (6). Sub 0 asks for weather file 7033 (7037 for zone 100 and up); sub 1 yields until it is read, then writes three forecast values into zone work values 2-4 (XiEvents OpCodes/0x0072).
- Both subs are stepped; the forecast is not decoded ([#125](https://github.com/jimmy58663/GordianXI/issues/125)).
- **Beyond XiEvents / xi-tools:** XiEvents' sub 0 advances 10 bytes when the read cannot start, and xi-tools lists a 10-byte form it never found. Both subs occur 702 times in 234 events in the census: the scripts always write `72 00` then `72 01`, so the 10 is 4 + 6, sub 0 skipping its own `72 01`, not a third encoding.

### 0x73 `CodeMAGICSCHEDULOR`, 0xC4, 0xAD

- Layout: 0x73 `73 resource:work actor:u32 target:u32` (11); 0xC4 and 0xAD `op sub resource:work actor:u32 target:u32` (12). All start a task with the routine tag `main` between two actors whose models are ready (XiEvents OpCodes/0x0073, 0x00C4, 0x00AD). 0xC4 subs 0 / 1 / 2 pick three more task kinds; 0xAD uses the sub's low nibble (0-9) and does nothing when the high nibble is set.
- All three are stepped.
- **Differs from XiEvents:** XiEvents gives 0xC4 as 11 bytes, but its own pseudo code steps 12 (the 0x73 helper with one extra byte), and it reads 0xC4's first actor at +3, inside the resource operand. The corpus walk confirms 12 ([events/vm.md](vm.md), 2026-09-28), and xi-tools `docs/events/typed_opcodes.md` lays it out as sub, resource, actor, actor. GordianXI uses 12. xi-tools `docs/events/opcodes.md` still says 11.

### 0x75 `CodeLOADROOM`

- Layout: `75 00 room:work` (4), `75 01` (2), `75 02` (2). Every sub yields while the zone is still reading room data. Sub 0 opens the indoor room without telling the server, sub 1 passes, sub 2 sends the player's sub-region (C2S 0x0F2) and retries until it is queued (XiEvents OpCodes/0x0075).
- Stepped: rooms are not loaded and 0x0F2 is not sent.
- **Beyond XiEvents:** XiEvents' sub 2 moves the program position back 6 bytes before it reads its operand, and moves forward 8 once the packet is queued. Every one of the 33 retail `75 02` sites (2026-10-01, all zones) follows `75 00 room 75 01`, so sub 2 reuses the room operand of the `75 00` six bytes earlier and is 2 bytes long.

### 0x77, 0x78, 0xC9

- Layout: `77 hour:work weather:work`; `78`; `C9`. 0x77 stops the game clock and sets the hour (minute 0) unless the hour is 255, and sets the weather unless it is 255. 0x78 restarts the clock if an event stopped it and always resets the weather; 0xC9 restarts the clock only (XiEvents OpCodes/0x0077, 0x0078, 0x00C9).
- GordianXI: 0x77 calls `IEventVmHost.LockEnvironment` (`WorldState.LockTimeOfDay`, read by the sky and lighting) and 0x78 `UnlockEnvironment`; the event's end releases both. 0xC9 is stepped. Used by the Windurst intros to hold the clock at midnight ([events/vm.md](vm.md)).

### 0x79

- Layout: `79 sub actor:u32 ...`.

| Sub | Bytes | Events | Meaning (XiEvents OpCodes/0x0079) | GordianXI |
|---|---|---|---|---|
| 0x00 | 10 | 3,860 | `actor target:u32`: lookatone with speech frame 6 | runs (`IEventVmHost.SetEntityLook`) |
| 0x01 | 12 | 7 | `actor target:u32 frame:work`: lookatone with that speech frame | runs |
| 0x02 | 10 | 143 | `actor x:work y:work`: look mode 2 with a fixed look axis | runs, provisional (`SetEntityLookAxis`) |

- Evidence: the head turns in-game (2026-10-01); the sub 2 axis is read as a turn and a tilt from a 2026-10-02 scan of zones 0-299 and the maintainer's Port Jeuno recording ([#188](https://github.com/jimmy58663/GordianXI/issues/188), [world/entities-and-animation.md](../world/entities-and-animation.md)).
- An unknown sub has no length and retail does not advance on it. `EventVm` reports it through `OnSkippedOpcode` and ends the request at once ([#201](https://github.com/jimmy58663/GordianXI/issues/201)); before, it added 0 and spun until the 2,000,000-step guard ended the request.

### 0x7A

- Layout: `7A 00 actor:u32` (6) clears every request stack of the actor's VM; `7A 01 actor:u32 slot` (7) clears the stack running that slot; `7A 02 actor:u32` (6) makes this VM act on the actor's event data (yields until it can); `7A 03` (2) goes back to its own; `7A 04 ? actor:u32 slot` (8) copies a request onto the actor; `7A 05 actor:u32` (6) undoes 0x04 (XiEvents OpCodes/0x007A).
- Census subs: 0x00 3,149 uses in 533 events, 0x02 22, 0x03 4, 0x04 23, 0x05 13 events.
- Stepped. With request stacks per entity now in `EventVm`, sub 0 / 1 would map onto freeing stacks of another VM in the scene; not done.

### 0x7B

- Layout: `7B actor:u32`. Clears the actor's look mode and sets its speech frame to -1 (XiEvents OpCodes/0x007B). GordianXI: `SetEntityLook(actor, none, -1)`, which also ends a 0x79 sub 2 axis hold. Tested in-game (2026-10-02): the head lets go at the script's 0x7B, as in retail ([world/entities-and-animation.md](../world/entities-and-animation.md)).

### 0x7E `CodeCHOCOBO`

- Layout: `7E sub actor:u32 ...`; subs 0 / 1 / 2 / 4 / 5 / 8 = 6, 3 = 16, 6 = 18, 7 = 8 (XiEvents OpCodes/0x007E; confirmed by the corpus walk, [events/vm.md](vm.md)).
- Census: 0x05 296 events, 0x04 215, 0x06 174, 0x03 41, 0x02 36, 0x01 30, 0x07 6, 0x00 3, 0x08 1.
- Stepped.
- **Beyond XiEvents:** sub 4, the second most used, has no case of its own in XiEvents' pseudo code: it sets nothing and steps 6. Sub 2 does not advance until the actor's attachment call reports done, so in retail it is a wait.

### 0x7F `CodeQUERYWAIT2`

- Layout: `7F`. GordianXI: `EventVm.ExecQueryWait(endOnCancel: false)`. The choice goes to zone work value 0 as option - 1; a cancel stores 255 and the event goes on, where 0x25 stores 254 and ends it. With no query open it steps over and yields (XiEvents OpCodes/0x007F).

### 0x80 `CodeLOADWAIT`

- Layout: `80 actor:u32`. Retail yields until the actor's model is ready and its resource list has loaded; an actor that is absent, not yet created, or of types 3-5 passes (XiEvents OpCodes/0x0080).
- GordianXI passes at once: motions load when they are asked for ([events/vm.md](vm.md)).
- **Beyond the census:** about 8,300 of the 37,175 0x80 uses are 0x9D table data walked as code (the high byte of an immediate key). About 6,900 events use it outside such runs (2026-10-01 scan).

### 0x81

- Layout: `81 on actor:u32`. Sets the actor model's blink flag from the byte at +1 (XiEvents OpCodes/0x0081).
- Retail: any non-zero byte turns the blink on, zero off; an actor without a ready model is passed over. GordianXI keeps the switch as `EventRenderFlags.NoBlink` on the entity (`IEventVmHost.SetEntityRenderFlag`, held until the entity arrives), and `EntityRenderer` then starts no new blink (`FaceMotion`, [world/entities-and-animation.md](../world/entities-and-animation.md)). The flag is cleared when the event ends ([#198](https://github.com/jimmy58663/GordianXI/issues/198)).
- **Beyond XiEvents:** census of the operand byte (2026-10-03): 0 in 3,146 uses, 1 in 1,873, other values in about 30 uses (table data walked as code). Scripts turn the blink off around a facial gesture and on again after it: 78% of the events that use 0x81 also play a 0x5B gesture (5% of all events), and the short talk events show the pattern whole, e.g. Ahkk Jharcham (Aht Urhgan Whitegate, event 10: `81 00 self`, 0x5B `kisi`, a line, 0x53, `81 01 self`) and Cacaroon (event 3036, the same with 0x7C 00 / 01 beside each 0x81). The gesture's own clip moves the eyes, and a random blink on top would fight it (*inference*). Some actors turn it off in one event and on in a later one (Port Jeuno actor 0x010F3038: events 18 off, 21 on, 22 off, 23 on), so retail may keep the switch past an event's end; the talk events restore it themselves, and GordianXI also clears it at the end, so an NPC never stays without a blink. Open: [#217](https://github.com/jimmy58663/GordianXI/issues/217).
- **Differs from xi-tools:** xi-tools `docs/events/opcodes.md` reads it as a value in the actor's warp data and calls that unverified; XiEvents' pseudo code writes the model's blink flag.

### 0x82

- Layout: `82 rect:u32 else:u16`. Looks up a range rectangle of the zone map by id and tests the event entity's position; inside goes on (+7), outside jumps to `else` (XiEvents OpCodes/0x0082).
- GordianXI always takes the outside branch: the zone's range rectangles are not loaded.

### 0x83

- Layout: `83 dest:work`. Stores retail's game time value (XiEvents OpCodes/0x0083). GordianXI stores `IEventVmHost.GameTime`, which `EventDialogController` gives as Unix seconds. Whether retail's value uses the same epoch has not been checked; 0xAA turns this value into a Vana'diel date, so a wrong epoch would show as a wrong date.

### 0x87, 0x88

- Layout: `87 sub`, `88 sub`. Subs 0 and 2 queue C2S 0x01B (world pass) with a mode (0x87: 0 / 2, 0x88: 1 / 3) and yield; sub 1 yields until the server's answer clears the wait flag (XiEvents OpCodes/0x0087, 0x0088).
- Stepped. Most census hits are table data (25 of 37 and 20 of 32 uses); about 3 events use each as code.

### 0x90

- Layout: `90`. Sets the event hide flag (`Render.Flags0` bit 17, as 0x22 01) and `Render.Flags1` bit 12 of the event's own entity (XiEvents OpCodes/0x0090). Bit 12 makes retail's InitEvent2 ask the server for the entity again (C2S 0x016).
- GordianXI hides the entity (`IEventVmHost.SetEntityHidden`, `WorldEntity.IsEventHidden`) and does not keep bit 12: `EventDialogController` asks for missing participants itself ([#198](https://github.com/jimmy58663/GordianXI/issues/198)).
- **Beyond the census:** 17 of the 23 events are one shared script: event 65 of an actor block in zones 48, 50, 80, 87, 94 and 230-241 runs `20 90 4E` at offset 3212 (control lock, hide itself, then 0x4E on an actor). The other uses in zones 222 and 241 sit among `80` table bytes.

### 0x94

- Layout: `94 on actor:u32`. Sets `Render.Flags3` bit 17 of the actor from bit 0 of the byte at +1 (XiEvents OpCodes/0x0094).
- GordianXI keeps the bit as `EventRenderFlags.Flags3Bit17` on the entity until the event ends; nothing reads it ([#198](https://github.com/jimmy58663/GordianXI/issues/198)).
- **Beyond XiEvents:** census (2026-10-03): 14,815 uses set it and 140 clear it; 2,233 of the sets name the player. Most scripts set it next to 0x92 at the event's start (Port Jeuno 324 on every actor it places). It is not the name plate (the player's plate stays in 324's retail recording). The events that use it are the staged cutscenes: the opcodes most over-represented beside it are the cutscene ones (0xBA placements, 0x52 scheduler ends, 0x77 / 0x78 clock lock, 0x9A), each about 10-16 times their rate in all events, with no single opcode that would point at an effect. A plain talk event uses it too: Deraquien (Southern San d'Oria, event 18, LandSandBoat's `onTrigger`) starts `94 01 self`, then turns to the player and plays the `sl00` / `tlk0` motion packages around two lines that wait for Enter. It is not the talking mouth: the mouth follows each line's length whether the bit is set or not (in-game comparison, 2026-10-03; [world/entities-and-animation.md](../world/entities-and-animation.md)). What it changes is open. Open: [#217](https://github.com/jimmy58663/GordianXI/issues/217).

### 0x99

- Layout: `99 actor:u32`. Retail moves past the opcode first and only then yields while the actor plays an animation, so it waits one frame at most (XiEvents OpCodes/0x0099). GordianXI does the same (`EventScene.IsEntityActing`).
- **Beyond XiEvents:** the description says it yields while the animation plays; the code it gives does not wait for the end.

### 0x9B

- Layout: `9B`. Unlike 0x99 it does wait: it stays on the opcode while the event's own entity plays an animation (XiEvents OpCodes/0x009B).
- Stepped, so the script goes on before the action ends (130 events). It could reuse 0x99's `IsEntityActing` test.

### 0x9C

- Layout: `9C dest:work`. Retail maps its language setting to 1 Japanese, 2 English, 3 French, 4 German, and the current client reports French and German as English (XiEvents OpCodes/0x009C). GordianXI stores 2.

<a id="0x9d"></a>
### 0x9D

- Tables are runs of 16-bit work references in the byte code, mostly immediate keys (`NN 80`). Layouts as GordianXI reads them (`EventVm.ExecTable`):

| Sub | Bytes | Events | Layout and meaning (XiEvents OpCodes/0x009D) | GordianXI |
|---|---|---|---|---|
| 0x00 | 8 | 1,630 | `table:u16 dest:work index:work`: dest = entry | runs |
| 0x01 | 8 | 6 | Copy a 16-byte string from the table into a work string | stepped |
| 0x02 | 6 | 37 | `table:u16 slot:work`: share the table in zone pointer slot 0-63 | runs |
| 0x03 | 8 | 107 | `slot:work dest:work index:work`: read through a shared slot | runs |
| 0x04 | 8 | 0 | String read through a shared slot | stepped |
| 0x05 | 8 | 806 | `table:u16 value:work index:work`: write value through the entry | runs |
| 0x06 | 8 | 0 | String write through a table | stepped |
| 0x07 | 6 | 5 | `table:u16 index:work`: call the entry's offset (pushes the return like 0x1A) | runs |
| 0x08 | 23 | 10 | Compare a work string with an inline 16-byte string, branch | stepped (falls through) |
| 0x09 | 9 | 0 | Compare two work strings, branch | stepped (falls through) |
| 0x0A | 10 | 119 | 0x00 with a bound: an index at or past a non-zero bound reads entry 0 | runs |
| 0x0B | 10 | 0 | 0x01 with a bound | stepped |
| 0x0C | 8 | 4 | 0x02 with a bound | runs |
| 0x0D | 10 | 13 | 0x03 with a bound | runs |
| 0x0E | 10 | 0 | 0x04 with a bound | stepped |
| 0x0F | 10 | 50 | 0x05 with a bound | runs |
| 0x10 | 10 | 0 | 0x06 with a bound | stepped |

- Evidence: the home point script writes its zone lists through these tables and reads them back for the query (checked against the maintainer's recording and capture, 2026-09-28, `EventDialogController` message parameters).
- **Differs from XiEvents:** read literally, XiEvents' sub 0x0D reads only when the shared slot is empty, which cannot work; GordianXI reads when the slot holds a table. XiEvents' sub 0x00 also applies a bound read at +8, past its own 8 bytes; GordianXI applies bounds only to the 10-byte subs. Its 0x0C / 0x0D / 0x0E failure paths step 6 / 8 / 8 bytes, not their full length; GordianXI always steps the full length.
- **Census caveat (beyond the census):** a table can sit after a `00` or `1B` inside an event's offset range, and the length walk then reads it as code: every `NN 80` key counts as opcode NN. Examples from a 2026-10-01 scan: zone 4 `1B A6 80 A7 80 A8 80 ...`, zone 256 `... C9 80 CA 80 CB 80 CC 80 ...`. All the census hits of 0x6D, 0xB2, 0xCB and 0xD7, 202 of 205 of 0xA6, most of 0x87 / 0x88 / 0x91 / 0x98, and every "sub 0x80" key are such data. The census rows are therefore not strict lower bounds for opcodes that look like table bytes.

### 0x9F `CodeLOADEVENTSCHEDULER2` and the other scene task families

- Layout as 0x45 / 0x55 / 0x52: start `op resource:work actor:u32 target:u32 routine:u32 value:work` (17); wait and stop `op resource:work actor:u32 target:u32 routine:u32` (15). Only the file base changes, and only 0x45's base 30704 gets the 300 / 600 remapping (XiEvents OpCodes/0x0045 and the variants).

| Start | Wait | Stop | File base | Events (start) |
|---|---|---|---|---|
| 0x62 | 0xA0 | none (0xA1 stops 0x45 tasks) | 5012 | 1,701 |
| 0x9F | 0xA2 | 0xA3 | 51183 | 1,883 |
| 0xBB | 0xBC | 0xBD | 56685 | 478 |
| 0xC5 | 0xC6 | 0xC7 | 67355 | 708 |
| 0xCD | 0xCE | 0xCF | 70435 | 1,039 |
| 0xD0 | 0xD1 | 0xD2 | 70691 | 453 |
| 0xD5 | 0xD6 | 0xD7 | 102449 | 73 |
| 0x7D (player only, tag `main`) | | | 5112 | 57 |

- Every family runs (#192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)): `EventSceneResource.GetBandBase` gives the base per opcode. 0xA1 runs as 0x52 (below). 0x9F / 0xA2 / 0xA3 run as `EventScene` tasks on file `51183 + p` (`EventVm.ExecStartTask` / `ExecWaitTask` / `ExecEndTask` with `EventSceneResource.GetSecondFileId`, [#192](https://github.com/jimmy58663/GordianXI/issues/192)): in Port Jeuno event 324 they carry the eyelid opening (51402) and the flash in the sky (51327). Their routines' effect commands are played as described in [vm.md](vm.md#cutscene-schedulers). In the same scene 0xCD starts `s002` of file 70443 (`p` 8) on the player at 15.6 s, the sparkles floating in front of the camera, and `kil2` ends them at 48.4 s.
- 0xA1: XiEvents gives it the base 30704 (0x52's), and the 30704 check in the shared helper then applies the 300 / 600 remapping, so it is a copy of 0x52 although it sits in the 0x62 family's stop place (0xA0 uses 5012). GordianXI follows XiEvents. 0xA1 never occurs in the retail scripts, so the reading cannot be checked against a scene, and the 0x62 tasks have no stop opcode.

### 0xA6

- Layout: `A6 00` (2) queues C2S 0x0EB and yields; `A6 01` (2) yields until the S2C 0x10E answer clears the flag; `A6 02 dest:work` (4) stores the sub-map number (XiEvents OpCodes/0x00A6; the packet pairing is also in xi-tools `docs/reference/ps2_decomp_crosscheck.md`).
- Real use: subs 0, 1 and 2 once each, in one event (2026-10-01 scan). The other 202 census events are table data (see 0x9D). Stepped.

### 0xAB

- Layout: `AB sub` (2), except 0x11 and 0x14-0x18 `AB sub value:work` (4) and 0x1B / 0x1C `AB sub actor:u32` (6). Subs 0x01-0x0E and 0x12-0x13 set or clear one render-flag bit of the event entity; 0x04 waits until the entity is in an event state or idle; 0x09 / 0x0A and 0x0F / 0x10 toggle client-wide flags; 0x11 stores a value that respawns every entity when it is not -1; 0x19 / 0x1A set `Render.Flags7` bit 19 of the event entity, 0x1B / 0x1C of an actor (XiEvents OpCodes/0x00AB).
- Census: 0x11 1,080 events, 0x0A 635, 0x09 396, 0x03 337, 0x04 301, other subs 63 or fewer, 0x1B 3.
- Partial ([#198](https://github.com/jimmy58663/GordianXI/issues/198)): the entity sub-cases (0x01-0x08, 0x0B-0x0E, 0x12, 0x13, 0x19-0x1C) set or clear their bit in `WorldEntity.EventRenderFlags` (`EventVm.ExecRenderFlags`), cleared when the event ends; nothing reads them yet. Sub 4 first waits while the event's own entity plays an event action (`EventScene.IsEntityActing`, as 0x99), then clears its bit; retail also goes on at once when the entity is in an event status, which GordianXI does not model. The client-wide subs (0x09 / 0x0A, 0x0F / 0x10), the respawn value 0x11 and the helpers 0x14-0x18 are stepped without a diagnostic; any other sub is logged and ends the request (retail stalls on it). Open: [#217](https://github.com/jimmy58663/GordianXI/issues/217).
- `EventOpcodeTable` has every XiEvents length (0x13 = 2, 0x14-0x18 = 4, 0x19 / 0x1A = 2, 0x1B / 0x1C = 6, added in [#198](https://github.com/jimmy58663/GordianXI/issues/198)); before, the three events that use 0x1B ended at that opcode.

### 0xB0

- Layout: `B0 sub speaker:u32 listener:u32 msg:work`. With sub 0, retail prints the line with the first actor's name and ties the mouth to the second actor; any other sub does nothing and does not advance (XiEvents OpCodes/0x00B0).
- GordianXI prints it as the first actor's line (`EventSpeaker.Entity`), plays that actor's mouth clip, and ignores the sub byte and the listener.
- **Differs from XiEvents:** XiEvents sets the speaking mouth index to the second actor. If that holds, the mouth GordianXI moves for 0xB0 lines is the wrong one. Not checked against a recording.

### 0xB2

- Layout per XiEvents' pseudo code: sub 0 waits a number of frames from a work value (4 bytes); sub 1 asks to open the delivery box and yields (2 bytes).
- `EventOpcodeTable` follows XiEvents' code: 0 = 4, 1 = 2 (the two were swapped until [#201](https://github.com/jimmy58663/GordianXI/issues/201)). No real use exists in the retail scripts (the one census hit is table data), so it changes nothing today. Stepped.

### 0xB4

- Layout: `B4 sub ...`, lengths per XiEvents OpCodes/0x00B4 (all in `EventOpcodeTable`). Every case yields once. Main subs by use: 0x00 (1,062 events) copies the 16-byte inline string at +4 into the work string at +2; 0x13 (237) does the same with `@` turned into spaces; 0x01 (106) copies one of four strings S2C 0x05D sent; 0x14 / 0x15 (87 / 83) update or test the map window; 0x04 (45) copies a work string into the event and world pass buffers; 0x06 (42) presses the open query's selection.
- Stepped, so work strings stay empty and 0x9D string compares see nothing ([events/vm.md](vm.md), Not done).

### 0xB6

- Layout: `B6 sub ...`; subs 0x00-0x0A, 0x0C, 0x0F, 0x11 = 4, 0x10 / 0x12 / 0x13 = 2, 0x0B = 20, 0x0D = 14, 0x0E = 16, 0x14 / 0x15 = 6 (XiEvents OpCodes/0x00B6). Every case yields once.
- Main subs by use: 0x00 (4,066 events, hair), 0x0B (3,292, race and every gear slot from nine work values), 0x0F (776, model size = value / 100), 0x0A (203, ranged slot), 0x0E (112) and 0x0D (40) (race + 32 and a reduced slot set, other slots cleared), 0x11 (76).
- Stepped: event actors keep their server look. One of the most used opcodes GordianXI does not run.

### 0xBA

- Layout: `BA actor:u32 x:work y:work height:work heading:work`. Writes the actor's event position and heading (4096 steps per turn), grounds it unless its flags say not to, then copies the event pose to the drawn pose (XiEvents OpCodes/0x00BA).
- GordianXI: `SetEventPosition` / `SetEventHeading(turn: false)` on the actor's `EventVm` when it takes part in the event; others are left alone (retail also needs the actor to have an event VM). No ground snap here; the renderer draws event entities on the floor below them ([events/vm.md](vm.md), #86, 2026-09-30).

### 0xBE

- Layout: `BE dest:work`. Stores the server id of the entity that queued the running request (`RequestStack.Who`; the event's own entity for its first stack), XiEvents OpCodes/0x00BE.

### 0xC0

- Layout: `C0 on:work`. Sets `Render.Flags3` bit 12 of the event's own entity from bit 0 of the work value (XiEvents OpCodes/0x00C0).
- GordianXI keeps the bit as `EventRenderFlags.Flags3Bit12` until the event ends; nothing reads it ([#198](https://github.com/jimmy58663/GordianXI/issues/198)).
- **Beyond XiEvents:** every use in the census (2026-10-03) takes an immediate-data operand (`NN 80`), and in 674 events it is the whole entry: `C0 value` then `00`. These are the entries a cutscene's actors run on themselves (#85), and the actors are story characters and summons that exist for cutscenes: Disjoined One (La Theine Plateau and other zones, 211 entries), Iroha, Lilisette, Excenmille, Aphmau, Luzaf, Nashmeira, Arciela, Aldo, Odin, Fenrir, Cait Sith, and a Moogle in Southern / Northern San d'Oria (events 30023-30026). Most set the bit; some helper entries clear it again. What it changes is open; that it marks the cutscene-only story actors is the pattern (*inference*). What the bit changes is open. Open: [#217](https://github.com/jimmy58663/GordianXI/issues/217).

### 0xC2

- Layout: `C2 01 dest:work` (4) stores a mask of party members whose Mog House can be visited; `C2 02 member:work dest:work` (6) says whether that member's house is open; any other sub steps 2 (XiEvents OpCodes/0x00C2).
- Real subs in the census are 0x01 (16 events) and 0x02 (16); the 19 "0x80" events are table data. Stepped. `EventOpcodeTable` steps any sub other than 0 / 1 / 2 by 2, as XiEvents' code does ([#201](https://github.com/jimmy58663/GordianXI/issues/201)); the census walk now continues past the table-data `C2 80`, so the opcodes after it (0xBD, 0xBE, 0xC3-0xC5) count a few more table bytes.

### 0xD4

- Layout: `D4 sub ...`.

| Sub | Bytes | Events | Meaning (XiEvents OpCodes/0x00D4) | GordianXI |
|---|---|---|---|---|
| 0x00 | 8 | 488 | The 0x24 query, one byte later, with the zone's own map (type 6) opened behind it | runs as the query; no map drawn |
| 0x01 | 8 | 488 | Marker data for the query from three work values | stepped (diagnostic) |
| 0x02 | 8 | 166 | The 0x24 query without the map | runs (`OpenQuery`) |
| 0x03 | 6 | 163 | Marker data from two work values | stepped (diagnostic) |
| 0x04 | 12 | 6 | Marker data from five work values | stepped (diagnostic) |
| 0x05 | 12 | 19 | As 0x04 with another marker kind | stepped (diagnostic) |

- Evidence: the home point lists open their query with 0xD4 and send their markers with subs 1 / 3 / 4 / 5 ([#168](https://github.com/jimmy58663/GordianXI/issues/168)); the query window's geometry is from the maintainer's retail recording of the home point menu (2026-09-28, [ui/stock-ui.md](../ui/stock-ui.md), chunk 6). The map and its markers are [#168](https://github.com/jimmy58663/GordianXI/issues/168).
- An unknown sub ends the request here; retail neither advances nor yields.

### 0xD8

- Layout: `D8 00 actor:u32` (6) copies the actor's last roll, heading and pitch into its event direction; `D8 0n actor:u32 angle:work` (8) sets roll (1), heading (2) or pitch (3); `D8 04 actor:u32 roll:work heading:work pitch:work` (12) sets all three; 4096 steps per turn (XiEvents OpCodes/0x00D8).
- Census: sub 0x03 38 events, 0x01 28, 0x02 24, 0x00 12. Stepped, so an actor turned with sub 2 keeps its old heading; sub 2 could reuse `SetEventHeading` the way 0xBA does.
- **Differs from xi-tools:** xi-tools `docs/events/opcodes.md` describes two handlers sharing the byte (a sound flag and the event direction) and calls that unverified; XiEvents has one handler, the event direction. The sound flag belongs to 0xD9.

## Open questions

- 0x12 range of retail rand() (GordianXI uses Random.Next, up to 2^31-1).
- 0x31 time operand: as transcribed, the walk stops moving but keeps yielding once the time runs out.
- 0x38 low or high byte (XiEvents example vs xi-tools correction).
- 0x3A / 0x3B read the running VM's own event position, not the actor's, when the actor is in an event (XiEvents as transcribed). GordianXI reads the named actor's; the two agree when the script names itself.
- 0x59 subs 0 / 1: the unit of `TurnSpeed` (read as 4096ths of a turn per frame).
- 0x5A axis cross-over.
- 0x57 rounding (GordianXI rounds the frame delay, retail's conversion not checked).
- Whether the 0x80 / 0x81 "sub-cases" of 0x1F / 0x31 / 0x47 / 0x59 / 0x5A / 0x5F are inline data like `1F 17` (retail would stall on them).
- 0x83: retail's game time value and its epoch, and whether 0xAA's date split of GordianXI's Unix seconds would match retail.
- 0xB0: whose mouth moves in retail, the speaker at +2 or the listener at +6.
- 0x9D sub 0x00: does retail really apply a bound read at +8, or is that XiEvents copying 0x0A?
- 0xB2 sub 0 reads its frame count at +1, its own sub byte, in XiEvents; probably +2.
- 0x93 in 5,479 events as an item information window seems a lot; not checked in a recording.
- The table-data share of 0x80 (about 8,300 of 37,175 uses) comes from a pattern heuristic (`NN 80 NN 80` around the position), not an exact count.
