using System;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The retail loading indicator (#350): "Downloading data ..." between two rows of four dots, in the bottom-right
    /// corner of the black screen while zoning. It is a ready-made sprite of the in-game menu DAT (ROM/119/51), not
    /// client-composed text: images 165-169 of the <c>keytops3</c> element group, each 206 x 16 layout pixels, the text
    /// built from the <c>font</c> texture's italic glyphs and the dots from <c>menu2fon</c> (the four sizes at
    /// (73,1), (79,1), (87,1), (98,1), mirrored on the right). Image 165 has every dot lit (colour 0x7F); 166-169 each
    /// dim one pair of dots to 0x60, from the outermost pair (166) to the innermost (169). No menu of the DAT draws these
    /// images, so the client places and cycles them itself. The Japanese client's version is a single sprite of the
    /// <c>menufont</c> texture (the "データダウンロード中..." row).
    /// <para>
    /// Placement and size are measured on the maintainer's retail screenshot (2026-10-11, 2560 x 1440, UI scale 1): the
    /// sprite is drawn at one screen pixel per layout pixel, its lit pixels ending 25 px from the right edge and 32 px from
    /// the bottom, and that frame is image 168. The frame order and rate are guessed (one still frame only).
    /// </para>
    /// </summary>
    public static class ZoneLoadingIndicator
    {
        /// <summary>The element group holding the indicator's frames.</summary>
        public const string GroupName = "keytops3";

        /// <summary>The image with every dot lit.</summary>
        public const int AllLitImage = 165;

        /// <summary>The first of the four images that dim one pair of dots (outermost pair first).</summary>
        public const int FirstDimmedImage = 166;

        /// <summary>The number of dimmed-pair images.</summary>
        public const int DimmedImageCount = 4;

        /// <summary>The sprite's size in layout pixels.</summary>
        public const float Width = 206f, Height = 16f;

        /// <summary>
        /// The sprite's top-left corner relative to the screen's bottom-right corner, in layout pixels (times the UI
        /// scale): measured from the retail screenshot, where the sprite's box spans x 2329-2535 and y 1394-1410 of 2560 x 1440.
        /// </summary>
        public const float RightOffset = 231f, BottomOffset = 46f;

        /// <summary>
        /// Seconds each frame shows (guessed). The dimmed dot pair walks from the outside in toward the text, one pair per
        /// frame, and starts over.
        /// </summary>
        public const double FrameSeconds = 0.2;

        /// <summary>The <see cref="GroupName"/> image to draw <paramref name="secondsShown"/> after the indicator appeared.</summary>
        public static int ImageAt(double secondsShown)
        {
            if (!(secondsShown > 0) || double.IsInfinity(secondsShown)) return FirstDimmedImage;
            long frame = (long)Math.Floor(secondsShown / FrameSeconds);
            return FirstDimmedImage + (int)(frame % DimmedImageCount);
        }

        /// <summary>The sprite's top-left screen position on a <paramref name="width"/> x <paramref name="height"/> screen at UI scale <paramref name="scale"/>.</summary>
        public static (float X, float Y) Position(float width, float height, float scale) =>
            (width - RightOffset * scale, height - BottomOffset * scale);
    }
}
