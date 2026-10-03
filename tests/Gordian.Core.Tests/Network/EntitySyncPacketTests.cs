// tests/Gordian.Core.Tests/Network/EntitySyncPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// S2C 0x067 / 0x068 (char sync, entity rename, pet sync). The payloads are laid out the way LandSandBoat's
    /// <c>char_sync.cpp</c>, <c>entity_set_name.cpp</c> and <c>pet_sync.cpp</c> write them (packet offset minus 4).
    /// </summary>
    public class EntitySyncPacketTests
    {
        private const uint LocalId = 0x01000001;
        private const ushort LocalIndex = 0x123;

        private static ushort ModeWord(int mode, int length) => (ushort)(mode | (length << 6));

        /// <summary>LandSandBoat <c>CCharSyncPacket</c>: 0x28 bytes, so a 36 byte payload.</summary>
        private static byte[] CharSync(uint id, ushort index, uint nameFlags = 0, byte levelCap = 0, byte jobLevel = 75,
            ushort mount = 0, bool mogExpansion = false, uint custom0 = 0, uint custom1 = 0)
        {
            var p = new byte[36];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), ModeWord(2, 36));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2, 2), index);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), id);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12, 4), nameFlags);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(15, 2), mount); // packet 0x13, over NameFlags' top byte
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20, 4), custom0);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(24, 4), custom1);
            p[33] = jobLevel;
            p[34] = levelCap;
            p[35] = (byte)(mogExpansion ? 1 : 0);
            return p;
        }

        /// <summary>LandSandBoat <c>CEntitySetNamePacket</c>: 0x2C bytes, the name at packet 0x18.</summary>
        private static byte[] Rename(uint id, ushort index, ushort owner, string name, int nameAt = 20)
        {
            var p = new byte[24 + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), ModeWord(3, 0x18 + name.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2, 2), index);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), id);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), owner);
            p[12] = 0x04; // NameFlags, packet 0x10
            Encoding.ASCII.GetBytes(name).CopyTo(p.AsSpan(nameAt));
            return p;
        }

        /// <summary>LandSandBoat <c>CPetSyncPacket</c>: the owner in the common fields, the pet's index at packet 0x0C.</summary>
        private static byte[] PetSync(uint ownerId, ushort ownerIndex, ushort petIndex, byte hpp = 0, byte mpp = 0, ushort tp = 0, uint target = 0, string name = "")
        {
            var p = new byte[name.Length > 0 ? 40 : 24];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), ModeWord(4, 0x18 + name.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2, 2), ownerIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), ownerId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), petIndex);
            p[10] = hpp;
            p[11] = mpp;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), tp);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16, 4), target);
            Encoding.ASCII.GetBytes(name).CopyTo(p.AsSpan(20));
            return p;
        }

        [Fact]
        public void CharSync_DecodesEveryField()
        {
            var packet = new S2C_0x067_EntityUpdate1(CharSync(LocalId, LocalIndex, nameFlags: 0x04, levelCap: 37, jobLevel: 75,
                mount: 5, mogExpansion: true, custom0: 0xAABBCCDD, custom1: 0x11223344));

            Assert.True(packet.IsValid);
            var sync = packet.Sync;
            Assert.Equal(EntitySyncMode.Player, sync.Mode);
            Assert.Equal(36, sync.Length);
            Assert.Equal(LocalIndex, sync.ActIndex);
            Assert.Equal(LocalId, sync.UniqueNo);
            Assert.True(sync.IsLevelSynced);
            Assert.False(sync.IsCampaign);
            Assert.Equal(37, sync.LevelRestriction);
            Assert.Equal(75, sync.MainJobLevel);
            Assert.Equal(1, sync.MogExpansionFlag);
            Assert.Equal(0xAABBCCDDu, sync.CustomProperties);
            Assert.Equal(0x11223344u, sync.CustomProperties2);
            Assert.Equal(5, sync.MountWord & 0xFF);
        }

        [Fact]
        public void CharSync_CampaignFlagIsBit1()
        {
            var sync = new S2C_0x067_EntityUpdate1(CharSync(LocalId, LocalIndex, nameFlags: 0x02)).Sync;
            Assert.True(sync.IsCampaign);
            Assert.False(sync.IsLevelSynced);
        }

        [Fact]
        public void Rename_ReadsTheNameAtLandSandBoatsOffset()
        {
            var sync = new S2C_0x067_EntityUpdate1(Rename(0x0100_0700, 0x700, 0x123, "Valaineral")).Sync;

            Assert.Equal(EntitySyncMode.Npc, sync.Mode);
            Assert.Equal(0x0100_0700u, sync.UniqueNo);
            Assert.Equal(0x700, sync.ActIndex);
            Assert.Equal(0x123, sync.SecondIndex);
            Assert.Equal(0x04u, sync.NameFlags);
            Assert.Equal("Valaineral", sync.Name);
        }

        [Fact]
        public void Rename_ReadsTheNameAtXiPacketsOffset()
        {
            var sync = new S2C_0x067_EntityUpdate1(Rename(0x0100_0700, 0x700, 0, "Chocobo", nameAt: 16)).Sync;
            Assert.Equal("Chocobo", sync.Name);
        }

        [Fact]
        public void Rename_WithoutAName_IsEmpty()
        {
            var sync = new S2C_0x067_EntityUpdate1(Rename(0x0100_0700, 0x700, 0x123, string.Empty)).Sync;
            Assert.True(sync.IsValid);
            Assert.Equal(string.Empty, sync.Name);
        }

        [Fact]
        public void PetSync_DecodesVitalsTargetAndName()
        {
            var sync = new S2C_0x068_EntityUpdate2(PetSync(LocalId, LocalIndex, 0x700, hpp: 80, mpp: 55, tp: 1500, target: 0x01000AAA, name: "Carbuncle")).Sync;

            Assert.Equal(EntitySyncMode.Pet, sync.Mode);
            Assert.Equal(LocalIndex, sync.ActIndex);
            Assert.Equal(LocalId, sync.UniqueNo);
            Assert.Equal(0x700, sync.SecondIndex);
            Assert.Equal(80, sync.Hpp);
            Assert.Equal(55, sync.Mpp);
            Assert.Equal(1500u, sync.Tp);
            Assert.Equal(0x01000AAAu, sync.TargetId);
            Assert.Equal("Carbuncle", sync.Name);
        }

        [Fact]
        public void PetSync_WithoutAPet_HasPetIndexZero()
        {
            var sync = new S2C_0x068_EntityUpdate2(PetSync(LocalId, LocalIndex, 0)).Sync;
            Assert.True(sync.IsValid);
            Assert.Equal(0, sync.SecondIndex);
            Assert.Equal(string.Empty, sync.Name);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(35)]
        public void CharSync_ShortPayloadIsInvalid(int length)
        {
            Assert.False(new S2C_0x067_EntityUpdate1(CharSync(LocalId, LocalIndex).AsSpan(0, length)).IsValid);
        }

        [Fact]
        public void UnknownMode_IsInvalid()
        {
            var p = new byte[40];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0, 2), ModeWord(7, 36));
            Assert.False(new S2C_0x067_EntityUpdate1(p).IsValid);
        }

        // ---- applying the packets ----

        private sealed class Fixture
        {
            public WorldState World { get; } = new();
            public LocalPlayerState Local { get; } = new() { ServerId = LocalId };
            public EntityPacketModule Module { get; }
            public PacketDispatcher Dispatcher { get; } = new();

            public Fixture()
            {
                Module = new EntityPacketModule(World, Local, (_, _) => Task.CompletedTask);
                Module.Register(Dispatcher);
                World.UpsertEntity(new PlayerEntity(LocalId, LocalIndex) { Name = "Me" });
            }

            public void Send(ushort id, byte[] payload) => Assert.True(Dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));
        }

        [Fact]
        public void CharSyncForTheLocalPlayer_FillsLocalCharSync()
        {
            var f = new Fixture();
            int raised = 0;
            f.Local.CharSyncUpdated += () => raised++;

            f.Send(0x067, CharSync(LocalId, LocalIndex, nameFlags: 0x04, levelCap: 37, mount: 3, mogExpansion: true));

            Assert.True(f.Local.IsLevelSynced);
            Assert.Equal(37, f.Local.CharSync.LevelRestriction);
            Assert.Equal(3, f.Local.CharSync.MountWord & 0xFF);
            Assert.True(f.Local.CharSync.MogExpansionUnlocked);
            Assert.Equal(1, raised);

            // The same data again changes nothing.
            f.Send(0x067, CharSync(LocalId, LocalIndex, nameFlags: 0x04, levelCap: 37, mount: 3, mogExpansion: true));
            Assert.Equal(1, raised);

            // Level Sync ending clears it.
            f.Send(0x067, CharSync(LocalId, LocalIndex));
            Assert.False(f.Local.IsLevelSynced);
            Assert.Equal(2, raised);
        }

        [Fact]
        public void CharSyncForAnotherPlayer_OnlyStoresTheirNameFlags()
        {
            var f = new Fixture();
            f.World.UpsertEntity(new PlayerEntity(0x01000099, 0x44) { Name = "Other" });

            f.Send(0x067, CharSync(0x01000099, 0x44, nameFlags: 0x02));

            Assert.False(f.Local.IsLevelSynced);
            Assert.True(f.World.TryGetByServerId(0x01000099, out var other));
            Assert.Equal(0x02u, other!.SyncNameFlags);
        }

        [Fact]
        public void RenameOfATrust_SetsNameOwnerAndType()
        {
            var f = new Fixture();
            f.World.UpsertEntity(new WorldEntity(0x01000700, 0x700, EntityType.Monster) { Name = "Valaineral_R" });

            f.Send(0x067, Rename(0x01000700, 0x700, LocalIndex, "Valaineral_R_Dv"));

            Assert.True(f.World.TryGetByServerId(0x01000700, out var trust));
            Assert.Equal("Valaineral R Dv", trust!.Name);
            Assert.Equal(LocalIndex, trust.OwnerTargetIndex);
            Assert.Equal(EntityType.Trust, trust.Type);
        }

        [Fact]
        public void RenameWithoutAnOwner_KeepsTheEntityType()
        {
            var f = new Fixture();
            f.World.UpsertEntity(new WorldEntity(0x01000701, 0x701, EntityType.Npc) { Name = "Old" });

            f.Send(0x067, Rename(0x01000701, 0x701, 0, "New Name"));

            Assert.True(f.World.TryGetByServerId(0x01000701, out var npc));
            Assert.Equal("New Name", npc!.Name);
            Assert.Equal(EntityType.Npc, npc.Type);
            Assert.Equal(0, npc.OwnerTargetIndex);
        }

        [Fact]
        public void RenameOfAnUnknownEntity_IsIgnored()
        {
            var f = new Fixture();
            f.Send(0x067, Rename(0x01000999, 0x999, 0, "Ghost"));
            Assert.False(f.World.TryGetByServerId(0x01000999, out _));
        }

        [Fact]
        public void PetSync_FillsThePetStateAndLinksTheEntities()
        {
            var f = new Fixture();
            f.World.UpsertEntity(new WorldEntity(0x01000700, 0x700, EntityType.Pet) { Name = "Carbuncle", Hpp = 100 });
            int changed = 0;
            f.Module.Pet.Changed += () => changed++;

            f.Send(0x068, PetSync(LocalId, LocalIndex, 0x700, hpp: 80, mpp: 55, tp: 1500, target: 0x01000AAA, name: "Carbuncle"));

            var pet = f.Module.Pet.Current;
            Assert.NotNull(pet);
            Assert.Equal(0x01000700u, pet!.ServerId);
            Assert.Equal(0x700, pet.TargetIndex);
            Assert.Equal(LocalIndex, pet.OwnerIndex);
            Assert.Equal(80, pet.Hpp);
            Assert.Equal(55, pet.Mpp);
            Assert.Equal(1500u, pet.Tp);
            Assert.Equal(0x01000AAAu, pet.TargetServerId);
            Assert.Equal("Carbuncle", pet.Name);
            Assert.Equal(1, changed);

            Assert.True(f.World.TryGetByServerId(0x01000700, out var entity));
            Assert.Equal(LocalIndex, entity!.OwnerTargetIndex);
            Assert.Equal(80, entity.Hpp);
            Assert.True(f.World.TryGetByServerId(LocalId, out var self));
            Assert.Equal(0x700, ((PlayerEntity)self!).PetActorIndex);

            // Vitals tick down.
            f.Send(0x068, PetSync(LocalId, LocalIndex, 0x700, hpp: 60, mpp: 55, tp: 1500, name: "Carbuncle"));
            Assert.Equal(60, f.Module.Pet.Current!.Hpp);
            Assert.Equal(2, changed);
        }

        [Fact]
        public void PetSync_BeforeThePetSpawns_StillRecordsThePet_AndLinksOnSpawn()
        {
            var f = new Fixture();
            f.Send(0x068, PetSync(LocalId, LocalIndex, 0x700, hpp: 100, mpp: 100, name: "Ifrit"));

            Assert.Equal("Ifrit", f.Module.Pet.Current!.Name);
            Assert.Equal(0u, f.Module.Pet.Current.ServerId);
        }

        [Fact]
        public void PetSync_WithPetIndexZero_ClearsThePet()
        {
            var f = new Fixture();
            f.Send(0x068, PetSync(LocalId, LocalIndex, 0x700, hpp: 100, mpp: 100, name: "Ifrit"));
            Assert.True(f.Module.Pet.HasPet);

            f.Send(0x068, PetSync(LocalId, LocalIndex, 0));

            Assert.False(f.Module.Pet.HasPet);
            Assert.True(f.World.TryGetByServerId(LocalId, out var self));
            Assert.Equal(0, ((PlayerEntity)self!).PetActorIndex);
        }

        [Fact]
        public void PetSync_InXiPacketsOrder_TreatsTheFirstIndexAsThePet()
        {
            // XiPackets labels the common fields as the pet's own; a packet whose id is not the local player's reads that way.
            var f = new Fixture();
            f.World.UpsertEntity(new WorldEntity(0x01000700, 0x700, EntityType.Pet) { Name = "Avatar" });

            f.Send(0x068, PetSync(0x01000700, 0x700, LocalIndex, hpp: 90, mpp: 10, name: "Avatar"));

            var pet = f.Module.Pet.Current!;
            Assert.Equal(0x700, pet.TargetIndex);
            Assert.Equal(LocalIndex, pet.OwnerIndex);
            Assert.Equal(0x01000700u, pet.ServerId);
        }

        [Fact]
        public void Pet_SetWithSameContents_RaisesNothing()
        {
            var state = new LocalPetState();
            int changed = 0;
            state.Changed += () => changed++;
            var pet = new LocalPet(1, 2, 3, 4, 5, 6, 7, "x");

            state.Set(pet);
            state.Set(pet with { });
            state.Clear();
            state.Clear();

            Assert.Equal(2, changed);
        }

        [Fact]
        public void Vector3Position_IsUntouchedBySync()
        {
            var f = new Fixture();
            f.World.TryGetByServerId(LocalId, out var self);
            self!.Position = new Vector3(1, 2, 3);

            f.Send(0x067, CharSync(LocalId, LocalIndex, nameFlags: 0x04));

            Assert.Equal(new Vector3(1, 2, 3), self.Position);
        }
    }
}
