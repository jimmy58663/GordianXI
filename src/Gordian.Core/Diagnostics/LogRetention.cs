using System;
using System.IO;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Deletes old diagnostic logs at startup: files in <c>logs/system</c>, <c>logs/network</c> and <c>logs/status</c>
    /// last written more than the retention period ago. Never touches <c>logs/chat</c>, the pre-#564 files at the top of
    /// <c>logs/</c> (the maintainer keeps some as calibration evidence) or anything else.
    /// </summary>
    public static class LogRetention
    {
        /// <summary>Deletes expired logs under <paramref name="logsDirectory"/>; returns how many were deleted.</summary>
        public static int Sweep(string logsDirectory, int retentionDays, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory)) return 0;
            DateTime cutoff = now - TimeSpan.FromDays(Math.Max(1, retentionDays));
            int deleted = 0;

            foreach (string folder in new[] { GordianLog.SystemFolder, GordianLog.NetworkFolder, GordianLog.StatusFolder })
            {
                string path = Path.Combine(logsDirectory, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.EnumerateFiles(path, "*.log", SearchOption.TopDirectoryOnly))
                {
                    deleted += DeleteIfOlder(file, cutoff);
                }
            }

            return deleted;
        }

        private static int DeleteIfOlder(string file, DateTime cutoff)
        {
            try
            {
                if (File.GetLastWriteTime(file) >= cutoff) return 0;
                File.Delete(file);
                return 1;
            }
            catch (IOException)
            {
                return 0; // In use (another GordianXI writing it) or gone already.
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
