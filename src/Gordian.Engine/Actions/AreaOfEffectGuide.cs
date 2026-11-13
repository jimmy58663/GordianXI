// src/Gordian.Engine/Actions/AreaOfEffectGuide.cs
using System;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Actions
{
    /// <summary>
    /// Retail's area-of-effect range guide (#356): while the sub-target cursor is up for an action that covers a circle
    /// around its target, the client marks that circle on the zone geometry around the entity under the cursor. This is the
    /// guide's state (what and where); the zone renderer draws it.
    /// <para>
    /// Retail capture on local LSB (2026-11-08, issue #356; Retail Captures <c>356-*.png</c>): the circle follows the cursor
    /// (Tab to an NPC 12.4 yalms away re-centres it there), single-target spells (Cure) draw nothing, and the band where the
    /// circle meets the ground lies at about 17.6 yalms for Advancing March, Curaga, Protectra and Diaga (radius index 8)
    /// and about 9 yalms for Horde Lullaby (index 3). Only <see cref="AreaOfEffectShape.AroundTarget"/> was seen; cones and
    /// caster-centred moves are not drawn until a capture shows how retail marks them.
    /// </para>
    /// </summary>
    /// <param name="Center">The entity the circle is centred on (the one under the cursor).</param>
    /// <param name="RadiusIndex">The table's radius index (<see cref="SpellRecord.AoeRadius"/> / <see cref="CommandRecord.AoeRadius"/>).</param>
    /// <param name="Radius">Where the band is drawn, in yalms (<see cref="GuideRadius"/>).</param>
    public readonly record struct AreaOfEffectGuide(WorldEntity Center, int RadiusIndex, float Radius)
    {
        /// <summary>
        /// Yalms per radius index step and at index 0, fitted to the two retail points (index 8: band at 17.6 yalms, index 3:
        /// at 9). PROVISIONAL: two points fit any line; a third radius class (a Corsair's Roll at 7, Paralyga at 11, Circle
        /// Blade at 3 as a command) would show whether the client's mapping is linear. Neither point is LandSandBoat's
        /// server radius (10 and 4) times one constant, so the band is not drawn at the server's radius.
        /// </summary>
        public const float YalmsPerIndex = 1.72f;

        /// <inheritdoc cref="YalmsPerIndex"/>
        public const float YalmsAtIndexZero = 3.84f;

        /// <summary>The band's radius in yalms for a table radius index.</summary>
        public static float GuideRadius(int radiusIndex) => YalmsAtIndexZero + YalmsPerIndex * radiusIndex;

        /// <summary>The guide for the sub-target cursor, or null when there is none (no cursor, an item, a single-target or non-circular action, no tables).</summary>
        public static AreaOfEffectGuide? For(SubTargetSelection? selection, ActionCatalog? catalog)
        {
            if (selection == null || catalog == null) return null;
            (int radius, AreaOfEffectShape shape) = selection.Kind switch
            {
                ActionKind.Spell => catalog.GetSpell(selection.Id) is { } spell ? (spell.AoeRadius, spell.AoeShape) : (0, AreaOfEffectShape.None),
                ActionKind.JobAbility => catalog.GetJobAbility(selection.Id) is { } ability ? (ability.AoeRadius, ability.AoeShape) : (0, AreaOfEffectShape.None),
                ActionKind.WeaponSkill => catalog.GetWeaponSkill(selection.Id) is { } ws ? (ws.AoeRadius, ws.AoeShape) : (0, AreaOfEffectShape.None),
                _ => (0, AreaOfEffectShape.None),
            };
            if (radius <= 0 || shape != AreaOfEffectShape.AroundTarget) return null;
            return new AreaOfEffectGuide(selection.Current, radius, GuideRadius(radius));
        }
    }
}
