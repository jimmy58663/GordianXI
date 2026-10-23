using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Gordian.App.Services;
using Gordian.Core.Network;
using Gordian.Core.World.Collision;

namespace Gordian.App;

public partial class App : Application
{
    private ZoneCollisionService? _zoneCollision;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Every session, displayed or not, follows the collision of its own zone (loaded off the UI thread).
        _zoneCollision = new ZoneCollisionService(SessionRegistry.Default,
            zoneId => AppResourceManager.Instance?.TryLoadZoneCollision(zoneId));
        // Building interiors near the player are walked as well (#68).
        Gordian.Core.World.SubAreaTracker.InteriorCollisionLoader = subAreaId => AppResourceManager.Instance?.TryLoadSubAreaCollision(subAreaId);

        // Event dialog (NPC talk): the zone's scripts and dialog table, and the names its text refers to.
        Gordian.Core.Events.EventDialogController.DatLoader = fileId => AppResourceManager.Instance?.LoadDatBytesByFileId(fileId);
        Gordian.Core.Events.EventDialogController.DatPathLoader = path => AppResourceManager.Instance?.LoadDatBytes(path);
        // Weapon skill / ability / spell effects (#132): decoded on the network thread when the action arrives, so the
        // renderer finds them cached instead of parsing the DAT mid-frame.
        Gordian.Core.Animation.ActionMotionLibrary.EffectsPreloader = fileId => AppResourceManager.Instance?.GetSceneEffects(fileId);
        // Zone routines an event starts (0x2D, #226): their length from the loaded zone, for the event's 0x54 wait.
        Gordian.Core.Events.EventDialogController.ZoneRoutineFrames = (zoneId, routine) =>
        {
            var zone = AppResourceManager.Instance?.TryGetLoadedZone(zoneId);
            if (zone == null) return 0;
            int frames = 0;
            foreach (var (_, found) in zone.MapRoutines.Find(routine)) frames = System.Math.Max(frames, found.TotalFrames);
            return frames;
        };
        // A door's open / clos an event starts and waits on (0x2D / 0x54, #233): the length of its leaves' move.
        Gordian.Core.Events.EventDialogController.DoorRoutineFrames = (zoneId, doorId, routine) =>
        {
            var zone = AppResourceManager.Instance?.TryGetLoadedZone(zoneId);
            if (zone == null) return 0;
            int frames = 0;
            if (zone.DoorRoutines.TryGetValue(doorId, out var leaves))
            {
                var move = routine switch { "open" => leaves.Open, "clos" => leaves.Close, "into" => leaves.InitOpen, _ => leaves.InitClose };
                frames = move?.TotalFrames ?? 0;
            }
            // A door without leaf routines: its effect routine's own length (not the routines it starts, which run on).
            if (frames == 0 && zone.DoorDirectories.TryGetValue(doorId, out var directory) && zone.MapRoutines.TryGetRoutine(directory, routine, out var effect))
            {
                frames = effect.TotalFrames;
            }
            return frames;
        };
        Gordian.Core.Events.EventDialogController.NameResolver = (kind, id) =>
        {
            var rm = AppResourceManager.Instance;
            return rm == null ? null : Gordian.Core.Events.EventMessageNames.Resolve(rm, kind, id);
        };
        // S2C 0x029 battle messages worded from the client's basic-message table (file 7027, #335), read through the
        // file-id loader above on first use.
        var clientTables = new Gordian.Core.Resources.Tables.ClientMessageTables();
        Gordian.Core.Network.Packets.CombatLogFormatter.BattleMessages = id => clientTables.BattleMessages?.GetMessage(id);

        // /reloaddats (#374): re-index the VFS packs and read every DAT again.
        Gordian.Core.Actions.PlayerActionService.DatReloader = Gordian.App.Services.DatReload.Reload;

        // Spells, job abilities and weapon skills by name (/ma, /ja, /ws and the Magic / Abilities windows, #240 / #353):
        // the client's spell and command tables with their name tables, read once per resource manager and again after
        // a VFS reload or /reloaddats (#374).
        object catalogGate = new();
        object? catalogSource = null;
        int catalogGeneration = -1;
        Gordian.Core.Actions.ActionCatalog? catalog = null;
        Gordian.Core.Actions.ActionCatalog.Provider = () =>
        {
            var rm = AppResourceManager.Instance;
            if (rm == null) return null;
            lock (catalogGate)
            {
                if (!ReferenceEquals(catalogSource, rm) || catalogGeneration != rm.CacheGeneration)
                {
                    catalogSource = rm;
                    catalogGeneration = rm.CacheGeneration;
                    catalog = Gordian.Core.Actions.ActionCatalog.Load(rm);
                }
                return catalog;
            }
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            // Window focus for the gamepad and the opt-in "play only while active" sound control: one tracker (#304).
            Gordian.App.Services.WindowFocusHook.Install(desktop, Gordian.App.Services.ViewportWindowManager.Default.WindowFocus);
            desktop.Exit += (_, _) =>
            {
                _zoneCollision?.Dispose();
                Gordian.App.Audio.GameAudioService.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}