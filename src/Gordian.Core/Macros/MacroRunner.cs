// src/Gordian.Core/Macros/MacroRunner.cs
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Macros
{
    /// <summary>
    /// Runs macros: each line in order, as if typed in the input line (<see cref="SubmitLine"/>, the chat's own path, so
    /// commands, chat and their echoes behave as typed). Pauses (BG-Wiki "Macro"):
    /// <list type="bullet">
    /// <item><c>/wait n</c> on a line of its own waits n seconds before the next line;</item>
    /// <item><c>&lt;wait n&gt;</c> in a line runs the line without it, then waits n seconds;</item>
    /// <item>a line that opens the sub-target cursor (<c>&lt;st&gt;</c> ...) holds the next line until the target is chosen or
    /// the cursor cancelled (BG-Wiki: "the line after the subtarget runs once the player confirms").</item>
    /// </list>
    /// Waits accept decimals and are capped at <see cref="MaxWaitSeconds"/>. Starting a macro while another is still
    /// running stops the first (PROVISIONAL: the retail behaviour is not documented; to check in retail). Retail runs
    /// lines on a tick of about 0.42 s (BG-Wiki, third-party timing), which snaps waits to that tick; GordianXI runs
    /// lines back to back and waits exactly the time given.
    /// </summary>
    public sealed class MacroRunner
    {
        /// <summary>Longest pause a line can ask for (PROVISIONAL: retail's limit is not documented).</summary>
        public const double MaxWaitSeconds = 60;

        /// <summary>How often a held line checks whether the sub-target cursor has closed.</summary>
        public static readonly TimeSpan SubTargetPoll = TimeSpan.FromMilliseconds(50);

        private readonly object _sync = new();
        private CancellationTokenSource? _running;
        private Task _task = Task.CompletedTask;

        /// <summary>Sends one line as typed (the chat's submit, with the default chat mode). Required to run anything.</summary>
        public Func<string, Task>? SubmitLine { get; set; }

        /// <summary>True while the sub-target cursor waits for a target (a line using <c>&lt;st&gt;</c> holds the macro).</summary>
        public Func<bool>? IsSubTargeting { get; set; }

        /// <summary>The delay the waits use; replaceable for tests.</summary>
        public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (time, token) => Task.Delay(time, token);

        /// <summary>The title of the macro running now (null when none).</summary>
        public string? RunningTitle { get; private set; }

        /// <summary>True while a macro runs.</summary>
        public bool IsRunning
        {
            get { lock (_sync) return _running != null; }
        }

        /// <summary>The run in progress (completed when idle); for tests.</summary>
        public Task Current
        {
            get { lock (_sync) return _task; }
        }

        /// <summary>Starts a macro, stopping one still running. An empty macro does nothing.</summary>
        public Task Run(Macro macro)
        {
            ArgumentNullException.ThrowIfNull(macro);
            bool hasLine = false;
            foreach (string line in macro.Lines) hasLine |= line.Trim().Length > 0;
            if (!hasLine) return Task.CompletedTask;
            var cts = new CancellationTokenSource();
            Task task;
            lock (_sync)
            {
                _running?.Cancel();
                _running = cts;
                RunningTitle = macro.Title;
                task = _task = RunLinesAsync(macro, cts);
            }
            return task;
        }

        /// <summary>Stops the macro running now, if any.</summary>
        public void Stop()
        {
            lock (_sync)
            {
                _running?.Cancel();
                _running = null;
                RunningTitle = null;
            }
        }

        private async Task RunLinesAsync(Macro macro, CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                // Run on the thread pool, so a caller on the input tick never waits for a send.
                await Task.Yield();
                foreach (string raw in macro.Lines)
                {
                    if (token.IsCancellationRequested) return;
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (TryParseWaitCommand(line, out double pause))
                    {
                        await WaitAsync(pause, token).ConfigureAwait(false);
                        continue;
                    }
                    string text = ExtractWait(line, out double after);
                    if (text.Length > 0 && SubmitLine is { } submit)
                    {
                        await submit(text).ConfigureAwait(false);
                        await HoldWhileSubTargetingAsync(token).ConfigureAwait(false);
                    }
                    if (after > 0) await WaitAsync(after, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Another macro started, or the macro was stopped.
            }
            catch (Exception ex)
            {
                GordianLog.Error("MACRO", $"Macro \"{macro.Title}\" failed: {ex.Message}", ex);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_running, cts))
                    {
                        _running = null;
                        RunningTitle = null;
                    }
                }
                cts.Dispose();
            }
        }

        private async Task HoldWhileSubTargetingAsync(CancellationToken token)
        {
            var subTargeting = IsSubTargeting;
            if (subTargeting == null) return;
            while (subTargeting())
            {
                token.ThrowIfCancellationRequested();
                await Delay(SubTargetPoll, token).ConfigureAwait(false);
            }
        }

        private Task WaitAsync(double seconds, CancellationToken token)
        {
            seconds = Math.Clamp(seconds, 0, MaxWaitSeconds);
            return seconds <= 0 ? Task.CompletedTask : Delay(TimeSpan.FromSeconds(seconds), token);
        }

        /// <summary>
        /// A line that is only a pause: <c>/wait n</c> (n seconds, decimals allowed; <c>/wait</c> alone is one second).
        /// </summary>
        public static bool TryParseWaitCommand(string line, out double seconds)
        {
            seconds = 0;
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("/wait", StringComparison.OrdinalIgnoreCase)) return false;
            string rest = trimmed[5..];
            if (rest.Length > 0 && !char.IsWhiteSpace(rest[0])) return false;
            rest = rest.Trim();
            if (rest.Length == 0)
            {
                seconds = 1;
                return true;
            }
            return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0;
        }

        /// <summary>
        /// Removes the <c>&lt;wait n&gt;</c> pauses from a line and returns the rest; <paramref name="seconds"/> is their sum.
        /// </summary>
        public static string ExtractWait(string line, out double seconds)
        {
            double total = 0;
            string text = ChatTextTokens.Expand(line, _ => null);
            int i = 0;
            while ((i = line.IndexOf('<', i)) >= 0)
            {
                int close = line.IndexOf('>', i + 1);
                if (close < 0) break;
                if (ChatTextTokens.IsWait(ChatTextTokens.NormalizeWord(line.Substring(i + 1, close - i - 1)), out double s)) total += s;
                i = close + 1;
            }
            seconds = total;
            return text.Trim();
        }
    }
}
