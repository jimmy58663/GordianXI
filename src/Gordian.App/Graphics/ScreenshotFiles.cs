// src/Gordian.App/Graphics/ScreenshotFiles.cs
using System;
using System.IO;

namespace Gordian.App.Graphics
{
    /// <summary>Screenshot file names: Windower's <c>img_yyyyMMdd_HHmmss.png</c> unless the player gave one.</summary>
    internal static class ScreenshotFiles
    {
        /// <summary>
        /// The path for a new screenshot in <paramref name="directory"/>. An existing file is kept: the new one takes a
        /// numbered name beside it.
        /// </summary>
        public static string ResolvePath(string directory, string? name, DateTime now)
        {
            string stem = string.IsNullOrEmpty(name) ? $"img_{now:yyyyMMdd_HHmmss}" : name;
            string path = Path.Combine(directory, stem + ".png");
            for (int copy = 2; File.Exists(path); copy++)
            {
                path = Path.Combine(directory, $"{stem}-{copy}.png");
            }
            return path;
        }
    }
}
