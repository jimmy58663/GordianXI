// src/Gordian.App/Services/DatReload.cs
using System;
using System.Diagnostics;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Services
{
    /// <summary>
    /// The user-triggered DAT reload (#374): <c>/reloaddats</c> and the Graphics &amp; Viewport page's Reload DATs button.
    /// Re-reads <c>vfs.json</c> and re-indexes the packs under <c>resources/dats</c> and <c>resources/assets</c>
    /// (<see cref="Gordian.Core.Resources.Vfs.IVirtualFileSystem.Reload"/>); the VFS then raises <c>OnReloaded</c> and the
    /// resource manager drops its caches and bumps <c>CacheGeneration</c>, which the viewports (zone, entity models and
    /// textures), the stock UI, the message tables, the event data and the action catalog follow on their next use. The
    /// same path runs on a hot reload; this one also picks up edits the watcher cannot see (a DAT replaced in the game
    /// install itself, or hot reload switched off in <c>vfs.json</c>).
    /// </summary>
    public static class DatReload
    {
        private static readonly object Gate = new();

        /// <summary>Reloads and returns a one-line report for the console or the settings page.</summary>
        public static string Reload()
        {
            var resources = AppResourceManager.Instance;
            if (resources == null) return "The game's resources are not loaded; nothing to reload.";
            lock (Gate)
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    var vfs = resources.Vfs;
                    vfs.Reload();
                    string report = $"DATs reloaded in {watch.ElapsedMilliseconds} ms: {vfs.ActiveDatPacks.Count} DAT pack(s) and " +
                                    $"{vfs.ActiveAssetPacks.Count} asset pack(s) active{(vfs.IsEnabled ? string.Empty : " (VFS switched off in vfs.json)")}. " +
                                    "The zone, models, textures and UI reload on the next frame.";
                    GordianLog.Info("VFS", report);
                    return report;
                }
                catch (Exception ex)
                {
                    GordianLog.Error("VFS", $"DAT reload failed: {ex.Message}", ex);
                    return $"DAT reload failed: {ex.Message}";
                }
            }
        }
    }
}
