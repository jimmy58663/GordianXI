// src/Gordian.Core/Ui/ButtonPreviewState.cs
using System;
using System.Globalization;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The <c>/buttonpreview</c> picker's state (#360): whether the candidate button glyphs are laid out over the screen, the folder
    /// the candidate art is read from (the packs are not committed) and the magnification. A temporary developer aid for choosing the
    /// art the menu hints use; the viewport's HUD reads it every frame.
    /// </summary>
    public sealed class ButtonPreviewState
    {
        public const string Usage = "Usage: /buttonpreview <folder holding the XBOX BUTTONS and PS4 BUTTONS packs> [zoom 1-4] | off - Shows the candidate menu hint button art at the hint's real size (zoom 1) or magnified.";

        private volatile string? _folder;
        private volatile int _zoom = 1;

        /// <summary>The folder being previewed, or null when the preview is off.</summary>
        public string? Folder => _folder;

        /// <summary>1 draws each glyph at the size the Options hint uses; 2 to 4 magnify it.</summary>
        public int Zoom => _zoom;

        public bool IsActive => _folder != null;

        /// <summary>Applies the command's arguments and returns the reply.</summary>
        public string Apply(string args)
        {
            args = (args ?? string.Empty).Trim();
            if (args.Length == 0) return IsActive ? "Button preview is on (/buttonpreview off to hide it)." : Usage;
            if (args.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                _folder = null;
                return "Button preview off.";
            }
            int zoom = 1;
            int lastSpace = args.LastIndexOf(' ');
            if (lastSpace > 0 && int.TryParse(args[(lastSpace + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int z) && z is >= 1 and <= 4)
            {
                zoom = z;
                args = args[..lastSpace].Trim();
            }
            args = args.Trim('"');
            if (!System.IO.Directory.Exists(args)) return $"Folder not found: {args}";
            _zoom = zoom;
            _folder = args;
            return $"Button preview on at zoom {zoom}.";
        }
    }
}
