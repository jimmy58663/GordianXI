// src/Gordian.Core/Ui/Lobby/LobbyPreview.cs
using System.Numerics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.World;

namespace Gordian.Core.Ui.Lobby
{
    /// <summary>
    /// The lobby's 3D preview: one player entity built from a character's look, standing at the origin of a world of
    /// its own (no zone), turned toward the preview camera. The model comes through the same path as any player in the
    /// world (<c>ResourceManager.TryLoadEntityModel</c>: GrapIDTbl[0] = (race &lt;&lt; 8) | face, then the gear).
    /// </summary>
    public sealed class LobbyPreview
    {
        /// <summary>The preview entity's id (no server id can collide: the world holds nothing else).</summary>
        public const uint EntityId = 0x00FF_FFFE;

        /// <summary>Heading byte 192: the model faces display +Z, where the preview camera stands.</summary>
        public const byte FacingCamera = 192;

        private string _key = string.Empty;

        public WorldState World { get; } = new();

        /// <summary>The entity on show, or null when nothing is previewed.</summary>
        public WorldEntity? Entity { get; private set; }

        /// <summary>Shows a listed character (its race, face and the gear the list carries).</summary>
        public void Show(LobbyCharacter? character)
        {
            if (character == null || character.IsEmpty)
            {
                Clear();
                return;
            }
            var grap = new ushort[9];
            grap[0] = character.FaceModel;
            var equipment = character.Equipment;
            for (int i = 0; i < LobbyEquipment.Count; i++) grap[i + 1] = equipment[i];
            grap[8] = 0x8000;
            Show($"c:{character.ContentId}:{string.Join(',', grap)}", grap, character.Name, character.Size);
        }

        /// <summary>Shows a character being created (race, face, hair; no gear).</summary>
        public void Show(in LobbyCharacterCreation creation)
        {
            var grap = new ushort[9];
            grap[0] = (ushort)((creation.Race << 8) | creation.CombinedFace);
            for (int slot = 1; slot <= 8; slot++) grap[slot] = (ushort)(slot << 12);
            Show($"n:{grap[0]}:{creation.Size}", grap, creation.Name, creation.Size);
        }

        private void Show(string key, ushort[] grap, string name, byte size)
        {
            if (key == _key && Entity != null) return;
            _key = key;
            var entity = new WorldEntity(EntityId, 0, EntityType.Player)
            {
                Name = name,
                Direction = FacingCamera,
                IsSpawned = true,
                // Alive: an entity with 0 % HP plays its death pose (AnimationStateClassifier); the lobby shows the idle stance.
                Hpp = 100,
                GraphSize = size,
            };
            entity.Appearance.GrapIdTable = grap;
            entity.Appearance.ModelId = 0;
            entity.Position = Vector3.Zero;
            World.Clear();
            World.UpsertEntity(entity);
            Entity = entity;
        }

        public void Clear()
        {
            if (Entity == null) return;
            _key = string.Empty;
            Entity = null;
            World.Clear();
        }
    }
}
