using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class GameAnalyzerTests : IDisposable
    {
        private const string Shipping = @"Arkansas\Binaries\Win64\TheOuterWorlds2-Win64-Shipping.exe";
        // Optional real-game check: set MFGUG_TOW2_DIR to an installed The Outer Worlds 2 folder.
        private static readonly string RealTow2 = Environment.GetEnvironmentVariable("MFGUG_TOW2_DIR") ?? "";
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));
        private readonly AppPaths _paths;

        public GameAnalyzerTests()
        {
            Directory.CreateDirectory(_tmp);
            _paths = new AppPaths(Path.Combine(_tmp, "app"));
        }

        public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

        private void MakeTemplate()
        {
            var dir = Path.Combine(_paths.Templates, "v1");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, TemplateStore.DllName), "dll");
            var info = new TemplateInfo { Tag = "v1", PublishedUtc = new DateTime(2026, 1, 1), Sha256 = Hashing.Sha256Bytes(Encoding.ASCII.GetBytes("dll")) };
            using (var fs = new FileStream(Path.Combine(dir, "template.json"), FileMode.Create))
                new DataContractJsonSerializer(typeof(TemplateInfo)).WriteObject(fs, info);
        }

        private GameAnalyzer Make(bool template = true)
        {
            if (template) MakeTemplate();
            var state = new StateStore(_paths);
            var store = new TemplateStore(_paths, null);
            var inst = new Installer(state, new BackupManager(_paths, state.Settings), store, p => false);
            return new GameAnalyzer(CompatibilityTable.BuiltIn(), state, inst, store);
        }

        private static FakeGame Tow2()
        {
            return FakeGame.Create()
                .File("TheOuterWorlds2.exe", 364112)
                .TextFile("MicrosoftGame.config", "<Game><ExecutableList><Executable Name=\"TheOuterWorlds2.exe\" Id=\"Game\" /></ExecutableList></Game>")
                .File(Shipping, 2 * 1024 * 1024)
                .File(@"Arkansas\Plugins\nvngx_dlssg.dll", 100)
                .Dir("Engine");
        }

        private static void AssertTow2(GameAnalysis a, bool realGame = false)
        {
            Assert.Equal(Engine.Unreal, a.Engine);
            Assert.EndsWith(@"Arkansas\Binaries\Win64", a.TargetDir);
            Assert.Empty(a.AntiCheat);
            Assert.NotEmpty(a.FrameGenFiles);
            Assert.Null(a.Compat);
            if (realGame && (a.Status == InstallStatus.Installed || a.Status == InstallStatus.InstalledByHand))
                Assert.Equal("version.dll", a.InstalledFile); // the real game already has the tool's own version.dll
            else if (realGame && File.Exists(Path.Combine(a.TargetDir, "version.dll")))
                Assert.NotEqual("version.dll", a.Advice.Recommended); // name is taken, so the advisor moves on
            else
                Assert.Equal("version.dll", a.Advice.Recommended);
            if (!realGame || a.Status == InstallStatus.NotInstalled) Assert.True(a.CanInstall, a.BlockReason);
        }

        [Fact]
        public void FakeTOW2_FullAnalysis()
        {
            using (var g = Tow2()) AssertTow2(Make().Analyze(g.Root));
        }

        [Fact]
        public void AntiCheat_BlocksInstall()
        {
            using (var g = Tow2())
            {
                g.File(@"EasyAntiCheat\EasyAntiCheat_EOS.dll", 10);
                var a = Make().Analyze(g.Root);
                Assert.False(a.CanInstall);
                Assert.StartsWith("Anti-cheat found:", a.BlockReason);
            }
        }

        [Fact]
        public void NoTemplate_BlocksInstall()
        {
            using (var g = Tow2())
            {
                var a = Make(false).Analyze(g.Root);
                Assert.False(a.CanInstall);
                Assert.Contains("download", a.BlockReason);
            }
        }

        [Fact]
        public void TwoUnrealProjects_NotConfident()
        {
            using (var g = FakeGame.Create()
                .File(@"GameA\Binaries\Win64\GameA-Win64-Shipping.exe", 3000000)
                .File(@"GameB\Binaries\Win64\GameB-Win64-Shipping.exe", 2000000)
                .Dir("Engine"))
            {
                Assert.False(Make().Analyze(g.Root).ExeConfident);
            }
        }

        [Fact]
        public void Witcher3_FolderName_StrongMatchGivesWinmm()
        {
            using (var g = FakeGame.Create()
                .File(@"The Witcher 3 Wild Hunt GOTY\bin\x64_dx12\witcher3.exe", 3000000)
                .File(@"The Witcher 3 Wild Hunt GOTY\bin\x64\REDprelauncher.exe", 100000))
            {
                var a = Make().Analyze(g.Path_("The Witcher 3 Wild Hunt GOTY"));
                Assert.Equal(Engine.REDengine, a.Engine);
                Assert.Equal("The Witcher 3", a.Compat.Game);
                Assert.True(a.CompatExact);
                Assert.Equal("winmm.dll", a.Advice.Recommended);
            }
        }

        [Fact]
        public void LooseCompatMatch_ShownButDoesNotSteerNames()
        {
            using (var g = FakeGame.Create()
                .File("Game.exe", 3000000)
                .TextFile("MicrosoftGame.config", "<Game><Identity DisplayName=\"Cyberpunk\" /></Game>"))
            {
                var a = Make().Analyze(g.Root);
                Assert.NotNull(a.Compat);
                Assert.False(a.CompatExact);
                Assert.NotEqual("xinput9_1_0.dll", a.Advice.Recommended);
            }
        }

        /// <summary>A Fact that reports as skipped (not a silent pass) when the real game folder is not on this PC.</summary>
        public sealed class RealTow2FactAttribute : FactAttribute
        {
            public RealTow2FactAttribute()
            {
                if (RealTow2.Length == 0 || !Directory.Exists(RealTow2)) Skip = "Set MFGUG_TOW2_DIR to an installed The Outer Worlds 2 folder to run this check.";
            }
        }

        [RealTow2Fact]
        public void RealTOW2_ReadOnly()
        {
            var target = Path.Combine(RealTow2, @"Arkansas\Binaries\Win64");
            Func<string[]> snap = () => Directory.Exists(target)
                ? Directory.GetFiles(target).Select(f => f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks).OrderBy(s => s).ToArray()
                : new string[0];
            var before = snap();
            var a = Make().Analyze(RealTow2);
            AssertTow2(a, true);
            Assert.Equal(before, snap());
        }
    }
}
