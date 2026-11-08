using System.Linq;
using MFGUnlockerGUI.Core;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class CompatibilityTableTests
    {
        static CompatibilityTable T() { return CompatibilityTable.BuiltIn(); }

        [Fact]
        public void Normalize_StripsCaseAndPunctuation()
        {
            Assert.Equal("cyberpunk2077", CompatibilityTable.Normalize("Cyberpunk 2077"));
            Assert.Equal("sts2", CompatibilityTable.Normalize("S.T.S-2!"));
            Assert.Equal("", CompatibilityTable.Normalize(null));
        }

        [Fact]
        public void Parses35Games()
        {
            var t = T();
            Assert.Equal(38, t.Games.Count); // brief said 35; the embedded 2026-11-08 doc has 38 rows
            Assert.Equal("built-in", t.Source);
            var c = t.Games.Single(g => g.Game == "Cyberpunk 2077");
            Assert.Equal(new[] { "xinput9_1_0.dll" }, c.Use);
            Assert.Equal(new[] { "dxgi.dll", "d3d12.dll" }, c.AlsoLoaded);
            Assert.Equal(3, c.DontUse.Count);
            Assert.Equal("CET", c.DontUse.Single(d => d.Name == "version.dll").Reason);
            Assert.Equal("RED4ext", c.DontUse.Single(d => d.Name == "winmm.dll").Reason);
            Assert.Equal("not loaded", c.DontUse.Single(d => d.Name == "dinput8.dll").Reason);
        }

        [Fact]
        public void SharedReason_AppliesToNamesBeforeIt()
        {
            var cd = T().Games.Single(g => g.Game == "Crimson Desert");
            Assert.Equal("not loaded", cd.DontUse.Single(d => d.Name == "version.dll").Reason);
            Assert.Equal("not loaded", cd.DontUse.Single(d => d.Name == "dinput8.dll").Reason);
            Assert.Equal("game's mod loader", cd.DontUse.Single(d => d.Name == "winmm.dll").Reason);
        }

        [Fact]
        public void MSFS2020_UseHasStoreQualifiers()
        {
            var g = T().Games.Single(x => x.Game == "Microsoft Flight Simulator 2020");
            Assert.Contains("version.dll", g.Use);
            Assert.Contains("dinput8.dll", g.Use);
        }

        [Fact]
        public void ControlResonant_BinkRouteKeptAsName()
        {
            var g = T().Games.Single(x => x.Game == "Control Resonant");
            Assert.Contains("bink2w64.dll", g.AlsoLoaded);
        }

        [Fact]
        public void AntiCheatGames_HasFive()
        {
            var a = T().AntiCheatGames;
            Assert.Equal(5, a.Count);
            Assert.Contains("Zenless Zone Zero", a);
            Assert.Contains("Battlefield 6 / RedSec", a);
        }

        [Fact]
        public void Find_ExactNormalized()
        {
            bool exact;
            var e = T().Find(new[] { "Cyberpunk2077" }, out exact);
            Assert.Equal("Cyberpunk 2077", e.Game);
            Assert.True(exact);
        }

        [Fact]
        public void Find_Contains()
        {
            bool exact;
            var e = T().Find(new[] { "Witcher3" }, out exact);
            Assert.Equal("The Witcher 3", e.Game);
            Assert.False(exact);
        }

        [Theory]
        [InlineData("The Witcher 3 Wild Hunt GOTY", "The Witcher 3", true)]
        [InlineData("Dying Light 2 Stay Human", "Dying Light 2", true)]
        [InlineData("S.T.A.L.K.E.R. 2 Heart of Chornobyl", "S.T.A.L.K.E.R. 2", true)]
        [InlineData("Control", "Control Resonant", false)]
        public void Find_StrongAndLooseMatches(string hint, string game, bool expectExact)
        {
            bool exact;
            var e = T().Find(new[] { hint }, out exact);
            Assert.Equal(game, e.Game);
            Assert.Equal(expectExact, exact);
        }

        [Fact]
        public void Find_StrongMatch_PrefersMostTokens()
        {
            var t = CompatibilityTable.Parse("## Games\n| Game | Use |\n|---|---|\n| Dying Light | `version.dll` |\n| Dying Light 2 | `winmm.dll` |\n", "built-in");
            bool exact;
            Assert.Equal("Dying Light 2", t.Find(new[] { "Dying Light 2 Stay Human" }, out exact).Game);
            Assert.True(exact);
        }

        [Fact]
        public void Find_StrongMatch_SingleWordGameNeverStrong()
        {
            var t = CompatibilityTable.Parse("## Games\n| Game | Use |\n|---|---|\n| Rage | `version.dll` |\n", "built-in");
            bool exact;
            var e = t.Find(new[] { "Rage 2" }, out exact);
            Assert.False(exact);
        }

        [Fact]
        public void Find_StrongMatch_SequelIsNotStrong()
        {
            var t = CompatibilityTable.Parse("## Games\n| Game | Use |\n|---|---|\n| Dying Light | `version.dll` |\n", "built-in");
            bool exact;
            t.Find(new[] { "Dying Light 2 Stay Human" }, out exact);
            Assert.False(exact);
            var e = t.Find(new[] { "Dying Light Stay Human" }, out exact);
            Assert.Equal("Dying Light", e.Game);
            Assert.True(exact);
        }

        [Fact]
        public void Find_None()
        {
            bool exact;
            Assert.Null(T().Find(new[] { "The Outer Worlds 2" }, out exact));
            Assert.False(exact);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not markdown at all \0 | | ||| ## Games\n| a |\n|")]
        [InlineData("## Games\n| Game | Use |\n|---|---|\n| X |")]
        public void Parse_GarbageInput_ReturnsEmptyNotThrow(string input)
        {
            var t = CompatibilityTable.Parse(input, "online 2026-01-01");
            Assert.Equal("online 2026-01-01", t.Source);
            Assert.Equal(5, t.AntiCheatGames.Count); // fallback list
            bool exact;
            Assert.Null(t.Find(new[] { "zzzzz" }, out exact));
        }
    }
}
