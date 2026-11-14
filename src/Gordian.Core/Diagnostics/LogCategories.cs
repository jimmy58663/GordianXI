using System;
using System.Collections.Generic;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// The category names <see cref="GordianLog"/> call sites use, all <c>UPPER_SNAKE</c>, plus which ones are network
    /// categories and the built-in minimum levels for the noisy ones. Add a constant here rather than passing a new
    /// string literal, and list it in <c>docs/app/logging.md</c>.
    /// </summary>
    public static class LogCategories
    {
        /// <summary><c>ACTION</c>.</summary>
        public const string Action = "ACTION";

        /// <summary><c>ANIM</c>.</summary>
        public const string Anim = "ANIM";

        /// <summary><c>APP</c>.</summary>
        public const string App = "APP";

        /// <summary><c>AUCTION</c>.</summary>
        public const string Auction = "AUCTION";

        /// <summary><c>AUDIO</c>.</summary>
        public const string Audio = "AUDIO";

        /// <summary><c>AVALONIA</c>.</summary>
        public const string Avalonia = "AVALONIA";

        /// <summary><c>BAZAAR</c>.</summary>
        public const string Bazaar = "BAZAAR";

        /// <summary><c>BLACKLIST</c>.</summary>
        public const string Blacklist = "BLACKLIST";

        /// <summary><c>BLUE</c>.</summary>
        public const string Blue = "BLUE";

        /// <summary><c>CHAT</c>.</summary>
        public const string Chat = "CHAT";

        /// <summary><c>CHAT_UI</c>.</summary>
        public const string ChatUi = "CHAT_UI";

        /// <summary><c>CHOCOBO</c>.</summary>
        public const string Chocobo = "CHOCOBO";

        /// <summary><c>CLI</c>.</summary>
        public const string Cli = "CLI";

        /// <summary><c>CLIPBOARD</c>.</summary>
        public const string Clipboard = "CLIPBOARD";

        /// <summary><c>COLLISION</c>.</summary>
        public const string Collision = "COLLISION";

        /// <summary><c>COMBAT</c>.</summary>
        public const string Combat = "COMBAT";

        /// <summary><c>CONQUEST</c>.</summary>
        public const string Conquest = "CONQUEST";

        /// <summary><c>CRAFT</c>.</summary>
        public const string Craft = "CRAFT";

        /// <summary><c>CRYPTO</c>.</summary>
        public const string Crypto = "CRYPTO";

        /// <summary><c>DELIVERY</c>.</summary>
        public const string Delivery = "DELIVERY";

        /// <summary><c>DIALOG</c>.</summary>
        public const string Dialog = "DIALOG";

        /// <summary><c>DISPATCHER</c>.</summary>
        public const string Dispatcher = "DISPATCHER";

        /// <summary><c>EFFECT</c>.</summary>
        public const string Effect = "EFFECT";

        /// <summary><c>ELEVATOR</c>.</summary>
        public const string Elevator = "ELEVATOR";

        /// <summary><c>EMOTE</c>.</summary>
        public const string Emote = "EMOTE";

        /// <summary><c>ENTITY</c>.</summary>
        public const string Entity = "ENTITY";

        /// <summary><c>EQUIPSET</c>.</summary>
        public const string Equipset = "EQUIPSET";

        /// <summary><c>EVENT</c>.</summary>
        public const string Event = "EVENT";

        /// <summary><c>FISHING</c>.</summary>
        public const string Fishing = "FISHING";

        /// <summary><c>FRIENDPASS</c>.</summary>
        public const string Friendpass = "FRIENDPASS";

        /// <summary><c>GFX</c>.</summary>
        public const string Gfx = "GFX";

        /// <summary><c>GPU_TEX</c>.</summary>
        public const string GpuTex = "GPU_TEX";

        /// <summary><c>GRAPHICS</c>.</summary>
        public const string Graphics = "GRAPHICS";

        /// <summary><c>GUILD</c>.</summary>
        public const string Guild = "GUILD";

        /// <summary><c>INPUT</c>.</summary>
        public const string Input = "INPUT";

        /// <summary><c>INSPECT</c>.</summary>
        public const string Inspect = "INSPECT";

        /// <summary><c>INVENTORY</c>.</summary>
        public const string Inventory = "INVENTORY";

        /// <summary><c>INVENTORY_UI</c>.</summary>
        public const string InventoryUi = "INVENTORY_UI";

        /// <summary><c>ITEM</c>.</summary>
        public const string Item = "ITEM";

        /// <summary><c>ITEMSEARCH</c>.</summary>
        public const string Itemsearch = "ITEMSEARCH";

        /// <summary><c>KEYITEM</c>.</summary>
        public const string Keyitem = "KEYITEM";

        /// <summary><c>LIFECYCLE</c>.</summary>
        public const string Lifecycle = "LIFECYCLE";

        /// <summary><c>LINKSHELL</c>.</summary>
        public const string Linkshell = "LINKSHELL";

        /// <summary><c>LOBBY</c>.</summary>
        public const string Lobby = "LOBBY";

        /// <summary><c>LOCK_ON</c>.</summary>
        public const string LockOn = "LOCK_ON";

        /// <summary><c>LOCKSTYLE</c>.</summary>
        public const string Lockstyle = "LOCKSTYLE";

        /// <summary><c>LOCOMOTION</c>.</summary>
        public const string Locomotion = "LOCOMOTION";

        /// <summary><c>LOGGING</c>.</summary>
        public const string Logging = "LOGGING";

        /// <summary><c>LSB_LOGIN</c>.</summary>
        public const string LsbLogin = "LSB_LOGIN";

        /// <summary><c>MACRO</c>.</summary>
        public const string Macro = "MACRO";

        /// <summary><c>MOGHOUSE</c>.</summary>
        public const string Moghouse = "MOGHOUSE";

        /// <summary><c>NET</c>.</summary>
        public const string Net = "NET";

        /// <summary><c>PARSER</c>.</summary>
        public const string Parser = "PARSER";

        /// <summary><c>PARTY</c>.</summary>
        public const string Party = "PARTY";

        /// <summary><c>PROFILE</c>.</summary>
        public const string Profile = "PROFILE";

        /// <summary><c>PROGRESSION</c>.</summary>
        public const string Progression = "PROGRESSION";

        /// <summary><c>RES</c>.</summary>
        public const string Res = "RES";

        /// <summary><c>RETAIL_LOGIN</c>.</summary>
        public const string RetailLogin = "RETAIL_LOGIN";

        /// <summary><c>ROE</c>.</summary>
        public const string Roe = "ROE";

        /// <summary><c>SCHEDULER</c>.</summary>
        public const string Scheduler = "SCHEDULER";

        /// <summary><c>SEARCH</c>.</summary>
        public const string Search = "SEARCH";

        /// <summary><c>SESSION</c>.</summary>
        public const string Session = "SESSION";

        /// <summary><c>SHOP</c>.</summary>
        public const string Shop = "SHOP";

        /// <summary><c>STATUS</c>.</summary>
        public const string Status = "STATUS";

        /// <summary><c>SUB_AREA</c>.</summary>
        public const string SubArea = "SUB_AREA";

        /// <summary><c>SYSMES</c>.</summary>
        public const string Sysmes = "SYSMES";

        /// <summary><c>TRADE</c>.</summary>
        public const string Trade = "TRADE";

        /// <summary><c>TREASURE</c>.</summary>
        public const string Treasure = "TREASURE";

        /// <summary><c>UI</c>.</summary>
        public const string Ui = "UI";

        /// <summary><c>UNITY</c>.</summary>
        public const string Unity = "UNITY";

        /// <summary><c>VFS</c>.</summary>
        public const string Vfs = "VFS";

        /// <summary><c>VIEWPORT</c>.</summary>
        public const string Viewport = "VIEWPORT";

        /// <summary><c>VIEWPORT_MANAGER</c>.</summary>
        public const string ViewportManager = "VIEWPORT_MANAGER";

        /// <summary><c>VOTE</c>.</summary>
        public const string Vote = "VOTE";

        /// <summary><c>WIDESCAN</c>.</summary>
        public const string Widescan = "WIDESCAN";

        /// <summary><c>WINDOW</c>.</summary>
        public const string Window = "WINDOW";

        /// <summary>
        /// <c>WIRE</c>: the bounded wire trace of the first 20 map datagrams each way after login and each zone line
        /// (headers and sub-packets). Not a network category, so it stays in the system log at Debug for login debugging.
        /// </summary>
        public const string Wire = "WIRE";

        /// <summary><c>WORLD</c>.</summary>
        public const string World = "WORLD";

        /// <summary><c>ZONE</c>.</summary>
        public const string Zone = "ZONE";

        /// <summary>
        /// Network categories: their detail (below <see cref="LogLevel.Info"/>) goes only to <c>logs/network/</c>, and
        /// only when the network file is on; their milestones go to the system log as usual.
        /// </summary>
        public static readonly IReadOnlyList<string> Network = new[] { Net, Parser, Crypto, Dispatcher };

        /// <summary>
        /// Built-in minimum levels for categories that would otherwise flood the system log; the user's
        /// <see cref="LoggingSettings.Categories"/> override them. Calibrated from a 2026-11-13 play session log
        /// (680k lines: LOCOMOTION 149k Info lines, GRAPHICS 50k and ENTITY 19k Debug lines, GPU_TEX 6.5k).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, LogLevel> DefaultLevels = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase)
        {
            [Locomotion] = LogLevel.Warn,
            [Graphics] = LogLevel.Info,
            [Entity] = LogLevel.Info,
            [GpuTex] = LogLevel.Info,
        };

        /// <summary>Whether <paramref name="category"/> is one of the <see cref="Network"/> categories.</summary>
        public static bool IsNetwork(string category) =>
            string.Equals(category, Net, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, Parser, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, Crypto, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, Dispatcher, StringComparison.OrdinalIgnoreCase);
    }
}
