# Assembly Layers

How the client's code is split into assemblies, what belongs in each, and how the split is enforced. Issue [#433](https://github.com/jimmy58663/GordianXI/issues/433) made the split; this page is the rule for new code.

```text
Gordian.App ──► Gordian.Engine ──► Gordian.Core
Gordian.App ──► Gordian.Addons ──► Gordian.Core
Gordian.App ──► Gordian.Automation ──► Gordian.Core
```

| Assembly | Holds | References |
|---|---|---|
| `Gordian.Core` | Protocol and session (`Network`: login, crypto, framing, packet decoders and builders, packet modules, `SessionNetworkManager`, `PacketParser`), world and entity state (`World`, including collision), the per-entity animation state and motion data that `WorldEntity` carries (`Animation`: `EntityAnimationState`, `ActionRequest` / `ActionMotion`, `ActionMotionLibrary`, `EventMotionBank`, `NpcStanceResolver`, `RestPosture`), DAT resources and the VFS (`Resources`), `Config` (with `FeatureRestrictions`), `Diagnostics`, account `Profiles` | BCL only |
| `Gordian.Engine` | Everything that plays, presents or takes input on top of that state: `Actions` (`PlayerActionService`), `Animation` playback (`ActionPlaybackQueue`, `SchedulerPlayback`, `SynthesisAnimationController`, head look, face motion, footfalls), `Audio`, `Events` (the event VM and dialog), `Graphics` (CPU-side particles, effects, camera), `Input`, `Macros`, `Ui` (stock UI state and text), and `Sessions`: `CharacterSession` (the per-character composition root that builds and wires all of the above), `SessionRegistry`, `ZoneCollisionService`, `LaunchOrchestrator` | Core |
| `Gordian.Addons`, `Gordian.Automation` | The addon runtime and the automation engine | Core only, never Engine or each other |
| `Gordian.App` | Avalonia shell, the GPU renderer (NeoVeldrid), OpenAL output, SDL input, ImGui | All of the above |

The GPU, audio and UI packages (Avalonia, NeoVeldrid, SDL3, OpenAL, Hexa.NET.ImGui) are referenced by `Gordian.App` only; Core and Engine are plain .NET.

## Where new code goes
- **State the server tells us about, or that a renderer reads every frame, goes in Core.** Packet modules write it; the engine and the renderer read it directly (no per-entity events per frame).
- **Anything that reacts to that state by playing, drawing, printing or sending goes in the engine.** The engine subscribes to Core's state events (`CombatState.ActionExecuted`, `SchedulerState.ActorSchedulerPosted`, ...); Core never calls into playback.
- **Handler order is construction order.** `CharacterSession` builds `ActionPlaybackQueue`, `SchedulerPlayback` and `PlayerActionService` first, so their handlers run before the chat, menus and the App's view models, as they did when the parser built them.
- **A type in the wrong layer is moved, not hidden behind an interface.** Constants a lower layer needs live in that layer (`EntityGrounding.StepUpHeight`, `WorldEntity.MouthFlapsPerClip`); the engine's names for them refer to Core's.
- A Core doc comment can't `cref` an engine type; name it as `<c>Type</c>` instead.

## Enforcement
- **The compiler.** Core has no reference to Engine, and Addons and Automation reference Core only.
- **`InternalsVisibleTo`.** Core shows its internals to `Gordian.Engine` and the test projects only. The App and the addon runtime see only Core's public surface, so `SessionProfile.FeatureRestrictions` (internal setter) can't be set from either. The engine shows its internals to the test projects only.
- **Tests.** `tests/Gordian.Core.Tests/Architecture/LayeringTests.cs` scans the IL of both assemblies for references from a type to a higher layer (with an allowlist, now empty, that may only shrink). It also gives every Core area a layer, keeps engine namespaces in the engine assembly, and checks the `FeatureRestrictions` setter. `AddonsProjectSanityTests` and `AutomationProjectSanityTests` fail if `Gordian.Engine.dll` is copied next to them, which means the project picked up a reference to the engine. The IL scan can't see inlined constants or default parameter values; the assembly split catches those.

## Not split (yet)
- **No separate data assembly.** Resources' loaders (`ResourceManager`, `ZoneDataLoader`) build world, collision and entity state and log through `Diagnostics`. A BCL-only `Gordian.Data` would need the logger and the collision types moved below it and the loaders moved above it.
- **One test project for both assemblies.** `Gordian.Core.Tests` references Core and Engine; the engine's tests can move to their own project later.
- **No curated addon surface yet.** Addons see all of Core's public types. The narrow UI interface the addon API needs is designed when Phase 6 starts.
