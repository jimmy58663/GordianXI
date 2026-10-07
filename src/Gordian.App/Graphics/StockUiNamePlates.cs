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
    /// <paramref name="Depth"/> in front of the camera (yalms), <paramref name="PixelsPerYalm"/>, how many screen
    /// pixels a yalm spans at that depth, and <paramref name="ClipDepth"/>, the depth-buffer value the plate is drawn
    /// at (null = drawn over everything).
    /// </summary>
    public readonly record struct NamePlateAnchor(uint ServerId, Vector2 Screen, float Depth, float PixelsPerYalm, float? ClipDepth = null);

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
    /// glyph, which with the 60 degree camera at about 6 yalms gives 0.0128 yalms per font pixel; the default is 10%
    /// larger than that (the maintainer's choice after the first in-game test, 2026-09-29), and players scale it with
    /// <c>/uilayout names &lt;n&gt;</c> (<see cref="StockUiLayout.NamePlateScale"/>).
    /// </para>
    /// <para>
    /// Each plate is a flat card at its entity's depth (<see cref="NamePlateAnchor.ClipDepth"/>), tested against the
    /// scene's depth buffer: walls and models in front cut it pixel by pixel, as retail does (a wall edge crossing the
    /// A.M.A.N. Liaison's name, in-game comparison 2026-09-29).
    /// </para>
    /// </summary>
    public static class StockUiNamePlates
    {
        /// <summary>
        /// World size of one fontshp layout pixel: 0.0128 (24 px cap / 9 px glyph at ~6 yalms, 1440 px, 60 degrees) made
        /// 10% larger.
        /// </summary>
        public const float YalmsPerFontPixel = 0.0128f * 1.1f;

        /// <summary>Half a texel trimmed from each glyph and icon so bilinear scaling does not pick up neighbouring atlas cells.</summary>
        private const float GlyphTexelInset = 0.5f;

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

        /// <summary>Glyph scale of a plate: world sized, clamped, times the player's size setting.</summary>
        public static float ScaleFor(float pixelsPerYalm, float sizeMultiplier = 1.0f) =>
            Math.Clamp(pixelsPerYalm * YalmsPerFontPixel, MinScale, MaxScale) * sizeMultiplier;

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
            uint width, uint height, float sizeMultiplier = 1.0f)
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
                // The server has stopped answering (#235): the player's own name shows the link-dead red circle.
                if (isLocal && session.NetworkManager.IsConnectionLost) flags |= NamePlateFlags.LinkDead;
                if (!NamePlateStyle.ShowsName(entity, flags)) continue;
                byte gmLevel = isLocal ? session.LocalPlayer.GmLevel : (entity as PlayerEntity)?.GmLevel ?? 0;

                var color = ColorOf(library, NamePlateStyle.Color(entity, flags, localId, ownPartyIds, claimGroup));
                var icon = NamePlateStyle.Icon(flags, gmLevel);
                UiColor? iconTint = null;
                if (icon == NamePlateIcon.Linkshell)
                {
                    var (r, g, b) = isLocal ? session.LocalPlayer.LinkshellColor
                        : entity is PlayerEntity p ? (p.LsColorR, p.LsColorG, p.LsColorB) : ((byte)0x80, (byte)0x80, (byte)0x80);
                    iconTint = PearlTint(r, g, b);
                }

                var bounds = DrawPlate(renderer, font, entity.Name, anchor, color, icon, iconTint,
                    (flags & NamePlateFlags.JobMaster) != 0, width, height, sizeMultiplier);
                if (bounds is { } drawn && entity.ServerId == targetId) target = drawn;
            }
            return target;
        }

        /// <summary>
        /// The linkshell pearl's tint: the entity update's colour bytes used as they are, as a half-scale UI colour
        /// (0x80 = the texture unchanged, 0xFF about double). The pearl art is mid grey, so halving them first came out
        /// at less than half retail's brightness; retail samples 2.1-2.6 times the halved result (in-game comparison,
        /// 2026-09-29). LandSandBoat sends each 4-bit channel as (c &lt;&lt; 4) + 15 (packets/char_update.cpp).
        /// </summary>
        public static UiColor PearlTint(byte r, byte g, byte b) => new(r, g, b, 0x80);

        /// <summary>
        /// Draws one plate centred on the anchor; null when it lies off screen.
        /// </summary>
        public static NamePlateBounds? DrawPlate(StockUiRenderer renderer, UiFont font, string name, NamePlateAnchor anchor,
            UiColor color, NamePlateIcon icon, UiColor? iconTint, bool jobMaster, uint width, uint height, float sizeMultiplier = 1.0f)
        {
            float s = ScaleFor(anchor.PixelsPerYalm, sizeMultiplier);
            float textWidth = font.MeasureWidth(name) * s;
            float lineHeight = font.LineHeight * s;
            float left = anchor.Screen.X - textWidth * 0.5f;
            float top = anchor.Screen.Y - lineHeight * 0.5f;

            float iconWidth = icon != NamePlateIcon.None ? (16 + IconGap) * s : 0;
            float starHeight = jobMaster ? 16 * s : 0;
            if (left + textWidth < 0 || left - iconWidth > width || top + lineHeight < 0 || top - starHeight > height) return null;

            renderer.TexelInset = GlyphTexelInset;
            renderer.Depth = anchor.ClipDepth;
            if (icon != NamePlateIcon.None && (int)icon < font.Group.Images.Count)
            {
                var image = font.Group.Images[(int)icon];
                if (image.Parts.Count > 0) renderer.DrawImage(image, left - iconWidth, top, s, iconTint);
            }

            renderer.DrawText(font, name, left, top, s, color);

            if (jobMaster) DrawStars(renderer, font, anchor.Screen.X, top, s);
            renderer.TexelInset = 0;
            renderer.Depth = null;

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
