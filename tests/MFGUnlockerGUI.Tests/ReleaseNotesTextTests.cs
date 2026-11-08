using MFGUnlockerGUI.Core;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class ReleaseNotesTextTests
    {
        [Fact] public void StripsDoubleAsterisks() { Assert.Equal("bold text", ReleaseNotesText.ToPlain("**bold** text")); }
        [Fact] public void StripsDoubleUnderscores() { Assert.Equal("bold text", ReleaseNotesText.ToPlain("__bold__ text")); }
        [Fact] public void StripsBackticks() { Assert.Equal("press Backspace now", ReleaseNotesText.ToPlain("press `Backspace` now")); }

        [Fact]
        public void StripsLeadingHashes()
        {
            Assert.Equal("What's new\nFixes", ReleaseNotesText.ToPlain("## What's new\n###Fixes"));
        }

        [Fact]
        public void KeepsHashesInsideALine() { Assert.Equal("issue #12 fixed", ReleaseNotesText.ToPlain("issue #12 fixed")); }

        [Fact]
        public void TurnsLinksIntoTextAndUrl()
        {
            Assert.Equal("see the guide (https://example.com/a) here", ReleaseNotesText.ToPlain("see [the guide](https://example.com/a) here"));
        }

        [Fact]
        public void Real170Start()
        {
            Assert.Equal("V1.7.0 adds DLSS 5 to the menu, and more.", ReleaseNotesText.ToPlain("**V1.7.0 adds DLSS 5 to the menu, and more.**"));
        }

        [Fact]
        public void NormalisesLineBreaks_AndHandlesNull()
        {
            Assert.Equal("a\nb", ReleaseNotesText.ToPlain("a\r\nb"));
            Assert.Equal("", ReleaseNotesText.ToPlain(null));
        }
    }
}
