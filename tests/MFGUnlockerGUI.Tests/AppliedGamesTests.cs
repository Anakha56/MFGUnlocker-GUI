using System.Linq;
using MFGUnlockerGUI.Core;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class AppliedGamesTests
    {
        private static GameRecord Rec(string root, string name, string ver, string sha = null)
        {
            return new GameRecord { GameRoot = root, InstalledName = name, Version = ver, Sha256 = sha };
        }

        private static TemplateInfo Tpl(string tag, string sha = "aa") { return new TemplateInfo { Tag = tag, Sha256 = sha }; }

        [Fact]
        public void Display_FolderNameDashFileAndVersion()
        {
            Assert.Equal("The Outer Worlds 2 \u2014 version.dll (v1.7.0)",
                AppliedGames.Display(Rec(@"C:\Games\The Outer Worlds 2", "version.dll", "v1.7.0")));
        }

        [Fact]
        public void Display_TrailingSlashAndUnknownVersion()
        {
            Assert.Equal("Game \u2014 winmm.dll (unknown version)", AppliedGames.Display(Rec(@"D:\Stuff\Game\", "winmm.dll", null)));
        }

        [Theory]
        [InlineData("v1.6.0", "v1.7.0", true)]
        [InlineData("v1.9.0", "v1.10.0", true)]
        [InlineData("v1.7.0", "v1.7.0", false)]
        [InlineData("V1.7.0", "v1.7.0", false)]
        [InlineData("v1.8.0", "v1.7.0", false)]
        public void NeedsUpgrade_ComparesVersionTags(string installed, string current, bool expected)
        {
            Assert.Equal(expected, AppliedGames.NeedsUpgrade(Rec("C:\\g", "a.dll", installed), Tpl(current)));
        }

        [Fact]
        public void NeedsUpgrade_NoVersion_UsesHash()
        {
            Assert.True(AppliedGames.NeedsUpgrade(Rec("C:\\g", "a.dll", null, "bb"), Tpl("v1.7.0", "aa")));
            Assert.False(AppliedGames.NeedsUpgrade(Rec("C:\\g", "a.dll", null, "AA"), Tpl("v1.7.0", "aa")));
        }

        [Theory]
        [InlineData("beta-1", "v1.7.0")]
        [InlineData("v1.7.0", "nightly")]
        public void NeedsUpgrade_UnparseableTag_FallsBackToHash(string installed, string current)
        {
            Assert.True(AppliedGames.NeedsUpgrade(Rec("Cg", "a.dll", installed, "bb"), Tpl(current, "aa")));
            Assert.False(AppliedGames.NeedsUpgrade(Rec("Cg", "a.dll", installed, "AA"), Tpl(current, "aa")));
        }

        [Fact]
        public void NeedsUpgrade_NoTemplateOrNoRecord_False()
        {
            Assert.False(AppliedGames.NeedsUpgrade(Rec("C:\\g", "a.dll", "v1"), null));
            Assert.False(AppliedGames.NeedsUpgrade(null, Tpl("v2")));
            Assert.False(AppliedGames.NeedsUpgrade(Rec("C:\\g", null, "v1"), Tpl("v2")));
        }

        [Fact]
        public void Entries_NothingRecordedNothingOpen_Empty()
        {
            Assert.Empty(AppliedGames.BuildEntries(new GameRecord[0], null));
            Assert.Empty(AppliedGames.BuildEntries(null, ""));
        }

        [Fact]
        public void Entries_RecordsOnly_SortedAndRecorded_IgnoresRecordsWithoutInstall()
        {
            var e = AppliedGames.BuildEntries(new[]
            {
                Rec(@"C:\Games\Zed", "version.dll", "v1"),
                Rec(@"C:\Games\Alpha", "winmm.dll", "v1"),
                Rec(@"C:\Games\Forgotten", null, "v1")
            }, null);
            Assert.Equal(2, e.Count);
            Assert.Equal(@"C:\Games\Alpha", e[0].Root);
            Assert.True(e[0].Recorded);
            Assert.Equal("Alpha \u2014 winmm.dll (v1)", e[0].Text);
            Assert.Equal(@"C:\Games\Zed", e[1].Root);
        }

        [Fact]
        public void Entries_OpenUnrecordedGame_GetsNotInstalledYetEntry()
        {
            var e = AppliedGames.BuildEntries(new[] { Rec(@"C:\Games\Alpha", "winmm.dll", "v1") }, @"G:\Games\The Witcher 3 Wild Hunt GOTY\");
            Assert.Equal(2, e.Count);
            var open = e.Single(x => !x.Recorded);
            Assert.Equal("The Witcher 3 Wild Hunt GOTY \u2014 not installed yet", open.Text);
            Assert.Equal(@"G:\Games\The Witcher 3 Wild Hunt GOTY\", open.Root);
        }

        [Fact]
        public void Entries_OpenGameIsRecorded_NoExtraEntry()
        {
            var e = AppliedGames.BuildEntries(new[] { Rec(@"C:\Games\Alpha", "winmm.dll", "v1") }, @"c:\games\alpha\");
            Assert.Single(e);
            Assert.True(e[0].Recorded);
        }

        [Fact]
        public void Entries_OpenGameWithRecordButNoInstall_ShownAsNotInstalledYet()
        {
            var e = AppliedGames.BuildEntries(new[] { Rec(@"C:\Games\Alpha", null, "v1") }, @"C:\Games\Alpha");
            Assert.Single(e);
            Assert.False(e[0].Recorded);
            Assert.Equal("Alpha \u2014 not installed yet", e[0].Text);
        }
    }
}
