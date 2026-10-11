// src/Gordian.Core/Ui/NamePlateStyle.cs
using System.Collections.Generic;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// A name plate's colour: the index of the "ncol" element group image that carries it (English menu DAT 39542,
    /// 25 images, each fontshp "A" with a different corner colour). The meanings were read from Windower captures
    /// (2026-09-29) except where noted.
    /// </summary>
    public enum NamePlateColor
    {
        /// <summary>#0 white: a player, the local player included (in a party too).</summary>
        Player = 0,

        /// <summary>#1 pale cyan: a member of your own party, trusts included (alliance members stay white).</summary>
        Party = 1,

        /// <summary>#2 lavender: a player seeking a party.</summary>
        SeekingParty = 2,

        /// <summary>#3 dark blue: an anonymous player.</summary>
        Anonymous = 3,

        /// <summary>#4 pale green: an NPC.</summary>
        Npc = 4,

        /// <summary>#5 pale yellow: an unclaimed monster.</summary>
        UnclaimedMonster = 5,

        /// <summary>#6 red: a monster claimed by you or your party (probable; not yet captured on a name plate).</summary>
        ClaimedByParty = 6,

        /// <summary>#7 purple: a monster claimed by someone else (probable).</summary>
        ClaimedByOther = 7,

        /// <summary>#8 orange: called for help on (XiPackets: YellFlag turns the name orange; the index is probable).</summary>
        CalledForHelp = 8,

        /// <summary>
        /// #9 grey: a dead monster (#327). The only grey in the group (half-scale 64, 64, 64), right after the eight known
        /// colours; that it is the one retail uses for the dead is probable, not captured.
        /// </summary>
        Dead = 9,
    }

    /// <summary>
    /// The icon drawn left of a name: the fontshp image index (fontshp 110-145 draw 32 x 32 cells of the
    /// <c>ustatsHD</c> sheet at 16 x 16).
    /// </summary>
    public enum NamePlateIcon
    {
        None = 0,
        PlayOnline = 110,
        LinkDead = 111,
        Away = 112,
        SeekingParty = 113,
        Linkshell = 114,
        Trial = 115,
        Gm = 118,
        Bazaar = 124,
        AutoParty = 125,
        NewPlayer = 129,
        Mentor = 130,
        SeekingMasterParty = 140,
        Info = 145,
    }

    /// <summary>
    /// Decides how the in-world name plate over an entity looks: whether it is drawn, its colour and its icon.
    /// Flag meanings referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x000D, 0x000E);
    /// colours and icons located in the English menu DAT and checked against Windower captures (see
    /// docs/ui/stock-ui.md, Name plates).
    /// </summary>
    public static class NamePlateStyle
    {
        /// <summary>
        /// Whether the client draws a name over the entity. Hidden and invisible entities, entities a running event hides
        /// the name of (opcode 0x92, <see cref="WorldEntity.HidesEventName"/>), NPCs flagged to hide their
        /// name, and NPCs that have no name plate at all (<see cref="HasNamePlate"/>) have none.
        /// </summary>
        public static bool ShowsName(WorldEntity entity, NamePlateFlags flags)
        {
            if (!entity.IsSpawned || ((entity.IsHidden || entity.IsSleeping || entity.IsAutoTargetOnly) && !entity.IsInEvent)
                || entity.IsInvisible || string.IsNullOrWhiteSpace(entity.Name)) return false;
            if (entity.HidesEventName) return false;
            return HasNamePlate(entity, flags);
        }

        /// <summary>
        /// Whether the entity is one that carries a name plate at all, leaving aside the passing states
        /// <see cref="ShowsName"/> also checks (hidden, invisible, an event hiding names). Players always do. NPCs do not
        /// when 0x00E flags3 <c>unknown_3_5</c> is set (<see cref="NamePlateFlags.NameHidden"/>: "health bar hidden and
        /// name not rendered", XiPackets), when they are doors, elevators or ships (no model is drawn for them), or when
        /// their model is one the client never names (<see cref="ModelHidesName"/>).
        /// </summary>
        public static bool HasNamePlate(WorldEntity entity, NamePlateFlags flags)
        {
            if (entity is PlayerEntity) return true;
            if (entity.Type is EntityType.Door or EntityType.Elevator or EntityType.Ship) return false;
            if ((flags & NamePlateFlags.NameHidden) != 0) return false;
            return !ModelHidesName(entity.Appearance.ModelId);
        }

        /// <summary>
        /// Whether the target window draws the target's HP gauge. Retail draws none for an NPC that has no name plate
        /// (maintainer's retail check in Port Jeuno, 2026-10-04, #259: Synthesis Focuser II, Abyssea Campaign, Treasure
        /// Coffer, the Door: NPCs and ??? have neither; Mewk Chorosap and Raging Lion have both), and none for an NPC
        /// with 0x00E flags1 <c>PlayOnelineFlag</c> set (<see cref="NamePlateFlags.HealthBarHidden"/>; XiPackets: "the
        /// entities health bar, when targeted, should be hidden").
        /// Flag meanings referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E.
        /// </summary>
        public static bool ShowsTargetHealthBar(WorldEntity entity, NamePlateFlags flags)
        {
            if (entity is PlayerEntity) return true;
            return (flags & NamePlateFlags.HealthBarHidden) == 0 && HasNamePlate(entity, flags);
        }

        /// <summary>
        /// Model ids whose NPCs the client draws without a name: Home Point crystals and other model ids 50-59, nation
        /// and beastmen flags 814-817, invisible models 1847-1862, special coffers 2425-2429 and Confluxes 2490-2494, from
        /// the client's 0x00E sub-kind 0 handling in XiPackets; and treasure chests and coffers 960-969, which that
        /// handling sends down a separate branch whose effect XiPackets does not show. Retail draws no plate and no
        /// target HP gauge for the Port Jeuno Treasure Coffer (model 968) and Abyssea Campaign (962) NPCs, whose
        /// LandSandBoat entries set no flag that hides a name (maintainer's retail check, 2026-10-04, #259).
        /// Referenced from XiPackets (https://github.com/atom0s/XiPackets) world/server/0x000E, SubKind 0.
        /// </summary>
        public static bool ModelHidesName(uint modelId) => modelId switch
        {
            >= 50 and <= 59 => true,
            >= 814 and <= 817 => true,
            >= 960 and <= 969 => true,
            >= 1847 and <= 1862 => true,
            >= 2425 and <= 2429 => true,
            >= 2490 and <= 2494 => true,
            _ => false,
        };

        /// <summary>
        /// The name colour. <paramref name="ownPartyIds"/> is the local player's own party (not the rest of an
        /// alliance); <paramref name="claimGroupIds"/> is everyone whose claim counts as yours (party and alliance).
        /// </summary>
        public static NamePlateColor Color(WorldEntity entity, NamePlateFlags flags, uint localServerId,
            IReadOnlyCollection<uint> ownPartyIds, IReadOnlyCollection<uint> claimGroupIds)
        {
            // A dead monster's name greys out (#327; the death status, WorldEntity.IsDeadBattleEntity).
            if (entity.IsDeadBattleEntity) return NamePlateColor.Dead;
            if ((flags & NamePlateFlags.CalledForHelp) != 0) return NamePlateColor.CalledForHelp;

            if (entity.Type == EntityType.Monster)
            {
                uint claim = entity.ClaimServerId;
                if (claim == 0) return NamePlateColor.UnclaimedMonster;
                if (claim == localServerId || Contains(claimGroupIds, claim)) return NamePlateColor.ClaimedByParty;
                return NamePlateColor.ClaimedByOther;
            }

            // A capture with trusts in the party shows the local player's own name white, the trusts cyan.
            if (entity.ServerId != localServerId && Contains(ownPartyIds, entity.ServerId)) return NamePlateColor.Party;

            if (entity is PlayerEntity || entity.ServerId == localServerId)
            {
                if ((flags & NamePlateFlags.SeekingParty) != 0) return NamePlateColor.SeekingParty;
                if ((flags & NamePlateFlags.Anonymous) != 0) return NamePlateColor.Anonymous;
                return NamePlateColor.Player;
            }

            return NamePlateColor.Npc;
        }

        /// <summary>
        /// The one icon shown left of the name. XiPackets says only that the linkshell pearl gives way to every other
        /// icon; the order among the rest is provisional: GM, link dead, PlayOnline, away, the seeking icons, bazaar,
        /// new player, mentor, trial, linkshell. GM level 4-7 shows the GM icon unless hidden, 3 the PlayOnline icon and
        /// 1-2 the trial arrow (XiPackets 0x000D GmLevel).
        /// </summary>
        public static NamePlateIcon Icon(NamePlateFlags flags, byte gmLevel)
        {
            bool gmShown = (flags & NamePlateFlags.GmIconHidden) == 0;
            if (gmShown && gmLevel >= 4) return NamePlateIcon.Gm;
            if ((flags & NamePlateFlags.LinkDead) != 0) return NamePlateIcon.LinkDead;
            if ((flags & NamePlateFlags.PlayOnline) != 0 || (gmShown && gmLevel == 3)) return NamePlateIcon.PlayOnline;
            if ((flags & NamePlateFlags.Away) != 0) return NamePlateIcon.Away;
            if ((flags & NamePlateFlags.SeekingMasterParty) != 0) return NamePlateIcon.SeekingMasterParty;
            if ((flags & NamePlateFlags.SeekingParty) != 0) return NamePlateIcon.SeekingParty;
            if ((flags & NamePlateFlags.AutoParty) != 0) return NamePlateIcon.AutoParty;
            if ((flags & NamePlateFlags.Bazaar) != 0) return NamePlateIcon.Bazaar;
            if ((flags & NamePlateFlags.NewPlayer) != 0) return NamePlateIcon.NewPlayer;
            if ((flags & NamePlateFlags.Mentor) != 0) return NamePlateIcon.Mentor;
            if ((flags & NamePlateFlags.Trial) != 0 || (gmShown && gmLevel is 1 or 2)) return NamePlateIcon.Trial;
            if ((flags & NamePlateFlags.InfoNpc) != 0) return NamePlateIcon.Info;
            if ((flags & NamePlateFlags.Linkshell) != 0) return NamePlateIcon.Linkshell;
            return NamePlateIcon.None;
        }

        private static bool Contains(IReadOnlyCollection<uint> ids, uint id)
        {
            foreach (uint candidate in ids)
            {
                if (candidate == id) return true;
            }
            return false;
        }
    }
}
