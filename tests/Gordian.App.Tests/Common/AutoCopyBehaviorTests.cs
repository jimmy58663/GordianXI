// tests/Gordian.App.Tests/Common/AutoCopyBehaviorTests.cs
using System;
using Avalonia.Controls;
using Gordian.App.Common;
using Xunit;

namespace Gordian.App.Tests.Common
{
    public sealed class AutoCopyBehaviorTests : IDisposable
    {
        private string? _lastCopiedText;

        public AutoCopyBehaviorTests()
        {
            AutoCopyBehavior.OnCopiedForTesting = text => _lastCopiedText = text;
        }

        public void Dispose()
        {
            AutoCopyBehavior.OnCopiedForTesting = null;
        }

        [Fact]
        public void AttachedProperty_CanBeSetAndRead()
        {
            var listBox = new ListBox();
            Assert.False(AutoCopyBehavior.GetIsEnabled(listBox));

            AutoCopyBehavior.SetIsEnabled(listBox, true);
            Assert.True(AutoCopyBehavior.GetIsEnabled(listBox));

            AutoCopyBehavior.SetIsEnabled(listBox, false);
            Assert.False(AutoCopyBehavior.GetIsEnabled(listBox));
        }

        [Fact]
        public void AttachHelper_EnablesBehavior()
        {
            var listBox = new ListBox();
            AutoCopyBehavior.Attach(listBox);
            Assert.True(AutoCopyBehavior.GetIsEnabled(listBox));
        }

        [Fact]
        public void TryCopySelectedText_WhenEmptySelection_DoesNotCopy()
        {
            var stb = new SelectableTextBlock
            {
                Text = "Hello World"
            };

            bool copied = AutoCopyBehavior.TryCopySelectedText(stb);

            Assert.False(copied);
            Assert.Null(_lastCopiedText);
        }

        [Fact]
        public void TryCopySelectedText_WhenSelectionPresent_TriggersAutoCopy()
        {
            var stb = new SelectableTextBlock
            {
                Text = "Target Rabbit"
            };
            // Select "Target"
            stb.SelectionStart = 0;
            stb.SelectionEnd = 6;

            Assert.Equal("Target", stb.SelectedText);

            bool copied = AutoCopyBehavior.TryCopySelectedText(stb);

            Assert.True(copied);
            Assert.Equal("Target", _lastCopiedText);
        }
    }
}
