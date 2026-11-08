using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Linq;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class TroubleshooterTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));
        private readonly AppPaths _paths;
        private readonly FakeGame _game = FakeGame.Create();
        private readonly string _tempDir;

        public TroubleshooterTests()
        {
            Directory.CreateDirectory(_tmp);
            _paths = new AppPaths(Path.Combine(_tmp, "app"));
            _tempDir = Path.Combine(_tmp, "fake-temp");
            Directory.CreateDirectory(_tempDir);
            _game.File("Game.exe");
        }

        public void Dispose()
        {
            _game.Dispose();
            try { Directory.Delete(_tmp, true); } catch { }
        }

        private GameRecord Rec()
        {
            return new GameRecord
            {
                GameRoot = _game.Root, ExePath = _game.Path_("Game.exe"), TargetDir = _game.Root,
                InstalledName = "winmm.dll", Version = "v1", InstalledUtc = DateTime.UtcNow.AddMinutes(-10)
            };
        }

        private bool _running;
        private Troubleshooter T(bool sac = false) { return new Troubleshooter((since, name) => sac, p => _running); }

        // ---- Classify ----

        [Fact]
        public void Classify_IntelPlusLaptop4070_Is20to40()
        {
            Assert.Equal(GpuClass.NvidiaRtx20to40,
                SystemInfo.Classify(new[] { "Intel(R) UHD Graphics", "NVIDIA GeForce RTX 4070 Laptop GPU" }));
        }

        [Theory]
        [InlineData("NVIDIA GeForce RTX 3070", true)]
        [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", true)]
        [InlineData("NVIDIA GeForce RTX 4070", false)]
        [InlineData("NVIDIA GeForce RTX 5080", false)]
        [InlineData("Intel(R) UHD Graphics", false)]
        public void HasRtx30_Matches30xxOnly(string name, bool expected)
        {
            Assert.Equal(expected, SystemInfo.HasRtx30(new[] { name }));
        }

        [Theory]
        [InlineData("NVIDIA GeForce RTX 2060", true)]
        [InlineData("NVIDIA GeForce RTX 3070", true)]
        [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", true)]
        [InlineData("NVIDIA GeForce RTX 4070", false)]
        [InlineData("NVIDIA GeForce RTX 5080", false)]
        [InlineData("Intel(R) UHD Graphics", false)]
        public void HasRtx20or30_Matches20xxAnd30xxOnly(string name, bool expected)
        {
            Assert.Equal(expected, SystemInfo.HasRtx20or30(new[] { name }));
        }

        [Fact]
        public void Classify_Rtx5080_Is50() { Assert.Equal(GpuClass.NvidiaRtx50, SystemInfo.Classify(new[] { "NVIDIA GeForce RTX 5080" })); }

        [Fact]
        public void Classify_Rtx2060_Is20to40() { Assert.Equal(GpuClass.NvidiaRtx20to40, SystemInfo.Classify(new[] { "NVIDIA GeForce RTX 2060" })); }

        [Theory]
        [InlineData("NVIDIA GeForce GTX 1080")]
        [InlineData("AMD Radeon RX 7900 XTX")]
        [InlineData("Intel(R) Arc(TM) A770")]
        [InlineData("NVIDIA RTX A4000")]
        public void Classify_Others_Unsupported(string name)
        {
            Assert.Equal(GpuClass.Unsupported, SystemInfo.Classify(new[] { name }));
        }

        [Fact]
        public void Classify_Empty_Unsupported() { Assert.Equal(GpuClass.Unsupported, SystemInfo.Classify(new string[0])); }

        // ---- menu missing ----

        [Fact]
        public void MenuMissing_SacBlocked_ReturnsBlocked_DoesNotMarkFailed()
        {
            var r = Rec();
            Assert.Equal(LogVerdict.Blocked, T(true).CheckMenuMissing(r, _tempDir));
            Assert.Empty(r.FailedNames);
            Assert.Equal("winmm.dll", r.InstalledName);
        }

        [Fact]
        public void MenuMissing_NoLogAfterInstall_NotLoaded()
        {
            var r = Rec();
            var old = _game.Path_("RTXMFG-old.log");
            File.WriteAllText(old, "x");
            File.SetLastWriteTimeUtc(old, r.InstalledUtc.AddHours(-1));
            Assert.Equal(LogVerdict.NotLoaded, T().CheckMenuMissing(r, _tempDir));
        }

        [Fact]
        public void MenuMissing_LogBesideExe_Loaded()
        {
            var r = Rec();
            File.WriteAllText(_game.Path_("RTXMFG-1.log"), "x");
            Assert.Equal(LogVerdict.Loaded, T().CheckMenuMissing(r, _tempDir));
        }

        [Fact]
        public void MenuMissing_LogInTemp_Loaded()
        {
            var r = Rec();
            File.WriteAllText(Path.Combine(_tempDir, "RTXMFG-1.log"), "x");
            Assert.Equal(LogVerdict.Loaded, T().CheckMenuMissing(r, _tempDir));
        }

        // ---- leftovers / dxgi ----

        [Fact]
        public void LeftoverFile_IsReported_NothingTouched()
        {
            var r = Rec();
            Assert.Null(T().FindLeftoverOrDxgiProblem(r));
            File.WriteAllText(_game.Path_("RTXMFG.dll"), "x");
            var msg = T().FindLeftoverOrDxgiProblem(r);
            Assert.Contains("RTXMFG.dll", msg);
            Assert.True(File.Exists(_game.Path_("RTXMFG.dll")));
        }

        // ---- flip metering ----

        [Fact]
        public void FlipMetering_CreatesSectionAndKey_BacksUpFirst()
        {
            var r = Rec();
            var ini = _game.Path_("RTX40MFG-UI.ini");
            File.WriteAllText(ini, "[Other]\r\nA=1\r\n");
            var state = new StateStore(_paths);
            var b = new BackupManager(_paths, state.Settings);
            T().SetRtx30FlipMetering(r, b);
            var text = File.ReadAllText(ini);
            Assert.Contains("[Compatibility]", text);
            Assert.Contains("Rtx30FlipMetering=0", text);
            Assert.Contains("[Other]", text);
            var backups = b.List(_game.Root);
            Assert.Single(backups);
            Assert.Equal("[Other]\r\nA=1\r\n", File.ReadAllText(Path.Combine(backups[0], "RTX40MFG-UI.ini")));
        }

        [Fact]
        public void FlipMetering_NoIniYet_CreatesIt()
        {
            var r = Rec();
            var state = new StateStore(_paths);
            T().SetRtx30FlipMetering(r, new BackupManager(_paths, state.Settings));
            Assert.Contains("Rtx30FlipMetering=0", File.ReadAllText(_game.Path_("RTX40MFG-UI.ini")));
        }

        [Fact]
        public void FlipMetering_UpdatesExistingKey_KeepsOtherLines()
        {
            var r = Rec();
            var ini = _game.Path_("RTX40MFG-UI.ini");
            File.WriteAllText(ini, "[Compatibility]\nFoo=bar\nrtx30flipmetering=1\nBaz=2\n[Other]\nA=1\n");
            var state = new StateStore(_paths);
            T().SetRtx30FlipMetering(r, new BackupManager(_paths, state.Settings));
            var lines = File.ReadAllLines(ini);
            Assert.Equal(new[] { "[Compatibility]", "Foo=bar", "Rtx30FlipMetering=0", "Baz=2", "[Other]", "A=1" }, lines);
        }

        [Fact]
        public void FlipMetering_GameRunning_RefusesAndChangesNothing()
        {
            var r = Rec();
            var ini = _game.Path_("RTX40MFG-UI.ini");
            File.WriteAllText(ini, "[Other]\nA=1\n");
            _running = true;
            var state = new StateStore(_paths);
            var b = new BackupManager(_paths, state.Settings);
            var msg = T().SetRtx30FlipMetering(r, b);
            Assert.Contains("close the game", msg);
            Assert.Equal("[Other]\nA=1\n", File.ReadAllText(ini));
            Assert.Empty(b.List(_game.Root));
        }

        [Fact]
        public void FlipMetering_AccessDenied_ReturnsAccessDeniedMessage()
        {
            var r = Rec();
            var ini = _game.Path_("RTX40MFG-UI.ini");
            File.WriteAllText(ini, "[Other]\nA=1\n");
            var state = new StateStore(_paths);
            var b = new BackupManager(_paths, state.Settings);
            var dir = new DirectoryInfo(_game.Root);
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                FileSystemRights.CreateFiles | FileSystemRights.WriteData | FileSystemRights.AppendData,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
            var sec = dir.GetAccessControl();
            sec.AddAccessRule(rule);
            dir.SetAccessControl(sec);
            try
            {
                Assert.Equal(Installer.AccessDeniedMessage, T().SetRtx30FlipMetering(r, b));
                Assert.Equal("[Other]\nA=1\n", File.ReadAllText(ini));
            }
            finally
            {
                sec = dir.GetAccessControl();
                sec.RemoveAccessRule(rule);
                dir.SetAccessControl(sec);
            }
        }

        [Fact]
        public void SacAdvice_DoesNotClaimItCannotBeTurnedBackOn()
        {
            Assert.DoesNotContain("resetting Windows", Troubleshooter.SacBlockedAdvice);
        }

        // ---- report ----

        [Fact]
        public void Report_CopiesLogsStatusAndSummary()
        {
            var r = Rec();
            File.WriteAllText(_game.Path_("RTXMFG-1.log"), "log one");
            File.WriteAllText(_game.Path_("RTXMFG-Universal.status.json"), "{}");
            File.WriteAllText(_game.Path_("RTXMFG-Universal.json"), "{private settings}");
            var folder = T().BuildReport(r, _paths, "NVIDIA GeForce RTX 4070", "32.0.15.6094", "v1.2.3");
            Assert.StartsWith(_paths.Reports, folder);
            Assert.True(File.Exists(Path.Combine(folder, "RTXMFG-1.log")));
            Assert.True(File.Exists(Path.Combine(folder, "RTXMFG-Universal.status.json")));
            var summary = File.ReadAllText(Directory.GetFiles(folder, "summary*").Single());
            Assert.Contains(Path.GetFileName(_game.Root), summary);
            Assert.Contains("NVIDIA GeForce RTX 4070", summary);
            Assert.Contains("32.0.15.6094", summary);
            Assert.Contains("winmm.dll", summary);
            Assert.Contains("v1.2.3", summary);
            Assert.True(File.Exists(_game.Path_("RTXMFG-1.log")), "originals stay");
        }
    }
}
