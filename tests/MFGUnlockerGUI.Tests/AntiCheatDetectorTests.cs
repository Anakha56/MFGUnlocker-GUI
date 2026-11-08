using System.Linq;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class AntiCheatDetectorTests
    {
        private static readonly string[] List =
        {
            "Battlefield 6 / RedSec", "Forza Horizon 6", "Gears of War: E-Day",
            "Grand Theft Auto V Enhanced", "Zenless Zone Zero"
        };

        private static System.Collections.Generic.IReadOnlyList<AntiCheatFinding> Scan(FakeGame g, params string[] hints)
        {
            return AntiCheatDetector.Scan(g.Root, hints, List);
        }

        [Theory]
        [InlineData(@"EasyAntiCheat\settings.json", "EasyAntiCheat")]
        [InlineData(@"EasyAntiCheat_EOS\Settings.json", "EasyAntiCheat")]
        [InlineData(@"BattlEye\BEClient_x64.dll", "BattlEye")]
        [InlineData(@"BEService_x64.exe", "BattlEye")]
        [InlineData(@"EAAntiCheat.GameServiceLauncher.exe", "EA")]
        [InlineData(@"pb\pbcl.dll", "PunkBuster")]
        [InlineData(@"PunkBuster\pb.dll", "PunkBuster")]
        [InlineData(@"GameGuard\GameMon.des", "GameGuard")]
        [InlineData(@"XIGNCODE\x3.xem", "XIGNCODE")]
        [InlineData(@"FACEIT\client.dll", "FACEIT")]
        [InlineData(@"vgk.sys", "Vanguard")]
        [InlineData(@"mhyprot3.sys", "mhyprot")]
        [InlineData(@"ACE-Base64.dll", "ACE")]
        [InlineData(@"SomeAntiCheat.dll", "Anti-cheat")]
        public void Signature_ReturnsOneFindingNamingIt(string rel, string nameFragment)
        {
            using (var g = FakeGame.Create().File(rel))
            {
                var f = Scan(g);
                Assert.Single(f);
                Assert.Contains(nameFragment, f[0].Name);
                Assert.False(string.IsNullOrEmpty(f[0].Evidence));
            }
        }

        [Fact]
        public void EacInsideHashName_NotFlagged()
        {
            using (var g = FakeGame.Create().File(@"Data\indices\01a871b0f9bded7740b95c056544eac8.index"))
                Assert.Empty(Scan(g));
        }

        [Fact]
        public void NamedGame_Flagged()
        {
            using (var g = FakeGame.Create().File("game.exe"))
            {
                var f = Scan(g, "Zenless Zone Zero");
                Assert.Single(f);
                Assert.Equal("Named in upstream anti-cheat list", f[0].Evidence);
            }
        }

        [Fact]
        public void UnlistedGame_NotFlagged()
        {
            using (var g = FakeGame.Create().File("game.exe"))
                Assert.Empty(Scan(g, "The Outer Worlds 2"));
        }

        [Fact]
        public void SameAntiCheatTwice_OneFinding()
        {
            using (var g = FakeGame.Create().File(@"EasyAntiCheat\a.dll").File(@"Bin\EasyAntiCheat_EOS\b.dll"))
                Assert.Single(Scan(g).Where(x => x.Name.Contains("EasyAntiCheat")));
        }
    }
}
