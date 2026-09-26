// src/Gordian.Core/Resources/Ui/StatusIconLibrary.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// Status-effect icons from the status resource DAT (file id 87, ROM/119/57): 640 fixed 0x1800-byte records, one
    /// per status-effect id in id order (record 40, Protect, holds "sts_icon protes_32"). Each record embeds its
    /// 32 x 32 icon at +0x280 in the same layout as item icons (u32 size, then a Section 0x20-style texture).
    /// Record layout derived from the retail data; the icon layout follows the item DAT format referenced from
    /// xi-tools (https://github.com/vekien/xi-tools).
    /// </summary>
    public sealed class StatusIconLibrary
    {
        public const int FileId = 87;
        public const int RecordSize = 0x1800;
        public const int IconOffset = 0x280;

        private readonly byte[] _dat;
        private readonly Dictionary<int, DecodedTexture?> _icons = new();
        private readonly object _lock = new();

        public int Count => _dat.Length / RecordSize;

        public StatusIconLibrary(byte[] dat)
        {
            _dat = dat ?? throw new ArgumentNullException(nameof(dat));
        }

        public static StatusIconLibrary? Load(ResourceManager resources)
        {
            var dat = resources.LoadDatBytesByFileId(FileId);
            return dat != null && dat.Length >= RecordSize ? new StatusIconLibrary(dat) : null;
        }

        /// <summary>
        /// Returns the icon for a status-effect id, decoding it on first use.
        /// </summary>
        public bool TryGetIcon(int statusId, out DecodedTexture icon)
        {
            icon = null!;
            if (statusId < 0 || statusId >= Count) return false;
            lock (_lock)
            {
                if (!_icons.TryGetValue(statusId, out var decoded))
                {
                    var record = _dat.AsSpan(statusId * RecordSize, RecordSize);
                    var pixels = ItemTableDecoder.DecodeEmbeddedIcon(record.Slice(IconOffset));
                    decoded = pixels != null ? new DecodedTexture($"status{statusId}", 32, 32, pixels) : null;
                    _icons[statusId] = decoded;
                }
                icon = decoded!;
                return decoded != null;
            }
        }
    }
}
