// src/Gordian.Core/Resources/ResourceManager.Furniture.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Resources
{
    public sealed partial class ResourceManager
    {
        private readonly Lazy<Dictionary<ushort, FurnitureInfo>> _furniture;
        private readonly ConcurrentDictionary<ushort, FurnitureModel?> _furnitureModels = new();

        private Dictionary<ushort, FurnitureInfo> LoadFurnitureTable()
        {
            byte[]? dat = LoadDatBytesByFileId(FurnitureTable.FileId);
            return dat == null ? new Dictionary<ushort, FurnitureInfo>() : FurnitureTable.Parse(dat);
        }

        /// <summary>A furnishing's footprint, height and placement from the client's table (#93); null for an item that is not furniture.</summary>
        public FurnitureInfo? GetFurnitureInfo(ushort itemId) => _furniture.Value.TryGetValue(itemId, out var info) ? info : null;

        /// <summary>A furnishing's mesh and textures, cached; null for a piece without a mesh file (mannequins, the statues).</summary>
        public FurnitureModel? TryLoadFurnitureModel(ushort itemId) => _furnitureModels.GetOrAdd(itemId, LoadFurnitureModel);

        private FurnitureModel? LoadFurnitureModel(ushort itemId)
        {
            int fileId = FurnitureTable.ModelFileId(itemId);
            if (fileId == 0) return null;
            EnsureKeyTables();
            byte[]? dat = LoadDatBytesByFileId(fileId);
            if (dat == null || _keyTable1 == null || !DatSectionWalker.IsSectionContainer(dat)) return null;
            var meshes = new List<MeshGroup>();
            foreach (var header in DatSectionWalker.ReadHeaders(dat))
            {
                if (header.TypeCode != DatSectionType.ZoneMesh || header.DataOffset + header.DataSizeBytes > dat.Length) continue;
                byte[] payload = dat.AsSpan(header.DataOffset, header.DataSizeBytes).ToArray();
                if (_keyTable2 != null && payload.Length >= 16) ZoneMeshDecoder.DecryptZoneMesh(payload, _keyTable1, _keyTable2);
                foreach (var submesh in ZoneMeshDecoder.ParseZoneMesh(payload))
                    meshes.Add(ZoneDefDecoder.InstantiateSubmesh(submesh, Matrix4x4.Identity, submesh.Name));
            }
            if (meshes.Count == 0) return null;
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            ZoneDataLoader.ParseZoneContainer(dat, 0, _keyTable1, _keyTable2 ?? ReadOnlySpan<byte>.Empty, textures, GetSharedEffects(),
                datSource: DecodedTexture.FileLabel(fileId), interior: true);
            GordianLog.Debug(LogCategories.Res, $"Loaded furniture {itemId} (file {fileId}: {meshes.Count} submeshes).");
            return new FurnitureModel(itemId, meshes, textures);
        }
    }
}
