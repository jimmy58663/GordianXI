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

        // Event dialog (NPC talk): the zone's scripts and dialog table, and the names its text refers to.
        Gordian.Core.Events.EventDialogController.DatLoader = fileId => AppResourceManager.Instance?.LoadDatBytesByFileId(fileId);
        Gordian.Core.Events.EventDialogController.DatPathLoader = path => AppResourceManager.Instance?.LoadDatBytes(path);
        Gordian.Core.Events.EventDialogController.NameResolver = (kind, id) =>
        {
            var rm = AppResourceManager.Instance;
            return rm == null ? null : Gordian.Core.Events.EventMessageNames.Resolve(rm, kind, id);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) => _zoneCollision?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}