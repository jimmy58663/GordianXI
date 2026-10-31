// src/Gordian.App/Graphics/StockUiEventMapWindow.cs
using System;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Events;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The event map (#168): the map an event opens (the intro map tutorial, opcodes 0xC8 / 0x8B / 0x8A) and the map
    /// behind the home point lists (0xD4). As the maintainer's retail capture shows it (<c>Home_Point_Map.mp4</c>, Southern San
    /// d'Oria, 2026-10-16) and the round 1 screenshots measure it (2556 x 1438, registered against the map images,
    /// 2026-10-30): the zone map with no window frame at the size that covers the screen (5.0 screen px per map px at 2556
    /// wide), drawn over the scene with the HUD over it; the point (the selected home point, or the last marker an event
    /// placed) lands at a third of the width and 0.4 of the height (<see cref="AnchorX"/>, <see cref="AnchorY"/>: retail's
    /// pin at (853, 571) for both Northern San d'Oria entries); the map is held only so its edge comes no further in than
    /// <see cref="EdgeShare"/> of the screen (Southern San d'Oria #2's pin at (989, 571) with the map's right edge at
    /// x = 1930), so the screen is black past it; on a new point the map jumps most of the way and settles with a short
    /// slide (<see cref="JumpShare"/>, <see cref="SlideSeconds"/>); a home point shows as the pale-blue <c>maphp</c> crystal
    /// upright and centred on the point, labelled "Home Point" to its right; the help bar's title box is empty and its help box holds
    /// the zone's name.
    /// <para>
    /// PROVISIONAL: the edge rule on the other three edges (only the right one was seen held), the slide's shares and
    /// length, that the label is upright (retail's is italic), the pin for an event's own markers (the menu DAT's
    /// <c>marker</c> ball, the first cell) and the intro tutorial's look (not captured; drawn as the home point map). A map
    /// without a calibrated scale (<see cref="ZoneMapProjection.ScaleOf"/>) shows centred, without markers.
    /// </para>
    /// </summary>
    public sealed class StockUiEventMapWindow
    {
        /// <summary>The map's size: this many times the size at which it covers the screen.</summary>
        public const float Zoom = 1f;

        /// <summary>Where on the screen (shares of its width and height) the point the map shows lands.</summary>
        public const float AnchorX = 0.334f, AnchorY = 0.397f;

        /// <summary>The share of the screen the map's edge may come in from the screen's edge (black past it).</summary>
        public const float EdgeShare = 0.245f;

        /// <summary>On a new point the map jumps this share of the way at once, then slides the rest in <see cref="SlideSeconds"/>.</summary>
        public const float JumpShare = 0.85f, SlideSeconds = 0.3f;

        /// <summary>The pins' size and the label's left edge right of the pin's centre, layout px; the label is centred on the pin.</summary>
        private const float PinSize = 16, LabelX = 8, LabelScale = 0.75f;

        private const string HomePointPin = "maphp", EventPin = "marker";
        private const int HomePointPinSize = 32, EventPinCell = 16;

        /// <summary>
        /// The home point crystal: the menu DAT stores it leaning right (its long axis 37 degrees off vertical, 37 x 16 px of
        /// the 32 x 32 cell), and retail draws it upright, 13 x 5 screen px at UI scale ~1 (round 1 screenshots image-5, 7, 9;
        /// round 2 of #168: ours leaned), so the cell is drawn this many layout px across and turned back by
        /// <see cref="HomePointTurn"/> degrees.
        /// </summary>
        internal const float HomePointSize = 11, HomePointTurn = -37;

        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>
        /// The map image at half strength over the black: the retail screenshots' parchment is 0.50-0.53 of the image's own
        /// colour (image-7, image-9, mean over the map), the pins and label at full strength.
        /// </summary>
        private static readonly UiColor MapColor = new(0x80, 0x80, 0x80, 0x40);

        private (int Zone, int Map)? _shown;
        private Vector2 _from, _to, _current;
        private long _slideStart;

        /// <summary>The map point (texture px) at the screen's centre in the last frame drawn.</summary>
        public Vector2 Center => _current;

        public void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, EventMapFocus focus, int mapNumber,
            DecodedTexture texture, ZoneMapOrigin? origin, string zoneName, float uiScale, uint width, uint height)
        {
            if (texture.Width <= 0 || texture.Height <= 0 || width == 0 || height == 0) return;
            float? scale = origin != null ? ZoneMapProjection.ScaleOf(focus.Zone, mapNumber) : null;
            var target = new Vector2(texture.Width * 0.5f, texture.Height * 0.5f);
            if (scale is float s && origin is { } o && focus.X is float fx && focus.Z is float fz)
            {
                var (u, v) = ZoneMapProjection.ToMap(o, s, fx, fz);
                target = new Vector2(u, v);
            }
            Advance((focus.Zone, mapNumber), target, Stopwatch.GetTimestamp());

            float cover = Math.Max(width / (float)texture.Width, height / (float)texture.Height) * Zoom;
            float left = Hold(width * AnchorX - _current.X * cover, texture.Width * cover, width);
            float top = Hold(height * AnchorY - _current.Y * cover, texture.Height * cover, height);
            renderer.DrawScreenTint(width, height, Vector3.Zero);
            renderer.DrawTexture("zonemap:" + texture.Source, texture, left, top, texture.Width * cover, texture.Height * cover, MapColor);

            if (scale is float ms && origin is { } mo)
            {
                foreach (var marker in focus.Markers)
                {
                    var (u, v) = ZoneMapProjection.ToMap(mo, ms, marker.X, marker.Z);
                    DrawPin(renderer, logFont, focus.Pin, marker.Name, left + u * cover, top + v * cover, uiScale);
                }
            }
            StockUiHelpBar.DrawOnScreen(renderer, library, font, logFont, string.Empty, zoneName, uiScale, width, height);
        }

        /// <summary>The map's near edge on one axis, held so neither edge comes in more than <see cref="EdgeShare"/> of the screen.</summary>
        internal static float Hold(float edge, float size, float screen) =>
            Math.Clamp(edge, Math.Min(screen * (1 - EdgeShare) - size, screen * EdgeShare), screen * EdgeShare);

        /// <summary>Moves the view toward <paramref name="target"/>: a new map snaps, a new point on the same map jumps and slides.</summary>
        internal void Advance((int Zone, int Map) map, Vector2 target, long now)
        {
            if (_shown != map)
            {
                _shown = map;
                _from = _to = _current = target;
                _slideStart = now;
                return;
            }
            if (target != _to)
            {
                _from = target + (_current - target) * (1 - JumpShare);
                _to = target;
                _slideStart = now;
            }
            float t = Math.Clamp((float)Stopwatch.GetElapsedTime(_slideStart, now).TotalSeconds / SlideSeconds, 0f, 1f);
            float eased = 1 - (1 - t) * (1 - t);
            _current = Vector2.Lerp(_from, _to, eased);
        }

        /// <summary>Forgets the shown map, so the next one opens on its point without a slide.</summary>
        public void Reset() => _shown = null;

        /// <summary>A pin centred on (<paramref name="x"/>, <paramref name="y"/>) and its label (if any) to the right.</summary>
        internal static void DrawPin(StockUiRenderer renderer, StockUiLogFont? logFont, EventMapPin pin, string label, float x, float y, float s)
        {
            float size = PinSize * s;
            if (pin == EventMapPin.HomePoint)
            {
                renderer.DrawTextureRectRotated(HomePointPin, 0, 0, HomePointPinSize, HomePointPinSize, x, y, HomePointSize * s, HomePointTurn, Neutral);
            }
            else
            {
                renderer.DrawTextureRect(EventPin, 0, 0, EventPinCell, EventPinCell, x - size * 0.5f, y - size * 0.5f, size, size, Neutral);
            }
            float row = StockUiLogFont.CellHeight * LabelScale * s;
            if (logFont != null && label.Length > 0) logFont.Draw(renderer, label, x + LabelX * s, y - row * 0.5f, s * LabelScale, Neutral);
        }
    }
}
