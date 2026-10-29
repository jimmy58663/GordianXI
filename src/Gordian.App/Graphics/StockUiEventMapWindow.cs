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
    /// d'Oria, 2026-10-16): the zone map with no window frame, magnified (<see cref="Zoom"/> times the size that covers the
    /// screen) and drawn over the scene, the HUD over it; the map is not held at its edges, so the screen is black past
    /// them; the map centres on the point (the selected home point, or the last marker an event placed) and on a new
    /// point jumps most of the way and settles with a short slide (<see cref="JumpShare"/>, <see cref="SlideSeconds"/>);
    /// a home point shows as the pale-blue <c>maphp</c> pin labelled "Home Point" in small white text beside it; the help
    /// bar's title box is empty and its help box holds the zone's name.
    /// <para>
    /// PROVISIONAL: the zoom (the capture's "roughly 2x"), the slide's shares and length, the label's size and that it is
    /// upright (retail's is italic), the pin for an event's own markers (the menu DAT's <c>marker</c> ball, the first cell)
    /// and the intro tutorial's look (not captured; drawn as the home point map). A map without a calibrated scale
    /// (<see cref="ZoneMapProjection.ScaleOf"/>) shows centred, without markers.
    /// </para>
    /// </summary>
    public sealed class StockUiEventMapWindow
    {
        /// <summary>The map's size: this many times the size at which it covers the screen.</summary>
        public const float Zoom = 2f;

        /// <summary>On a new point the map jumps this share of the way at once, then slides the rest in <see cref="SlideSeconds"/>.</summary>
        public const float JumpShare = 0.85f, SlideSeconds = 0.3f;

        /// <summary>The pins' size and the label's place beside the pin's tip, layout px.</summary>
        private const float PinSize = 16, LabelX = 10, LabelY = -15, LabelScale = 0.75f;

        private const string HomePointPin = "maphp", EventPin = "marker";
        private const int HomePointPinSize = 32, EventPinCell = 16;

        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

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
            float left = width * 0.5f - _current.X * cover, top = height * 0.5f - _current.Y * cover;
            renderer.DrawScreenTint(width, height, Vector3.Zero);
            renderer.DrawTexture("zonemap:" + texture.Source, texture, left, top, texture.Width * cover, texture.Height * cover, Neutral);

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

        /// <summary>A pin with its tip at (<paramref name="x"/>, <paramref name="y"/>) and its label to the right.</summary>
        private static void DrawPin(StockUiRenderer renderer, StockUiLogFont? logFont, EventMapPin pin, string label, float x, float y, float s)
        {
            float size = PinSize * s;
            if (pin == EventMapPin.HomePoint)
            {
                renderer.DrawTextureRect(HomePointPin, 0, 0, HomePointPinSize, HomePointPinSize, x - size * 0.5f, y - size, size, size, Neutral);
            }
            else
            {
                renderer.DrawTextureRect(EventPin, 0, 0, EventPinCell, EventPinCell, x - size * 0.5f, y - size * 0.5f, size, size, Neutral);
            }
            if (logFont != null && label.Length > 0) logFont.Draw(renderer, label, x + LabelX * s, y + LabelY * s, s * LabelScale, Neutral);
        }
    }
}
