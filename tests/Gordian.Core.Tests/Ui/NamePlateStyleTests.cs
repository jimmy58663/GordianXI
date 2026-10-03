// tests/Gordian.Core.Tests/Ui/NamePlateStyleTests.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Network.Packets;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class NamePlateStyleTests
    {
        private static readonly uint[] None = Array.Empty<uint>();

        [Fact]
        public void CharPc_DecodesNamePlateFlags()
        {
            byte[] payload = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), (1u << 11) | (1u << 12) | (1u << 17) | (1u << 31));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), 1u << 28);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), (1u << 23) | (1u << 24));
            payload[0x2F] = 0x42; // Trial | JobMaster

            var flags = new S2C_0x00D_CharPc(payload).NamePlate;

            Assert.Equal(NamePlateFlags.SeekingParty | NamePlateFlags.Anonymous | NamePlateFlags.Linkshell | NamePlateFlags.Bazaar
                | NamePlateFlags.GmIconHidden | NamePlateFlags.NewPlayer | NamePlateFlags.Mentor | NamePlateFlags.Trial
                | NamePlateFlags.JobMaster, flags);
        }

        [Fact]
        public void CharNpc_DecodesNamePlateFlags()
        {
            byte[] payload = new byte[0x50];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), 1u << 13);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), (1u << 24) | (1u << 29));

            Assert.Equal(NamePlateFlags.CalledForHelp | NamePlateFlags.InfoNpc | NamePlateFlags.NameHidden,
                new S2C_0x00E_CharNpc(payload).NamePlate);
        }

        [Fact]
        public void CharStatus_DecodesNamePlateFlagsAtItsOwnBitPositions()
        {
            byte[] payload = new byte[0x5C];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), (1u << 5) | (1u << 25) | (5u << 29));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(52, 4), (1u << 3) | (1u << 4));
            payload[0x54] = 0x80;

            var status = new S2C_0x037_CharStatus(payload);

            Assert.Equal(NamePlateFlags.Anonymous | NamePlateFlags.Linkshell | NamePlateFlags.NewPlayer | NamePlateFlags.Mentor
                | NamePlateFlags.JobMaster, status.NamePlate);
            Assert.Equal(5, status.GmLevel);
        }

        [Fact]
        public void Color_FollowsTheCapturedConventions()
        {
            const uint local = 1;
            var self = new PlayerEntity(local, 1) { Name = "Tarudrake" };
            var trust = new WorldEntity(2, 0x700, EntityType.Trust) { Name = "Selh'teus" };
            var stranger = new PlayerEntity(3, 3) { Name = "Other" };
            var npc = new WorldEntity(4, 4, EntityType.Npc) { Name = "Moogle" };
            var party = new uint[] { local, 2 };

            // The local player stays white in a party; trusts in it are cyan.
            Assert.Equal(NamePlateColor.Player, NamePlateStyle.Color(self, NamePlateFlags.None, local, party, party));
            Assert.Equal(NamePlateColor.Party, NamePlateStyle.Color(trust, NamePlateFlags.None, local, party, party));
            Assert.Equal(NamePlateColor.Anonymous, NamePlateStyle.Color(self, NamePlateFlags.Anonymous, local, party, party));
            Assert.Equal(NamePlateColor.SeekingParty, NamePlateStyle.Color(stranger, NamePlateFlags.SeekingParty | NamePlateFlags.Anonymous, local, None, None));
            Assert.Equal(NamePlateColor.Player, NamePlateStyle.Color(stranger, NamePlateFlags.None, local, None, None));
            Assert.Equal(NamePlateColor.Npc, NamePlateStyle.Color(npc, NamePlateFlags.None, local, None, None));
        }

        [Fact]
        public void Color_ClassifiesMonstersByClaim()
        {
            var mob = new WorldEntity(10, 10, EntityType.Monster) { Name = "Island Rarab" };
            var alliance = new uint[] { 1, 5 };

            Assert.Equal(NamePlateColor.UnclaimedMonster, NamePlateStyle.Color(mob, NamePlateFlags.None, 1, None, alliance));
            mob.ClaimServerId = 5;
            Assert.Equal(NamePlateColor.ClaimedByParty, NamePlateStyle.Color(mob, NamePlateFlags.None, 1, None, alliance));
            mob.ClaimServerId = 9;
            Assert.Equal(NamePlateColor.ClaimedByOther, NamePlateStyle.Color(mob, NamePlateFlags.None, 1, None, alliance));
            Assert.Equal(NamePlateColor.CalledForHelp, NamePlateStyle.Color(mob, NamePlateFlags.CalledForHelp, 1, None, alliance));
        }

        [Fact]
        public void Icon_LinkshellGivesWayToEveryOtherIcon()
        {
            Assert.Equal(NamePlateIcon.Linkshell, NamePlateStyle.Icon(NamePlateFlags.Linkshell, 0));
            Assert.Equal(NamePlateIcon.SeekingParty, NamePlateStyle.Icon(NamePlateFlags.Linkshell | NamePlateFlags.SeekingParty, 0));
            Assert.Equal(NamePlateIcon.NewPlayer, NamePlateStyle.Icon(NamePlateFlags.Linkshell | NamePlateFlags.NewPlayer, 0));
            Assert.Equal(NamePlateIcon.Info, NamePlateStyle.Icon(NamePlateFlags.InfoNpc, 0));
            Assert.Equal(NamePlateIcon.None, NamePlateStyle.Icon(NamePlateFlags.JobMaster, 0));
        }

        [Fact]
        public void Icon_ShowsGmLevelsUnlessHidden()
        {
            Assert.Equal(NamePlateIcon.Gm, NamePlateStyle.Icon(NamePlateFlags.Linkshell, 5));
            Assert.Equal(NamePlateIcon.Linkshell, NamePlateStyle.Icon(NamePlateFlags.Linkshell | NamePlateFlags.GmIconHidden, 5));
            Assert.Equal(NamePlateIcon.PlayOnline, NamePlateStyle.Icon(NamePlateFlags.None, 3));
            Assert.Equal(NamePlateIcon.Trial, NamePlateStyle.Icon(NamePlateFlags.None, 1));
        }

        [Fact]
        public void ShowsName_HidesFlaggedAndUnnamedModels()
        {
            var npc = new WorldEntity(4, 4, EntityType.Npc) { Name = "Home Point #1" };
            npc.Appearance.ModelId = 52;
            Assert.False(NamePlateStyle.ShowsName(npc, NamePlateFlags.None));

            npc.Appearance.ModelId = 1000;
            Assert.True(NamePlateStyle.ShowsName(npc, NamePlateFlags.None));
            Assert.False(NamePlateStyle.ShowsName(npc, NamePlateFlags.NameHidden));

            npc.IsHidden = true;
            Assert.False(NamePlateStyle.ShowsName(npc, NamePlateFlags.None));

            // A server-hidden cutscene NPC that takes part in the event is drawn, so it has its name (Curilla, the guards).
            npc.IsInEvent = true;
            Assert.True(NamePlateStyle.ShowsName(npc, NamePlateFlags.None));
            npc.IsInEvent = false;

            var player = new PlayerEntity(1, 1) { Name = "Tarudrake" };
            Assert.True(NamePlateStyle.ShowsName(player, NamePlateFlags.NameHidden));
        }

        /// <summary>An event's 0x92 hides an NPC's or a player's plate until it clears the flag or ends (#191).</summary>
        [Fact]
        public void ShowsName_NotWhileAnEventHidesIt()
        {
            var joachim = new WorldEntity(4, 4, EntityType.Npc) { Name = "Joachim" };
            joachim.Appearance.ModelId = 1000;
            joachim.HidesEventName = true;
            Assert.False(NamePlateStyle.ShowsName(joachim, NamePlateFlags.None));
            joachim.HidesEventName = false;
            Assert.True(NamePlateStyle.ShowsName(joachim, NamePlateFlags.None));

            var player = new PlayerEntity(1, 1) { Name = "Gemini", HidesEventName = true };
            Assert.False(NamePlateStyle.ShowsName(player, NamePlateFlags.None));
        }
    }
}
