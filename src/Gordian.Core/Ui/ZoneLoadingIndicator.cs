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
    /// Measured on the maintainer's retail recording (2026-10-11, 2558 x 1440 at 30 fps, UI scale 1, Southern San d'Oria to East
    /// Ronfaure and back): the sprite is drawn at one screen pixel per layout pixel with its top-left 232 x 48 px from the
    /// bottom-right corner (lit pixels x 2327-2530, y 1393-1406; the big dots centred at x 2358 / 2499, y 1399.5), and it
    /// cycles 165, 166, 167, 168, 169, 165, ... starting at 165: all lit, then the dimmed pair walking inward. One image
    /// lasted 2.1 video frames on average in the second zoning (a 0.34 s cycle) and 4.1 in the first (0.68 s), so the
    /// client steps it per rendered frame and was running at half speed while it loaded the first zone; GordianXI uses the
    /// faster, steady rate.
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
        /// scale): measured on the retail recording, where the sprite's box spans x 2326-2532 and y 1392-1408 of 2558 x 1440.
        /// </summary>
        public const float RightOffset = 232f, BottomOffset = 48f;

        /// <summary>
        /// Seconds each image shows: two frames at 30 fps (the retail recording's second zoning, 2.06 video frames per image
        /// over ten cycles). The cycle is 165 (all lit), then 166-169 (the dimmed pair walking inward toward the text).
        /// </summary>
        public const double FrameSeconds = 2.0 / 30.0;

        /// <summary>The number of images in the cycle (165-169).</summary>
        public const int CycleLength = DimmedImageCount + 1;

        /// <summary>The <see cref="GroupName"/> image to draw <paramref name="secondsShown"/> after the indicator appeared.</summary>
        public static int ImageAt(double secondsShown)
        {
            if (!(secondsShown > 0) || double.IsInfinity(secondsShown)) return AllLitImage;
            long frame = (long)Math.Floor(secondsShown / FrameSeconds);
            return AllLitImage + (int)(frame % CycleLength);
        }

        /// <summary>The sprite's top-left screen position on a <paramref name="width"/> x <paramref name="height"/> screen at UI scale <paramref name="scale"/>.</summary>
        public static (float X, float Y) Position(float width, float height, float scale) =>
            (width - RightOffset * scale, height - BottomOffset * scale);
    }
}
