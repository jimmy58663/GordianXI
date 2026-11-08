// src/Gordian.App/Graphics/MogHouseFurnitureScene.cs
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Sessions;

namespace Gordian.App.Graphics
{
    /// <summary>A furnishing to draw: its model and its place in display space.</summary>
    public readonly record struct FurnitureInstance(FurnitureModel Model, Matrix4x4 World);

    /// <summary>
    /// The furniture of the character's own Mog House (#93), rebuilt every frame from the Mog Safes' placements: the retail client
    /// draws the pieces itself, the zone DAT has none of them. The piece in hand is drawn where it is held. A piece without a mesh
    /// (mannequins, the statues) is left out. A visit to another character's house still shows the character's own furniture.
    /// </summary>
    public static class MogHouseFurnitureScene
    {
        /// <summary>Scenery wholly above this is the room's ceiling, left out of the Layout's view (<see cref="ZoneTerrainRenderer.HideSceneryAbove"/>).</summary>
        public const float CeilingHeight = 4.9f;

        /// <summary>Fills <paramref name="instances"/> for the session's room; null outside a Mog House.</summary>
        public static IReadOnlyList<FurnitureInstance>? Build(CharacterSession session, ResourceManager? resources, List<FurnitureInstance> instances)
        {
            if (resources == null || session.World.MogHouseRoom == 0) return null;
            var layout = session.ActionService.Menus.MogHouseLayout;
            instances.Clear();
            foreach (var piece in layout.Pieces)
            {
                if (!layout.IsShown(piece) || layout.IsHeld(piece)) continue;
                if (resources.TryLoadFurnitureModel(piece.ItemId) is { } model && layout.TransformOf(piece) is { } world) instances.Add(new FurnitureInstance(model, world));
            }
            if (layout.Held is { } held && resources.TryLoadFurnitureModel(held.ItemId) is { } heldModel && layout.HeldTransform() is { } heldWorld)
            {
                instances.Add(new FurnitureInstance(heldModel, heldWorld));
            }
            return instances;
        }
    }
}
