using System;
using System.Collections.Generic;
using System.IO;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class StateAndBackupTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));
        private readonly AppPaths _paths;

        public StateAndBackupTests()
        {
            Directory.CreateDirectory(_tmp);
            _paths = new AppPaths(Path.Combine(_tmp, "app"));
        }

        public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

        [Fact]
        public void State_RoundTrip()
        {
            var s = new StateStore(_paths);
            s.Save(new GameRecord
            {
                GameRoot = @"C:\Games\X", ExePath = @"C:\Games\X\x.exe", TargetDir = @"C:\Games\X\Bin",
                InstalledName = "dxgi.dll", Version = "v1.7.0", Sha256 = "AB",
                InstalledUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                BinkHookedName = "binkw64Hooked.dll", WorkingName = "version.dll"
            });
            s.Settings.BackupsToKeep = 5;
            s.SaveSettings();
            var s2 = new StateStore(_paths);
            var r = s2.Get(@"C:\Games\X");
            Assert.Equal("dxgi.dll", r.InstalledName);
            Assert.Equal("binkw64Hooked.dll", r.BinkHookedName);
            Assert.Equal("v1.7.0", r.Version);
            Assert.Equal(2026, r.InstalledUtc.Year);
            Assert.Equal(5, s2.Settings.BackupsToKeep);
            Assert.Single(s2.All());
            s2.Remove(@"C:\Games\X");
            Assert.Empty(new StateStore(_paths).All());
        }

        [Fact]
        public void State_KeyIgnoresCase()
        {
            new StateStore(_paths).Save(new GameRecord { GameRoot = @"C:\Games\X" });
            Assert.NotNull(new StateStore(_paths).Get(@"c:\games\x"));
        }

        [Fact]
        public void State_CorruptFile_StartsEmptyAndKeepsCopy()
        {
            File.WriteAllText(_paths.StateFile, "{{{ not json");
            var s = new StateStore(_paths);
            Assert.Empty(s.All());
            Assert.False(File.Exists(_paths.StateFile));
            Assert.Single(Directory.GetFiles(_paths.Root, "state.json.bad-*"));
        }

        [Fact]
        public void Settings_BackupsToKeep_MinimumOne()
        {
            var s = new StateStore(_paths);
            s.Settings.BackupsToKeep = 0;
            s.SaveSettings();
            Assert.Equal(1, new StateStore(_paths).Settings.BackupsToKeep);
        }

        [Fact]
        public void Backup_MovesFilesIntoNewFolder()
        {
            using (var g = FakeGame.Create().TextFile("a.dll", "hello"))
            {
                var src = g.Path_("a.dll");
                var hash = Hashing.Sha256File(src);
                var folder = new BackupManager(_paths, new AppSettings()).NewBackup(g.Root, new[] { src });
                Assert.False(File.Exists(src));
                Assert.Equal(hash, Hashing.Sha256File(Path.Combine(folder, "a.dll")));
            }
        }

        [Fact]
        public void Backup_CopyBackup_KeepsOriginal()
        {
            using (var g = FakeGame.Create().TextFile("a.dll", "hello"))
            {
                var folder = new BackupManager(_paths, new AppSettings()).CopyBackup(g.Root, g.Path_("a.dll"));
                Assert.True(File.Exists(g.Path_("a.dll")));
                Assert.True(File.Exists(Path.Combine(folder, "a.dll")));
            }
        }

        [Fact]
        public void Backup_KeepsThree_DeletesOldestOnlyAfterNewWritten()
        {
            using (var g = FakeGame.Create())
            {
                var bm = new BackupManager(_paths, new AppSettings { BackupsToKeep = 3 });
                var made = new List<string>();
                for (int i = 0; i < 4; i++)
                {
                    g.TextFile("f.dll", "v" + i);
                    made.Add(bm.NewBackup(g.Root, new[] { g.Path_("f.dll") }));
                }
                var left = bm.List(g.Root);
                Assert.Equal(3, left.Count);
                Assert.DoesNotContain(made[0], left);
                Assert.Contains(made[3], left);
                Assert.Equal("v3", File.ReadAllText(Path.Combine(made[3], "f.dll")));
            }
        }

        [Fact]
        public void Backup_NeverOverwritesExisting()
        {
            using (var g = FakeGame.Create())
            {
                var bm = new BackupManager(_paths, new AppSettings { BackupsToKeep = 10 });
                g.TextFile("f.dll", "one");
                var a = bm.NewBackup(g.Root, new[] { g.Path_("f.dll") });
                g.TextFile("f.dll", "two");
                var b = bm.NewBackup(g.Root, new[] { g.Path_("f.dll") });
                Assert.NotEqual(a, b);
                Assert.Equal(2, bm.List(g.Root).Count);
                Assert.Equal("one", File.ReadAllText(Path.Combine(a, "f.dll")));
            }
        }

        [Fact]
        public void Backup_CopyFails_OldOnesUntouched()
        {
            using (var g = FakeGame.Create().TextFile("f.dll", "one"))
            {
                var bm = new BackupManager(_paths, new AppSettings { BackupsToKeep = 1 });
                var a = bm.NewBackup(g.Root, new[] { g.Path_("f.dll") });
                Assert.ThrowsAny<IOException>(() => bm.NewBackup(g.Root, new[] { g.Path_("missing.dll") }));
                Assert.Single(bm.List(g.Root));
                Assert.True(File.Exists(Path.Combine(a, "f.dll")));
            }
        }
    }
}
