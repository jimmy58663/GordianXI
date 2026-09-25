// src/Gordian.Core/Resources/Ui/UiResourceLibrary.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// The stock 2D UI resources: menu layouts (Section 0x30), element groups (Section 0x31) and their textures,
    /// merged from the localized menu DAT, the base menu DAT and the active window skin.
    /// <para>
    /// Sources (file ids resolved through FTABLE/VTABLE): the English menu DAT 39542 (ROM/119/51), the base
    /// (Japanese) menu DAT 1 (ROM/0/1), which also carries fonts the localized DAT omits, and the eight window
    /// skins 14-21 (ROM/0/14-21: textures newtex, hfr1, corner, vfr1). File ids referenced from xi-tools
    /// (https://github.com/vekien/xi-tools, docs/ui/export.md, docs/ui/list.md).
    /// </para>
    /// Textures decode lazily on first use (the menu DATs carry a 1024 x 2048 kanji sheet).
    /// </summary>
    public sealed class UiResourceLibrary
    {
        public const int EnglishMenuFileId = 39542;
        public const int BaseMenuFileId = 1;
        public const int FirstWindowSkinFileId = 14;
        public const int WindowSkinCount = 8;

        /// <summary>Layout space the menus are authored in (the PS2 frame).</summary>
        public const int LayoutWidth = 512;
        public const int LayoutHeight = 448;

        private readonly Dictionary<string, UiElementGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UiMenuDefinition> _menus = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReadOnlyMemory<byte>> _texturePayloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DecodedTexture?> _decodedTextures = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _textureLock = new();

        /// <summary>The window skin (1-8) whose frame textures override the menu DATs'.</summary>
        public int WindowSkin { get; private set; }

        public IReadOnlyDictionary<string, UiElementGroup> Groups => _groups;
        public IReadOnlyDictionary<string, UiMenuDefinition> Menus => _menus;

        /// <summary>Names (trimmed 8-character texture names) of every texture available.</summary>
        public IEnumerable<string> TextureNames => _texturePayloads.Keys;

        /// <summary>
        /// Loads the UI resources through the file table. Returns null when no menu DAT is found.
        /// </summary>
        public static UiResourceLibrary? Load(ResourceManager resources, int windowSkin = 1)
        {
            var english = resources.LoadDatBytesByFileId(EnglishMenuFileId);
            var baseMenu = resources.LoadDatBytesByFileId(BaseMenuFileId);
            if (english == null && baseMenu == null)
            {
                GordianLog.Warning("UI", "No menu DAT found (file ids 39542, 1); stock UI unavailable.");
                return null;
            }

            int skin = Math.Clamp(windowSkin, 1, WindowSkinCount);
            var skinBytes = resources.LoadDatBytesByFileId(FirstWindowSkinFileId + skin - 1);
            var library = FromDats(english, baseMenu, skinBytes);
            library.WindowSkin = skin;
            GordianLog.Info("UI", $"Stock UI resources: {library._menus.Count} menus, {library._groups.Count} element groups, " +
                                  $"{library._texturePayloads.Count} textures (window skin {skin}).");
            return library;
        }

        /// <summary>
        /// Builds a library from raw DAT bytes, in priority order: the window skin's textures, then the localized
        /// menu DAT, then the base menu DAT (first definition of a name wins).
        /// </summary>
        public static UiResourceLibrary FromDats(byte[]? localizedMenu, byte[]? baseMenu, byte[]? windowSkin)
        {
            var library = new UiResourceLibrary();
            if (windowSkin != null) library.AddDat(windowSkin);
            if (localizedMenu != null) library.AddDat(localizedMenu);
            if (baseMenu != null) library.AddDat(baseMenu);
            return library;
        }

        private void AddDat(byte[] dat)
        {
            var memory = new ReadOnlyMemory<byte>(dat);
            foreach (var header in DatSectionWalker.ReadHeaders(dat))
            {
                if (header.DataOffset + header.DataSizeBytes > dat.Length) continue;
                var payload = memory.Slice(header.DataOffset, header.DataSizeBytes);

                switch (header.TypeCode)
                {
                    case DatSectionType.Texture:
                        if (payload.Length > 17)
                        {
                            string id = UiElementGroupDecoder.ReadResourceId(payload.Span.Slice(1, UiElementGroupDecoder.ResourceIdLength));
                            UiElementGroupDecoder.SplitResourceId(id, out _, out string name);
                            if (name.Length > 0) _texturePayloads.TryAdd(name, payload);
                        }
                        break;

                    case DatSectionType.UiElementGroup:
                        var group = UiElementGroupDecoder.Decode(payload.Span, header.DatId);
                        if (group != null && group.Name.Length > 0) _groups.TryAdd(group.Name, group);
                        break;

                    case DatSectionType.UiMenu:
                        var menu = UiMenuDecoder.Decode(payload.Span, header.DatId);
                        if (menu != null && menu.Name.Length > 0) _menus.TryAdd(menu.Name, menu);
                        break;
                }
            }
        }

        /// <summary>
        /// Finds an element group by name or 16-character resource id. Menus name localized groups without their
        /// language suffix: "frames" means the English "framesus" (the base DAT's own "frames" is the Japanese
        /// set, with fewer images), so the "us" variant wins when it exists.
        /// </summary>
        public bool TryGetGroup(string nameOrResourceId, out UiElementGroup group)
        {
            string name = TrimResourceName(nameOrResourceId);
            if (name.Length <= 6 && _groups.TryGetValue(name + "us", out group!)) return true;
            return _groups.TryGetValue(name, out group!);
        }

        public bool TryGetMenu(string nameOrResourceId, out UiMenuDefinition menu) =>
            _menus.TryGetValue(TrimResourceName(nameOrResourceId), out menu!);

        /// <summary>
        /// Resolves the image a shape reference names, if its group and index exist.
        /// </summary>
        public bool TryGetImage(UiShapeReference shape, out UiImage image)
        {
            image = null!;
            if (!TryGetGroup(shape.GroupId, out var group) || shape.ImageIndex >= group.Images.Count) return false;
            image = group.Images[shape.ImageIndex];
            return true;
        }

        /// <summary>
        /// Returns a texture by name or 16-character resource id (case-insensitive), decoding it on first use.
        /// </summary>
        public bool TryGetTexture(string nameOrResourceId, out DecodedTexture texture)
        {
            string name = TrimResourceName(nameOrResourceId);
            lock (_textureLock)
            {
                if (!_decodedTextures.TryGetValue(name, out var decoded))
                {
                    decoded = _texturePayloads.TryGetValue(name, out var payload) ? TextureDecoder.DecodeTexture(payload.Span) : null;
                    _decodedTextures[name] = decoded;
                }
                texture = decoded!;
                return decoded != null;
            }
        }

        /// <summary>
        /// Reduces a 16-character resource id ("menu    windowps") to its trimmed name; plain names pass through.
        /// </summary>
        public static string TrimResourceName(string nameOrResourceId)
        {
            if (nameOrResourceId.Length > 8 && nameOrResourceId.Length <= 16 && nameOrResourceId[7] == ' ')
            {
                return nameOrResourceId.Substring(8).Trim();
            }
            return nameOrResourceId.Trim();
        }
    }
}
