// src/Gordian.App/Audio/GameAudioService.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gordian.App.Services;
using Gordian.Core.Audio;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.App.Audio
{
    /// <summary>
    /// The application's sound: one audio device and mixer shared by every viewport, following the session of the
    /// viewport that last claimed it (<see cref="Claim"/>; with multi-boxing only the focused character is heard). Each
    /// frame the owning viewport calls <see cref="Update"/>, which picks the music, the zone's ambient loop and the
    /// listener; the sound effect entry points (<see cref="PlayEffect"/>) can be called from any thread.
    /// </summary>
    public sealed class GameAudioService : IDisposable
    {
        /// <summary>Crossfade between ambient loops (provisional).</summary>
        public const float AmbientFadeSeconds = 2f;

        private static readonly Lazy<GameAudioService> _instance = new(() => new GameAudioService(), LazyThreadSafetyMode.ExecutionAndPublication);
        private static volatile bool _created;

        private readonly AudioEngine _engine;
        private readonly SoundLibrary? _library;
        private readonly ResourceManager? _resources;
        private readonly MusicDirector _music;
        private readonly ZoneEmitterAudio _emitters;
        private volatile IReadOnlyList<ZoneSoundEmitter>? _pendingEmitters;
        private object? _owner;
        private CharacterSession? _session;
        private ushort _zoneId;
        private volatile ZoneSoundTable _zoneSounds = ZoneSoundTable.Empty;
        private int _zoneLoadToken;
        private int _ambientSound;
        private int _ambientHandle;
        private int _ambientToken;
        private uint _cueTarget;
        private readonly FootstepTracker _footsteps = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, FootwearInfo> _footwear = new();
        private readonly System.Collections.Generic.List<FootstepEvent> _steps = new();
        private Vector3 _listenerPosition;
        private int _appliedMusicVolume = -1;
        private int _eventVolumeVersion = -1;
        private int _appliedEffectsVolume = -1;
        private int _controlVersion = -1;
        private volatile bool _heardIsPreferred;
        private readonly Dictionary<CharacterSession, Action<Gordian.Core.Network.Packets.ChatMessage>> _tellHandlers = new();

        private GameAudioService()
        {
            _resources = AppResourceManager.Instance;
            _engine = new AudioEngine();
            if (_resources is not null)
            {
                VfsSoundResolver resolver = new(_resources);
                _library = new SoundLibrary(new FfxiSoundLocator(_resources.GameDirectory, resolver.Resolve));
                if (!_library.Locator.HasRoots)
                {
                    GordianLog.Warn("AUDIO", $"No sound folders under '{_resources.GameDirectory}'.");
                }
            }

            _music = new MusicDirector(_engine.Mixer, id => _library?.OpenMusic(id));
            _emitters = new ZoneEmitterAudio(_engine.Mixer, id => _library?.GetEffectAsync(id) ?? Task.FromResult<PcmClip?>(null));
            Gordian.Core.Events.EventDialogController.MusicReady = () => !_engine.IsAvailable || _music.IsSettled;

            // Tell cues come from every session (#265: the cue of a character not heard can be let through).
            SessionRegistry.Default.SessionRegistered += (_, s) => WatchTells(s);
            SessionRegistry.Default.SessionUnregistered += (_, s) => UnwatchTells(s);
            foreach (CharacterSession s in SessionRegistry.Default.ActiveSessions)
            {
                WatchTells(s);
            }

            _created = true;
        }

        /// <summary>The shared service, created on first use.</summary>
        public static GameAudioService Instance => _instance.Value;

        /// <summary>Disposes the service if it was ever created (app exit).</summary>
        public static void Shutdown()
        {
            if (_created)
            {
                _instance.Value.Dispose();
            }
        }

        /// <summary>Releases <paramref name="owner"/> without creating the service.</summary>
        public static void ReleaseIfCreated(object owner)
        {
            if (_created)
            {
                _instance.Value.Release(owner);
            }
        }

        /// <summary>The mixer.</summary>
        public AudioMixer Mixer => _engine.Mixer;

        /// <summary>The music director (event overrides go through it).</summary>
        public MusicDirector Music => _music;

        /// <summary>The current zone's sound pointers (empty until loaded).</summary>
        public ZoneSoundTable ZoneSounds => _zoneSounds;

        /// <summary>The session being heard.</summary>
        public CharacterSession? Session => Volatile.Read(ref _session);

        /// <summary>
        /// Makes <paramref name="owner"/> (a viewport whose window was activated) the one whose session is heard, unless
        /// the multi-box policy (#265) prefers the character heard now: then focus does not take the sound away from it.
        /// </summary>
        public void Claim(object owner)
        {
            if (_heardIsPreferred && SoundControls.Current.MultiBoxPolicy != MultiBoxSoundPolicy.FocusedWindow)
            {
                return;
            }

            Volatile.Write(ref _owner, owner);
        }

        /// <summary>Whether <paramref name="owner"/> is heard; the first caller claims an unowned service.</summary>
        public bool IsOwner(object owner)
        {
            object? current = Volatile.Read(ref _owner);
            if (current is null)
            {
                Interlocked.CompareExchange(ref _owner, owner, null);
                current = Volatile.Read(ref _owner);
            }

            return ReferenceEquals(current, owner);
        }

        /// <summary>Releases ownership when a viewport closes, silencing its session.</summary>
        public void Release(object owner)
        {
            if (Interlocked.CompareExchange(ref _owner, null, owner) == owner)
            {
                _heardIsPreferred = false;
                DetachSession(_session);
                Volatile.Write(ref _session, null);
                _music.StopAll(1f);
                StopAmbient(1f);
                _emitters.StopAll(1f);
                _zoneId = 0;
            }
        }

        /// <summary>
        /// Per-frame update from the owning viewport's render loop: the listener (camera), the zone, music and ambience.
        /// </summary>
        public void Update(object owner, CharacterSession? session, ViewportCamera camera, double deltaSeconds)
        {
            // The multi-box policy (#265): a viewport showing the preferred character takes the sound.
            SoundControlSettings controls = SoundControls.Current;
            bool preferred = session is not null && SoundControls.IsPreferred(controls,
                ReferenceEquals(session, SessionRegistry.Default.PrimaryRenderingSession));
            if (preferred && !_heardIsPreferred)
            {
                Volatile.Write(ref _owner, owner);
            }

            if (!IsOwner(owner))
            {
                return;
            }

            _heardIsPreferred = preferred;
            ApplyControls(controls);

            if (!ReferenceEquals(session, _session))
            {
                DetachSession(_session);
                Volatile.Write(ref _session, session);
                AttachSession(session);
                _music.StopAll(1f);
                StopAmbient(1f);
                _emitters.StopAll(1f);
                _zoneId = 0;
            }

            if (session is null)
            {
                return;
            }

            WorldState world = session.World;
            ApplyVolumes(session.ActionService.UiSettings);
            ApplyEventVolumes(world.EventSoundVolumes);
            UpdateListener(camera);
            ushort zone = world.CurrentZoneId;
            if (zone != _zoneId)
            {
                _zoneId = zone;
                LoadZoneSounds(zone);
                StopAmbient(0.5f);
                _emitters.SetEmitters(Array.Empty<ZoneSoundEmitter>());
            }

            float hour = world.GetTimeOfDayHours(DateTime.UtcNow);
            var context = new MusicContext(MusicStatus(session.LocalPlayer.ServerStatus, session.Combat.IsEngaged), session.Party.Members.Count > 1, hour);
            _music.Update(world.Music, context, deltaSeconds);
            UpdateAmbient(world, hour);
            UpdateFootsteps(world, session.LocalPlayer.ServerId);
            UpdateActionSounds(world);
            PlaySceneSounds(session.Events.Presentation);
            if (Interlocked.Exchange(ref _pendingEmitters, null) is { } loaded)
            {
                _emitters.SetEmitters(loaded);
            }

            _emitters.Update(_listenerPosition, hour / 24f);
        }

        /// <summary>Near / far range of footsteps (provisional).</summary>
        public static readonly (float Near, float Far) FootstepRange = (4f, FootstepTracker.HearingRange);

        /// <summary>
        /// The footstep digits of an actor: a character's feet item, else a creature's own model DAT (xi-tools
        /// <c>docs/sounds/footsteps.md</c> §3). Unknown until its DAT is read on a worker; meanwhile the default.
        /// </summary>
        private FootwearInfo FootwearOf(WorldEntity entity)
        {
            if (_resources is null)
            {
                return FootwearInfo.Default;
            }

            int fileId;
            ushort face = entity.Appearance.FaceModel;
            var race = (Gordian.Core.Resources.Tables.CharacterRace)((face >> 8) & 0xFF);
            if (race != Gordian.Core.Resources.Tables.CharacterRace.Unknown)
            {
                ushort feet = (ushort)(entity.Appearance.Feet & 0x0FFF);
                if (!Gordian.Core.Resources.Tables.CharacterEquipmentResolver.TryResolveGearFileId(race, Gordian.Core.Resources.Tables.CharacterSlot.Feet, feet, out fileId))
                {
                    return FootwearInfo.Default;
                }
            }
            else if (entity.Appearance.ModelId != 0)
            {
                fileId = Gordian.Core.Resources.Tables.CharacterEquipmentResolver.GetMonsterFileId(entity.Appearance.ModelId);
                if (fileId <= 0)
                {
                    return FootwearInfo.Default;
                }
            }
            else
            {
                return FootwearInfo.Default;
            }

            if (_footwear.TryGetValue(fileId, out FootwearInfo known))
            {
                return known;
            }

            if (_footwear.TryAdd(fileId, FootwearInfo.Default))
            {
                ResourceManager resources = _resources;
                _ = Task.Run(() =>
                {
                    byte[]? dat = resources.LoadDatBytesByFileId(fileId);
                    if (dat is not null && FootwearInfo.TryRead(dat, out FootwearInfo info))
                    {
                        _footwear[fileId] = info;
                    }
                });
            }

            return FootwearInfo.Default;
        }

        /// <summary>Range of a cutscene sound command played at its actor (provisional; generators carry their own range).</summary>
        public static readonly (float Near, float Far) SceneSoundRange = (15f, 60f);

        private readonly List<Gordian.Core.Events.EventPresentation.SceneSound> _sceneSounds = new();
        private readonly Dictionary<(int FileId, string Generator), int> _sceneLoops = new();
        private int _sceneSoundResets;

        /// <summary>
        /// Plays the cutscene's routine sounds as they come due. Sound commands: global ones (0x60, 0x4A, 0x53) centred,
        /// 0x0A / 0x0B at the task's actor. Sound generators: at the task's actor with their own 0x4C range (the lightning of
        /// Port Jeuno 324 is authored with far = 3000, so it is heard from anywhere: with a fixed 60-yalm range it was
        /// silent, in-game round 3). A generator whose file loops keeps playing until the generator is killed (0x1E / 0x3F),
        /// its spawn duration runs out, or the event ends.
        /// </summary>
        private void PlaySceneSounds(Gordian.Core.Events.EventPresentation presentation)
        {
            int resets = presentation.SoundResets;
            if (resets != _sceneSoundResets)
            {
                _sceneSoundResets = resets;
                StopSceneLoops(0.5f);
            }

            _sceneSounds.Clear();
            presentation.TakeDueSounds(_sceneSounds);
            foreach (var sound in _sceneSounds)
            {
                var key = (sound.FileId, sound.Generator);
                if (sound.Opcode == 0x1E)
                {
                    lock (_sceneLoops)
                    {
                        if (_sceneLoops.Remove(key, out int killed))
                        {
                            _engine.Mixer.Stop(killed, 0.3f);
                        }
                    }

                    continue;
                }

                AudioEmitter? emitter;
                if (sound.Opcode is 0x60 or 0x4A or 0x53)
                {
                    emitter = null;
                }
                else if (sound.Opcode == 0x02 && sound.Far > 0f)
                {
                    emitter = new AudioEmitter(sound.Origin, Math.Clamp(sound.Near, 0f, sound.Far), sound.Far);
                }
                else
                {
                    emitter = new AudioEmitter(sound.Origin, SceneSoundRange.Near, SceneSoundRange.Far);
                }

                if (sound.Opcode != 0x02)
                {
                    PlayEffect(sound.SoundId, AudioCategory.Effects, 1f, emitter);
                    continue;
                }

                _ = PlaySceneGenerator(key, sound.SoundId, emitter, sound.Duration);
            }
        }

        private async Task PlaySceneGenerator((int FileId, string Generator) key, int soundId, AudioEmitter? emitter, double seconds)
        {
            // loop: null keeps the file's own looping: a looped file loops, a one-shot plays once.
            int handle = await PlayEffectAsync(soundId, AudioCategory.Effects, 1f, emitter, loop: null).ConfigureAwait(false);
            if (handle == 0)
            {
                return;
            }

            lock (_sceneLoops)
            {
                if (_sceneLoops.Remove(key, out int previous))
                {
                    _engine.Mixer.Stop(previous, 0.3f);
                }

                _sceneLoops[key] = handle;
            }

            if (seconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                lock (_sceneLoops)
                {
                    if (_sceneLoops.TryGetValue(key, out int current) && current == handle)
                    {
                        _sceneLoops.Remove(key);
                    }
                }

                _engine.Mixer.Stop(handle, 0.3f);
            }
        }

        private void StopSceneLoops(float fadeSeconds)
        {
            lock (_sceneLoops)
            {
                foreach (int handle in _sceneLoops.Values)
                {
                    _engine.Mixer.Stop(handle, fadeSeconds);
                }

                _sceneLoops.Clear();
            }
        }
        /// <summary>
        /// Near / far range of combat and action sounds: full volume within 5 yalms (provisional), fading to silence at 30
        /// (the maintainer's retail check, 2026-10-07: retail fades with distance and is silent by 25-30 yalms). The
        /// routines' own range fields read 0.
        /// </summary>
        public static readonly (float Near, float Far) ActionSoundRange = (5f, ActionSoundTracker.HearingRange);

        private readonly ActionSoundTracker _actionSounds = new();
        private readonly List<ActionSoundEvent> _actionEvents = new();

        /// <summary>Combat and action sounds of the actors near the listener (#41), on the Effects bus.</summary>
        private void UpdateActionSounds(WorldState world)
        {
            _actionEvents.Clear();
            _actionSounds.Update(world.Entities, _listenerPosition,
                id => world.TryGetByServerId(id, out WorldEntity? e) ? ActionSoundTracker.Snapshot(e) : null, _actionEvents);
            foreach (ActionSoundEvent sound in _actionEvents)
            {
                PlayEffect(sound.SoundId, AudioCategory.Effects, 1f, new AudioEmitter(sound.Position, ActionSoundRange.Near, ActionSoundRange.Far));
            }
        }

        private void UpdateFootsteps(WorldState world, uint localPlayerId)
        {
            ZoneSoundTable sounds = _zoneSounds;
            if (sounds.WalkSteps.Count == 0)
            {
                return;
            }

            _steps.Clear();
            _footsteps.Update(world.Entities, localPlayerId, _listenerPosition, world.Collision, sounds, _steps, FootwearOf);
            foreach (FootstepEvent step in _steps)
            {
                PlayEffect(step.SoundId, AudioCategory.Effects, 1f, new AudioEmitter(step.Position, FootstepRange.Near, FootstepRange.Far));
            }
        }

        /// <summary>
        /// The local player's status for the music choice. It comes from S2C 0x037 (<c>LocalPlayerState.ServerStatus</c>):
        /// the server does not send the player's own 0x00D, so the player's <c>WorldEntity.AnimationState</c> stays 0 and
        /// battle music never started (in-game round 1). While the client has engaged a target before the server's status
        /// arrives, it counts as engaged too.
        /// </summary>
        public static byte MusicStatus(byte serverStatus, bool clientEngaged)
        {
            byte status = serverStatus;
            if (status == 0 && clientEngaged)
            {
                status = 1;
            }

            return status;
        }

        /// <summary>
        /// Plays a sound effect once (asynchronously: the first play of an id decodes it on a worker).
        /// </summary>
        /// <param name="soundId">The <c>.spw</c> id.</param>
        /// <param name="category">The bus.</param>
        /// <param name="volume">Linear gain.</param>
        /// <param name="emitter">Where it plays, or null for a centred (2D) sound.</param>
        public void PlayEffect(int soundId, AudioCategory category, float volume = 1f, AudioEmitter? emitter = null) =>
            _ = PlayEffectAsync(soundId, category, volume, emitter, loop: false);

        /// <summary>Plays a sound effect and returns its mixer handle (0 when it cannot play).</summary>
        public async Task<int> PlayEffectAsync(int soundId, AudioCategory category, float volume = 1f, AudioEmitter? emitter = null,
            bool? loop = null, float fadeInSeconds = 0f)
        {
            if (_library is null || soundId <= 0 || !_engine.IsAvailable)
            {
                return 0;
            }

            PcmClip? clip = await _library.GetEffectAsync(soundId).ConfigureAwait(false);
            return clip is null ? 0 : _engine.Mixer.Play(clip.Open(loop), category, volume, emitter, fadeInSeconds);
        }

        /// <summary>Plays a stock UI system sound (centred, System bus).</summary>
        public void PlayCue(StockUiSoundCue cue) => PlayEffect((int)cue, AudioCategory.System);

        private void AttachSession(CharacterSession? session)
        {
            if (session is null)
            {
                return;
            }

            _cueTarget = session.ActionService.CurrentTarget?.ServerId ?? 0;
            session.ActionService.Menus.SoundCue += PlayCue;
            session.ActionService.DebugAudioCommand = HandleDebugCommand;
            session.ActionService.TargetChanged += OnTargetChanged;
        }

        private void DetachSession(CharacterSession? session)
        {
            if (session is null)
            {
                return;
            }

            session.ActionService.Menus.SoundCue -= PlayCue;
            session.ActionService.DebugAudioCommand = null;
            session.ActionService.TargetChanged -= OnTargetChanged;
        }

        private void WatchTells(CharacterSession session)
        {
            void Handler(Gordian.Core.Network.Packets.ChatMessage message) => OnChatMessage(session, message);
            lock (_tellHandlers)
            {
                if (!_tellHandlers.TryAdd(session, Handler))
                {
                    return;
                }
            }

            session.ChatModule.ChatMessageReceived += Handler;
        }

        private void UnwatchTells(CharacterSession session)
        {
            Action<Gordian.Core.Network.Packets.ChatMessage>? handler;
            lock (_tellHandlers)
            {
                _tellHandlers.Remove(session, out handler);
            }

            if (handler is not null)
            {
                session.ChatModule.ChatMessageReceived -= handler;
            }
        }

        /// <summary>
        /// Applies the GordianXI sound controls (#265) as the buses' control gains when the settings or the focus change.
        /// With the defaults every gain stays 1, the retail mix.
        /// </summary>
        private void ApplyControls(SoundControlSettings controls)
        {
            int version = SoundControls.Version;
            if (version == _controlVersion)
            {
                return;
            }

            bool first = _controlVersion < 0;
            _controlVersion = version;
            bool active = SoundControls.IsActive(controls, SoundControls.AnyWindowActive, SoundControls.ViewportWindowActive);
            float seconds = first ? 0f : Math.Max(0f, controls.FadeSeconds);
            var muted = new List<string>();
            for (int c = 0; c < AudioMixer.CategoryCount; c++)
            {
                var category = (AudioCategory)c;
                float gain = SoundControls.CategoryGain(controls, category, active);
                _engine.Mixer.FadeControl(category, gain, seconds);
                if (gain <= 0f)
                {
                    muted.Add(category.ToString());
                }
            }

            // A bus the Sound tab mutes is easy to forget (#309: Music left off after testing read as "no zone music").
            string summary = muted.Count == 0 ? "none" : string.Join(", ", muted);
            if (summary != _loggedMuted)
            {
                _loggedMuted = summary;
                GordianLog.Info("AUDIO", $"Sound controls: muted buses: {summary}{(active ? string.Empty : " (GordianXI inactive)")}.");
            }
        }

        private string _loggedMuted = "none";

        private readonly List<int> _debugSounds = new();

        /// <summary>
        /// Debug commands (client only, never sent to the server): <c>/playsound &lt;id&gt;</c> plays sound effect
        /// <c>seNNNNNN.spw</c> centred on the Effects bus, a looped file looping until <c>/playsound stop</c>;
        /// <c>/playmusic &lt;n&gt;</c> plays <c>musicNNN.bgw</c> in place of the zone's music until <c>/playmusic stop</c>.
        /// </summary>
        public string HandleDebugCommand(bool music, string args)
        {
            string arg = args.Trim();
            if (music)
            {
                if (arg.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    _music.ClearOverride();
                    return "Debug: back to the zone music.";
                }

                if (!int.TryParse(arg, out int track) || track < 0)
                {
                    return "Usage: /playmusic <n> | stop";
                }

                _music.SetOverride(track);
                return $"Debug: playing music {track} ({Describe(_library?.Locator.FindMusic(track))}).";
            }

            if (arg.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                lock (_debugSounds)
                {
                    foreach (int handle in _debugSounds)
                    {
                        _engine.Mixer.Stop(handle, 0.2f);
                    }

                    _debugSounds.Clear();
                }

                return "Debug: sound effects stopped.";
            }

            if (!int.TryParse(arg, out int id) || id <= 0)
            {
                return "Usage: /playsound <id> | stop";
            }

            string? path = _library?.Locator.FindEffect(id);
            if (path is null)
            {
                return $"Debug: sound effect {id} not found.";
            }

            _ = PlayEffectAsync(id, AudioCategory.Effects, 1f, null, loop: null).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result != 0)
                {
                    lock (_debugSounds)
                    {
                        _debugSounds.Add(t.Result);
                    }
                }
            }, TaskScheduler.Default);
            return $"Debug: playing sound effect {id} ({Describe(path)}).";
        }

        private static string Describe(string? path)
        {
            if (path is null)
            {
                return "not found";
            }

            try
            {
                var head = new byte[FfxiSoundHeader.DataOffset];
                using (var fs = System.IO.File.OpenRead(path))
                {
                    fs.ReadExactly(head);
                }

                return FfxiSoundHeader.TryParse(head, out FfxiSoundHeader h)
                    ? $"{h.Format}, {h.Channels} ch, {h.SampleRate} Hz, {(h.IsLooped ? "looped" : "one-shot")}"
                    : "unknown header";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>A new target plays "Target Selection"; changing from one target to another plays "Target Switch".</summary>
        private void OnTargetChanged(WorldEntity? target)
        {
            uint previous = _cueTarget;
            _cueTarget = target?.ServerId ?? 0;
            if (target is null || target.ServerId == previous)
            {
                return;
            }

            PlayCue(previous == 0 ? StockUiSoundCue.TargetSelect : StockUiSoundCue.TargetSwitch);
        }

        /// <summary>
        /// An incoming tell plays the tell sound (<see cref="StockUiSoundCue.TellArrival"/>) on the Notification bus: the heard character's always, another
        /// character's only when the sound controls let it through (#265).
        /// </summary>
        private void OnChatMessage(CharacterSession receiver, Gordian.Core.Network.Packets.ChatMessage message)
        {
            if (message.Type != Gordian.Core.Network.Packets.ChatMessageType.Tell
                || string.Equals(message.Sender, receiver.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (SoundControls.PlaysTellCue(SoundControls.Current, ReferenceEquals(receiver, Session)))
            {
                PlayEffect((int)StockUiSoundCue.TellArrival, AudioCategory.Notification);
            }
        }

        /// <summary>
        /// Applies the character's config-page volumes (0-100, linear: provisional): the music slider drives the Music
        /// bus, the sound effect slider the Effects, System and Zone buses, as the retail config page has only these two.
        /// </summary>
        private void ApplyVolumes(StockUiSettings settings)
        {
            int music = settings.GetValue(StockUiSettingKey.MusicVolume);
            int effects = settings.GetValue(StockUiSettingKey.SoundEffectsVolume);
            if (music == _appliedMusicVolume && effects == _appliedEffectsVolume)
            {
                return;
            }

            _appliedMusicVolume = music;
            _appliedEffectsVolume = effects;
            foreach ((AudioCategory category, float gain) in VolumeMix.CategoryGains(music, effects))
            {
                _engine.Mixer.SetCategoryVolume(category, gain);
            }
        }

        /// <summary>
        /// Applies an event's category volumes (opcodes 0x69 / 0x6A) as script fades on the buses: effect → Effects,
        /// system → System, zone → Zone, master → those three (time in 1/60 s frames, provisional). The master is the sound
        /// elements' master (XiEvents <c>YmSoundElem_SetMasterVolume</c>), not the music: Lufaise Meadows <c>!cs 117</c> mutes
        /// mask 0x1F for the whole scene while it plays track 900, and applying it to the music silenced the scene (round 3).
        /// </summary>
        private void ApplyEventVolumes(EventSoundVolumes volumes)
        {
            int version = volumes.Version;
            if (version == _eventVolumeVersion)
            {
                return;
            }

            _eventVolumeVersion = version;
            float seconds = volumes.FadeTime / 60f;
            float master = volumes.Get(EventSoundCategory.Master);
            _engine.Mixer.FadeCategory(AudioCategory.Effects, master * volumes.Get(EventSoundCategory.Effect), seconds);
            _engine.Mixer.FadeCategory(AudioCategory.System, master * volumes.Get(EventSoundCategory.System), seconds);
            _engine.Mixer.FadeCategory(AudioCategory.Notification, master * volumes.Get(EventSoundCategory.System), seconds);
            _engine.Mixer.FadeCategory(AudioCategory.Zone, master * volumes.Get(EventSoundCategory.Zone), seconds);
        }

        private void UpdateListener(ViewportCamera camera)
        {
            // The camera lives in display space, (-x, -y, z) of the internal (Y = height) space; emitters are given in
            // internal space, so the listener is converted back. The view matrix's first column is the screen-right axis.
            Vector3 eye = camera.Position;
            Matrix4x4 view = camera.ViewMatrix;
            var right = new Vector3(view.M11, view.M21, view.M31);
            _listenerPosition = new Vector3(-eye.X, -eye.Y, eye.Z);
            _engine.Mixer.SetListener(_listenerPosition, new Vector3(-right.X, -right.Y, right.Z));
        }

        private void LoadZoneSounds(ushort zone)
        {
            int token = Interlocked.Increment(ref _zoneLoadToken);
            _zoneSounds = ZoneSoundTable.Empty;
            if (_resources is null || zone == 0)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    byte[]? dat = _resources.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(zone));
                    if (dat is null || Volatile.Read(ref _zoneLoadToken) != token)
                    {
                        return;
                    }

                    DatDirectoryNode tree = DatDirectoryTree.Build(dat);
                    ZoneSoundTable table = ZoneSoundTable.Read(tree);
                    List<ZoneSoundEmitter> emitters = ZoneSoundEmitterDecoder.Read(tree);
                    if (Volatile.Read(ref _zoneLoadToken) == token)
                    {
                        _zoneSounds = table;
                        _pendingEmitters = emitters;
                        GordianLog.Info("AUDIO", $"Zone {zone} sounds: {table.AmbientWeathers.Count} ambient weathers, {table.WalkSteps.Count} footsteps, {table.Doors.Count} doors, {emitters.Count} sound generators.");
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("AUDIO", $"Zone {zone} sound pointers failed to load: {ex.Message}");
                }
            });
        }

        private void UpdateAmbient(WorldState world, float hour)
        {
            string weather = world.WeatherId;
            int minute = (int)(hour * 60f) % 1440;
            int wanted = _zoneSounds.AmbientSound(weather, minute, false, VanaTime.GetCanonicalWeatherCategory(weather));
            if (wanted == _ambientSound)
            {
                return;
            }

            _ambientSound = wanted;
            StopAmbient(AmbientFadeSeconds);
            if (wanted == 0)
            {
                return;
            }

            int token = Interlocked.Increment(ref _ambientToken);
            _ = PlayEffectAsync(wanted, AudioCategory.Zone, 1f, null, loop: true, fadeInSeconds: AmbientFadeSeconds).ContinueWith(t =>
            {
                int handle = t.Result;
                if (Volatile.Read(ref _ambientToken) != token)
                {
                    _engine.Mixer.Stop(handle);
                    return;
                }

                Volatile.Write(ref _ambientHandle, handle);
            }, TaskScheduler.Default);
        }

        private void StopAmbient(float fadeSeconds)
        {
            Interlocked.Increment(ref _ambientToken);
            int handle = Interlocked.Exchange(ref _ambientHandle, 0);
            if (handle != 0)
            {
                _engine.Mixer.Stop(handle, fadeSeconds);
            }

            if (fadeSeconds < AmbientFadeSeconds)
            {
                _ambientSound = 0;
            }
        }

        /// <inheritdoc />
        public void Dispose() => _engine.Dispose();

        /// <summary>Resolves install-relative sound paths through the VFS (overlay packs first).</summary>
        private sealed class VfsSoundResolver
        {
            private readonly ResourceManager _resources;

            public VfsSoundResolver(ResourceManager resources) => _resources = resources;

            public string? Resolve(string relative) =>
                _resources.Vfs.TryResolveDat(relative, out var resolved) && resolved is not null ? resolved.PhysicalPath : null;
        }
    }
}
