// tests/Gordian.Core.Tests/Resources/KeyItemRealDataTests.cs
using System.IO;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Resolves key-item names against the retail EN key-item table (ROM/175/35), when an install is present (#89).
    /// Ids are from LandSandBoat's key item list; the table stores them sparse and in category order.
    /// </summary>
    public class KeyItemRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(1u, "Zeruhn report")]
        [InlineData(8u, "airship pass")]
        [InlineData(512u, "Moghancement: Fire")]
        [InlineData(1271u, "traverser stone")]
        [InlineData(3072u, "Chocobo companion")]
        public void KeyItemName_ResolvesById(uint keyItemId, string expected)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            Assert.True(rm.TryGetKeyItemName(keyItemId, out var name));
            // Mount key items carry a leading note glyph in the table ("♪Chocobo companion").
            Assert.EndsWith(expected, name);
        }
    }
}
