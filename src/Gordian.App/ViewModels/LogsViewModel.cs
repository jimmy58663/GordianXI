using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.App.Services;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.App.ViewModels
{
    /// <summary>One line in the Logs tab's viewer, with the level it was logged at (continuation lines take the previous line's).</summary>
    public sealed class LogLineItem
    {
        public LogLineItem(string text, LogLevel? level)
        {
            Text = text;
            Level = level;
        }

        public string Text { get; }

        /// <summary>The line's level for a system or network log; null for status and chat lines.</summary>
        public LogLevel? Level { get; }

        /// <summary>The line's colour: errors red, warnings amber, debug grey, the rest light.</summary>
        public string Foreground => Level switch
        {
            LogLevel.Error => "#F48771",
            LogLevel.Warn => "#E5C07B",
            LogLevel.Debug or LogLevel.Trace => "#8A8A8A",
            _ => "#D4D4D4",
        };
    }

    /// <summary>
    /// The Logs tab (#566): a tree of the logs folder on the left and a terminal-style viewer on the right. It opens on
    /// this launch's status file and follows it; picking a file in the tree shows that file, following it too while
    /// GordianLog is still writing it. The view's timer calls <see cref="Poll"/> to pick up appended lines and folder
    /// changes, so this class needs no dispatcher of its own.
    /// </summary>
    public sealed class LogsViewModel : ViewModelBase, IDisposable
    {
        /// <summary>The level filter choices, lowest first.</summary>
        public static readonly IReadOnlyList<string> LevelFilterOptions = new[]
        {
            "All levels", "Debug and up", "Info and up", "Warnings and errors", "Errors only",
        };

        private static readonly Regex LevelPattern = new(@"^\[[^\]]*\] \[(TRACE|DEBUG|INFO|WARN|ERROR|STATUS)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly Func<string> _logsDirectory;
        private readonly Func<string?> _statusFile;
        private readonly List<LogLineItem> _all = new();
        private FileSystemWatcher? _watcher;
        private int _treeDirty = 1;
        private int _loadGeneration;
        private LogFileReader? _reader;
        private bool _isLevelFile;
        private LogTreeNode? _selectedNode;
        private ObservableCollection<LogLineItem> _lines = new();
        private string _searchText = string.Empty;
        private int _levelFilterIndex;
        private bool _follow = true;
        private string _header = string.Empty;

        /// <summary>Uses the running logger's folder and status file.</summary>
        public LogsViewModel()
            : this(() => GordianLog.LogsDirectory ?? GordianStorage.LogsDirectory, () => GordianLog.StatusFilePath)
        {
        }

        /// <summary>Uses the given logs folder and status file (tests).</summary>
        public LogsViewModel(Func<string> logsDirectory, Func<string?> statusFile)
        {
            _logsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
            _statusFile = statusFile ?? throw new ArgumentNullException(nameof(statusFile));
            StatusNode = new LogTreeNode("Status (this session)", string.Empty, isDirectory: false, isPinnedStatus: true);
            RefreshCommand = new RelayCommand(() => { Interlocked.Exchange(ref _treeDirty, 1); Poll(); });
            ShowStatusCommand = new RelayCommand(() => SelectedNode = StatusNode);
            OpenFolderCommand = new RelayCommand(() => OpenFolderRequested?.Invoke(LogsDirectory));
        }

        /// <summary>
        /// The folder the logs are written to and the tree shows: <c>GordianLog.LogsDirectory</c> (next to the exe, or the
        /// user's application data folder when the exe is in a protected folder such as Program Files).
        /// </summary>
        public string LogsDirectory => _logsDirectory();

        /// <summary>The pinned entry above the tree that shows this launch's status file.</summary>
        public LogTreeNode StatusNode { get; }

        public ObservableCollection<LogTreeNode> Nodes { get; } = new();

        /// <summary>The visible lines, after the search and level filters.</summary>
        public ObservableCollection<LogLineItem> Lines
        {
            get => _lines;
            private set => SetProperty(ref _lines, value);
        }

        /// <summary>The file shown (or the pinned status entry); picking one loads it.</summary>
        public LogTreeNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                // The tree writes null when its selection is not among its items (the pinned status entry, or after a
                // rebuild); a folder only expands. Neither changes what is shown.
                if (value is null || value.IsDirectory) return;
                if (!SetProperty(ref _selectedNode, value)) return;
                OnPropertyChanged(nameof(IsStatusSelected));
                _ = LoadSelectedAsync();
            }
        }

        /// <summary>Whether the pinned status entry is the one shown.</summary>
        public bool IsStatusSelected => SelectedNode is { IsPinnedStatus: true };

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilters();
            }
        }

        public IReadOnlyList<string> LevelFilters => LevelFilterOptions;

        public int LevelFilterIndex
        {
            get => _levelFilterIndex;
            set
            {
                if (SetProperty(ref _levelFilterIndex, Math.Clamp(value, 0, LevelFilterOptions.Count - 1))) ApplyFilters();
            }
        }

        /// <summary>Whether the level filter applies (system and network files carry levels).</summary>
        public bool IsLevelFilterAvailable
        {
            get => _isLevelFile;
            private set => SetProperty(ref _isLevelFile, value);
        }

        /// <summary>Keep the newest line in view as lines arrive.</summary>
        public bool Follow
        {
            get => _follow;
            set => SetProperty(ref _follow, value);
        }

        /// <summary>What is shown: the file's path, its size and how many lines.</summary>
        public string Header
        {
            get => _header;
            private set => SetProperty(ref _header, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand ShowStatusCommand { get; }
        public ICommand OpenFolderCommand { get; }

        /// <summary>Raised with the logs folder when the user asks to open it; the view opens it in the OS file manager.</summary>
        public event Action<string>? OpenFolderRequested;

        /// <summary>Raised after lines were appended to <see cref="Lines"/> (the view scrolls to the newest when following).</summary>
        public event Action? LinesAppended;

        /// <summary>Builds the tree, starts watching the folder and shows this launch's status file.</summary>
        public void Start()
        {
            StartWatcher();
            Interlocked.Exchange(ref _treeDirty, 1);
            RebuildTreeIfDirty();
            SelectedNode ??= StatusNode;
        }

        /// <summary>
        /// Called by the view's timer on the UI thread: rebuilds the tree if the folder changed and reads lines appended to
        /// the shown file.
        /// </summary>
        public void Poll()
        {
            RebuildTreeIfDirty();
            if (_reader is null) return;
            List<string> appended;
            bool restarted;
            try
            {
                appended = _reader.ReadAppended(out restarted);
            }
            catch (IOException)
            {
                return;
            }
            if (restarted)
            {
                _ = LoadSelectedAsync();
                return;
            }
            if (appended.Count == 0) return;

            int before = _all.Count;
            AddParsed(appended, IsLevelFilterAvailable);
            bool any = false;
            for (int i = before; i < _all.Count; i++)
            {
                if (!Matches(_all[i])) continue;
                Lines.Add(_all[i]);
                any = true;
            }
            UpdateHeader();
            if (any) LinesAppended?.Invoke();
        }

        /// <summary>Loads the selected file (or this launch's status file) off the UI thread, then shows it.</summary>
        public async Task LoadSelectedAsync()
        {
            int generation = Interlocked.Increment(ref _loadGeneration);
            string? path = SelectedNode is { IsPinnedStatus: true } ? _statusFile() : SelectedNode?.FullPath;
            if (string.IsNullOrEmpty(path))
            {
                _reader = null;
                _all.Clear();
                Lines = new ObservableCollection<LogLineItem>();
                IsLevelFilterAvailable = false;
                Header = SelectedNode is { IsPinnedStatus: true } ? "No status file: logging is not running." : string.Empty;
                return;
            }

            var reader = new LogFileReader(path);
            bool levels = HasLevels(path);
            List<string> raw;
            try
            {
                raw = await Task.Run(reader.ReadInitial).ConfigureAwait(true);
            }
            catch (IOException ex)
            {
                if (generation != Volatile.Read(ref _loadGeneration)) return;
                _reader = null;
                _all.Clear();
                Lines = new ObservableCollection<LogLineItem>();
                Header = $"Could not read {path}: {ex.Message}";
                return;
            }
            if (generation != Volatile.Read(ref _loadGeneration)) return;

            _reader = reader;
            _all.Clear();
            AddParsed(raw, levels);
            IsLevelFilterAvailable = levels;
            ApplyFilters();
            UpdateHeader();
            LinesAppended?.Invoke();
        }

        /// <summary>The visible lines' text, for Copy.</summary>
        public static string JoinLines(IEnumerable<LogLineItem> lines) =>
            string.Join(Environment.NewLine, lines.Select(l => l.Text));

        /// <summary>Whether a file carries <c>[LEVEL]</c> columns: the system and network logs (and the pre-#564 ones).</summary>
        public static bool HasLevels(string path)
        {
            string name = Path.GetFileName(path);
            string? folder = Path.GetFileName(Path.GetDirectoryName(path));
            return string.Equals(folder, GordianLog.SystemFolder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder, GordianLog.NetworkFolder, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("gordian_system_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("gordian_network_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The level written in a line's <c>[LEVEL]</c> column, or null when it has none (a continuation line).</summary>
        public static LogLevel? ParseLevel(string line)
        {
            var match = LevelPattern.Match(line);
            if (!match.Success) return null;
            return match.Groups[1].Value switch
            {
                "TRACE" => LogLevel.Trace,
                "DEBUG" => LogLevel.Debug,
                "INFO" => LogLevel.Info,
                "WARN" => LogLevel.Warn,
                "ERROR" => LogLevel.Error,
                _ => LogLevel.Status,
            };
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        private void AddParsed(List<string> raw, bool levels)
        {
            LogLevel? previous = _all.Count > 0 ? _all[^1].Level : null;
            foreach (string text in raw)
            {
                LogLevel? level = null;
                if (levels)
                {
                    level = ParseLevel(text) ?? previous;
                    previous = level;
                }
                _all.Add(new LogLineItem(text, level));
            }
        }

        private void ApplyFilters()
        {
            Lines = new ObservableCollection<LogLineItem>(_all.Where(Matches));
            UpdateHeader();
        }

        private bool Matches(LogLineItem line)
        {
            if (IsLevelFilterAvailable && LevelFilterIndex > 0)
            {
                LogLevel minimum = LevelFilterIndex switch
                {
                    1 => LogLevel.Debug,
                    2 => LogLevel.Info,
                    3 => LogLevel.Warn,
                    _ => LogLevel.Error,
                };
                if (line.Level is { } level && level < minimum) return false;
            }
            return SearchText.Length == 0 || line.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateHeader()
        {
            if (_reader is null) return;
            string size = FormatSize(_reader.Length);
            string skipped = _reader.SkippedBytes > 0 ? $", showing the last {FormatSize(_reader.Length - _reader.SkippedBytes)}" : string.Empty;
            string filtered = Lines.Count != _all.Count ? $", {Lines.Count:N0} shown" : string.Empty;
            Header = $"{_reader.Path}  ({size}{skipped}, {_all.Count:N0} lines{filtered})";
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            >= 1024 => $"{bytes / 1024.0:F0} KB",
            _ => $"{bytes} B",
        };

        private void StartWatcher()
        {
            _watcher?.Dispose();
            _watcher = null;
            string root = LogsDirectory;
            if (!Directory.Exists(root)) return;
            try
            {
                _watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                };
                FileSystemEventHandler changed = (_, _) => Interlocked.Exchange(ref _treeDirty, 1);
                _watcher.Created += changed;
                _watcher.Deleted += changed;
                _watcher.Renamed += (_, _) => Interlocked.Exchange(ref _treeDirty, 1);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
            {
                GordianLog.Warn(LogCategories.App, $"Logs tab: cannot watch {root} for new files ({ex.Message}); use Refresh.");
            }
        }

        private void RebuildTreeIfDirty()
        {
            if (Interlocked.Exchange(ref _treeDirty, 0) == 0) return;
            var expanded = LogTreeBuilder.ExpandedPaths(Nodes);
            if (Nodes.Count == 0)
            {
                // First build: open the folders a user reaches for first.
                expanded.Add(Path.Combine(LogsDirectory, GordianLog.StatusFolder));
                expanded.Add(Path.Combine(LogsDirectory, GordianLog.SystemFolder));
            }
            var built = LogTreeBuilder.Build(LogsDirectory, expanded);
            Nodes.Clear();
            foreach (var node in built) Nodes.Add(node);

            // Keep the shown file highlighted: point the selection at its node in the new tree without reloading it.
            if (_selectedNode is { IsPinnedStatus: false } shown && LogTreeBuilder.Find(Nodes, shown.FullPath) is { } again)
            {
                _selectedNode = again;
                OnPropertyChanged(nameof(SelectedNode));
            }
        }
    }
}
