// src/Gordian.Core/World/WorldEntity.cs
using System;
using System.Numerics;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// High-level categorization of world entities.
    /// </summary>
    public enum EntityType : byte
    {
        Player = 0,
        Npc = 1,
        Monster = 2,
        Pet = 3,
        Trust = 4,
        Elevator = 5,
        Ship = 6,
        Door = 7
    }

    /// <summary>
    /// Visual model and equipment appearance for an entity.
    /// </summary>
    public sealed class EntityAppearance
    {
        public uint ModelId { get; set; }
        public ushort CostumeId { get; set; }
        public ushort[] GrapIdTable { get; set; } = new ushort[9];

        public ushort FaceModel => GrapIdTable[0];
        public ushort Head => GrapIdTable[1];
        public ushort Body => GrapIdTable[2];
        public ushort Hands => GrapIdTable[3];
        public ushort Legs => GrapIdTable[4];
        public ushort Feet => GrapIdTable[5];
        public ushort MainWeapon => GrapIdTable[6];
        public ushort SubWeapon => GrapIdTable[7];
        public ushort RangedWeapon => GrapIdTable[8];

        public void CopyFrom(ReadOnlySpan<ushort> grapTable)
        {
            int limit = Math.Min(grapTable.Length, GrapIdTable.Length);
            for (int i = 0; i < limit; i++)
            {
                GrapIdTable[i] = grapTable[i];
            }
        }
    }

    /// <summary>
    /// Base class representing any dynamic or static entity in the game world.
    /// </summary>
    public class WorldEntity
    {
        public uint ServerId { get; }
        public ushort TargetIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public EntityType Type { get; set; }

        public Vector3 Position { get; set; }
        public byte Direction { get; set; }
        public float HeadingRadians => (Direction / 256.0f) * MathF.PI * 2.0f;

        public byte Speed { get; set; }
        public byte SpeedBase { get; set; }
        public byte AnimationState { get; set; }
        public byte Hpp { get; set; }
        public uint ClaimServerId { get; set; }

        public EntityAppearance Appearance { get; } = new EntityAppearance();

        public bool IsSpawned { get; set; } = true;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

        public WorldEntity(uint serverId, ushort targetIndex, EntityType type)
        {
            ServerId = serverId;
            TargetIndex = targetIndex;
            Type = type;
        }

        public override string ToString()
        {
            return $"[{Type}] {Name} (ID: {ServerId}, Index: {TargetIndex}) at {Position:F1}";
        }
    }

    /// <summary>
    /// Represents another player character (PC) in the zone.
    /// </summary>
    public sealed class PlayerEntity : WorldEntity
    {
        public JobId MainJob { get; set; } = JobId.None;
        public byte MainJobLevel { get; set; }
        public JobId SubJob { get; set; } = JobId.None;
        public byte SubJobLevel { get; set; }

        public byte LsColorR { get; set; }
        public byte LsColorG { get; set; }
        public byte LsColorB { get; set; }

        public byte GmLevel { get; set; }
        public bool IsSeekingParty { get; set; }
        public bool IsAnonymous { get; set; }
        public bool IsAway { get; set; }
        public bool IsInvisible { get; set; }
        public bool HasBazaar { get; set; }
        public bool IsCharmed { get; set; }
        public bool IsMentor { get; set; }
        public bool IsNewPlayer { get; set; }

        public ushort PetActorIndex { get; set; }

        public PlayerEntity(uint serverId, ushort targetIndex)
            : base(serverId, targetIndex, EntityType.Player)
        {
        }
    }
}
