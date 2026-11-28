// src/Gordian.Core/Profiles/ProfilePath.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gordian.Core.Profiles
{
    /// <summary>
    /// A profile's place in the launch list as people read and type it (#638): its folder and name joined by <c>/</c>,
    /// e.g. <c>Horizon/Gordian</c> or <c>Test2/Test3/Gordian</c>, or the bare name at the root. Profile names are unique
    /// within a folder, not across folders, so a path always names one profile and a bare name may name several. The
    /// <c>--profile</c> launch option and messages that list profiles use it; the hidden <see cref="AccountProfile.Id"/>
    /// is never shown or typed.
    /// </summary>
    public static class ProfilePath
    {
        private static readonly char[] Separators = ['/', '\\'];

        /// <summary>A folder path in its stored form: trimmed, <c>/</c>-separated, no empty or leading/trailing parts.</summary>
        public static string NormalizeFolder(string? folder) =>
            string.Join('/', (folder ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => part.Length > 0));

        /// <summary><paramref name="profile"/>'s path: <c>&lt;folder&gt;/&lt;name&gt;</c>, or the name at the root.</summary>
        public static string Format(AccountProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            string folder = NormalizeFolder(profile.Folder);
            return folder.Length == 0 ? profile.ProfileName : $"{folder}/{profile.ProfileName}";
        }

        /// <summary>Whether <paramref name="name"/> holds a folder separator, which a profile name may not.</summary>
        public static bool HasSeparator(string? name) => name?.IndexOfAny(Separators) >= 0;

        /// <summary>
        /// Whether <paramref name="a"/> and <paramref name="b"/> are in the same launch-list folder (ignoring case, as the
        /// launch list matches folders).
        /// </summary>
        public static bool SameFolder(string? a, string? b) =>
            string.Equals(NormalizeFolder(a), NormalizeFolder(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The profile among <paramref name="profiles"/> named <paramref name="name"/> in <paramref name="folder"/>
        /// (ignoring case), other than the one with id <paramref name="exceptId"/>; null when the name is free there.
        /// </summary>
        public static AccountProfile? FindNameInFolder(IEnumerable<AccountProfile> profiles, string? folder, string? name, string? exceptId = null)
        {
            string wanted = (name ?? string.Empty).Trim();
            return profiles.FirstOrDefault(p =>
                (exceptId == null || !string.Equals(p.Id, exceptId, StringComparison.Ordinal)) &&
                SameFolder(p.Folder, folder) &&
                string.Equals(p.ProfileName.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The first of <paramref name="baseName"/>, then <paramref name="next"/>(2), <paramref name="next"/>(3)... that
        /// no profile in <paramref name="folder"/> has.
        /// </summary>
        public static string FreeNameInFolder(IEnumerable<AccountProfile> profiles, string? folder, string baseName, Func<int, string> next)
        {
            var list = profiles as IReadOnlyCollection<AccountProfile> ?? profiles.ToList();
            string candidate = baseName;
            for (int n = 2; FindNameInFolder(list, folder, candidate) != null; n++) candidate = next(n);
            return candidate;
        }

        /// <summary>
        /// Finds the profiles <paramref name="spec"/> names, ignoring case. A spec with a <c>/</c> (or <c>\</c>) is a
        /// path: everything before the last separator is the folder (empty, as in <c>/Gordian</c>, for the root) and
        /// the rest the name. A spec without one is a bare name, matched in every folder. One match is the profile to
        /// use; several (a bare name more than one folder has) means the caller asks for a path, see
        /// <see cref="Format"/>.
        /// </summary>
        public static IReadOnlyList<AccountProfile> Find(IEnumerable<AccountProfile> profiles, string? spec)
        {
            string text = (spec ?? string.Empty).Trim();
            if (text.Length == 0) return [];
            int split = text.LastIndexOfAny(Separators);
            if (split < 0)
                return profiles.Where(p => string.Equals(p.ProfileName.Trim(), text, StringComparison.OrdinalIgnoreCase)).ToList();

            string folder = text[..split];
            string name = text[(split + 1)..].Trim();
            return profiles.Where(p =>
                SameFolder(p.Folder, folder) &&
                string.Equals(p.ProfileName.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
