// src/Gordian.Core/Resources/Containers/DatDirectoryTree.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Containers
{
    /// <summary>
    /// Represents a single resource item within a DAT directory node.
    /// </summary>
    public sealed class DatResourceEntry
    {
        public string DatId => Header.DatId;
        public DatSectionType TypeCode => Header.TypeCode;
        public DatSectionHeader Header { get; }
        public DatDirectoryNode Parent { get; }
        public ReadOnlyMemory<byte> Payload { get; }

        public DatResourceEntry(DatSectionHeader header, DatDirectoryNode parent, ReadOnlyMemory<byte> source)
        {
            Header = header;
            Parent = parent;
            Payload = DatSectionWalker.GetSectionPayload(source, header);
        }

        public override string ToString() => $"Entry [{DatId}] Type: {TypeCode} ({Header.DataSizeBytes} bytes)";
    }

    /// <summary>
    /// Represents a directory node in a hierarchical FFXI DAT file.
    /// Manages scoped link resolution matching official client behavior (local -> parents -> global).
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xim DirectoryResource.
    /// </summary>
    public sealed class DatDirectoryNode
    {
        public string DatId { get; }
        public DatDirectoryNode? Parent { get; }

        private readonly List<DatDirectoryNode> _subDirectories = new();
        private readonly List<DatResourceEntry> _resources = new();
        private readonly Dictionary<string, List<DatResourceEntry>> _resourcesById = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<DatDirectoryNode>> _subDirectoriesById = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<DatDirectoryNode> SubDirectories => _subDirectories;
        public IReadOnlyList<DatResourceEntry> Resources => _resources;

        public DatDirectoryNode(string datId, DatDirectoryNode? parent = null)
        {
            DatId = datId;
            Parent = parent;
        }

        public void AddSubDirectory(DatDirectoryNode subDir)
        {
            ArgumentNullException.ThrowIfNull(subDir);
            _subDirectories.Add(subDir);

            if (!_subDirectoriesById.TryGetValue(subDir.DatId, out var list))
            {
                list = new List<DatDirectoryNode>();
                _subDirectoriesById[subDir.DatId] = list;
            }
            list.Add(subDir);
        }

        public void AddResource(DatResourceEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            _resources.Add(entry);

            if (!_resourcesById.TryGetValue(entry.DatId, out var list))
            {
                list = new List<DatResourceEntry>();
                _resourcesById[entry.DatId] = list;
            }
            list.Add(entry);
        }

        public DatDirectoryNode Root
        {
            get
            {
                var cur = this;
                while (cur.Parent != null)
                {
                    cur = cur.Parent;
                }
                return cur;
            }
        }

        /// <summary>
        /// Finds a direct child resource with the specified ID and optional type.
        /// </summary>
        public DatResourceEntry? GetChild(string id, DatSectionType? type = null)
        {
            if (_resourcesById.TryGetValue(id, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (type == null || item.TypeCode == type.Value)
                    {
                        return item;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Searches current directory children first, then walks up parent directories.
        /// </summary>
        public DatResourceEntry? SearchLocalAndParents(string id, DatSectionType? type = null)
        {
            for (var d = this; d != null; d = d.Parent)
            {
                var found = d.GetChild(id, type);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Searches this directory and all descendant directories recursively.
        /// </summary>
        public DatResourceEntry? GetChildRecursive(string id, DatSectionType? type = null)
        {
            var local = GetChild(id, type);
            if (local != null) return local;

            for (int i = 0; i < _subDirectories.Count; i++)
            {
                var found = _subDirectories[i].GetChildRecursive(id, type);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Searches from the root node down across the entire DAT file.
        /// </summary>
        public DatResourceEntry? FindFirstInEntireTree(string id, DatSectionType? type = null)
        {
            return Root.GetChildRecursive(id, type);
        }

        /// <summary>
        /// Collects all direct child resources matching the given type code.
        /// </summary>
        public List<DatResourceEntry> CollectByType(DatSectionType type)
        {
            var results = new List<DatResourceEntry>();
            for (int i = 0; i < _resources.Count; i++)
            {
                if (_resources[i].TypeCode == type)
                {
                    results.Add(_resources[i]);
                }
            }
            return results;
        }

        /// <summary>
        /// Collects all descendant resources matching the given type code across the subtree.
        /// </summary>
        public List<DatResourceEntry> CollectByTypeRecursive(DatSectionType type)
        {
            var results = new List<DatResourceEntry>();
            void Walk(DatDirectoryNode dir)
            {
                for (int i = 0; i < dir.Resources.Count; i++)
                {
                    if (dir.Resources[i].TypeCode == type)
                    {
                        results.Add(dir.Resources[i]);
                    }
                }
                for (int i = 0; i < dir.SubDirectories.Count; i++)
                {
                    Walk(dir.SubDirectories[i]);
                }
            }

            Walk(this);
            return results;
        }

        public override string ToString() => $"Directory [{DatId}] ({_resources.Count} resources, {_subDirectories.Count} subdirs)";
    }

    /// <summary>
    /// Builder that walks sections and reconstructs the full DAT directory tree.
    /// </summary>
    public static class DatDirectoryTree
    {
        public static DatDirectoryNode Build(ReadOnlyMemory<byte> buffer)
        {
            var root = new DatDirectoryNode(string.Empty);
            var current = root;

            var headers = DatSectionWalker.ReadHeaders(buffer.Span);

            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];

                if (h.TypeCode == DatSectionType.Directory)
                {
                    var dir = new DatDirectoryNode(h.DatId, current);
                    current.AddSubDirectory(dir);
                    current = dir;
                }
                else if (h.TypeCode == DatSectionType.End)
                {
                    if (current.Parent != null)
                    {
                        current = current.Parent;
                    }
                }
                else
                {
                    var entry = new DatResourceEntry(h, current, buffer);
                    current.AddResource(entry);
                }
            }

            return root;
        }
    }
}
