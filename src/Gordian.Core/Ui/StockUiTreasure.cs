// src/Gordian.Core/Ui/StockUiTreasure.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The message log lines for treasure pool events, in the retail wording documented in XiPackets
    /// <c>world/server/0x00D2</c> and <c>0x00D3</c> ("You find a fire crystal on the Goblin.", "Ayame's lot for the
    /// fire crystal: 512 points.", "You obtain a fire crystal."). The client prints these itself; the server sends
    /// no text for them.
    /// </summary>
    public sealed class StockUiTreasure
    {
        private readonly Func<ushort, ItemRecord?>? _itemLookup;
        private readonly Func<uint, string?> _entityName;
        private readonly Func<uint> _localId;

        public StockUiTreasure(Func<ushort, ItemRecord?>? itemLookup, Func<uint, string?> entityName, Func<uint> localId)
        {
            _itemLookup = itemLookup;
            _entityName = entityName;
            _localId = localId;
        }

        /// <summary>
        /// "You find 100 gil on the Goblin.", "You find a fire crystal on the Goblin." ("in the" for a container;
        /// no "the" before a named monster). Gil comes first when a packet carries both.
        /// </summary>
        public IEnumerable<string> FormatFound(TreasureFound found)
        {
            string where = (found.IsContainer ? "in " : "on ") + Dropper(found);
            if (found.Gold > 0) yield return $"You find {StockUiShop.FormatGil(found.Gold)} gil {where}.";
            if (found.ItemId != 0)
            {
                yield return $"You find {StockUiShop.DescribeCount(Item(found.ItemId), 1)} {where}.";
            }
        }

        /// <summary>The lines for a lot, pass or judgement; a pass and a silent clear print nothing.</summary>
        public IEnumerable<string> FormatSolution(TreasureSolution solution)
        {
            var item = solution.ItemId != 0 ? Item(solution.ItemId) : null;
            string name = item != null ? StockUiShop.LongName(item) : "item";
            bool leaderIsMe = solution.LeaderId == 0 || solution.LeaderId == _localId();

            switch (solution.Judge)
            {
                case TreasureJudge.Progress:
                    if (solution.EntryIsLot && solution.EntryLot > 0 && solution.EntryName.Length > 0)
                    {
                        yield return $"{solution.EntryName}'s lot for the {name}: {solution.EntryLot} points.";
                    }
                    break;

                case TreasureJudge.Win:
                    string what = item != null ? StockUiShop.DescribeCount(item, 1) : "an item";
                    yield return leaderIsMe ? $"You obtain {what}." : $"{solution.LeaderName} obtains {what}.";
                    break;

                case TreasureJudge.WinError:
                    yield return leaderIsMe
                        ? $"You do not meet the requirements to obtain the {name}."
                        : $"{solution.LeaderName} does not meet the necessary requirements to obtain the {name}.";
                    yield return $"{Capitalize(name)} lost.";
                    break;
            }
        }

        private string Dropper(TreasureFound found)
        {
            string? name = _entityName(found.DropperId);
            if (string.IsNullOrEmpty(name)) return "something";
            return found.DropperNamed ? name : "the " + name;
        }

        private ItemRecord Item(ushort itemId) => StockUiShop.Lookup(_itemLookup, itemId);

        private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
