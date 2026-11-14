// tests/Gordian.App.Tests/LogsViewModelTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.App.Tests
{
    /// <summary>The Logs tab (#566): the folder tree, the file reader, filters, following and the settings editor.</summary>
    public sealed class LogsViewModelTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"gordian_logstab_{Guid.NewGuid():N}");

        public LogsViewModelTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        private string Write(string relative, params string[] lines)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
            return path;
        }

        private static void Append(string path, string text) => File.AppendAllText(path, text);

        [Fact]
        public void Tree_ListsKnownFoldersFirstAndFilesNewestFirst()
        {
            Write(Path.Combine("chat", "Retail", "Gemini", "2026-11-14.log"), "x");
            Write(Path.Combine("system", "gordian_2026-11-13.log"), "x");
            Write(Path.Combine("system", "gordian_2026-11-14.log"), "x");
            Write(Path.Combine("system", "gordian_2026-11-14_2.log"), "x");
            Write(Path.Combine("status", "status_2026-11-14_09-55-35.log"), "x");
            Write("gordian_system_20261113.log", "x");

            var tree = LogTreeBuilder.Build(_root);

            Assert.Equal(new[] { "status", "system", "chat", "gordian_system_20261113.log" }, tree.Select(n => n.Name));
            Assert.Equal(new[] { "gordian_2026-11-14_2.log", "gordian_2026-11-14.log", "gordian_2026-11-13.log" },
                tree[1].Children.Select(n => n.Name));
            Assert.Equal("Gemini", tree[2].Children[0].Children[0].Name);
            Assert.True(tree[2].IsDirectory);
            Assert.False(tree[3].IsDirectory);
        }

        [Fact]
        public void Tree_KeepsExpandedFolders()
        {
            Write(Path.Combine("system", "gordian_2026-11-14.log"), "x");
            Write(Path.Combine("chat", "Retail", "Gemini", "2026-11-14.log"), "x");
            var first = LogTreeBuilder.Build(_root);
            first.Single(n => n.Name == "chat").IsExpanded = true;

            var second = LogTreeBuilder.Build(_root, LogTreeBuilder.ExpandedPaths(first));

            Assert.True(second.Single(n => n.Name == "chat").IsExpanded);
            Assert.False(second.Single(n => n.Name == "system").IsExpanded);
        }

        [Fact]
        public void Reader_FollowsAppendedLinesAndHoldsBackAnUnfinishedOne()
        {
            string path = Write("a.log", "one", "two");
            var reader = new LogFileReader(path);

            Assert.Equal(new[] { "one", "two" }, reader.ReadInitial());
            Append(path, "three\nfou");
            Assert.Equal(new[] { "three" }, reader.ReadAppended(out _));
            Append(path, "r\r\n");
            Assert.Equal(new[] { "four" }, reader.ReadAppended(out bool restarted));
            Assert.False(restarted);
            Assert.Empty(reader.ReadAppended(out _));
        }

        [Fact]
        public void Reader_ReadsOnlyTheTailOfALargeFile()
        {
            string path = Write("big.log", Enumerable.Range(0, 2000).Select(i => $"line {i:D5} " + new string('x', 40)).ToArray());
            var reader = new LogFileReader(path, maxBytes: 4096);

            var lines = reader.ReadInitial();

            Assert.True(reader.SkippedBytes > 0);
            Assert.Equal("line 01999 " + new string('x', 40), lines[^1]);
            Assert.All(lines, l => Assert.StartsWith("line ", l)); // the cut first fragment was dropped
            Assert.True(lines.Count < 100);
        }

        [Fact]
        public void Reader_StartsOverWhenTheFileShrinks()
        {
            string path = Write("a.log", "one", "two");
            var reader = new LogFileReader(path);
            reader.ReadInitial();
            File.WriteAllText(path, "new\n");

            var lines = reader.ReadAppended(out bool restarted);

            Assert.True(restarted);
            Assert.Equal(new[] { "new" }, lines);
        }

        [Theory]
        [InlineData("[08:39:14.512] [ERROR] [NET] boom", LogLevel.Error)]
        [InlineData("[08:39:14.512] [WARN] [AVALONIA] [Binding] x", LogLevel.Warn)]
        [InlineData("[08:39:14.512] [DEBUG] [NET] [Gemini] y", LogLevel.Debug)]
        [InlineData("   at Gordian.Core.Foo()", null)]
        public void ParseLevel_ReadsTheLevelColumn(string line, LogLevel? expected) =>
            Assert.Equal(expected, LogsViewModel.ParseLevel(line));

        [Fact]
        public async Task OpensOnTheStatusFileAndFollowsIt()
        {
            string status = Write(Path.Combine("status", "status_2026-11-14_09-55-35.log"), "[09:55:36] Saved profile 'Main'.");
            using var vm = new LogsViewModel(() => _root, () => status);

            vm.Start();
            await vm.LoadSelectedAsync();
            int appended = 0;
            vm.LinesAppended += () => appended++;

            Assert.True(vm.IsStatusSelected);
            Assert.Equal(new[] { "[09:55:36] Saved profile 'Main'." }, vm.Lines.Select(l => l.Text));
            Assert.False(vm.IsLevelFilterAvailable);

            Append(status, "[09:56:01] [Gemini] Character select: choose a character in the viewport.\n");
            vm.Poll();

            Assert.Equal(2, vm.Lines.Count);
            Assert.Equal(1, appended);
        }

        [Fact]
        public async Task SystemLog_FiltersByLevelAndSearch_WithStackTracesKeepingTheirLevel()
        {
            string system = Write(Path.Combine("system", "gordian_2026-11-14.log"),
                "[10:00:00.000] [DEBUG] [NET] packet",
                "[10:00:01.000] [INFO] [APP] starting",
                "[10:00:02.000] [ERROR] [SESSION] Login failed",
                "System.InvalidOperationException: boom",
                "   at Gordian.Login()",
                "[10:00:03.000] [WARN] [AVALONIA] binding");
            using var vm = new LogsViewModel(() => _root, () => null);
            vm.Start();
            vm.SelectedNode = LogTreeBuilder.Find(vm.Nodes, system);
            await vm.LoadSelectedAsync();

            Assert.True(vm.IsLevelFilterAvailable);
            Assert.Equal(6, vm.Lines.Count);

            vm.LevelFilterIndex = 3; // warnings and errors
            Assert.Equal(new[] { "[10:00:02.000] [ERROR] [SESSION] Login failed", "System.InvalidOperationException: boom", "   at Gordian.Login()", "[10:00:03.000] [WARN] [AVALONIA] binding" },
                vm.Lines.Select(l => l.Text));
            Assert.Equal("#F48771", vm.Lines[1].Foreground);

            vm.SearchText = "login";
            Assert.Equal(2, vm.Lines.Count);
            Assert.Contains("2 shown", vm.Header);
        }

        [Fact]
        public void SelectingAFolderOrNull_KeepsTheShownFile()
        {
            string status = Write(Path.Combine("status", "s.log"), "x");
            using var vm = new LogsViewModel(() => _root, () => status);
            vm.Start();

            vm.SelectedNode = vm.Nodes.First(n => n.IsDirectory);
            vm.SelectedNode = null;

            Assert.Same(vm.StatusNode, vm.SelectedNode);
        }

        [Fact]
        public void OpenFolder_RaisesTheLoggersFolder()
        {
            using var vm = new LogsViewModel(() => _root, () => null);
            string? opened = null;
            vm.OpenFolderRequested += path => opened = path;

            vm.OpenFolderCommand.Execute(null);

            Assert.Equal(_root, opened);
        }

        [Fact]
        public void Settings_SaveWritesOnlyOverridesAndApplies()
        {
            string path = Path.Combine(_root, "logging.json");
            new LoggingSettings { RetentionDays = 3 }.Save(path);
            LoggingSettings? applied = null;
            var vm = new LoggingSettingsViewModel(path, debugByDefault: true, s => applied = s);

            var locomotion = vm.Categories.Single(r => r.Category == LogCategories.Locomotion);
            Assert.Equal("Default (Warn)", locomotion.Options[0]);
            locomotion.SelectedIndex = LoggingSettingsViewModel.LevelIndex(LogLevel.Info) + 1;
            vm.NetworkFile = true;
            vm.ChatLog = false;
            vm.SaveCommand.Execute(null);

            var saved = LoggingSettings.Load(path);
            Assert.Equal(LogLevel.Info, saved.Categories[LogCategories.Locomotion]);
            Assert.Single(saved.Categories);
            Assert.True(saved.NetworkFile);
            Assert.False(saved.ChatLog);
            Assert.Null(saved.DefaultLevel);
            Assert.Equal(3, saved.RetentionDays); // kept from the file
            Assert.NotNull(applied);
            Assert.Equal("Default (Debug)", vm.DefaultLevelOptions[0]);
        }

        [Fact]
        public void Segments_ColourEachTokenOfASystemLine()
        {
            var segments = LogLineSegments.Split("[10:43:53.120] [WARN] [SESSION] [Gemini] Lost connection.", LogLevel.Warn, hasLevels: true, search: null);

            Assert.Equal(new[] { "[10:43:53.120]", " [WARN]", " [SESSION]", " [Gemini]", " Lost connection." }, segments.Select(s => s.Text));
            Assert.Equal(new[] { LogLineSegments.TimestampColor, LogLineSegments.LevelColor(LogLevel.Warn), LogLineSegments.CategoryColor, LogLineSegments.TagColor, LogLineSegments.MessageColorFor(LogLevel.Warn) },
                segments.Select(s => s.Foreground));
            Assert.DoesNotContain(segments, s => s.Match);
        }

        [Fact]
        public void Segments_StatusLineAndStackTraceLine()
        {
            var status = LogLineSegments.Split("[10:43:53] [Gordian] Session disconnected.", null, hasLevels: false, search: null);
            Assert.Equal(new[] { "[10:43:53]", " [Gordian]", " Session disconnected." }, status.Select(s => s.Text));

            var trace = LogLineSegments.Split("   at Gordian.Login()", LogLevel.Error, hasLevels: true, search: null);
            Assert.Equal(LogLineSegments.MessageColorFor(LogLevel.Error), Assert.Single(trace).Foreground);
        }

        [Fact]
        public void Segments_HighlightMatchesAcrossTokensCaseInsensitively()
        {
            var segments = LogLineSegments.Split("[10:00:00.000] [WARN] [NET] warn again", LogLevel.Warn, hasLevels: true, search: "N] [n");

            Assert.Equal("N] [N", string.Concat(segments.Where(s => s.Match).Select(s => s.Text)));
            Assert.Equal("[10:00:00.000] [WARN] [NET] warn again", string.Concat(segments.Select(s => s.Text)));

            var many = LogLineSegments.Split("[10:00:00] warn and WARN", null, hasLevels: false, search: "warn");
            Assert.Equal(2, many.Count(s => s.Match));
        }

        [Fact]
        public void SessionStatus_ReportsADisconnectOnceAndNothingForDisconnecting()
        {
            var ticket = new Gordian.Core.Network.Lobby.MapSessionTicket { ZoneIp = "127.0.0.1", ZonePort = 54230 };

            Assert.Null(MainWindowViewModel.SessionStateStatus("Gemini", Gordian.Core.Network.SessionState.Disconnecting, ticket, false, false));
            Assert.Equal("[Gemini] Session disconnected.", MainWindowViewModel.SessionStateStatus("Gemini", Gordian.Core.Network.SessionState.Disconnected, ticket, false, true));
            Assert.Null(MainWindowViewModel.SessionStateStatus("Gemini", Gordian.Core.Network.SessionState.Disconnected, ticket, false, false));
            Assert.Equal("[Gemini] Lost connection to the server.", MainWindowViewModel.SessionStateStatus("Gemini", Gordian.Core.Network.SessionState.Disconnected, ticket, true, true));
        }

        [Fact]
        public void SessionStatus_ADisconnectThroughBothPathsIsReportedOnce_AndAgainAfterReconnecting()
        {
            var ticket = new Gordian.Core.Network.Lobby.MapSessionTicket { ZoneIp = "127.0.0.1", ZonePort = 54230 };
            // Disconnect(), then the read loop's cleanup, then a later login and logout of the same session.
            var states = new[]
            {
                Gordian.Core.Network.SessionState.ActiveInWorld,
                Gordian.Core.Network.SessionState.Disconnecting, Gordian.Core.Network.SessionState.Disconnected,
                Gordian.Core.Network.SessionState.Disconnecting, Gordian.Core.Network.SessionState.Disconnected,
                Gordian.Core.Network.SessionState.ConnectingToGameServer,
                Gordian.Core.Network.SessionState.Disconnecting, Gordian.Core.Network.SessionState.Disconnected,
            };
            int reported = 0;
            var messages = new System.Collections.Generic.List<string>();
            foreach (var state in states)
            {
                // The same steps as the session's StateChanged handler in MainWindowViewModel.
                bool first = state == Gordian.Core.Network.SessionState.Disconnected && System.Threading.Interlocked.Exchange(ref reported, 1) == 0;
                if (MainWindowViewModel.DisconnectReportRearms(state)) reported = 0;
                if (MainWindowViewModel.SessionStateStatus("Gemini", state, ticket, false, first) is { } message) messages.Add(message);
            }

            Assert.Equal(2, messages.Count(m => m == "[Gemini] Session disconnected."));
            Assert.Equal("[Gemini] Session disconnected.", messages[1]);
            Assert.Equal("[Gemini] Session disconnected.", messages[^1]);
        }

        [Fact]
        public async Task SwitchingFiles_ReplacesTheLines()
        {
            string longFile = Write(Path.Combine("system", "gordian_2026-11-14.log"), Enumerable.Range(0, 200).Select(i => $"[10:00:00.000] [INFO] [APP] line {i}").ToArray());
            string shortFile = Write(Path.Combine("system", "gordian_2026-11-13.log"), "[09:00:00.000] [INFO] [APP] only");
            using var vm = new LogsViewModel(() => _root, () => null);
            vm.Start();
            int replaced = 0;
            vm.LinesReplaced += () => replaced++;

            vm.SelectedNode = LogTreeBuilder.Find(vm.Nodes, longFile);
            await vm.LoadSelectedAsync();
            vm.SelectedNode = LogTreeBuilder.Find(vm.Nodes, shortFile);
            await vm.LoadSelectedAsync();

            Assert.Single(vm.Lines);
            Assert.True(replaced >= 2);
        }
    }
}
