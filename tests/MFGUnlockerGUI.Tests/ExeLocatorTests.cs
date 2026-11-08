using System.Linq;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class ExeLocatorTests
    {
        private const string Shipping = @"Arkansas\Binaries\Win64\TheOuterWorlds2-Win64-Shipping.exe";

        private static FakeGame Tow2()
        {
            return FakeGame.Create()
                .File("TheOuterWorlds2.exe", 364112)
                .TextFile("MicrosoftGame.config", "<Game><ExecutableList><Executable Name=\"TheOuterWorlds2.exe\" Id=\"Game\" /></ExecutableList></Game>")
                .File(Shipping, 2 * 1024 * 1024)
                .File(@"GamingRepair\GamingRepair.exe", 500000)
                .Dir("Engine");
        }

        [Fact]
        public void TOW2Layout_PicksShippingExe()
        {
            using (var g = Tow2())
            {
                var c = ExeLocator.FindCandidates(g.Root);
                Assert.EndsWith("TheOuterWorlds2-Win64-Shipping.exe", c[0].Path);
                Assert.True(ExeLocator.IsConfident(c));
            }
        }

        [Fact]
        public void ExcludesHelpers()
        {
            using (var g = FakeGame.Create())
            {
                g.File("Game.exe", 5000)
                 .File("UnityCrashHandler64.exe", 9000)
                 .File(@"Engine\Binaries\CrashReportClient.exe", 9000)
                 .File("UnrealCEFSubProcess.exe", 9000)
                 .File("unins000.exe", 9000)
                 .File("vc_redist.x64.exe", 9000)
                 .File("EasyAntiCheat_EOS_Setup.exe", 9000)
                 .File(@"GamingRepair\GamingRepair.exe", 9000);
                var names = ExeLocator.FindCandidates(g.Root).Select(x => System.IO.Path.GetFileName(x.Path)).ToList();
                Assert.Equal(new[] { "Game.exe" }, names);
            }
        }

        [Fact]
        public void PlainGame_LargestExeWins()
        {
            using (var g = FakeGame.Create())
            {
                g.File("small.exe", 100).File("big.exe", 5000).File(@"bin\mid.exe", 1000);
                var c = ExeLocator.FindCandidates(g.Root);
                Assert.EndsWith("big.exe", c[0].Path);
            }
        }

        [Fact]
        public void UserPickedWin64Folder_StillFindsExe()
        {
            using (var g = Tow2())
            {
                var c = ExeLocator.FindCandidates(g.Path_(@"Arkansas\Binaries\Win64"));
                Assert.Single(c);
                Assert.EndsWith("TheOuterWorlds2-Win64-Shipping.exe", c[0].Path);
                Assert.True(ExeLocator.IsConfident(c));
            }
        }

        [Fact]
        public void LibraryRootWithTwoGames_NotConfident()
        {
            using (var g = FakeGame.Create())
            {
                g.File(@"GameA\a.exe", 4000).File(@"GameB\b.exe", 3500);
                var c = ExeLocator.FindCandidates(g.Root);
                Assert.Equal(2, c.Count);
                Assert.False(ExeLocator.IsConfident(c));
            }
        }

        [Fact]
        public void EmptyFolder_NoCandidates_NotConfident()
        {
            using (var g = FakeGame.Create())
            {
                var c = ExeLocator.FindCandidates(g.Root);
                Assert.Empty(c);
                Assert.False(ExeLocator.IsConfident(c));
            }
        }

        [Fact]
        public void MissingFolder_ReturnsEmpty()
        {
            Assert.Empty(ExeLocator.FindCandidates(@"C:\definitely\not\here\" + System.Guid.NewGuid()));
        }
    }
}
