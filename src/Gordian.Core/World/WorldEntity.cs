// src/Gordian.Core/World/WorldEntity.cs
using System;
using System.Numerics;
using Gordian.Core.Animation;
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

    public enum LocomotionDirection : byte
    {
        Forward = 0,
        Backward = 1,
        Left = 2,
        Right = 3
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

        private Vector3 _position;
        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                if (StartPosition == Vector3.Zero && TargetPosition == Vector3.Zero)
                {
                    StartPosition = value;
                    _targetPosition = value;
                }
            }
        }

        public Vector3 StartPosition { get; set; }

        private Vector3 _targetPosition;
        public Vector3 TargetPosition
        {
            get => _targetPosition;
            set
            {
                if (_targetPosition != value)
                {
                    StartPosition = Position;
                    _targetPosition = value;
                    InterpolationElapsed = 0f;
                }
            }
        }

        public float InterpolationDuration { get; set; } = 1.35f;
        public float InterpolationElapsed { get; set; }
        public float RenderHeadingRadians { get; set; }
        public LocomotionDirection LocomotionDirection { get; set; } = LocomotionDirection.Forward;

        /// <summary>
        /// Smoothly interpolates the entity's current render position towards its target network position
        /// at constant uniform velocity across the network broadcast interval (~1.35s) with extrapolation grace,
        /// and smoothly aligns visual heading with the direction of travel.
        /// </summary>
        public void InterpolatePosition(float deltaSeconds)
        {
            float distToTarget = Vector3.Distance(Position, TargetPosition);
            if (distToTarget <= 0.001f && Speed == 0)
            {
                Position = TargetPosition;
                UpdateHeading(deltaSeconds);
                return;
            }

            InterpolationElapsed += deltaSeconds;
            float duration = Math.Max(0.05f, InterpolationDuration);
            float t = InterpolationElapsed / duration;

            if (t <= 1.0f || LastMovTime <= 1 || Type != EntityType.Player)
            {
                t = Math.Clamp(t, 0f, 1f);
                // Smooth constant-velocity glide from StartPosition to TargetPosition across the full tick interval
                Position = Vector3.Lerp(StartPosition, TargetPosition, t);
            }
            else
            {
                // Predictive dead-reckoning extrapolation: for remote players actively running (LastMovTime > 1),
                // continue coasting forward along the travel vector at authentic speed for up to 400ms
                // so the character never pauses/hitches if the next server packet is slightly delayed.
                float extraSeconds = InterpolationElapsed - duration;
                if (extraSeconds <= 0.40f)
                {
                    Vector3 travelDir = TargetPosition - StartPosition;
                    float travelDist = travelDir.Length();
                    if (travelDist > 0.01f)
                    {
                        Vector3 dir = travelDir / travelDist;
                        float fallbackSpeed = LocomotionDirection == LocomotionDirection.Backward ? 2.5f : 5.0f;
                        float speedYalms = Speed > 0 ? (Speed / 10.0f) : fallbackSpeed;
                        Position = TargetPosition + dir * (speedYalms * extraSeconds);
                    }
                }
            }

            UpdateHeading(deltaSeconds);
        }

        private void UpdateHeading(float deltaSeconds)
        {
            // For remote players actively moving towards a target destination, orient facing along the forward movement travel vector.
            // Using TargetPosition - StartPosition ensures the heading stays facing forward even during predictive
            // extrapolation when Position travels past TargetPosition.
            // For NPCs and monsters, the server is the absolute authority on facing rotation in every 0x00E packet;
            // preserving wire Direction prevents backward flips and ensures custom poses (e.g. Uragnite in-shell) face correctly.
            if (Type == EntityType.Player)
            {
                Vector3 travel = TargetPosition - StartPosition;
                float flatDistSq = (travel.X * travel.X) + (travel.Z * travel.Z);
                float distToTarget = Vector3.Distance(Position, TargetPosition);
                if (flatDistSq > 0.0025f && distToTarget > 0.05f && (Speed > 0 || LastMovTime > 1))
                {
                    if (AnimationState != 1 && ClaimServerId == 0)
                    {
                        float moveAngleRad = MathF.Atan2(travel.Z, travel.X);
                        if (moveAngleRad < 0f) moveAngleRad += MathF.PI * 2.0f;
                        Direction = (byte)Math.Round((moveAngleRad / (MathF.PI * 2.0f)) * 256.0f);
                        LocomotionDirection = LocomotionDirection.Forward;
                    }
                    else
                    {
                        float moveAngleRad = MathF.Atan2(travel.Z, travel.X);
                        float diffRad = moveAngleRad - HeadingRadians;
                        while (diffRad > MathF.PI) diffRad -= MathF.PI * 2.0f;
                        while (diffRad < -MathF.PI) diffRad += MathF.PI * 2.0f;

                        if (MathF.Abs(diffRad) >= (3.0f * MathF.PI / 4.0f))
                        {
                            LocomotionDirection = LocomotionDirection.Backward;
                        }
                        else if (diffRad > (MathF.PI / 4.0f))
                        {
                            LocomotionDirection = LocomotionDirection.Right;
                        }
                        else if (diffRad < -(MathF.PI / 4.0f))
                        {
                            LocomotionDirection = LocomotionDirection.Left;
                        }
                        else
                        {
                            LocomotionDirection = LocomotionDirection.Forward;
                        }
                    }
                }
                else if (distToTarget <= 0.05f && LastMovTime <= 1)
                {
                    LocomotionDirection = LocomotionDirection.Forward;
                }
            }

            // Smoothly rotate visual heading towards target heading
            float targetHeadingRad = HeadingRadians;
            float diff = targetHeadingRad - RenderHeadingRadians;
            while (diff > MathF.PI) diff -= MathF.PI * 2.0f;
            while (diff < -MathF.PI) diff += 2.0f * MathF.PI;
            RenderHeadingRadians += diff * Math.Min(1.0f, deltaSeconds * 15.0f);
        }

        public byte Direction { get; set; }
        public float HeadingRadians => (Direction / 256.0f) * MathF.PI * 2.0f;

        public byte Speed { get; set; }
        public byte SpeedBase { get; set; }
        public ushort LastMovTime { get; set; }
        public byte AnimationState { get; set; }
        public byte AnimationSub { get; set; }
        public byte Hpp { get; set; }
        public uint ClaimServerId { get; set; }

        public EntityAppearance Appearance { get; } = new EntityAppearance();
        public EntityAnimationState Animation { get; } = new EntityAnimationState();

        public bool IsSpawned { get; set; } = true;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastPositionChangeUtc { get; set; } = DateTime.MinValue;

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
