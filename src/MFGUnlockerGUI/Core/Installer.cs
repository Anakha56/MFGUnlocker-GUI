using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFGUnlockerGUI.Core
{
    public class InstallResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public static InstallResult Good(string m) { return new InstallResult { Ok = true, Message = m }; }
        public static InstallResult Fail(string m) { return new InstallResult { Ok = false, Message = m }; }
    }

    public enum InstallStatus { NotInstalled, Installed, InstalledByHand, RecordButFileMissing, RecordButFileChanged }

    public class Installer
    {
        public const string AccessDeniedMessage = "Windows won't let MFGUnlocker GUI change files in this folder. Some game folders (often Microsoft Store / Game Pass) are protected. If you want to try anyway: close MFGUnlocker GUI, right-click it and choose \"Run as administrator\". That is your choice; the tool never asks for admin rights itself.";
        public const string SettingsFileName = "RTXMFG-Universal.json";

        private readonly StateStore _state;
        private readonly BackupManager _backup;
        private readonly TemplateStore _templates;
        private readonly Func<string, bool> _isRunning;

        public Installer(StateStore s, BackupManager b, TemplateStore t, Func<string, bool> isRunning = null)
        {
            _state = s; _backup = b; _templates = t;
            _isRunning = isRunning ?? GameProcess.IsRunning;
        }

        // ---------- status ----------

        public InstallStatus Status(string gameRoot, string targetDir, out string fileName)
        {
            fileName = null;
            var rec = _state.Get(gameRoot);
            if (rec != null && !string.IsNullOrEmpty(rec.InstalledName))
            {
                fileName = rec.InstalledName;
                var p = Path.Combine(rec.TargetDir ?? targetDir, rec.InstalledName);
                if (!File.Exists(p)) return InstallStatus.RecordButFileMissing;
                return Same(Hashing.Sha256File(p), rec.Sha256) ? InstallStatus.Installed : InstallStatus.RecordButFileChanged;
            }
            if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                foreach (var n in ProxyNames.DefaultOrder.Concat(ProxyNames.BinkNames))
                {
                    var p = Path.Combine(targetDir, n);
                    if (File.Exists(p) && IsKnown(Hashing.Sha256File(p), null)) { fileName = n; return InstallStatus.InstalledByHand; }
                }
            return InstallStatus.NotInstalled;
        }

        public IReadOnlyList<string> FindLeftovers(string targetDir)
        {
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir)) return new List<string>();
            return ProxyNames.OldLeftovers.Select(n => Path.Combine(targetDir, n)).Where(File.Exists).ToList();
        }

        // ---------- install ----------

        public InstallResult Install(string gameRoot, string exePath, string targetDir, string name)
        {
            return Guard(() =>
            {
                var stop = Preflight(exePath, targetDir);
                if (stop != null) return stop;
                if (!ProxyNames.IsSupported(name) || ProxyNames.BinkNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return InstallResult.Fail(name + " is not one of the supported file names.");
                var rec = _state.Get(gameRoot);
                var blocked = ExistingRecordBlocks(rec, targetDir);
                if (blocked != null) return blocked;
                var target = Path.Combine(targetDir, name);
                if (File.Exists(target) || Directory.Exists(target))
                    return InstallResult.Fail(name + " already exists in this folder, so nothing was installed. This tool never overwrites a file it did not put there.");
                var t = _templates.Current;
                if (t == null) return InstallResult.Fail("No MFG Unlocker download is available yet. Check for updates first.");

                var hash = PlaceTemplate(t, target);
                _state.Save(NewRecord(gameRoot, exePath, targetDir, name, t.Tag, hash, null, rec));
                return InstallResult.Good("Installed MFG Unlocker " + t.Tag + " as " + name + ".");
            });
        }

        public InstallResult InstallBink(string gameRoot, string exePath, string targetDir, string binkName)
        {
            return Guard(() =>
            {
                var stop = Preflight(exePath, targetDir);
                if (stop != null) return stop;
                if (!ProxyNames.BinkNames.Contains(binkName, StringComparer.OrdinalIgnoreCase))
                    return InstallResult.Fail(binkName + " is not a Bink file name this tool handles.");
                var blocked = ExistingRecordBlocks(_state.Get(gameRoot), targetDir);
                if (blocked != null) return blocked;
                var original = Path.Combine(targetDir, binkName);
                if (!File.Exists(original)) return InstallResult.Fail(binkName + " was not found in this folder.");
                if (IsKnown(Hashing.Sha256File(original), null))
                    return InstallResult.Fail(binkName + " is already the MFG Unlocker file. Nothing to do.");
                var hookedName = Path.GetFileNameWithoutExtension(binkName) + "Hooked.dll";
                var hooked = Path.Combine(targetDir, hookedName);
                if (File.Exists(hooked) || Directory.Exists(hooked))
                    return InstallResult.Fail(hookedName + " already exists, so the Bink route was not used.");
                var t = _templates.Current;
                if (t == null) return InstallResult.Fail("No MFG Unlocker download is available yet. Check for updates first.");

                File.Move(original, hooked); // the only game-file rename this tool ever does
                string hash;
                try { hash = PlaceTemplate(t, original); }
                catch
                {
                    try { if (!File.Exists(original)) File.Move(hooked, original); } catch { }
                    throw;
                }
                _state.Save(NewRecord(gameRoot, exePath, targetDir, binkName, t.Tag, hash, hookedName, _state.Get(gameRoot)));
                return InstallResult.Good("Installed MFG Unlocker " + t.Tag + " as " + binkName + "; the original was kept as " + hookedName + ".");
            });
        }

        public InstallResult MarkFailedAndRename(string gameRoot, string nextName)
        {
            return Guard(() =>
            {
                var rec = _state.Get(gameRoot);
                if (rec == null || string.IsNullOrEmpty(rec.InstalledName)) return InstallResult.Fail("MFG Unlocker is not installed by this tool in this game.");
                var stop = Preflight(rec.ExePath, rec.TargetDir);
                if (stop != null) return stop;
                if (!string.IsNullOrEmpty(rec.BinkHookedName)) return InstallResult.Fail("The Bink route uses a fixed file name, so it cannot be renamed.");
                if (!ProxyNames.IsSupported(nextName) || ProxyNames.BinkNames.Contains(nextName, StringComparer.OrdinalIgnoreCase))
                    return InstallResult.Fail(nextName + " is not one of the supported file names.");
                var cur = Path.Combine(rec.TargetDir, rec.InstalledName);
                if (!File.Exists(cur)) return InstallResult.Fail(rec.InstalledName + " is no longer in the folder.");
                if (!IsKnown(Hashing.Sha256File(cur), rec)) return Changed(rec.InstalledName);
                var dest = Path.Combine(rec.TargetDir, nextName);
                if (File.Exists(dest) || Directory.Exists(dest)) return InstallResult.Fail(nextName + " already exists in this folder, so nothing was renamed.");

                File.Move(cur, dest); // our own file, to an unused name
                if (!rec.FailedNames.Contains(rec.InstalledName, StringComparer.OrdinalIgnoreCase)) rec.FailedNames.Add(rec.InstalledName);
                rec.InstalledName = nextName;
                _state.Save(rec);
                return InstallResult.Good("Renamed to " + nextName + ".");
            });
        }

        public InstallResult Upgrade(string gameRoot)
        {
            return Guard(() =>
            {
                var rec = _state.Get(gameRoot);
                if (rec == null || string.IsNullOrEmpty(rec.InstalledName)) return InstallResult.Fail("MFG Unlocker is not installed by this tool in this game.");
                var stop = Preflight(rec.ExePath, rec.TargetDir);
                if (stop != null) return stop;
                var cur = Path.Combine(rec.TargetDir, rec.InstalledName);
                if (!File.Exists(cur)) return InstallResult.Fail(rec.InstalledName + " is no longer in the folder.");
                var curHash = Hashing.Sha256File(cur);
                if (!IsKnown(curHash, rec)) return Changed(rec.InstalledName);
                var t = _templates.Current;
                if (t == null) return InstallResult.Fail("No MFG Unlocker download is available yet. Check for updates first.");
                if (Same(curHash, Hashing.Sha256File(t.DllPath)))
                    return InstallResult.Good("Already up to date (" + t.Tag + ").");

                var leftovers = FindLeftovers(rec.TargetDir);
                string newHash;
                var tmp = StageTemplate(t, rec.TargetDir, out newHash);
                try
                {
                    if (leftovers.Count > 0) _backup.NewBackup(gameRoot, leftovers); // moved, never deleted
                    File.Replace(tmp, cur, null); // replaces only our own verified file
                    tmp = null;
                }
                finally { if (tmp != null) try { File.Delete(tmp); } catch { } }

                rec.Sha256 = newHash; rec.Version = t.Tag; rec.InstalledUtc = DateTime.UtcNow;
                _state.Save(rec);
                return InstallResult.Good("Upgraded to " + t.Tag + (leftovers.Count > 0 ? "; old files were moved to a backup." : "."));
            });
        }

        public InstallResult Uninstall(string gameRoot, bool removeSettings, bool removeLogs)
        {
            return Guard(() =>
            {
                var rec = _state.Get(gameRoot);
                if (rec == null || string.IsNullOrEmpty(rec.InstalledName)) return InstallResult.Fail("MFG Unlocker is not installed by this tool in this game.");
                var stop = Preflight(rec.ExePath, rec.TargetDir);
                if (stop != null) return stop;
                var cur = Path.Combine(rec.TargetDir, rec.InstalledName);
                var hooked = string.IsNullOrEmpty(rec.BinkHookedName) ? null : Path.Combine(rec.TargetDir, rec.BinkHookedName);
                string note;

                if (!File.Exists(cur))
                {
                    note = "The file was already gone; the record was cleared.";
                    if (hooked != null && File.Exists(hooked) && !Directory.Exists(cur))
                    {
                        File.Move(hooked, cur);
                        note += " The original " + rec.InstalledName + " was put back.";
                    }
                }
                else
                {
                    if (!IsKnown(Hashing.Sha256File(cur), rec)) return Changed(rec.InstalledName);
                    File.Delete(cur); // our own verified file
                    note = "Removed " + rec.InstalledName + ".";
                    if (hooked != null)
                    {
                        if (File.Exists(hooked)) { File.Move(hooked, cur); note += " The original was put back."; }
                        else note += " " + rec.BinkHookedName + " was not found, so the original could not be put back.";
                    }
                }

                var extra = new List<string>();
                if (removeSettings) { var s = Path.Combine(rec.TargetDir, SettingsFileName); if (File.Exists(s)) extra.Add(s); }
                if (removeLogs && Directory.Exists(rec.TargetDir)) extra.AddRange(Directory.GetFiles(rec.TargetDir, "RTXMFG-*.log"));
                if (extra.Count > 0)
                {
                    _backup.NewBackup(gameRoot, extra); // moved to a backup, recoverable
                    note += " Settings/log files were moved to a backup.";
                }
                _state.Remove(gameRoot);
                return InstallResult.Good(note);
            });
        }

        /// <summary>
        /// Removes ONLY this tool's record for the game. It never deletes, moves or renames any file.
        /// For a stuck record, e.g. after a launcher repair restored or removed the file this tool placed.
        /// </summary>
        public InstallResult Forget(string gameRoot)
        {
            return Guard(() =>
            {
                var rec = _state.Get(gameRoot);
                if (rec == null) return InstallResult.Fail("This tool has no record for this game.");
                string note = "MFGUnlocker GUI has stopped tracking this game. No files were changed.";
                if (!string.IsNullOrEmpty(rec.BinkHookedName) && !string.IsNullOrEmpty(rec.TargetDir)
                    && File.Exists(Path.Combine(rec.TargetDir, rec.BinkHookedName)))
                    note += " The game's original Bink file is still renamed to " + rec.BinkHookedName
                        + "; the tool has left it in place. If the game misbehaves, use the launcher's repair.";
                _state.Remove(gameRoot);
                return InstallResult.Good(note);
            });
        }

        public InstallResult Adopt(string gameRoot, string exePath, string targetDir, string fileName)
        {
            return Guard(() =>
            {
                var stop = Preflight(exePath, targetDir);
                if (stop != null) return stop;
                var blocked = ExistingRecordBlocks(_state.Get(gameRoot), targetDir);
                if (blocked != null) return blocked;
                var p = Path.Combine(targetDir, fileName ?? "");
                if (!File.Exists(p)) return InstallResult.Fail(fileName + " was not found in this folder.");
                var h = Hashing.Sha256File(p);
                if (!IsKnown(h, null)) return InstallResult.Fail(fileName + " does not match any known MFG Unlocker release, so it was not adopted.");
                string hookedName = null;
                if (ProxyNames.BinkNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    var hn = Path.GetFileNameWithoutExtension(fileName) + "Hooked.dll";
                    if (File.Exists(Path.Combine(targetDir, hn))) hookedName = hn;
                }
                var ver = _templates.VersionForHash(h) ?? (_templates.Current != null && Same(h, _templates.Current.Sha256) ? _templates.Current.Tag : null);
                _state.Save(NewRecord(gameRoot, exePath, targetDir, fileName, ver, h, hookedName, _state.Get(gameRoot)));
                return InstallResult.Good("Now managing the existing " + fileName + (ver != null ? " (" + ver + ")." : "."));
            });
        }

        // ---------- helpers ----------

        private InstallResult Guard(Func<InstallResult> action)
        {
            try { return action(); }
            catch (UnauthorizedAccessException) { return InstallResult.Fail(AccessDeniedMessage); }
            catch (IOException ex) when ((uint)ex.HResult == 0x80070005u) { return InstallResult.Fail(AccessDeniedMessage); }
            catch (IOException ex) { return InstallResult.Fail("A file operation failed: " + ex.Message); }
        }

        private InstallResult Preflight(string exePath, string targetDir)
        {
            if (_isRunning(exePath))
                return InstallResult.Fail("The game is running. Please close the game first, then try again.");
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                return InstallResult.Fail("The game folder was not found.");
            return null;
        }

        /// <summary>Refuses when an existing record still points at a placed file, or at a renamed Bink original.</summary>
        private static InstallResult ExistingRecordBlocks(GameRecord rec, string targetDir)
        {
            if (rec == null || string.IsNullOrEmpty(rec.InstalledName)) return null;
            var dir = rec.TargetDir ?? targetDir;
            if (File.Exists(Path.Combine(dir, rec.InstalledName)))
                return InstallResult.Fail("MFG Unlocker is already installed here as " + rec.InstalledName + ". Use upgrade or uninstall instead.");
            if (!string.IsNullOrEmpty(rec.BinkHookedName) && File.Exists(Path.Combine(dir, rec.BinkHookedName)))
                return InstallResult.Fail("This game's original Bink file is still saved as " + rec.BinkHookedName + ". Please uninstall first; that puts the original back. Nothing was changed.");
            return null;
        }

        private static InstallResult Changed(string name)
        {
            return InstallResult.Fail(name + " is not the file this tool installed and does not match any known MFG Unlocker release, so it was left alone.");
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsKnown(string hash, GameRecord rec)
        {
            if (rec != null && Same(hash, rec.Sha256)) return true;
            if (_templates.IsKnownRelease(hash)) return true;
            var c = _templates.Current;
            return c != null && Same(hash, c.Sha256);
        }

        private static GameRecord NewRecord(string gameRoot, string exePath, string targetDir, string name,
            string version, string hash, string hookedName, GameRecord old)
        {
            return new GameRecord
            {
                GameRoot = gameRoot, ExePath = exePath, TargetDir = targetDir, InstalledName = name,
                Version = version, Sha256 = hash, InstalledUtc = DateTime.UtcNow, BinkHookedName = hookedName,
                FailedNames = old != null && old.FailedNames != null ? old.FailedNames : new List<string>(),
                WorkingName = old != null ? old.WorkingName : null
            };
        }

        /// <summary>Copies the template to a temp name inside dir and verifies it. Caller moves or deletes it.</summary>
        private static string StageTemplate(TemplateInfo t, string dir, out string hash)
        {
            var tmp = Path.Combine(dir, ".mfgunlocker-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(t.DllPath, tmp, false);
                hash = Hashing.Sha256File(tmp);
                if (!Same(hash, Hashing.Sha256File(t.DllPath))) throw new IOException("The copied file did not verify.");
                return tmp;
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        /// <summary>Stages the template and renames it into place; target must not exist (Move refuses to overwrite).</summary>
        private static string PlaceTemplate(TemplateInfo t, string target)
        {
            string hash;
            var tmp = StageTemplate(t, Path.GetDirectoryName(target), out hash);
            try { File.Move(tmp, target); tmp = null; }
            finally { if (tmp != null) try { File.Delete(tmp); } catch { } }
            return hash;
        }
    }
}
