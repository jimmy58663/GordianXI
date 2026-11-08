// src/Gordian.Engine/Ui/StockUiCompass.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// Retail's compass (#485): a radar ring with the cardinal letters at the bottom left, just above the log window, and
    /// under it the day's element orb, the Vana'diel time and the map grid square ("● 9:27 (J-5)"). Laid out from the
    /// maintainer's retail captures of 2026-10-31 (2560 x 1440, UI 1:1: main menu closed, open, and the camera turned 90
    /// degrees). This class holds the measured geometry and the pure maths; <c>StockUiCompassWindow</c> (App) draws it.
    /// <list type="bullet">
    /// <item><b>The sprites</b> are the English menu DAT's element group <c>compass</c>, which no menu places: #0 the ring
    /// (256 x 256 around its centre, one 32 x 32 quarter texture drawn four times with mirror flags 0-3), #1 E, #2 W, #3 S,
    /// #4 N (red; texture <c>news</c>, 16 x 16 each, centred on the origin), #5 a 6 x 6 red dot (<c>marker</c>).</item>
    /// <item><b>Seen from above and behind:</b> the ring is drawn as an ellipse (<see cref="Squash"/>) and turns with the
    /// camera, not with the character: the far side (top) is where the camera looks. The ring's spokes are the north-south
    /// and east-west lines (both captures), so its texture diagonals carry the cardinal points.</item>
    /// <item><b>Radar:</b> the maintainer's notes: NPCs green, monsters reddish, players blue, and the edge of the ring
    /// about 16 yalms out (<see cref="RangeYalms"/>).</item>
    /// <item><b>Shown</b> always, unless a window takes the same place above the log (the target command menu and the
    /// like): the maintainer's notes. Hidden with the rest of the stock UI by <c>/stockui hide compass</c>.</item>
    /// </list>
    /// </summary>
    public static class StockUiCompass
    {
        /// <summary>The menu DAT element group with the ring, letters and dot.</summary>
        public const string GroupName = "compass";

        public const int RingImage = 0, EastImage = 1, WestImage = 2, SouthImage = 3, NorthImage = 4, DotImage = 5;

        /// <summary>The ring image's radius in its own layout pixels (its quadrants span 128 from the centre).</summary>
        public const float RingImageRadius = 128;

        /// <summary>
        /// The ring's visible edge across the screen (layout pixels): the captures' ellipse spans 78 x 35 px, the same in
        /// both views. The radar's <see cref="RangeYalms"/> maps to it.
        /// </summary>
        public const float RingRadius = 39;

        /// <summary>
        /// The radius the ring image's 128 is drawn at (layout pixels): its texture fades out short of its edge, so this is
        /// a little over <see cref="RingRadius"/> (fitted offscreen against the turned capture).
        /// </summary>
        public const float RingImageDrawnRadius = 42;

        /// <summary>The ellipse's height over its width (35 / 78): how flat the ring lies, seen from behind.</summary>
        public const float Squash = 0.44f;

        /// <summary>
        /// The letters' distance from the centre across the screen (layout pixels): just outside the ring. Fitted to the
        /// four letter centres of the turned capture (N, E, S, W at 45 px across, 19 px down at the same angles).
        /// </summary>
        public const float LetterRadius = 45;

        /// <summary>How far out the ring's edge is in the world, in yalms (the maintainer's estimate).</summary>
        public const float RangeYalms = 16;

        /// <summary>The ring's centre from the screen's left edge (layout pixels; 88 in both captures).</summary>
        public const float CenterX = 88;

        /// <summary>The ring's centre above the text line's bottom (layout pixels; 36 in both captures; the digits' bottom row is the line's last).</summary>
        public const float CenterRise = 36;

        /// <summary>The text line's bottom above the log window's top edge (layout pixels; the capture with the log).</summary>
        public const float LogGap = 5;

        /// <summary>The text line's bottom above the screen's bottom edge when the log is hidden (the capture without it).</summary>
        public const float ScreenBottomGap = 24;

        /// <summary>
        /// The day orb's left edge, size and centre above the line's bottom (layout pixels): a 10 px ball with its outline
        /// from x 26, centred 4 px above the digits' bottom.
        /// </summary>
        public const float OrbX = 26, OrbSize = 11, OrbRise = 4;

        /// <summary>The text's left edge (layout pixels; the time starts at x 40).</summary>
        public const float TextX = 40;

        /// <summary>The line's height (layout pixels): the digits stand 8 px tall.</summary>
        public const float LineHeight = 8;

        /// <summary>
        /// The text's scale: <c>fontshp</c> at 7/8, as the party window's (the capture's "11:25 (J-5)" is 72 px wide, its
        /// digits 8 px tall).
        /// </summary>
        public const float TextScale = 0.875f;

        /// <summary>A radar dot's size (layout pixels): the captures' dots are 4 px squares with soft corners.</summary>
        public const float DotSize = 4;

        /// <summary>What a radar dot stands for.</summary>
        public enum DotKind
        {
            None,
            Npc,
            Monster,
            Player,
        }

        /// <summary>
        /// The dot for an entity type: NPCs (and doors, elevators, ships: anything the server lists as an NPC) green,
        /// monsters reddish, players, pets and trusts blue. <b>Provisional:</b> the captures show only an NPC and a player;
        /// where pets and trusts go is our guess.
        /// </summary>
        public static DotKind Classify(EntityType type) => type switch
        {
            EntityType.Monster => DotKind.Monster,
            EntityType.Player or EntityType.Pet or EntityType.Trust => DotKind.Player,
            _ => DotKind.Npc,
        };

        /// <summary>
        /// A bearing in degrees clockwise from north [0, 360) of a direction (<paramref name="east"/>, <paramref name="north"/>)
        /// in world terms (+X east, +Z north).
        /// </summary>
        public static float BearingOf(float east, float north)
        {
            float degrees = MathF.Atan2(east, north) * (180f / MathF.PI);
            return degrees < 0 ? degrees + 360f : degrees;
        }

        /// <summary>
        /// The screen angle (radians, clockwise from screen right, y down) at which a direction of <paramref name="bearing"/>
        /// lies on the ring when the camera looks along <paramref name="cameraBearing"/>: straight ahead is the far side
        /// (up), to the right is right.
        /// </summary>
        public static float ScreenAngle(float bearing, float cameraBearing) => (bearing - cameraBearing - 90f) * (MathF.PI / 180f);

        /// <summary>
        /// The screen offset from the ring's centre of a direction of <paramref name="bearing"/> at <paramref name="radius"/>
        /// (layout pixels across): on the flattened ellipse.
        /// </summary>
        public static Vector2 Project(float bearing, float radius, float cameraBearing)
        {
            float angle = ScreenAngle(bearing, cameraBearing);
            return new Vector2(radius * MathF.Cos(angle), radius * Squash * MathF.Sin(angle));
        }

        /// <summary>
        /// The radar offset (layout pixels from the centre) of a world offset (<paramref name="east"/>, <paramref name="north"/>)
        /// from the player, or null when it lies beyond <see cref="RangeYalms"/>.
        /// </summary>
        public static Vector2? DotOffset(float east, float north, float cameraBearing)
        {
            float distance = MathF.Sqrt(east * east + north * north);
            if (distance > RangeYalms) return null;
            if (distance < 1e-4f) return Vector2.Zero;
            return Project(BearingOf(east, north), RingRadius * distance / RangeYalms, cameraBearing);
        }

        /// <summary>
        /// The ring image's turn (radians, clockwise on the screen, before it is flattened): its dotted spokes, along the
        /// image's own axes, carry the cardinal points, so its +X axis is turned onto north.
        /// </summary>
        public static float RingTurn(float cameraBearing) => ScreenAngle(0, cameraBearing);

        /// <summary>
        /// The transform from the ring image's layout points to the screen: turned (<see cref="RingTurn"/>), scaled to
        /// <see cref="RingImageDrawnRadius"/>, flattened by <see cref="Squash"/> and centred on <paramref name="center"/>. Everything
        /// in screen pixels at <paramref name="scale"/> per layout pixel.
        /// </summary>
        public static Matrix3x2 RingTransform(Vector2 center, float cameraBearing, float scale) =>
            Matrix3x2.CreateRotation(RingTurn(cameraBearing))
            * Matrix3x2.CreateScale(RingImageDrawnRadius / RingImageRadius * scale, RingImageDrawnRadius / RingImageRadius * Squash * scale)
            * Matrix3x2.CreateTranslation(center);

        /// <summary>The cardinal letters' images and bearings, north first.</summary>
        public static readonly (int Image, float Bearing)[] Letters =
        {
            (NorthImage, 0f), (EastImage, 90f), (SouthImage, 180f), (WestImage, 270f),
        };

        /// <summary>
        /// The time as retail writes it: the hour without a leading zero, the minutes with one ("9:27", "11:25").
        /// </summary>
        public static string TimeText(int hour, int minute) => $"{hour}:{minute:00}";

        /// <summary>The Vana'diel hour and minute on <paramref name="clock"/> at <paramref name="utcNow"/>.</summary>
        public static (int Hour, int Minute) TimeOfDay(VanaClock clock, DateTime utcNow)
        {
            long seconds = clock.GetVanadielSeconds(utcNow) % VanaTime.SecondsPerVanadielDay;
            if (seconds < 0) seconds += VanaTime.SecondsPerVanadielDay;
            return ((int)(seconds / 3600), (int)(seconds % 3600 / 60));
        }

        /// <summary>
        /// The day's orb: the cell (texture pixels, 16 x 16) of the menu DAT's <c>colorbal</c> texture in the colour of the
        /// day's element, for <see cref="VanaTime.GetDayOfWeekIndex(DateTime)"/> (0 Firesday .. 7 Darksday). The texture's
        /// eight balls: top row blue, green, red, light blue; bottom row white, yellow, dark, violet. The captures' Iceday
        /// shows the light blue ball.
        /// </summary>
        public static (int X, int Y) DayOrbCell(int dayOfWeek) => (dayOfWeek & 7) switch
        {
            0 => (32, 0),  // Firesday: red
            1 => (16, 16), // Earthsday: yellow
            2 => (0, 0),   // Watersday: blue
            3 => (16, 0),  // Windsday: green
            4 => (48, 0),  // Iceday: light blue
            5 => (48, 16), // Lightningday: violet
            6 => (0, 16),  // Lightsday: white
            _ => (32, 16), // Darksday: dark
        };

        /// <summary>The map's grid: 15 squares a side, A-O across and 1-15 down, 32 map pixels each from 16 in.</summary>
        public const int GridSquares = 15, GridMargin = 16, GridSquare = 32;

        /// <summary>
        /// The grid square ("J-5") of a map pixel (0-512 across and down the 512 x 512 zone map image), or null off the
        /// grid. The map pixel comes from <c>ZoneMapProjection.ToMap</c>. <b>Provisional:</b> the grid's place on the image
        /// (16 px in, 32 px squares) is fitted to the one capture (Lower Jeuno (31.4, 70.2) reads J-5).
        /// </summary>
        public static string? GridLabel(float u, float v)
        {
            int column = (int)MathF.Floor((u - GridMargin) / GridSquare), row = (int)MathF.Floor((v - GridMargin) / GridSquare);
            if (column < 0 || column >= GridSquares || row < 0 || row >= GridSquares) return null;
            return $"{(char)('A' + column)}-{row + 1}";
        }

        /// <summary>
        /// The grid square of world point (<paramref name="x"/>, <paramref name="z"/>) on map <paramref name="map"/> of a zone
        /// (the floor the point is on, <see cref="StockUiMap.FloorOf"/>, #481), from where its image sits
        /// (<paramref name="originOf"/>); null when the map is not calibrated (<see cref="ZoneMapProjection.ScaleOf"/>) or
        /// the point is off its grid.
        /// </summary>
        public static string? MapSquare(int zone, int map, float x, float z, Func<int, int, ZoneMapOrigin?> originOf)
        {
            if (map < 0 || ZoneMapProjection.ScaleOf(zone, map) is not float scale || originOf(zone, map) is not { } origin) return null;
            var (u, v) = ZoneMapProjection.ToMap(origin, scale, x, z);
            return GridLabel(u, v);
        }

        /// <summary>The text after the orb: the time and, when the grid square is known, the square in brackets.</summary>
        public static string LineText(int hour, int minute, string? grid) =>
            grid == null ? TimeText(hour, minute) : $"{TimeText(hour, minute)} ({grid})";

        /// <summary>
        /// The text line's bottom (screen pixels): <see cref="LogGap"/> above the log's top edge, or
        /// <see cref="ScreenBottomGap"/> above the screen's bottom while the log is hidden.
        /// </summary>
        public static float LineBottom(float? logTop, float screenHeight, float scale) =>
            logTop is { } top ? top - LogGap * scale : screenHeight - ScreenBottomGap * scale;

        /// <summary>
        /// The compass's screen rectangle (letters and text included) for a line bottom: what a window placed there hides
        /// it for.
        /// </summary>
        public static (float X, float Y, float Width, float Height) Bounds(float lineBottom, float scale)
        {
            float top = lineBottom - (CenterRise + LetterRadius * Squash + 8) * scale;
            float right = (CenterX + LetterRadius + 8) * scale;
            return (0, top, right, lineBottom - top);
        }

        /// <summary>Whether a window with this left edge stays right of the dial's centre, so it only reaches the compass's right side.</summary>
        public static bool ClearsDial(float windowX, float scale) => windowX >= CenterX * scale;

        /// <summary>Whether a window's screen rectangle overlaps the compass's.</summary>
        public static bool Overlaps((float X, float Y, float Width, float Height) compass, float x, float y, float width, float height) =>
            x < compass.X + compass.Width && x + width > compass.X && y < compass.Y + compass.Height && y + height > compass.Y;

        /// <summary>
        /// Whether any of the window rectangles from <paramref name="from"/> on overlaps the compass: every window drawn over
        /// the HUD after the log, the menus and the boxes they draw outside them (the item info window, #506).
        /// </summary>
        public static bool IsCovered((float X, float Y, float Width, float Height) compass,
            IReadOnlyList<(float X, float Y, float Width, float Height)> windows, int from = 0)
        {
            for (int i = Math.Max(0, from); i < windows.Count; i++)
            {
                var w = windows[i];
                if (Overlaps(compass, w.X, w.Y, w.Width, w.Height)) return true;
            }
            return false;
        }
    }
}
