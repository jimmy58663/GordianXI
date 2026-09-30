// src/Gordian.Core/Resources/Models/GearOcclusion.cs
using System.Collections.Generic;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Which pieces of a character the worn gear hides: hair under a helmet, wrist skin under sleeves, shins under
    /// greaves. Every equipped mesh declares an occludeType (<see cref="SkeletonMeshGroup.OccludeType"/>) and every piece
    /// a displayType (<see cref="SkeletonMeshPiece.DisplayType"/>); a piece is dropped when the union of the worn set's
    /// occludeTypes hides its displayType.
    /// Rule referenced from xi-tools (docs/gear/pose.md, "Hidden pieces", after xim ActorModel.isOccluded).
    /// </summary>
    public sealed class GearOcclusion
    {
        // One bit per occludeType value (a byte), so the union and the lookups are a few word operations.
        private readonly ulong[] _occludeTypes = new ulong[4];

        /// <summary>Adds a worn mesh's occludeType to the union. 0 and the self-markers 0x11/0x21/0x31 hide nothing.</summary>
        public void Add(byte occludeType) => _occludeTypes[occludeType >> 6] |= 1UL << (occludeType & 63);

        /// <summary>The union of every mesh's occludeType in <paramref name="meshes"/>.</summary>
        public static GearOcclusion From(IEnumerable<SkeletonMeshGroup> meshes)
        {
            var occlusion = new GearOcclusion();
            foreach (var mesh in meshes) occlusion.Add(mesh.OccludeType);
            return occlusion;
        }

        public bool Contains(byte occludeType) => (_occludeTypes[occludeType >> 6] & (1UL << (occludeType & 63))) != 0;

        /// <summary>Whether a piece of this displayType is hidden by the worn set.</summary>
        public bool Hides(byte displayType) => displayType switch
        {
            1 => Contains(0x02) || Contains(0x03) || Contains(0x04) || Contains(0x05) || Contains(0x06), // hair
            2 or 3 => Contains(0x04) || Contains(0x05) || Contains(0x06),                              // hair
            4 => Contains(0x05),                                                                        // face
            5 => Contains(0x12),                                                                        // wrist
            6 => Contains(0x32),                                                                        // pants
            7 => Contains(0x22),                                                                        // shins
            _ => false
        };

        /// <summary>Removes the pieces the worn set hides from <paramref name="mesh"/>; returns how many were removed.</summary>
        public int RemoveHiddenPieces(SkeletonMeshGroup mesh) => mesh.Pieces.RemoveAll(p => Hides(p.DisplayType));
    }
}
