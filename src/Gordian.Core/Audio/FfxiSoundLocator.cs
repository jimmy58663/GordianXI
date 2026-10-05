// src/Gordian.Core/Audio/FfxiSoundLocator.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// Resolves sound ids to the retail files outside the DAT tree: sound effects at
    /// <c>&lt;root&gt;/win/se/se{id/1000:000}/se{id:000000}.spw</c> and music at
    /// <c>&lt;root&gt;/win/music/data/music{id:000}.bgw</c>, across the sound roots <c>sound</c>, <c>sound2</c> ... <c>sound9</c>.
    /// </summary>
    /// <remarks>
    /// Path scheme referenced from xi-tools <c>docs/audio/format.md</c> ("id → file", "On-disk layout",
    /// https://github.com/vekien/xi-tools); the sound effect id is the value a DAT <c>0x3D</c> SoundEffectPointer section
    /// carries. Every directory and file name is lower case on disk, and the paths are built with exactly that casing so
    /// lookups work on case-sensitive file systems. Roots are searched in order; the first hit wins (no id is known to
    /// exist in two roots). An optional resolver (the VFS, so XIPivot-style overlay packs holding
    /// <c>sound/win/se/...</c> replace sounds without touching the game folder) is asked first with the forward-slash
    /// relative path, e.g. <c>sound2/win/se/se002/se002060.spw</c>.
    /// </remarks>
    public sealed class FfxiSoundLocator
    {
        private readonly List<string> _roots = new();
        private readonly List<string> _rootNames = new();
        private readonly ConcurrentDictionary<long, string?> _cache = new();
        private readonly Func<string, string?>? _resolve;

        /// <summary>Builds a locator over a game install.</summary>
        /// <param name="installDirectory">The FINAL FANTASY XI directory (the one holding <c>ROM</c> and <c>sound</c>).</param>
        /// <param name="resolve">Optional resolver asked first with a forward-slash path relative to the install; returns a file path or null.</param>
        public FfxiSoundLocator(string? installDirectory, Func<string, string?>? resolve = null)
        {
            _resolve = resolve;
            if (!string.IsNullOrEmpty(installDirectory))
            {
                foreach (string name in RootNames())
                {
                    string root = Path.Combine(installDirectory, name);
                    if (Directory.Exists(root))
                    {
                        _roots.Add(root);
                        _rootNames.Add(name);
                    }
                }
            }
        }

        /// <summary>The sound root directories found, in search order.</summary>
        public IReadOnlyList<string> Roots => _roots;

        /// <summary>Whether any sound root was found.</summary>
        public bool HasRoots => _roots.Count > 0;

        /// <summary>The names of the sound roots under an install, in search order.</summary>
        public static IEnumerable<string> RootNames()
        {
            yield return "sound";
            for (int n = 2; n <= 9; n++)
            {
                yield return "sound" + n;
            }
        }

        /// <summary>The path of a sound effect relative to a sound root.</summary>
        public static string EffectRelativePath(int soundId) =>
            Path.Combine("win", "se", $"se{soundId / 1000:D3}", $"se{soundId:D6}.spw");

        /// <summary>The path of a music track relative to a sound root.</summary>
        public static string MusicRelativePath(int musicId) =>
            Path.Combine("win", "music", "data", $"music{musicId:D3}.bgw");

        /// <summary>The full path of a sound effect, or null when no root holds it.</summary>
        public string? FindEffect(int soundId) =>
            soundId <= 0 ? null : Find(soundId, EffectRelativePath(soundId), $"win/se/se{soundId / 1000:D3}/se{soundId:D6}.spw");

        /// <summary>The full path of a music track, or null when no root holds it.</summary>
        public string? FindMusic(int musicId) =>
            musicId <= 0 ? null : Find(-(long)musicId, MusicRelativePath(musicId), $"win/music/data/music{musicId:D3}.bgw");

        private string? Find(long key, string relative, string vfsRelative) =>
            _cache.GetOrAdd(key, _ =>
            {
                if (_resolve is not null)
                {
                    IEnumerable<string> names = _rootNames.Count > 0 ? _rootNames : RootNames();
                    foreach (string name in names)
                    {
                        string? resolved = _resolve(name + "/" + vfsRelative);
                        if (resolved is not null && File.Exists(resolved))
                        {
                            return resolved;
                        }
                    }
                }

                foreach (string root in _roots)
                {
                    string path = Path.Combine(root, relative);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                return null;
            });
    }
}
