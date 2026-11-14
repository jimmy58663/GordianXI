using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Gordian.Core.Diagnostics;

namespace Gordian.App.ViewModels
{
    /// <summary>A folder or log file in the Logs tab's tree, or the pinned "Status (this session)" entry (#566).</summary>
    public sealed class LogTreeNode : ViewModelBase
    {
        private bool _isExpanded;

        public LogTreeNode(string name, string fullPath, bool isDirectory, bool isPinnedStatus = false)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            IsPinnedStatus = isPinnedStatus;
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }

        /// <summary>The entry above the tree that opens this launch's status file.</summary>
        public bool IsPinnedStatus { get; }

        public ObservableCollection<LogTreeNode> Children { get; } = new();

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>Folder or file glyph for the tree row.</summary>
        public string Icon => IsPinnedStatus ? "★" : IsDirectory ? "📁" : "📄";

        public override string ToString() => Name;
    }

    /// <summary>Builds the Logs tab's tree from the logs folder.</summary>
    public static class LogTreeBuilder
    {
        private static readonly string[] KnownFolders =
            { GordianLog.StatusFolder, GordianLog.SystemFolder, GordianLog.NetworkFolder, GordianLog.ChatFolder };

        /// <summary>
        /// The tree under <paramref name="root"/>: the known folders first (status, system, network, chat), then any other
        /// folder by name, then loose files (the pre-#564 logs). Files are listed newest first by name, which for the dated
        /// names GordianLog writes is newest first by date (a rolled <c>_2</c> before its first file). Folders keep the
        /// expansion state of <paramref name="expanded"/> paths.
        /// </summary>
        public static List<LogTreeNode> Build(string root, ISet<string>? expanded = null)
        {
            var nodes = new List<LogTreeNode>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return nodes;
            AddChildren(root, nodes, expanded, isRoot: true);
            return nodes;
        }

        /// <summary>The full paths of every expanded folder in <paramref name="nodes"/>.</summary>
        public static HashSet<string> ExpandedPaths(IEnumerable<LogTreeNode> nodes)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes) Collect(node, set);
            return set;

            static void Collect(LogTreeNode node, HashSet<string> set)
            {
                if (node.IsDirectory && node.IsExpanded) set.Add(node.FullPath);
                foreach (var child in node.Children) Collect(child, set);
            }
        }

        /// <summary>The node for <paramref name="fullPath"/>, searching depth first.</summary>
        public static LogTreeNode? Find(IEnumerable<LogTreeNode> nodes, string fullPath)
        {
            foreach (var node in nodes)
            {
                if (string.Equals(node.FullPath, fullPath, StringComparison.Ordinal) && !node.IsPinnedStatus) return node;
                if (Find(node.Children, fullPath) is { } found) return found;
            }
            return null;
        }

        private static void AddChildren(string directory, IList<LogTreeNode> into, ISet<string>? expanded, bool isRoot)
        {
            IEnumerable<string> folders;
            IEnumerable<string> files;
            try
            {
                folders = Directory.EnumerateDirectories(directory).ToList();
                files = Directory.EnumerateFiles(directory).ToList();
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            var orderedFolders = folders
                .OrderBy(f => isRoot ? KnownRank(Path.GetFileName(f)) : 0)
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
            foreach (string folder in orderedFolders)
            {
                var node = new LogTreeNode(Path.GetFileName(folder), folder, isDirectory: true)
                {
                    IsExpanded = expanded?.Contains(folder) ?? false,
                };
                AddChildren(folder, node.Children, expanded, isRoot: false);
                into.Add(node);
            }

            foreach (string file in files.OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                into.Add(new LogTreeNode(Path.GetFileName(file), file, isDirectory: false));
            }
        }

        private static int KnownRank(string name)
        {
            int index = Array.FindIndex(KnownFolders, k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : KnownFolders.Length;
        }
    }
}
