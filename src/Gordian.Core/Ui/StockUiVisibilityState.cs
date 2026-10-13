// src/Gordian.Core/Ui/StockUiVisibilityState.cs
using System;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The stock HUD elements that can be hidden at run time (#29), so an addon or an ImGui replacement can take one's
    /// place without the two overlapping. Flags: an owner hides any combination.
    /// </summary>
    [Flags]
    public enum StockUiElement
    {
        None = 0,

        /// <summary>The target window ("targetwi": the target's name and HP gauge).</summary>
        TargetBar = 1 << 0,

        /// <summary>
        /// The player's own vitals. The stock client shows them in the party window, so this hides the party window while
        /// the character is not in a party (the "Solo" window, "ptw0"). In a party the window also lists the other members
        /// and stays unless <see cref="Party"/> is hidden too.
        /// </summary>
        Vitals = 1 << 1,

        /// <summary>The party window while the character is in a party ("ptw1".."ptw6").</summary>
        Party = 1 << 2,

        /// <summary>The alliance windows ("raid1", "raid2").</summary>
        Alliance = 1 << 3,

        /// <summary>The player's status icons ("buff").</summary>
        Buffs = 1 << 4,

        /// <summary>
        /// The menus and prompts (main menu tree, event choices, target command menu, shop, home point, check window) and
        /// the action help bar. Hidden menus still take menu input: the replacement is expected to drive or close them.
        /// </summary>
        Menus = 1 << 5,

        /// <summary>The log windows and the chat input line.</summary>
        Chat = 1 << 6,

        /// <summary>The in-world name plates.</summary>
        NamePlates = 1 << 7,

        /// <summary>The in-world target and sub-target cursors.</summary>
        TargetCursor = 1 << 8,

        /// <summary>Every element above (a clean screen for screenshots or a full replacement HUD).</summary>
        All = TargetBar | Vitals | Party | Alliance | Buffs | Menus | Chat | NamePlates | TargetCursor,
    }

    /// <summary>
    /// Which stock HUD elements are hidden right now, and by whom (#29). Each owner (an addon, an ImGui overlay, the
    /// player's <c>/stockui</c> command) keeps its own set; an element is hidden while any owner hides it, so one addon
    /// showing an element again never undoes another's hide, and <see cref="Release"/> drops everything an owner hid
    /// (an addon unloading). Not saved: owners apply their hides each time they start. The per-window
    /// <c>/uilayout &lt;window&gt; hide</c> overrides are separate and persistent; the HUD hides a window when either says so.
    /// <para>
    /// Lives in Core so the App's HUD and (Phase 6) the addon runtime share it without referencing each other. Writes
    /// take a lock; the render thread reads <see cref="Hidden"/> without one.
    /// </para>
    /// </summary>
    public sealed class StockUiVisibilityState
    {
        /// <summary>The owner name of the player's own <c>/stockui</c> hides.</summary>
        public const string UserOwner = "user";

        private readonly object _sync = new();
        private readonly Dictionary<string, StockUiElement> _byOwner = new(StringComparer.OrdinalIgnoreCase);
        private volatile int _hidden;

        /// <summary>Every element at least one owner hides.</summary>
        public StockUiElement Hidden => (StockUiElement)_hidden;

        /// <summary>Raised after the combined hidden set changes (outside the lock, on the caller's thread).</summary>
        public event Action? Changed;

        /// <summary>True when any of <paramref name="elements"/> is hidden.</summary>
        public bool IsHidden(StockUiElement elements) => (Hidden & elements) != 0;

        /// <summary>The elements one owner hides.</summary>
        public StockUiElement HiddenBy(string owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            lock (_sync) return _byOwner.TryGetValue(owner, out var set) ? set : StockUiElement.None;
        }

        /// <summary>Owners hiding something, with what each hides (a snapshot).</summary>
        public IReadOnlyList<KeyValuePair<string, StockUiElement>> Owners
        {
            get
            {
                lock (_sync) return new List<KeyValuePair<string, StockUiElement>>(_byOwner);
            }
        }

        /// <summary>Adds elements to an owner's hidden set.</summary>
        public void Hide(string owner, StockUiElement elements) => Update(owner, set => set | elements);

        /// <summary>Removes elements from an owner's hidden set (another owner may still hide them).</summary>
        public void Show(string owner, StockUiElement elements) => Update(owner, set => set & ~elements);

        /// <summary>Replaces an owner's hidden set.</summary>
        public void Set(string owner, StockUiElement elements) => Update(owner, _ => elements);

        /// <summary>Drops everything an owner hides.</summary>
        public void Release(string owner) => Update(owner, _ => StockUiElement.None);

        private void Update(string owner, Func<StockUiElement, StockUiElement> change)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            bool changed;
            lock (_sync)
            {
                _byOwner.TryGetValue(owner, out var before);
                var after = change(before) & StockUiElement.All;
                if (after == StockUiElement.None) _byOwner.Remove(owner);
                else _byOwner[owner] = after;

                var combined = StockUiElement.None;
                foreach (var set in _byOwner.Values) combined |= set;
                changed = (int)combined != _hidden;
                _hidden = (int)combined;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>
        /// Whether the HUD hides a persistent window (<see cref="StockUiWindowIds"/>) because of these flags.
        /// <paramref name="inParty"/> picks <see cref="StockUiElement.Party"/> or <see cref="StockUiElement.Vitals"/> for
        /// the party window.
        /// </summary>
        public static bool HidesWindow(StockUiElement hidden, string windowId, bool inParty)
        {
            if (hidden == StockUiElement.None) return false;
            var element = ElementOf(windowId, inParty);
            return element != StockUiElement.None && (hidden & element) != 0;
        }

        /// <summary>The element a persistent window belongs to (None for an unknown id; ids are the lower-case constants).</summary>
        public static StockUiElement ElementOf(string windowId, bool inParty)
        {
            switch (windowId)
            {
                case StockUiWindowIds.Target: return StockUiElement.TargetBar;
                case StockUiWindowIds.Party: return inParty ? StockUiElement.Party : StockUiElement.Vitals;
                case StockUiWindowIds.Alliance1:
                case StockUiWindowIds.Alliance2: return StockUiElement.Alliance;
                case StockUiWindowIds.StatusIcons: return StockUiElement.Buffs;
                case StockUiWindowIds.Log:
                case StockUiWindowIds.ChatInput: return StockUiElement.Chat;
                case StockUiWindowIds.MainMenu:
                case StockUiWindowIds.Query:
                case StockUiWindowIds.CommandMenu:
                case StockUiWindowIds.Shop:
                case StockUiWindowIds.Death:
                case StockUiWindowIds.Check: return StockUiElement.Menus;
                default: return StockUiElement.None;
            }
        }

        /// <summary>The command names of the elements, in display order.</summary>
        public static readonly IReadOnlyList<(string Name, StockUiElement Element)> Names = new[]
        {
            ("target", StockUiElement.TargetBar),
            ("vitals", StockUiElement.Vitals),
            ("party", StockUiElement.Party),
            ("alliance", StockUiElement.Alliance),
            ("buffs", StockUiElement.Buffs),
            ("menus", StockUiElement.Menus),
            ("chat", StockUiElement.Chat),
            ("names", StockUiElement.NamePlates),
            ("cursor", StockUiElement.TargetCursor),
        };

        /// <summary>
        /// Parses an element name (<see cref="Names"/>, a few aliases, or <c>all</c>), case-insensitively.
        /// </summary>
        public static bool TryParse(string name, out StockUiElement element)
        {
            string key = name.Trim().ToLowerInvariant();
            element = key switch
            {
                "all" => StockUiElement.All,
                "targetbar" => StockUiElement.TargetBar,
                "hp" => StockUiElement.Vitals,
                "status" or "buff" => StockUiElement.Buffs,
                "menu" => StockUiElement.Menus,
                "log" => StockUiElement.Chat,
                "nameplates" or "plates" => StockUiElement.NamePlates,
                "cursors" or "targetcursor" => StockUiElement.TargetCursor,
                _ => StockUiElement.None,
            };
            if (element != StockUiElement.None) return true;
            foreach (var (n, e) in Names)
            {
                if (n == key)
                {
                    element = e;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The command names of a set of elements ("none" when empty).</summary>
        public static string Describe(StockUiElement elements)
        {
            if (elements == StockUiElement.None) return "none";
            var sb = new StringBuilder();
            foreach (var (n, e) in Names)
            {
                if ((elements & e) == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(n);
            }
            return sb.ToString();
        }
    }
}
