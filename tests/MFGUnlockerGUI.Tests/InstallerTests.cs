using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class InstallerTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));
        private readonly AppPaths _paths;
        private readonly FakeGame _game = FakeGame.Create();
        private readonly string _exe;
        private bool _running;

        public InstallerTests()
        {
            Directory.CreateDirectory(_tmp);
            _paths = new AppPaths(Path.Combine(_tmp, "app"));
            _game.File("Game.exe");
            _exe = _game.Path_("Game.exe");
        }

        public void Dispose()
        {
            _game.Dispose();
            try { Directory.Delete(_tmp, true); } catch { }
        }

        private static string Hash(string text) { return Hashing.Sha256Bytes(Encoding.ASCII.GetBytes(text)); }

        /// <summary>Writes a template folder directly (no network) and, optionally, known-release hashes.</summary>
        private void MakeTemplate(string tag, string dllText, DateTime published)
        {
            var dir = Path.Combine(_paths.Templates, tag);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, TemplateStore.DllName), dllText);
            var info = new TemplateInfo { Tag = tag, PublishedUtc = published, Sha256 = Hash(dllText) };
            using (var fs = new FileStream(Path.Combine(dir, "template.json"), FileMode.Create))
                new DataContractJsonSerializer(typeof(TemplateInfo)).WriteObject(fs, info);
        }

        private void KnownReleases(params string[][] tagText)
        {
            File.WriteAllText(_paths.KnownHashesFile,
                string.Concat(tagText.Select(t => Hash(t[1]) + "\t" + t[0] + "\n")), new UTF8Encoding(false));
        }

        private Installer Make(out StateStore state)
        {
            state = new StateStore(_paths);
            var backup = new BackupManager(_paths, state.Settings);
            var store = new TemplateStore(_paths, null);
            return new Installer(state, backup, store, p => _running);
        }

        private Installer Make() { StateStore s; return Make(out s); }

        [Fact]
        public void Install_CopiesTemplate_RecordsHashAndTime()
        {
            MakeTemplate("v1", "DLL-ONE", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            StateStore st; var inst = Make(out st);
            var r = inst.Install(_game.Root, _exe, _game.Root, "winmm.dll");
            Assert.True(r.Ok, r.Message);
            Assert.Equal("DLL-ONE", File.ReadAllText(_game.Path_("winmm.dll")));
            var rec = st.Get(_game.Root);
            Assert.Equal(Hash("DLL-ONE"), rec.Sha256);
            Assert.Equal("v1", rec.Version);
            Assert.Equal("winmm.dll", rec.InstalledName);
            Assert.True((DateTime.UtcNow - rec.InstalledUtc).TotalMinutes < 1);
            Assert.Empty(Directory.GetFiles(_game.Root, "*.tmp"));
            string n;
            Assert.Equal(InstallStatus.Installed, inst.Status(_game.Root, _game.Root, out n));
        }

        [Fact]
        public void Install_TargetNameExists_Refuses()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("winmm.dll"), "GAMES-OWN");
            var r = Make().Install(_game.Root, _exe, _game.Root, "winmm.dll");
            Assert.False(r.Ok);
            Assert.Equal("GAMES-OWN", File.ReadAllText(_game.Path_("winmm.dll")));
        }

        [Fact]
        public void Install_GameRunning_Refuses()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            _running = true;
            var r = Make().Install(_game.Root, _exe, _game.Root, "winmm.dll");
            Assert.False(r.Ok);
            Assert.Contains("close the game", r.Message);
            Assert.False(File.Exists(_game.Path_("winmm.dll")));
        }

        [Fact]
        public void Install_AccessDenied_ReturnsAccessDeniedMessage()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            var dir = new DirectoryInfo(_game.Root);
            var me = WindowsIdentity.GetCurrent().User;
            var rule = new FileSystemAccessRule(me, FileSystemRights.CreateFiles | FileSystemRights.WriteData | FileSystemRights.AppendData,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
            var sec = dir.GetAccessControl();
            sec.AddAccessRule(rule);
            dir.SetAccessControl(sec);
            try
            {
                var r = Make().Install(_game.Root, _exe, _game.Root, "winmm.dll");
                Assert.False(r.Ok);
                Assert.Equal(Installer.AccessDeniedMessage, r.Message);
            }
            finally
            {
                sec = dir.GetAccessControl();
                sec.RemoveAccessRule(rule);
                dir.SetAccessControl(sec);
            }
        }

        [Fact]
        public void Bink_RenamesOriginalToHooked_InstallsUnderOriginalName()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("binkw64.dll"), "ORIGINAL-BINK");
            StateStore st; var inst = Make(out st);
            var r = inst.InstallBink(_game.Root, _exe, _game.Root, "binkw64.dll");
            Assert.True(r.Ok, r.Message);
            Assert.Equal("ORIGINAL-BINK", File.ReadAllText(_game.Path_("binkw64Hooked.dll")));
            Assert.Equal("DLL-ONE", File.ReadAllText(_game.Path_("binkw64.dll")));
            Assert.Equal("binkw64Hooked.dll", st.Get(_game.Root).BinkHookedName);
        }

        [Fact]
        public void MarkFailedAndRename_AddsFailed_RenamesFile()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "version.dll").Ok);
            var r = inst.MarkFailedAndRename(_game.Root, "winmm.dll");
            Assert.True(r.Ok, r.Message);
            Assert.False(File.Exists(_game.Path_("version.dll")));
            Assert.True(File.Exists(_game.Path_("winmm.dll")));
            var rec = st.Get(_game.Root);
            Assert.Contains("version.dll", rec.FailedNames);
            Assert.Equal("winmm.dll", rec.InstalledName);
        }

        [Fact]
        public void Upgrade_SameName_NewHash_MovesLeftovers_KeepsSettings()
        {
            MakeTemplate("v1", "DLL-ONE", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            KnownReleases(new[] { "v1", "DLL-ONE" });
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            MakeTemplate("v2", "DLL-TWO", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(_game.Path_("RTXMFG.dll"), "OLD-LEFTOVER");
            File.WriteAllText(_game.Path_("RTXMFG-Universal.json"), "{settings}");
            inst = Make(out st);
            var r = inst.Upgrade(_game.Root);
            Assert.True(r.Ok, r.Message);
            Assert.Equal("DLL-TWO", File.ReadAllText(_game.Path_("winmm.dll")));
            Assert.Equal(Hash("DLL-TWO"), st.Get(_game.Root).Sha256);
            Assert.Equal("v2", st.Get(_game.Root).Version);
            Assert.False(File.Exists(_game.Path_("RTXMFG.dll")));
            var backups = new BackupManager(_paths, st.Settings).List(_game.Root);
            Assert.Single(backups);
            Assert.Equal("OLD-LEFTOVER", File.ReadAllText(Path.Combine(backups[0], "RTXMFG.dll")));
            Assert.Equal("{settings}", File.ReadAllText(_game.Path_("RTXMFG-Universal.json")));
        }

        [Fact]
        public void Upgrade_InstalledFileUnknownHash_Refuses()
        {
            MakeTemplate("v1", "DLL-ONE", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            File.WriteAllText(_game.Path_("winmm.dll"), "SOMETHING-ELSE");
            MakeTemplate("v2", "DLL-TWO", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            inst = Make(out st);
            var r = inst.Upgrade(_game.Root);
            Assert.False(r.Ok);
            Assert.Equal("SOMETHING-ELSE", File.ReadAllText(_game.Path_("winmm.dll")));
        }

        [Fact]
        public void Uninstall_RemovesOnlyOurFile_RestoresBink_KeepsSettingsByDefault()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("binkw64.dll"), "ORIGINAL-BINK");
            File.WriteAllText(_game.Path_("RTXMFG-Universal.json"), "{settings}");
            File.WriteAllText(_game.Path_("RTXMFG-1.log"), "log");
            File.WriteAllText(_game.Path_("other.dll"), "x");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.InstallBink(_game.Root, _exe, _game.Root, "binkw64.dll").Ok);
            var r = inst.Uninstall(_game.Root, false, false);
            Assert.True(r.Ok, r.Message);
            Assert.Equal("ORIGINAL-BINK", File.ReadAllText(_game.Path_("binkw64.dll")));
            Assert.False(File.Exists(_game.Path_("binkw64Hooked.dll")));
            Assert.True(File.Exists(_game.Path_("other.dll")));
            Assert.True(File.Exists(_game.Path_("RTXMFG-Universal.json")));
            Assert.True(File.Exists(_game.Path_("RTXMFG-1.log")));
            Assert.Null(st.Get(_game.Root));
        }

        private string Listing()
        {
            return string.Join("|", Directory.GetFileSystemEntries(_game.Root, "*", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Substring(_game.Root.Length) + (File.Exists(x) ? ":" + new FileInfo(x).Length : "")));
        }

        [Fact]
        public void Forget_RemovesRecord_ChangesNoFiles()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("other.dll"), "x");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            File.WriteAllText(_game.Path_("winmm.dll"), "LAUNCHER-RESTORED");
            string n;
            Assert.Equal(InstallStatus.RecordButFileChanged, inst.Status(_game.Root, _game.Root, out n));
            var before = Listing();
            var r = inst.Forget(_game.Root);
            Assert.True(r.Ok, r.Message);
            Assert.Null(st.Get(_game.Root));
            Assert.Equal(before, Listing());
            Assert.Equal("LAUNCHER-RESTORED", File.ReadAllText(_game.Path_("winmm.dll")));
            Assert.Equal(InstallStatus.NotInstalled, inst.Status(_game.Root, _game.Root, out n));
        }

        [Fact]
        public void Forget_NoRecord_Fails()
        {
            var r = Make().Forget(_game.Root);
            Assert.False(r.Ok);
        }

        [Fact]
        public void Forget_BinkOriginalStillRenamed_MessageNamesFileAndLeavesIt()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("binkw64.dll"), "ORIGINAL-BINK");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.InstallBink(_game.Root, _exe, _game.Root, "binkw64.dll").Ok);
            File.WriteAllText(_game.Path_("binkw64.dll"), "REPAIRED-BINK"); // launcher repair
            var before = Listing();
            var r = inst.Forget(_game.Root);
            Assert.True(r.Ok, r.Message);
            Assert.Contains("binkw64Hooked.dll", r.Message);
            Assert.Contains("left it in place", r.Message);
            Assert.Contains("launcher's repair", r.Message);
            Assert.Equal(before, Listing());
            Assert.True(File.Exists(_game.Path_("binkw64Hooked.dll")));
            Assert.Null(st.Get(_game.Root));
        }

        [Fact]
        public void Forget_BinkHookedFileAlreadyGone_NoWarning()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("binkw64.dll"), "ORIGINAL-BINK");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.InstallBink(_game.Root, _exe, _game.Root, "binkw64.dll").Ok);
            File.Delete(_game.Path_("binkw64Hooked.dll"));
            var r = inst.Forget(_game.Root);
            Assert.True(r.Ok, r.Message);
            Assert.DoesNotContain("binkw64Hooked.dll", r.Message);
        }

        [Fact]
        public void Uninstall_RemoveSettingsAndLogs_MovesThemToBackup()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("RTXMFG-Universal.json"), "{settings}");
            File.WriteAllText(_game.Path_("RTXMFG-1.log"), "log");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            Assert.True(inst.Uninstall(_game.Root, true, true).Ok);
            Assert.False(File.Exists(_game.Path_("winmm.dll")));
            Assert.False(File.Exists(_game.Path_("RTXMFG-Universal.json")));
            Assert.False(File.Exists(_game.Path_("RTXMFG-1.log")));
            var b = new BackupManager(_paths, st.Settings).List(_game.Root);
            Assert.True(File.Exists(Path.Combine(b.Last(), "RTXMFG-Universal.json")));
        }

        [Fact]
        public void Uninstall_FileChanged_Refuses()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            File.WriteAllText(_game.Path_("winmm.dll"), "MODIFIED");
            var r = inst.Uninstall(_game.Root, false, false);
            Assert.False(r.Ok);
            Assert.True(File.Exists(_game.Path_("winmm.dll")));
            Assert.NotNull(st.Get(_game.Root));
        }

        [Fact]
        public void Status_RecordButFileMissing_UninstallClearsRecord()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            File.Delete(_game.Path_("winmm.dll"));
            string n;
            Assert.Equal(InstallStatus.RecordButFileMissing, inst.Status(_game.Root, _game.Root, out n));
            Assert.True(inst.Uninstall(_game.Root, false, false).Ok);
            Assert.Null(st.Get(_game.Root));
        }

        [Fact]
        public void Status_InstalledByHand_ThenAdopt()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            KnownReleases(new[] { "v1", "DLL-ONE" });
            File.WriteAllText(_game.Path_("winmm.dll"), "DLL-ONE");
            StateStore st; var inst = Make(out st);
            string n;
            Assert.Equal(InstallStatus.InstalledByHand, inst.Status(_game.Root, _game.Root, out n));
            Assert.Equal("winmm.dll", n);
            Assert.True(inst.Adopt(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            Assert.Equal("v1", st.Get(_game.Root).Version);
            Assert.Equal(InstallStatus.Installed, inst.Status(_game.Root, _game.Root, out n));
        }

        [Fact]
        public void Adopt_UnknownFile_Refuses()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            File.WriteAllText(_game.Path_("winmm.dll"), "NOT-OURS");
            StateStore st; var inst = Make(out st);
            Assert.False(inst.Adopt(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            Assert.Null(st.Get(_game.Root));
        }

        [Fact]
        public void InstallBink_RecordWithFilePresent_Refuses_AndAdoptToo()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            KnownReleases(new[] { "v1", "DLL-ONE" });
            File.WriteAllText(_game.Path_("binkw64.dll"), "ORIGINAL-BINK");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.Install(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            var before = st.Get(_game.Root).Sha256;
            var r = inst.InstallBink(_game.Root, _exe, _game.Root, "binkw64.dll");
            Assert.False(r.Ok);
            Assert.Equal("ORIGINAL-BINK", File.ReadAllText(_game.Path_("binkw64.dll")));
            Assert.False(File.Exists(_game.Path_("binkw64Hooked.dll")));
            Assert.False(inst.Adopt(_game.Root, _exe, _game.Root, "winmm.dll").Ok);
            Assert.Equal("winmm.dll", st.Get(_game.Root).InstalledName);
            Assert.Equal(before, st.Get(_game.Root).Sha256);
        }

        [Fact]
        public void RecordButFileMissing_WithHookedOriginal_AllInstallsRefuse_UninstallRestores()
        {
            MakeTemplate("v1", "DLL-ONE", DateTime.UtcNow);
            KnownReleases(new[] { "v1", "DLL-ONE" });
            File.WriteAllText(_game.Path_("binkw64.dll"), "ORIGINAL-BINK");
            StateStore st; var inst = Make(out st);
            Assert.True(inst.InstallBink(_game.Root, _exe, _game.Root, "binkw64.dll").Ok);
            File.Delete(_game.Path_("binkw64.dll"));
            File.WriteAllText(_game.Path_("winmm.dll"), "DLL-ONE");

            var r1 = inst.Install(_game.Root, _exe, _game.Root, "version.dll");
            var r2 = inst.InstallBink(_game.Root, _exe, _game.Root, "bink2w64.dll");
            var r3 = inst.Adopt(_game.Root, _exe, _game.Root, "winmm.dll");
            foreach (var r in new[] { r1, r2, r3 })
            {
                Assert.False(r.Ok);
                Assert.Contains("uninstall first", r.Message);
            }
            Assert.Equal("binkw64Hooked.dll", st.Get(_game.Root).BinkHookedName);
            Assert.False(File.Exists(_game.Path_("version.dll")));
            Assert.True(inst.Uninstall(_game.Root, false, false).Ok);
            Assert.Equal("ORIGINAL-BINK", File.ReadAllText(_game.Path_("binkw64.dll")));
        }
    }
}
