// src/Gordian.App/Audio/GameAudioService.cs
using System;
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
        private object? _owner;
        private CharacterSession? _session;
        private ushort _zoneId;
        private volatile ZoneSoundTable _zoneSounds = ZoneSoundTable.Empty;
        private int _zoneLoadToken;
        private int _ambientSound;
        private int _ambientHandle;
        private int _ambientToken;
        private uint _cueTarget;
        private int _appliedMusicVolume = -1;
        private int _appliedEffectsVolume = -1;

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

        /// <summary>Makes <paramref name="owner"/> (a viewport) the one whose session is heard.</summary>
        public void Claim(object owner) => Volatile.Write(ref _owner, owner);

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
                DetachSession(_session);
                Volatile.Write(ref _session, null);
                _music.StopAll(1f);
                StopAmbient(1f);
                _zoneId = 0;
            }
        }

        /// <summary>
        /// Per-frame update from the owning viewport's render loop: the listener (camera), the zone, music and ambience.
        /// </summary>
        public void Update(object owner, CharacterSession? session, ViewportCamera camera, double deltaSeconds)
        {
            if (!IsOwner(owner))
            {
                return;
            }

            if (!ReferenceEquals(session, _session))
            {
                DetachSession(_session);
                Volatile.Write(ref _session, session);
                AttachSession(session);
                _music.StopAll(1f);
                StopAmbient(1f);
                _zoneId = 0;
            }

            if (session is null)
            {
                return;
            }

            WorldState world = session.World;
            ApplyVolumes(session.ActionService.UiSettings);
            UpdateListener(camera);
            ushort zone = world.CurrentZoneId;
            if (zone != _zoneId)
            {
                _zoneId = zone;
                LoadZoneSounds(zone);
                StopAmbient(0.5f);
            }

            float hour = world.GetTimeOfDayHours(DateTime.UtcNow);
            var context = new MusicContext(MusicStatus(session.LocalPlayer.ServerStatus, session.Combat.IsEngaged), session.Party.Members.Count > 1, hour);
            _music.Update(world.Music, context, deltaSeconds);
            UpdateAmbient(world, hour);
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
            session.ActionService.TargetChanged += OnTargetChanged;
            session.ChatModule.ChatMessageReceived += OnChatMessage;
        }

        private void DetachSession(CharacterSession? session)
        {
            if (session is null)
            {
                return;
            }

            session.ActionService.Menus.SoundCue -= PlayCue;
            session.ActionService.TargetChanged -= OnTargetChanged;
            session.ChatModule.ChatMessageReceived -= OnChatMessage;
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

        /// <summary>An incoming tell plays "Message Arrival".</summary>
        private void OnChatMessage(Gordian.Core.Network.Packets.ChatMessage message)
        {
            CharacterSession? session = Session;
            if (message.Type == Gordian.Core.Network.Packets.ChatMessageType.Tell && session is not null
                && !string.Equals(message.Sender, session.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                PlayCue(StockUiSoundCue.MessageArrival);
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

        private void UpdateListener(ViewportCamera camera)
        {
            // The camera lives in display space, (-x, -y, z) of the internal (Y = height) space; emitters are given in
            // internal space, so the listener is converted back. The view matrix's first column is the screen-right axis.
            Vector3 eye = camera.Position;
            Matrix4x4 view = camera.ViewMatrix;
            var right = new Vector3(view.M11, view.M21, view.M31);
            _engine.Mixer.SetListener(new Vector3(-eye.X, -eye.Y, eye.Z), new Vector3(-right.X, -right.Y, right.Z));
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

                    ZoneSoundTable table = ZoneSoundTable.Read(DatDirectoryTree.Build(dat));
                    if (Volatile.Read(ref _zoneLoadToken) == token)
                    {
                        _zoneSounds = table;
                        GordianLog.Info("AUDIO", $"Zone {zone} sounds: {table.AmbientWeathers.Count} ambient weathers, {table.WalkSteps.Count} footsteps, {table.Doors.Count} doors.");
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
