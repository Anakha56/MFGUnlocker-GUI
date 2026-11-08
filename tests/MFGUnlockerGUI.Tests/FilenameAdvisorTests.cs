using System.Linq;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class FilenameAdvisorTests
    {
        static readonly string[] None = new string[0];

        static CompatEntry Cyberpunk()
        {
            return CompatibilityTable.BuiltIn().Games.Single(g => g.Game == "Cyberpunk 2077");
        }

        [Fact]
        public void TOW2_EmptyUnrealFolder_RecommendsVersion()
        {
            using (var g = FakeGame.Create())
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, None, null);
                Assert.Equal("version.dll", a.Recommended);
                Assert.False(a.BinkAvailable);
                Assert.Empty(a.ModWarnings);
                Assert.Equal(17, a.Options.Count);
            }
        }

        [Fact]
        public void TakenNameSkipped_CaseInsensitive()
        {
            using (var g = FakeGame.Create().File("VERSION.DLL"))
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, None, null);
                Assert.Equal("winmm.dll", a.Recommended);
                var v = a.Options.Single(o => o.Name == "version.dll");
                Assert.False(v.Allowed);
                Assert.Contains("Taken", v.Note);
            }
        }

        [Fact]
        public void CompatUseFirst()
        {
            using (var g = FakeGame.Create())
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unknown, Cyberpunk(), None, null);
                Assert.Equal("xinput9_1_0.dll", a.Recommended);
                Assert.Equal("Recommended (from player reports)", a.Options.First().Note);
            }
        }

        [Fact]
        public void CompatDontUse_NotAllowedButListed()
        {
            using (var g = FakeGame.Create())
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unknown, Cyberpunk(), None, null);
                var v = a.Options.Single(o => o.Name == "version.dll");
                Assert.False(v.Allowed);
                Assert.Equal("Player reports: CET", v.Note);
            }
        }

        [Fact]
        public void FailedNamesSkipped()
        {
            using (var g = FakeGame.Create())
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, new[] { "version.dll" }, null);
                Assert.Equal("winmm.dll", a.Recommended);
                Assert.Equal("Tried, didn't load", a.Options.Single(o => o.Name == "version.dll").Note);
            }
        }

        [Fact]
        public void WorkingNameWins()
        {
            using (var g = FakeGame.Create().File("d3d12.dll"))
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unknown, Cyberpunk(), None, "d3d12.dll");
                Assert.Equal("d3d12.dll", a.Recommended);
            }
        }

        [Fact]
        public void AllFailed_RecommendedNull_BinkOfferedIfPresent()
        {
            using (var g = FakeGame.Create().File("binkw64.dll"))
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, ProxyNames.DefaultOrder.ToList(), null);
                Assert.Null(a.Recommended);
                Assert.True(a.BinkAvailable);
                var b = a.Options.Single(o => o.Name == "binkw64.dll");
                Assert.True(b.Allowed);
                Assert.StartsWith("Bink route", b.Note);
            }
        }

        [Fact]
        public void BinkNotOffered_WhenAbsent()
        {
            using (var g = FakeGame.Create())
            {
                var a = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, None, null);
                Assert.False(a.BinkAvailable);
                Assert.DoesNotContain(a.Options, o => o.Name == "binkw64.dll");
            }
        }

        [Fact]
        public void ReShade_WarnsDxgiMayHideMenu()
        {
            using (var g = FakeGame.Create().File("ReShade.ini"))
            {
                var w = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, None, null).ModWarnings;
                Assert.Single(w);
                Assert.Contains("dxgi", w[0]);
                Assert.Contains("hide", w[0]);
            }
        }

        [Theory]
        [InlineData("reframework", true, "dinput8")]
        [InlineData("red4ext", true, "winmm")]
        [InlineData(@"bin\x64\plugins\cyber_engine_tweaks", true, "version")]
        [InlineData("OptiScaler.ini", false, "dxgi")]
        [InlineData("SpecialK64.dll", false, "dxgi")]
        public void ModWarnings(string marker, bool isDir, string expected)
        {
            using (var g = FakeGame.Create())
            {
                if (isDir) g.Dir(marker); else g.File(marker);
                var w = FilenameAdvisor.Advise(g.Root, Engine.Unreal, null, None, null).ModWarnings;
                Assert.Single(w);
                Assert.Contains(expected, w[0]);
            }
        }

        [Fact]
        public void ModChecks_LookInParentAndGrandparent()
        {
            using (var g = FakeGame.Create().Dir(@"Game\Binaries\Win64").File(@"Game\ReShade.ini"))
            {
                var target = System.IO.Path.Combine(g.Root, @"Game\Binaries\Win64");
                var w = FilenameAdvisor.Advise(target, Engine.Unreal, null, None, null).ModWarnings;
                Assert.Single(w);
            }
        }
    }
}
