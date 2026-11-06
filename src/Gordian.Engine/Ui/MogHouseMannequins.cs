// src/Gordian.Engine/Ui/MogHouseMannequins.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The mannequins standing in the Mog House (#93). A mannequin is a character model, not a furniture mesh, so each placed one is a
    /// client-side entity, as the room's door is: no name (nothing targets it), the race of its item, what it wears from S2C 0x026
    /// (LandSandBoat sends that at zone-in and after 0x03B). Faces are the mannequins' own model 31 (LandSandBoat's note in
    /// <c>0x026_item_subcontainer.cpp</c>). While its window is open it spins, as in the maintainer's image-49.
    /// </summary>
    public sealed class MogHouseMannequins
    {
        /// <summary>The entity ids: never a server's (the room's door is 0x7FFF0720).</summary>
        private const uint FirstServerId = 0x7FFF0800;
        private const ushort FirstTargetIndex = 0x7E00;

        /// <summary>The mannequins' face and hair model (LandSandBoat <c>0x026_item_subcontainer.cpp</c>).</summary>
        public const byte FaceModel = 31;

        /// <summary>One turn every few seconds (<b>provisional</b>: image-49 is a still).</summary>
        public const float SpinSecondsPerTurn = 6f;

        /// <summary>A mannequin at rotation 0 faces the door: wire direction 64, south (<b>provisional</b>).</summary>
        private const byte FacingAtRotationZero = 64;

        private readonly WorldState _world;
        private readonly InventoryState _inventory;
        private readonly MogHouseLayoutState _layout;
        private readonly Dictionary<uint, WorldEntity> _shown = new();
        private readonly object _sync = new();
        private float _spin;

        public MogHouseMannequins(WorldState world, InventoryState inventory, MogHouseLayoutState layout)
        {
            _world = world;
            _inventory = inventory;
            _layout = layout;
        }

        private static uint IdOf(FurniturePlacement piece) => FirstServerId + (piece.Container == ContainerId.MogSafe2 ? 0x100u : 0u) + piece.Slot;

        /// <summary>The race of a mannequin item: 256-263 are races 1-8 (Hume male ... Galka).</summary>
        public static byte RaceOf(ushort itemId) => (byte)(itemId - 255);

        /// <summary>Makes the room's mannequin entities match the Mog Safes; none outside a Mog House.</summary>
        public void Sync(bool inRoom)
        {
            lock (_sync)
            {
                var wanted = new HashSet<uint>();
                if (inRoom)
                {
                    foreach (var piece in _layout.Pieces)
                    {
                        if (!MogHouseFurniture.IsMannequin(piece.ItemId) || !_layout.IsShown(piece)) continue;
                        var world = _layout.IsHeld(piece) ? _layout.HeldTransform() : _layout.TransformOf(piece);
                        if (world is not { } transform) continue;
                        uint id = IdOf(piece);
                        wanted.Add(id);
                        Place(id, piece, transform.Translation, _layout.IsHeld(piece) ? _layout.Rotation : piece.Rotation);
                    }
                    if (_layout.Held is { Installed: false } held && MogHouseFurniture.IsMannequin(held.ItemId) && _layout.HeldTransform() is { } hand)
                    {
                        wanted.Add(IdOf(held));
                        Place(IdOf(held), held, hand.Translation, _layout.Rotation);
                    }
                }
                foreach (uint id in new List<uint>(_shown.Keys))
                {
                    if (wanted.Contains(id)) continue;
                    _shown.Remove(id);
                    _world.RemoveEntity(id);
                }
            }
        }

        private void Place(uint id, FurniturePlacement piece, Vector3 display, int rotation)
        {
            if (!_shown.TryGetValue(id, out var entity) || !_world.TryGetByServerId(id, out _))
            {
                entity = new WorldEntity(id, (ushort)(FirstTargetIndex + (id - FirstServerId)), EntityType.Npc) { Hpp = 100, IsSpawned = true };
                _shown[id] = entity;
                _world.UpsertEntity(entity);
            }
            entity.Position = new Vector3(-display.X, -display.Y, display.Z);
            entity.Direction = (byte)(FacingAtRotationZero + rotation * 64);
            var look = _inventory.GetMannequinLook(piece.Container, piece.Slot);
            byte race = look is { Race: > 0 } known ? known.Race : RaceOf(piece.ItemId);
            entity.Appearance.CopyFrom(new[]
            {
                (ushort)((race << 8) | FaceModel),
                look?.Head ?? (ushort)0x1000, look?.Body ?? (ushort)0x2000, look?.Hands ?? (ushort)0x3000, look?.Legs ?? (ushort)0x4000, look?.Feet ?? (ushort)0x5000,
                look?.Main ?? (ushort)0x6000, look?.Sub ?? (ushort)0x7000, look?.Range ?? (ushort)0x8000,
            });
        }

        /// <summary>Turns the mannequin whose window is open.</summary>
        public void Spin(FurniturePlacement? mannequin, float seconds)
        {
            if (mannequin is not { } piece) return;
            lock (_sync)
            {
                if (!_shown.TryGetValue(IdOf(piece), out var entity)) return;
                _spin += seconds / SpinSecondsPerTurn * 256f;
                int steps = (int)_spin;
                _spin -= steps;
                entity.Direction = (byte)(entity.Direction + steps);
            }
        }
    }
}
