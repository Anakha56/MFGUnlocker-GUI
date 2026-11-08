using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    /// <summary>The window's flow, driven through Core against a fake game and a temp app folder.</summary>
    public class WindowFlowTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

        [Fact]
        public void Browse_Install_MenuMissing_Rename_Uninstall()
        {
            Directory.CreateDirectory(_tmp);
            var paths = new AppPaths(Path.Combine(_tmp, "app"));
            var dir = Path.Combine(paths.Templates, "v1");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, TemplateStore.DllName), "dll");
            using (var fs = new FileStream(Path.Combine(dir, "template.json"), FileMode.Create))
                new DataContractJsonSerializer(typeof(TemplateInfo)).WriteObject(fs, new TemplateInfo
                {
                    Tag = "v1", PublishedUtc = new DateTime(2026, 1, 1),
                    Sha256 = Hashing.Sha256Bytes(Encoding.ASCII.GetBytes("dll"))
                });

            using (var game = FakeGame.Create()
                .File("TheOuterWorlds2.exe", 364112)
                .File(@"Arkansas\Binaries\Win64\TheOuterWorlds2-Win64-Shipping.exe", 2 * 1024 * 1024)
                .File(@"Arkansas\Plugins\nvngx_dlssg.dll", 100)
                .Dir("Engine"))
            {
                var state = new StateStore(paths);
                var store = new TemplateStore(paths, null);
                var inst = new Installer(state, new BackupManager(paths, state.Settings), store, p => false);
                var analyzer = new GameAnalyzer(CompatibilityTable.BuiltIn(), state, inst, store);
                var ts = new Troubleshooter((since, name) => false, p => false);

                // browse -> analyze
                var a = analyzer.Analyze(game.Root);
                Assert.True(a.CanInstall, a.BlockReason);
                Assert.Equal(InstallStatus.NotInstalled, a.Status);

                // install
                var name = a.Advice.Recommended;
                var r = inst.Install(a.GameRoot, a.ChosenExe.Path, a.TargetDir, name);
                Assert.True(r.Ok, r.Message);
                var rec = new StateStore(paths).Get(game.Root);
                Assert.Equal(name, rec.InstalledName);
                Assert.True(File.Exists(Path.Combine(a.TargetDir, name)));
                Assert.Equal(InstallStatus.Installed, analyzer.Analyze(game.Root).Status);

                // menu missing -> nothing loaded -> try the next name
                Assert.Equal(LogVerdict.NotLoaded, ts.CheckMenuMissing(rec, Path.Combine(_tmp, "emptytemp")));
                var next = analyzer.Analyze(game.Root).Advice.Recommended;
                Assert.NotEqual(name, next);
                var rn = inst.MarkFailedAndRename(game.Root, next);
                Assert.True(rn.Ok, rn.Message);
                var rec2 = new StateStore(paths).Get(game.Root);
                Assert.Equal(next, rec2.InstalledName);
                Assert.Contains(name, rec2.FailedNames);
                Assert.False(File.Exists(Path.Combine(a.TargetDir, name)));
                Assert.True(File.Exists(Path.Combine(a.TargetDir, next)));

                // uninstall
                var un = inst.Uninstall(game.Root, false, false);
                Assert.True(un.Ok, un.Message);
                Assert.Null(new StateStore(paths).Get(game.Root));
                Assert.False(File.Exists(Path.Combine(a.TargetDir, next)));
                Assert.Equal(InstallStatus.NotInstalled, analyzer.Analyze(game.Root).Status);
            }
        }
    }
}
