// src/Gordian.App/Services/DevLaunchOptions.cs
using System;

namespace Gordian.App.Services
{
    /// <summary>
    /// Developer command-line options, so a test script can start any build of the client on a known profile:
    /// <c>--profiles-dir &lt;path&gt;</c> reads profiles from a shared folder instead of the one beside the build, and
    /// <c>--profile &lt;name&gt;</c> logs that profile in once the main window opens. Worktree builds have no profiles of
    /// their own, which is why the folder can be chosen. See <c>docs/app/desktop-shell.md</c>.
    /// </summary>
    public sealed record DevLaunchOptions(string? ProfilesDirectory, string? ProfileName)
    {
        /// <summary>The options this process started with.</summary>
        public static DevLaunchOptions Current { get; set; } = new(null, null);

        /// <summary>
        /// Reads the options from the process arguments. Other arguments are left alone, and an option with no value
        /// after it is ignored.
        /// </summary>
        public static DevLaunchOptions Parse(string[] args)
        {
            string? profilesDirectory = null;
            string? profileName = null;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i].Equals("--profiles-dir", StringComparison.OrdinalIgnoreCase)) profilesDirectory = args[++i];
                else if (args[i].Equals("--profile", StringComparison.OrdinalIgnoreCase)) profileName = args[++i];
            }
            return new DevLaunchOptions(profilesDirectory, profileName);
        }
    }
}
