// src/Gordian.App/ViewModels/EntityItemViewModel.cs
using System;
using System.Numerics;
using Gordian.Core.World;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// Presentation model for a world entity displayed in the State Inspector entity table.
    /// </summary>
    public sealed class EntityItemViewModel : ViewModelBase
    {
        private ushort _targetIndex;
        private uint _serverId;
        private string _name = string.Empty;
        private EntityType _type;
        private Vector3 _position;
        private float _distance;
        private byte _hpp;
        private byte _speed;
        private bool _isSpawned;

        public ushort TargetIndex
        {
            get => _targetIndex;
            private set => SetProperty(ref _targetIndex, value);
        }

        public uint ServerId
        {
            get => _serverId;
            private set => SetProperty(ref _serverId, value);
        }

        public string Name
        {
            get => _name;
            private set => SetProperty(ref _name, value);
        }

        public EntityType Type
        {
            get => _type;
            private set => SetProperty(ref _type, value);
        }

        public Vector3 Position
        {
            get => _position;
            private set => SetProperty(ref _position, value);
        }

        public float Distance
        {
            get => _distance;
            internal set
            {
                if (SetProperty(ref _distance, value))
                {
                    OnPropertyChanged(nameof(DistanceDisplay));
                }
            }
        }

        public byte Hpp
        {
            get => _hpp;
            private set
            {
                if (SetProperty(ref _hpp, value))
                {
                    OnPropertyChanged(nameof(HppDisplay));
                }
            }
        }

        public byte Speed
        {
            get => _speed;
            private set => SetProperty(ref _speed, value);
        }

        public bool IsSpawned
        {
            get => _isSpawned;
            private set => SetProperty(ref _isSpawned, value);
        }

        public string TargetIndexHex => $"0x{TargetIndex:X3}";
        public string ServerIdHex => $"0x{ServerId:X8}";
        public string PositionDisplay => $"({Position.X:F1}, {Position.Y:F1}, {Position.Z:F1})";
        public string DistanceDisplay => $"{Distance:F1}y";
        public string HppDisplay => $"{Hpp}%";
        public string TypeBadge => Type switch
        {
            EntityType.Player => "PC",
            EntityType.Npc => "NPC",
            EntityType.Monster => "MOB",
            EntityType.Pet => "PET",
            EntityType.Trust => "TRUST",
            _ => Type.ToString().ToUpperInvariant()
        };

        public string TypeColor => Type switch
        {
            EntityType.Player => "#4EC9B0",   // Cyan/Teal
            EntityType.Monster => "#F44747",  // Red
            EntityType.Npc => "#DCDCAA",      // Yellow/Gold
            EntityType.Pet => "#C586C0",      // Violet
            EntityType.Trust => "#9CDCFE",    // Light blue
            _ => "#A0A0A0"
        };

        public EntityItemViewModel(WorldEntity entity, Vector3 localPlayerPos, uint localPlayerServerId = 0)
        {
            Update(entity, localPlayerPos, localPlayerServerId);
        }

        public void Update(WorldEntity entity, Vector3 localPlayerPos, uint localPlayerServerId = 0)
        {
            TargetIndex = entity.TargetIndex;
            ServerId = entity.ServerId;
            Name = string.IsNullOrEmpty(entity.Name) ? $"<Entity 0x{entity.ServerId:X8}>" : entity.Name;
            Type = entity.Type;
            Position = entity.Position;
            Distance = (localPlayerServerId != 0 && entity.ServerId == localPlayerServerId)
                ? 0.0f
                : Vector3.Distance(localPlayerPos, entity.Position);
            Hpp = entity.Hpp;
            Speed = entity.Speed;
            IsSpawned = entity.IsSpawned;

            OnPropertyChanged(nameof(PositionDisplay));
            OnPropertyChanged(nameof(TargetIndexHex));
            OnPropertyChanged(nameof(ServerIdHex));
            OnPropertyChanged(nameof(TypeBadge));
            OnPropertyChanged(nameof(TypeColor));
        }
    }
}
