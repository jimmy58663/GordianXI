// src/Gordian.Core/Network/Packets/CurrencyTables.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Every currency of S2C 0x113 (GP_SERV_COMMAND_CURRENCIES1), in packet order. The values are stored in
    /// <see cref="Gordian.Core.World.InventoryState.GetCurrency(Currency1Kind)"/>. The names follow XiPackets' field names;
    /// the <c>_stored</c> suffix is dropped.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0113</c>.
    /// </summary>
    public enum Currency1Kind : byte
    {
        ConquestPointsSandoria = 0,
        ConquestPointsBastok = 1,
        ConquestPointsWindurst = 2,
        BeastmansSeals = 3,
        KindredsSeals = 4,
        KindredsCrests = 5,
        HighKindredsCrests = 6,
        SacredKindredsCrests = 7,
        AncientBeastcoins = 8,
        ValorPoints = 9,
        Scylds = 10,
        GuildPointsFishing = 11,
        GuildPointsWoodworking = 12,
        GuildPointsSmithing = 13,
        GuildPointsGoldsmithing = 14,
        GuildPointsWeaving = 15,
        GuildPointsLeathercraft = 16,
        GuildPointsBonecraft = 17,
        GuildPointsAlchemy = 18,
        GuildPointsCooking = 19,
        Cinders = 20,
        SynergyFewellFire = 21,
        SynergyFewellIce = 22,
        SynergyFewellWind = 23,
        SynergyFewellEarth = 24,
        SynergyFewellLightning = 25,
        SynergyFewellWater = 26,
        SynergyFewellLight = 27,
        SynergyFewellDark = 28,
        BallistaPoints = 29,
        FellowPoints = 30,
        ChocobucksSandoriaTeam = 31,
        ChocobucksBastokTeam = 32,
        ChocobucksWindurstTeam = 33,
        DailyTally = 34,
        ResearchMarks = 35,
        WizenedTunnelWorms = 36,
        WizenedMorionWorms = 37,
        WizenedPhantomWorms = 38,
        MoblinMarbles = 39,
        Infamy = 40,
        Prestige = 41,
        LegionPoints = 42,
        SparksOfEminence = 43,
        ShiningStars = 44,
        ImperialStanding = 45,
        AssaultPointsLSanctum = 46,
        AssaultPointsMjtg = 47,
        AssaultPointsLCavern = 48,
        AssaultPointsPeriqia = 49,
        AssaultPointsIlrusiAtoll = 50,
        Tokens = 51,
        Zeni = 52,
        Jettons = 53,
        TherionIchor = 54,
        AlliedNotes = 55,
        CopperAmanVouchers = 56,
        LoginPoints = 57,
        Cruor = 58,
        ResistanceCredits = 59,
        DominionNotes = 60,
        EchelonBattleTrophies5th = 61,
        EchelonBattleTrophies4th = 62,
        EchelonBattleTrophies3rd = 63,
        EchelonBattleTrophies2nd = 64,
        EchelonBattleTrophies1st = 65,
        CaveConservationPoints = 66,
        ImperialArmyIdTags = 67,
        OpCredits = 68,
        TraverserStones = 69,
        Voidstones = 70,
        KupofriedsCorundums = 71,
        MoblinPheromoneSacks = 72,
        RemsTaleChapters1 = 73,
        RemsTaleChapters2 = 74,
        RemsTaleChapters3 = 75,
        RemsTaleChapters4 = 76,
        RemsTaleChapters5 = 77,
        RemsTaleChapters6 = 78,
        RemsTaleChapters7 = 79,
        RemsTaleChapters8 = 80,
        RemsTaleChapters9 = 81,
        RemsTaleChapters10 = 82,
        BloodshedPlans = 83,
        UmbragePlans = 84,
        RitualisticPlans = 85,
        TutelaryPlans = 86,
        PrimacyPlans = 87,
        ReclamationMarks = 88,
        UnityAccolades = 89,
        FireCrystals = 90,
        IceCrystals = 91,
        WindCrystals = 92,
        EarthCrystals = 93,
        LightningCrystals = 94,
        WaterCrystals = 95,
        LightCrystals = 96,
        DarkCrystals = 97,
        Deeds = 98,
    }

    /// <summary>
    /// Every currency of S2C 0x118 (GP_SERV_COMMAND_CURRENCIES2), in packet order: Bayld, Kinetic units, the Salvage
    /// and Nyzul stones, Escha beads and silt, Hallmarks, Domain points and so on.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0118</c>.
    /// </summary>
    public enum Currency2Kind : byte
    {
        Bayld = 0,
        KineticUnits = 1,
        CoalitionImprimaturs = 2,
        MysticalCanteens = 3,
        ObsidianFragments = 4,
        LebondoptWings = 5,
        PulchridoptWings = 6,
        MweyaPlasmCorpuscles = 7,
        GhastlyStones = 8,
        GhastlyStonesPlus1 = 9,
        GhastlyStonesPlus2 = 10,
        VerdigrisStones = 11,
        VerdigrisStonesPlus1 = 12,
        VerdigrisStonesPlus2 = 13,
        WailingStones = 14,
        WailingStonesPlus1 = 15,
        WailingStonesPlus2 = 16,
        SnowslitStones = 17,
        SnowslitStonesPlus1 = 18,
        SnowslitStonesPlus2 = 19,
        SnowtipStones = 20,
        SnowtipStonesPlus1 = 21,
        SnowtipStonesPlus2 = 22,
        SnowdimStones = 23,
        SnowdimStonesPlus1 = 24,
        SnowdimStonesPlus2 = 25,
        SnoworbStones = 26,
        SnoworbStonesPlus1 = 27,
        SnoworbStonesPlus2 = 28,
        LeafslitStones = 29,
        LeafslitStonesPlus1 = 30,
        LeafslitStonesPlus2 = 31,
        LeaftipStones = 32,
        LeaftipStonesPlus1 = 33,
        LeaftipStonesPlus2 = 34,
        LeafdimStones = 35,
        LeafdimStonesPlus1 = 36,
        LeafdimStonesPlus2 = 37,
        LeaforbStones = 38,
        LeaforbStonesPlus1 = 39,
        LeaforbStonesPlus2 = 40,
        DuskslitStones = 41,
        DuskslitStonesPlus1 = 42,
        DuskslitStonesPlus2 = 43,
        DusktipStones = 44,
        DusktipStonesPlus1 = 45,
        DusktipStonesPlus2 = 46,
        DuskdimStones = 47,
        DuskdimStonesPlus1 = 48,
        DuskdimStonesPlus2 = 49,
        DuskorbStones = 50,
        DuskorbStonesPlus1 = 51,
        DuskorbStonesPlus2 = 52,
        PellucidStones = 53,
        FernStones = 54,
        TaupeStones = 55,
        MellidoptWings = 56,
        EschaBeads = 57,
        EschaSilt = 58,
        Potpourri = 59,
        Hallmarks = 60,
        TotalHallmarks = 61,
        BadgesOfGallantry = 62,
        CrafterPoints = 63,
        FireCrystalsSet = 64,
        IceCrystalsSet = 65,
        WindCrystalsSet = 66,
        EarthCrystalsSet = 67,
        LightningCrystalsSet = 68,
        WaterCrystalsSet = 69,
        LightCrystalsSet = 70,
        DarkCrystalsSet = 71,
        McISr01Set = 72,
        McISr02Set = 73,
        McISr03Set = 74,
        LiquefactionSpheresSet = 75,
        IndurationSpheresSet = 76,
        DetonationSpheresSet = 77,
        ScissionSpheresSet = 78,
        ImpactionSpheresSet = 79,
        ReverberationSpheresSet = 80,
        TransfixionSpheresSet = 81,
        CompressionSpheresSet = 82,
        FusionSpheresSet = 83,
        DistortionSpheresSet = 84,
        FragmentationSpheresSet = 85,
        GravitationSpheresSet = 86,
        LightSpheresSet = 87,
        DarknessSpheresSet = 88,
        SilverAmanVouchers = 89,
        DomainPoints = 90,
        DomainPointsEarnedToday = 91,
        MogSegments = 92,
        Gallimaufry = 93,
    }


    /// <summary>
    /// The field layout of the two currency packets: each entry is the payload offset, the width in bytes (1, 2 or 4; 0
    /// for a 9-bit field of the 64-bit plans word at +212) and, for the plans, the bit shift. Offsets are the XiPackets
    /// offsets minus the 4-byte header, checked against the values GordianXI already read (Sparks of Eminence at 112,
    /// Unity Accolades at 224, Domain Points at 128).
    /// Packet structures referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0113</c> and <c>0x0118</c>.
    /// </summary>
    public static class CurrencyLayout
    {
        /// <summary>The number of <see cref="Currency1Kind"/> values.</summary>
        public static int Count1 => Layout1.Length;

        /// <summary>The number of <see cref="Currency2Kind"/> values.</summary>
        public static int Count2 => Layout2.Length;

        /// <summary>Reads a currency from an S2C 0x113 payload; 0 when the payload is too short for the field.</summary>
        public static int Read(ReadOnlySpan<byte> payload, Currency1Kind kind) => Read(payload, Layout1[(int)kind]);

        /// <summary>Reads a currency from an S2C 0x118 payload; 0 when the payload is too short for the field.</summary>
        public static int Read(ReadOnlySpan<byte> payload, Currency2Kind kind) => Read(payload, Layout2[(int)kind]);

        private static int Read(ReadOnlySpan<byte> payload, (ushort Offset, byte Size, byte Shift, bool Signed) f)
        {
            int o = f.Offset;
            switch (f.Size)
            {
                case 0:
                    return payload.Length >= o + 8
                        ? (int)((BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(o, 8)) >> f.Shift) & 0x1FF)
                        : 0;
                case 1:
                    return payload.Length > o ? payload[o] : 0;
                case 2:
                    return payload.Length >= o + 2 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(o, 2)) : 0;
                default:
                    return payload.Length >= o + 4 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(o, 4)) : 0;
            }
        }

        private static readonly (ushort Offset, byte Size, byte Shift, bool Signed)[] Layout1 =
        {
            (0, 4, 0, true), // ConquestPointsSandoria
            (4, 4, 0, true), // ConquestPointsBastok
            (8, 4, 0, true), // ConquestPointsWindurst
            (12, 2, 0, false), // BeastmansSeals
            (14, 2, 0, false), // KindredsSeals
            (16, 2, 0, false), // KindredsCrests
            (18, 2, 0, false), // HighKindredsCrests
            (20, 2, 0, false), // SacredKindredsCrests
            (22, 2, 0, false), // AncientBeastcoins
            (24, 2, 0, false), // ValorPoints
            (26, 2, 0, false), // Scylds
            (28, 4, 0, true), // GuildPointsFishing
            (32, 4, 0, true), // GuildPointsWoodworking
            (36, 4, 0, true), // GuildPointsSmithing
            (40, 4, 0, true), // GuildPointsGoldsmithing
            (44, 4, 0, true), // GuildPointsWeaving
            (48, 4, 0, true), // GuildPointsLeathercraft
            (52, 4, 0, true), // GuildPointsBonecraft
            (56, 4, 0, true), // GuildPointsAlchemy
            (60, 4, 0, true), // GuildPointsCooking
            (64, 4, 0, true), // Cinders
            (68, 1, 0, false), // SynergyFewellFire
            (69, 1, 0, false), // SynergyFewellIce
            (70, 1, 0, false), // SynergyFewellWind
            (71, 1, 0, false), // SynergyFewellEarth
            (72, 1, 0, false), // SynergyFewellLightning
            (73, 1, 0, false), // SynergyFewellWater
            (74, 1, 0, false), // SynergyFewellLight
            (75, 1, 0, false), // SynergyFewellDark
            (76, 4, 0, true), // BallistaPoints
            (80, 4, 0, true), // FellowPoints
            (84, 2, 0, false), // ChocobucksSandoriaTeam
            (86, 2, 0, false), // ChocobucksBastokTeam
            (88, 2, 0, false), // ChocobucksWindurstTeam
            (90, 2, 0, false), // DailyTally
            (92, 4, 0, true), // ResearchMarks
            (96, 1, 0, false), // WizenedTunnelWorms
            (97, 1, 0, false), // WizenedMorionWorms
            (98, 1, 0, false), // WizenedPhantomWorms
            (100, 4, 0, true), // MoblinMarbles
            (104, 2, 0, false), // Infamy
            (106, 2, 0, false), // Prestige
            (108, 4, 0, true), // LegionPoints
            (112, 4, 0, true), // SparksOfEminence
            (116, 4, 0, true), // ShiningStars
            (120, 4, 0, true), // ImperialStanding
            (124, 4, 0, true), // AssaultPointsLSanctum
            (128, 4, 0, true), // AssaultPointsMjtg
            (132, 4, 0, true), // AssaultPointsLCavern
            (136, 4, 0, true), // AssaultPointsPeriqia
            (140, 4, 0, true), // AssaultPointsIlrusiAtoll
            (144, 4, 0, true), // Tokens
            (148, 4, 0, true), // Zeni
            (152, 4, 0, true), // Jettons
            (156, 4, 0, true), // TherionIchor
            (160, 4, 0, true), // AlliedNotes
            (164, 2, 0, false), // CopperAmanVouchers
            (166, 2, 0, false), // LoginPoints
            (168, 4, 0, true), // Cruor
            (172, 4, 0, true), // ResistanceCredits
            (176, 4, 0, true), // DominionNotes
            (180, 1, 0, false), // EchelonBattleTrophies5th
            (181, 1, 0, false), // EchelonBattleTrophies4th
            (182, 1, 0, false), // EchelonBattleTrophies3rd
            (183, 1, 0, false), // EchelonBattleTrophies2nd
            (184, 1, 0, false), // EchelonBattleTrophies1st
            (185, 1, 0, false), // CaveConservationPoints
            (186, 1, 0, false), // ImperialArmyIdTags
            (187, 1, 0, false), // OpCredits
            (188, 4, 0, true), // TraverserStones
            (192, 4, 0, true), // Voidstones
            (196, 4, 0, true), // KupofriedsCorundums
            (200, 1, 0, false), // MoblinPheromoneSacks
            (202, 1, 0, false), // RemsTaleChapters1
            (203, 1, 0, false), // RemsTaleChapters2
            (204, 1, 0, false), // RemsTaleChapters3
            (205, 1, 0, false), // RemsTaleChapters4
            (206, 1, 0, false), // RemsTaleChapters5
            (207, 1, 0, false), // RemsTaleChapters6
            (208, 1, 0, false), // RemsTaleChapters7
            (209, 1, 0, false), // RemsTaleChapters8
            (210, 1, 0, false), // RemsTaleChapters9
            (211, 1, 0, false), // RemsTaleChapters10
            (212, 0, 0, false), // BloodshedPlans
            (212, 0, 9, false), // UmbragePlans
            (212, 0, 18, false), // RitualisticPlans
            (212, 0, 27, false), // TutelaryPlans
            (212, 0, 36, false), // PrimacyPlans
            (220, 2, 0, false), // ReclamationMarks
            (224, 4, 0, true), // UnityAccolades
            (228, 2, 0, false), // FireCrystals
            (230, 2, 0, false), // IceCrystals
            (232, 2, 0, false), // WindCrystals
            (234, 2, 0, false), // EarthCrystals
            (236, 2, 0, false), // LightningCrystals
            (238, 2, 0, false), // WaterCrystals
            (240, 2, 0, false), // LightCrystals
            (242, 2, 0, false), // DarkCrystals
            (244, 2, 0, false), // Deeds
        };

        private static readonly (ushort Offset, byte Size, byte Shift, bool Signed)[] Layout2 =
        {
            (0, 4, 0, true), // Bayld
            (4, 2, 0, false), // KineticUnits
            (6, 1, 0, false), // CoalitionImprimaturs
            (7, 1, 0, false), // MysticalCanteens
            (8, 4, 0, true), // ObsidianFragments
            (12, 2, 0, false), // LebondoptWings
            (14, 2, 0, false), // PulchridoptWings
            (16, 4, 0, true), // MweyaPlasmCorpuscles
            (20, 1, 0, false), // GhastlyStones
            (21, 1, 0, false), // GhastlyStonesPlus1
            (22, 1, 0, false), // GhastlyStonesPlus2
            (23, 1, 0, false), // VerdigrisStones
            (24, 1, 0, false), // VerdigrisStonesPlus1
            (25, 1, 0, false), // VerdigrisStonesPlus2
            (26, 1, 0, false), // WailingStones
            (27, 1, 0, false), // WailingStonesPlus1
            (28, 1, 0, false), // WailingStonesPlus2
            (29, 1, 0, false), // SnowslitStones
            (30, 1, 0, false), // SnowslitStonesPlus1
            (31, 1, 0, false), // SnowslitStonesPlus2
            (32, 1, 0, false), // SnowtipStones
            (33, 1, 0, false), // SnowtipStonesPlus1
            (34, 1, 0, false), // SnowtipStonesPlus2
            (35, 1, 0, false), // SnowdimStones
            (36, 1, 0, false), // SnowdimStonesPlus1
            (37, 1, 0, false), // SnowdimStonesPlus2
            (38, 1, 0, false), // SnoworbStones
            (39, 1, 0, false), // SnoworbStonesPlus1
            (40, 1, 0, false), // SnoworbStonesPlus2
            (41, 1, 0, false), // LeafslitStones
            (42, 1, 0, false), // LeafslitStonesPlus1
            (43, 1, 0, false), // LeafslitStonesPlus2
            (44, 1, 0, false), // LeaftipStones
            (45, 1, 0, false), // LeaftipStonesPlus1
            (46, 1, 0, false), // LeaftipStonesPlus2
            (47, 1, 0, false), // LeafdimStones
            (48, 1, 0, false), // LeafdimStonesPlus1
            (49, 1, 0, false), // LeafdimStonesPlus2
            (50, 1, 0, false), // LeaforbStones
            (51, 1, 0, false), // LeaforbStonesPlus1
            (52, 1, 0, false), // LeaforbStonesPlus2
            (53, 1, 0, false), // DuskslitStones
            (54, 1, 0, false), // DuskslitStonesPlus1
            (55, 1, 0, false), // DuskslitStonesPlus2
            (56, 1, 0, false), // DusktipStones
            (57, 1, 0, false), // DusktipStonesPlus1
            (58, 1, 0, false), // DusktipStonesPlus2
            (59, 1, 0, false), // DuskdimStones
            (60, 1, 0, false), // DuskdimStonesPlus1
            (61, 1, 0, false), // DuskdimStonesPlus2
            (62, 1, 0, false), // DuskorbStones
            (63, 1, 0, false), // DuskorbStonesPlus1
            (64, 1, 0, false), // DuskorbStonesPlus2
            (65, 1, 0, false), // PellucidStones
            (66, 1, 0, false), // FernStones
            (67, 1, 0, false), // TaupeStones
            (68, 2, 0, false), // MellidoptWings
            (70, 2, 0, false), // EschaBeads
            (72, 4, 0, true), // EschaSilt
            (76, 4, 0, true), // Potpourri
            (80, 4, 0, true), // Hallmarks
            (84, 4, 0, true), // TotalHallmarks
            (88, 4, 0, true), // BadgesOfGallantry
            (92, 4, 0, true), // CrafterPoints
            (96, 1, 0, false), // FireCrystalsSet
            (97, 1, 0, false), // IceCrystalsSet
            (98, 1, 0, false), // WindCrystalsSet
            (99, 1, 0, false), // EarthCrystalsSet
            (100, 1, 0, false), // LightningCrystalsSet
            (101, 1, 0, false), // WaterCrystalsSet
            (102, 1, 0, false), // LightCrystalsSet
            (103, 1, 0, false), // DarkCrystalsSet
            (104, 1, 0, false), // McISr01Set
            (105, 1, 0, false), // McISr02Set
            (106, 1, 0, false), // McISr03Set
            (107, 1, 0, false), // LiquefactionSpheresSet
            (108, 1, 0, false), // IndurationSpheresSet
            (109, 1, 0, false), // DetonationSpheresSet
            (110, 1, 0, false), // ScissionSpheresSet
            (111, 1, 0, false), // ImpactionSpheresSet
            (112, 1, 0, false), // ReverberationSpheresSet
            (113, 1, 0, false), // TransfixionSpheresSet
            (114, 1, 0, false), // CompressionSpheresSet
            (115, 1, 0, false), // FusionSpheresSet
            (116, 1, 0, false), // DistortionSpheresSet
            (117, 1, 0, false), // FragmentationSpheresSet
            (118, 1, 0, false), // GravitationSpheresSet
            (119, 1, 0, false), // LightSpheresSet
            (120, 1, 0, false), // DarknessSpheresSet
            (124, 4, 0, true), // SilverAmanVouchers
            (128, 4, 0, true), // DomainPoints
            (132, 4, 0, true), // DomainPointsEarnedToday
            (136, 4, 0, true), // MogSegments
            (140, 4, 0, true), // Gallimaufry
        };
    }
}
