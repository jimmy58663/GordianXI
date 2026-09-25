// src/Gordian.Core/Resources/Ui/UiMenuDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x30 (UiMenu) chunks.
    /// Layout (payload after the 16-byte section header):
    /// <code>
    /// +0x00  char[16]  resource id (e.g. "menu    logwindo")
    /// +0x10  u8        menu type
    /// +0x11  u8        buttonCount
    /// +0x20  frame record, then buttonCount button records
    /// </code>
    /// Every record starts with a u16 total size; its shape references (20 bytes each: u16 kind, u16 image index,
    /// char[16] element-group id) begin at record +0x20 and are followed by the help and title text ids as
    /// NUL-terminated decimal strings ("-1" = none).
    /// Record headers (FrameDefinitionHeader / ButtonDefinitionHeader field order) referenced from xi-tools
    /// (https://github.com/vekien/xi-tools, docs/ffximain/ffximain.md "Menu definition structs",
    /// src/xi/ui/xi_menu_pos.py) and xi-model-viewer (https://github.com/vekien/xi-model-viewer,
    /// ui/js/dat/inspect.js parseInspectUiMenu); the shape-reference and text-id framing was derived from the
    /// retail data (every menu in ROM/119/51.DAT decodes to within its section padding).
    /// </summary>
    public static class UiMenuDecoder
    {
        private const int RecordHeaderSize = 0x20;
        private const int ShapeReferenceSize = 4 + UiElementGroupDecoder.ResourceIdLength;

        public static UiMenuDefinition? Decode(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < 0x20 + RecordHeaderSize) return null;

            string id = UiElementGroupDecoder.ReadResourceId(payload.Slice(0, UiElementGroupDecoder.ResourceIdLength));
            byte menuType = payload[0x10];
            int buttonCount = payload[0x11];

            int p = 0x20;
            if (!TryReadRecord(payload, p, out var frameRecord)) return null;
            var frame = ReadFrame(frameRecord);
            if (frame == null) return null;
            p += frameRecord.Length;

            var buttons = new List<UiMenuButton>(buttonCount);
            for (int i = 0; i < buttonCount; i++)
            {
                if (!TryReadRecord(payload, p, out var record)) return null;
                var button = ReadButton(record);
                if (button == null) return null;
                buttons.Add(button);
                p += record.Length;
            }

            UiElementGroupDecoder.SplitResourceId(id, out string category, out string name);
            return new UiMenuDefinition
            {
                DatId = datId,
                Category = category,
                Name = name,
                MenuType = menuType,
                Frame = frame,
                Buttons = buttons,
            };
        }

        private static bool TryReadRecord(ReadOnlySpan<byte> payload, int offset, out ReadOnlySpan<byte> record)
        {
            record = default;
            if (offset + 2 > payload.Length) return false;
            int size = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset));
            if (size < RecordHeaderSize || offset + size > payload.Length) return false;
            record = payload.Slice(offset, size);
            return true;
        }

        private static UiMenuFrame? ReadFrame(ReadOnlySpan<byte> r)
        {
            int shapeCount = r[20];
            if (!TryReadTail(r, shapeCount, r[21], r[22], out var shapes, out int help, out int title)) return null;
            return new UiMenuFrame
            {
                X = I16(r, 2),
                Y = I16(r, 4),
                CursorOffsetX = I16(r, 6),
                CursorOffsetY = I16(r, 8),
                Width = I16(r, 10),
                Height = I16(r, 12),
                DrawOffsetX = I16(r, 14),
                DrawOffsetY = I16(r, 16),
                Anchor = (UiAnchor)(r[19] & 0x03),
                Shapes = shapes,
                HelpTextId = help,
                TitleTextId = title,
            };
        }

        private static UiMenuButton? ReadButton(ReadOnlySpan<byte> r)
        {
            int shapeCount = r[27];
            if (!TryReadTail(r, shapeCount, r[29], r[30], out var shapes, out int help, out int title)) return null;
            return new UiMenuButton
            {
                X = I16(r, 2),
                Y = I16(r, 4),
                CursorOffsetX = I16(r, 6),
                CursorOffsetY = I16(r, 8),
                Width = I16(r, 10),
                Height = I16(r, 12),
                SelectRectOffsetX = I16(r, 14),
                SelectRectOffsetY = I16(r, 16),
                ButtonId = I16(r, 18),
                NavUp = (sbyte)r[23],
                NavDown = (sbyte)r[24],
                NavLeft = (sbyte)r[25],
                NavRight = (sbyte)r[26],
                Shapes = shapes,
                HelpTextId = help,
                TitleTextId = title,
            };
        }

        private static bool TryReadTail(ReadOnlySpan<byte> r, int shapeCount, int helpLength, int titleLength,
            out UiShapeReference[] shapes, out int helpTextId, out int titleTextId)
        {
            shapes = Array.Empty<UiShapeReference>();
            helpTextId = titleTextId = -1;

            int p = RecordHeaderSize;
            if (p + shapeCount * ShapeReferenceSize > r.Length) return false;
            shapes = new UiShapeReference[shapeCount];
            for (int i = 0; i < shapeCount; i++)
            {
                ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(p));
                ushort index = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(p + 2));
                string group = UiElementGroupDecoder.ReadResourceId(r.Slice(p + 4, UiElementGroupDecoder.ResourceIdLength));
                shapes[i] = new UiShapeReference(kind, index, group);
                p += ShapeReferenceSize;
            }

            if (!TryReadTextId(r, ref p, helpLength, out helpTextId)) return false;
            return TryReadTextId(r, ref p, titleLength, out titleTextId);
        }

        private static bool TryReadTextId(ReadOnlySpan<byte> r, ref int p, int length, out int textId)
        {
            textId = -1;
            if (p + length + 1 > r.Length) return false;
            string text = Encoding.ASCII.GetString(r.Slice(p, length));
            p += length + 1;
            if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)) textId = value;
            return true;
        }

        private static short I16(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadInt16LittleEndian(r.Slice(offset));
    }
}
