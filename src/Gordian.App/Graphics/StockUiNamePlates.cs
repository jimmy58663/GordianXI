// src/Gordian.App/Graphics/StockUiNamePlates.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Network;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// An entity's overhead point projected to the screen: <paramref name="Screen"/> in pixels, its
    /// <paramref name="Depth"/> in front of the camera (yalms) and <paramref name="PixelsPerYalm"/>, how many screen
    /// pixels a yalm spans at that depth.
    /// </summary>
    public readonly record struct NamePlateAnchor(uint ServerId, Vector2 Screen, float Depth, float PixelsPerYalm);

    /// <summary>
    /// Where the target's name plate was drawn this frame, so the target cursor can sit above it.
    /// </summary>
    public readonly record struct NamePlateBounds(Vector2 Center, float Top, float GlyphHeight);

    /// <summary>
    /// Draws the in-world name plates: each entity's name in the stock <c>fontshp</c> glyphs, centred on its overhead
    /// point (<see cref="EntityRenderer.OverheadAnchors"/>) and sized in the world, coloured from the "ncol" group,
    /// with one status icon left of it and the job mastery stars above it.
    /// <para>
    /// Measured from Windower captures (2026-09-29): the plates scale with distance, not with targeting; the local
    /// player's plate at the default camera distance has a 24 px cap height at 2560 x 1440 against the ~9 px source
    /// glyph, which with the 60 degree camera at about 6 yalms gives <see cref="YalmsPerFontPixel"/>.
    /// </para>
    /// </summary>
    public static class StockUiNamePlates
    {
        /// <summary>World size of one fontshp layout pixel (24 px cap / 9 px glyph at ~6 yalms, 1440 px, 60 degrees).</summary>
        public const float YalmsPerFontPixel = 0.0128f;

        /// <summary>Smallest glyph scale (screen pixels per font pixel), so distant names stay legible. Provisional.</summary>
        public const float MinScale = 1.0f;

        /// <summary>Largest glyph scale, for entities right next to the camera. Provisional.</summary>
        public const float MaxScale = 4.0f;

        /// <summary>Plates nearer the camera than this (yalms) are skipped: the local player's own in first person.</summary>
        public const float NearestDepth = 1.0f;

        /// <summary>Font pixels between the icon and the name. Provisional.</summary>
        public const float IconGap = 2.0f;

        /// <summary>
        /// Between the name's centre and the target cursor's tip, as a share of the line height (about 0.75 of the name's
        /// glyph height, measured from the Raven / Marine Dhalmel / Island Rarab screenshots, 2026-09-29).
        /// </summary>
        public const float CursorGapShare = 0.75f;

        /// <summary>The small job mastery star: a 16 x 16 cell in the top-left block of <c>ustatsHD</c>.</summary>
        private const float SmallStarX = 112, SmallStarY = 16, SmallStarSize = 16;

        /// <summary>fontshp image of the large star (ustatsHD (192, 64)).</summary>
        private const int LargeStarGlyph = 138;

        /// <summary>The ncol colours, used when the group is missing (half scale, read from the DAT).</summary>
        private static readonly UiColor[] FallbackColors =
        {
            new(127, 127, 127, 127), new(96, 127, 127, 127), new(96, 96, 127, 127), new(64, 96, 127, 127),
            new(96, 127, 96, 127), new(127, 127, 96, 127), new(127, 64, 64, 127), new(127, 64, 127, 127),
            new(127, 96, 64, 127),
        };

        /// <summary>Glyph scale of a plate: world sized, clamped.</summary>
        public static float ScaleFor(float pixelsPerYalm) => Math.Clamp(pixelsPerYalm * YalmsPerFontPixel, MinScale, MaxScale);

        /// <summary>A name colour from the "ncol" group (the corner colour of its image), or the fallback table.</summary>
        public static UiColor ColorOf(UiResourceLibrary library, NamePlateColor color)
        {
            int index = (int)color;
            if (library.TryGetGroup("ncol", out var group) && index < group.Images.Count && group.Images[index].Parts.Count > 0)
            {
                return group.Images[index].Parts[0].ColorTopLeft;
            }
            return FallbackColors[Math.Clamp(index, 0, FallbackColors.Length - 1)];
        }

        /// <summary>
        /// Draws every plate far to near and returns where the target's went (null when it has none on screen).
        /// </summary>
        public static NamePlateBounds? Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font,
            CharacterSession session, IReadOnlyList<NamePlateAnchor> anchors, IReadOnlyCollection<uint> ownPartyIds,
            uint width, uint height)
        {
            if (anchors.Count == 0) return null;
            var ordered = new List<NamePlateAnchor>(anchors);
            ordered.Sort((a, b) => a.PixelsPerYalm.CompareTo(b.PixelsPerYalm));

            uint localId = session.LocalPlayer.ServerId;
            var claimGroup = new List<uint>();
            foreach (var member in session.Party.Members) claimGroup.Add(member.ServerId);
            uint targetId = session.ActionService.CurrentTarget?.ServerId ?? 0;

            NamePlateBounds? target = null;
            foreach (var anchor in ordered)
            {
                if (anchor.Depth < NearestDepth) continue;
                if (!session.World.TryGetByServerId(anchor.ServerId, out var entity) || entity == null) continue;

                bool isLocal = entity.ServerId == localId;
                var flags = isLocal ? session.LocalPlayer.NamePlate | entity.NamePlate : entity.NamePlate;
                if (!NamePlateStyle.ShowsName(entity, flags)) continue;
                byte gmLevel = isLocal ? session.LocalPlayer.GmLevel : (entity as PlayerEntity)?.GmLevel ?? 0;

                var color = ColorOf(library, NamePlateStyle.Color(entity, flags, localId, ownPartyIds, claimGroup));
                var icon = NamePlateStyle.Icon(flags, gmLevel);
                UiColor? iconTint = icon == NamePlateIcon.Linkshell && entity is PlayerEntity p
                    ? new UiColor((byte)(p.LsColorR >> 1), (byte)(p.LsColorG >> 1), (byte)(p.LsColorB >> 1), 0x80)
                    : null;

                var bounds = DrawPlate(renderer, font, entity.Name, anchor, color, icon, iconTint,
                    (flags & NamePlateFlags.JobMaster) != 0, width, height);
                if (bounds is { } b && entity.ServerId == targetId) target = b;
            }
            return target;
        }

        /// <summary>
        /// Draws one plate centred on the anchor; null when it lies off screen.
        /// </summary>
        public static NamePlateBounds? DrawPlate(StockUiRenderer renderer, UiFont font, string name, NamePlateAnchor anchor,
            UiColor color, NamePlateIcon icon, UiColor? iconTint, bool jobMaster, uint width, uint height)
        {
            float s = ScaleFor(anchor.PixelsPerYalm);
            float textWidth = font.MeasureWidth(name) * s;
            float lineHeight = font.LineHeight * s;
            float left = anchor.Screen.X - textWidth * 0.5f;
            float top = anchor.Screen.Y - lineHeight * 0.5f;

            float iconWidth = icon != NamePlateIcon.None ? (16 + IconGap) * s : 0;
            float starHeight = jobMaster ? 16 * s : 0;
            if (left + textWidth < 0 || left - iconWidth > width || top + lineHeight < 0 || top - starHeight > height) return null;

            if (icon != NamePlateIcon.None && (int)icon < font.Group.Images.Count)
            {
                var image = font.Group.Images[(int)icon];
                if (image.Parts.Count > 0) renderer.DrawImage(image, left - iconWidth, top, s, iconTint);
            }

            renderer.DrawText(font, name, left, top, s, color);

            if (jobMaster) DrawStars(renderer, font, anchor.Screen.X, top, s);

            return new NamePlateBounds(new Vector2(anchor.Screen.X, anchor.Screen.Y), top - starHeight, lineHeight);
        }

        /// <summary>
        /// Job mastery: a large star centred above the name between two small ones (a retail capture of Cybin). The
        /// star sizes and spacing are provisional.
        /// </summary>
        private static void DrawStars(StockUiRenderer renderer, UiFont font, float centerX, float nameTop, float s)
        {
            float bigTop = nameTop - 16 * s;
            if (LargeStarGlyph < font.Group.Images.Count && font.Group.Images[LargeStarGlyph].Parts.Count > 0)
            {
                // fontshp icons are authored from y -2 to 14; lift by their own offset so the star sits on the name.
                renderer.DrawImage(font.Group.Images[LargeStarGlyph], centerX - 8 * s, bigTop + 2 * s, s);
            }
            float small = 10 * s;
            float smallTop = nameTop - small - 1 * s;
            var white = new UiColor(0x80, 0x80, 0x80, 0x80);
            renderer.DrawTextureRect("ustatsHD", SmallStarX, SmallStarY, SmallStarSize, SmallStarSize,
                centerX - 8 * s - small, smallTop, small, small, white);
            renderer.DrawTextureRect("ustatsHD", SmallStarX, SmallStarY, SmallStarSize, SmallStarSize,
                centerX + 8 * s, smallTop, small, small, white);
        }
    }
}
