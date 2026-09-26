// src/Gordian.App/Graphics/StockUiTargetWindow.cs
using System.Collections.Generic;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// How the target window colours a target's name.
    /// </summary>
    public enum TargetNameKind
    {
        /// <summary>Players, NPCs and objects.</summary>
        Neutral,

        /// <summary>A monster nobody has claimed.</summary>
        UnclaimedMonster,

        /// <summary>A monster claimed by you or your party.</summary>
        ClaimedByParty,

        /// <summary>A monster claimed by someone else.</summary>
        ClaimedByOther,
    }

    /// <summary>
    /// Draws the target window ("targetwi", 112 x 44 above the party window) and the status-icon row.
    /// <para>
    /// The DAT authors only the target window's frame and title; the client adds the target's name and HP gauge.
    /// Measured from Windower captures (2026-09-25): the name at (3, 13) in fontshp at 7/8 size, the HP gauge (the
    /// party window's) at (28, 28) with no number. Name colours: unclaimed monsters pale yellow, monsters claimed by
    /// you red (both sampled); monsters claimed by others purple (the retail convention, not yet captured).
    /// </para>
    /// <para>
    /// Status icons use the "buff" menu grid (frame (142, 48), top-left; 36 slots of 24 x 24 at a 26-pixel pitch,
    /// nine per row), which matches the icon row of the captures; icons come from <see cref="StatusIconLibrary"/>.
    /// </para>
    /// </summary>
    public static class StockUiTargetWindow
    {
        public static readonly UiColor NeutralNameColor = new(0x7F, 0x7F, 0x7F, 0x7F);
        public static readonly UiColor UnclaimedNameColor = new(0x7F, 0x7F, 0x6F, 0x7F);
        public static readonly UiColor ClaimedNameColor = new(0x7F, 0x48, 0x48, 0x7F);
        public static readonly UiColor OtherClaimNameColor = new(0x7F, 0x48, 0x7F, 0x7F);

        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        public static UiColor NameColor(TargetNameKind kind) => kind switch
        {
            TargetNameKind.UnclaimedMonster => UnclaimedNameColor,
            TargetNameKind.ClaimedByParty => ClaimedNameColor,
            TargetNameKind.ClaimedByOther => OtherClaimNameColor,
            _ => NeutralNameColor,
        };

        /// <summary>
        /// Classifies a target: monsters by who holds the claim, everything else neutral.
        /// </summary>
        public static TargetNameKind Classify(WorldEntity target, uint localServerId, IReadOnlyCollection<uint> partyServerIds)
        {
            if (target.Type != EntityType.Monster) return TargetNameKind.Neutral;
            uint claim = target.ClaimServerId;
            if (claim == 0) return TargetNameKind.UnclaimedMonster;
            if (claim == localServerId) return TargetNameKind.ClaimedByParty;
            foreach (uint id in partyServerIds)
            {
                if (id == claim) return TargetNameKind.ClaimedByParty;
            }
            return TargetNameKind.ClaimedByOther;
        }

        public static void Draw(StockUiRenderer renderer, UiFont font, UiMenuDefinition menu, StockUiPlacement placement,
            string name, int hpPercent, TargetNameKind kind)
        {
            float s = placement.Scale;
            StockUiPartyWindow.DrawGauge(renderer, placement.X + 28 * s, placement.Y + 28 * s, 64, withKnob: true,
                StockUiPartyWindow.HpGaugeColor, hpPercent, s);

            float limit = menu.Frame.Width - 6;
            renderer.DrawText(font, StockUiPartyWindow.FitName(font, name, limit), placement.X + 3 * s, placement.Y + 13 * s,
                s * StockUiPartyWindow.TextScale, NameColor(kind));
        }

        /// <summary>
        /// Draws status icons into the "buff" grid, one per slot in the order given.
        /// </summary>
        public static void DrawStatusIcons(StockUiRenderer renderer, StatusIconLibrary icons, UiMenuDefinition grid,
            StockUiPlacement placement, IReadOnlyList<ushort> statusIds)
        {
            float s = placement.Scale;
            for (int i = 0; i < statusIds.Count && i < grid.Buttons.Count; i++)
            {
                if (!icons.TryGetIcon(statusIds[i], out DecodedTexture icon)) continue;
                var slot = grid.Buttons[i];
                renderer.DrawTexture($"status:{statusIds[i]}", icon, placement.X + slot.X * s, placement.Y + slot.Y * s,
                    slot.Width * s, slot.Height * s, Neutral);
            }
        }
    }
}
