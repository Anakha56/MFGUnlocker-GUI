using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;

namespace MFGUnlockerGUI.Core
{
    public enum LogVerdict { Blocked, NotLoaded, Loaded }

    public class Troubleshooter
    {
        public const string FrozenPictureAdvice = "If the picture froze, try Preset B in the mod's menu (press Backspace in the game). This was reported in issue #6 on the MFG Unlocker GitHub page. On an RTX 30 card, restart the game after changing presets. It is a workaround other players reported, not a fix that works for every game.";
        public const string FgOptionMissingAdvice = "If the game's own frame generation option is missing, the game may not expose DLSS Frame Generation on this setup. Check the game is on DirectX 12, that DLSS Super Resolution is turned on, and that the NVIDIA driver is current. Not every game is supported; see the compatibility list.";
        public const string SacBlockedAdvice = "Windows Smart App Control blocked the MFG Unlocker file. This tool does not change Smart App Control or work around it. Whether to run games with Smart App Control off is your own decision. You can report the game as not working instead.";
        public const string ReportUrl = "https://github.com/dashdogy/MFG-Unlocker/issues";
        public const string CompatReportUrl = "https://github.com/dashdogy/MFG-Unlocker/issues/new?template=game-compatibility.yml";

        private const string IniName = "RTX40MFG-UI.ini";
        private readonly Func<DateTime, string, bool> _sacBlockedSince;
        private readonly Func<string, bool> _isRunning;

        public Troubleshooter(Func<DateTime, string, bool> sacBlockedSince = null, Func<string, bool> isRunning = null)
        {
            _sacBlockedSince = sacBlockedSince ?? EventLogSacBlocked;
            _isRunning = isRunning ?? GameProcess.IsRunning;
        }

        /// <summary>tempDir null means the real temp folder; tests pass their own.</summary>
        public LogVerdict CheckMenuMissing(GameRecord r, string tempDir = null)
        {
            if (_sacBlockedSince(r.InstalledUtc, r.InstalledName)) return LogVerdict.Blocked;
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(r.ExePath)) dirs.Add(Path.GetDirectoryName(r.ExePath));
            if (!string.IsNullOrEmpty(r.TargetDir)) dirs.Add(r.TargetDir);
            dirs.Add(tempDir ?? Path.GetTempPath());
            foreach (var d in dirs.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var f in Logs(d))
                    if (File.GetLastWriteTimeUtc(f) >= r.InstalledUtc) return LogVerdict.Loaded;
            return LogVerdict.NotLoaded;
        }

        /// <summary>Read-only. Returns a plain-English problem, or null.</summary>
        public string FindLeftoverOrDxgiProblem(GameRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.TargetDir) || !Directory.Exists(r.TargetDir)) return null;
            var left = ProxyNames.OldLeftovers.Where(n => File.Exists(Path.Combine(r.TargetDir, n))).ToList();
            if (left.Count > 0)
                return "Files from an older version of the mod are still in the folder (" + string.Join(", ", left)
                    + "). They can stop the new one from working. Upgrade moves them to a backup for you.";
            if (!string.Equals(r.InstalledName, "dxgi.dll", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(r.TargetDir, "dxgi.dll")))
                return "There is another dxgi.dll in the folder (often ReShade or Special K). It can load ahead of MFG Unlocker. This tool will not touch it; try the next file name.";
            if (string.Equals(r.InstalledName, "dxgi.dll", StringComparison.OrdinalIgnoreCase))
                return "MFG Unlocker is installed as dxgi.dll, which overlays and graphics tools also use. If the menu is missing, try the next file name.";
            return null;
        }

        /// <summary>Sets Rtx30FlipMetering=0 under [Compatibility] in RTX40MFG-UI.ini, backing the file up first.</summary>
        public string SetRtx30FlipMetering(GameRecord r, BackupManager b)
        {
            if (_isRunning(r.ExePath))
                return "The game is running. Please close the game first, then try again. Nothing was changed.";
            try { return SetFlipCore(r, b); }
            catch (UnauthorizedAccessException) { return Installer.AccessDeniedMessage; }
            catch (IOException ex) when ((uint)ex.HResult == 0x80070005u) { return Installer.AccessDeniedMessage; }
        }

        private static string SetFlipCore(GameRecord r, BackupManager b)
        {
            var ini = Path.Combine(r.TargetDir, IniName);
            var lines = new List<string>();
            string nl = "\r\n";
            bool hadFile = File.Exists(ini);
            if (hadFile)
            {
                b.CopyBackup(r.GameRoot, ini); // backup before any change
                var text = File.ReadAllText(ini);
                if (text.IndexOf("\r\n", StringComparison.Ordinal) < 0 && text.IndexOf('\n') >= 0) nl = "\n";
                lines = text.Replace("\r\n", "\n").Split('\n').ToList();
                if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            }

            const string line = "Rtx30FlipMetering=0";
            int sec = lines.FindIndex(l => string.Equals(l.Trim(), "[Compatibility]", StringComparison.OrdinalIgnoreCase));
            if (sec < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim() != "") lines.Add("");
                lines.Add("[Compatibility]");
                lines.Add(line);
            }
            else
            {
                int end = lines.Count; // end of section
                for (int i = sec + 1; i < lines.Count; i++)
                    if (lines[i].TrimStart().StartsWith("[", StringComparison.Ordinal)) { end = i; break; }
                int key = -1;
                for (int i = sec + 1; i < end; i++)
                {
                    var l = lines[i].TrimStart();
                    if (l.StartsWith("Rtx30FlipMetering", StringComparison.OrdinalIgnoreCase) && l.Substring("Rtx30FlipMetering".Length).TrimStart().StartsWith("=", StringComparison.Ordinal))
                    { key = i; break; }
                }
                if (key >= 0) lines[key] = line;
                else
                {
                    int at = end;
                    while (at > sec + 1 && lines[at - 1].Trim() == "") at--; // keep blank spacing after the section
                    lines.Insert(at, line);
                }
            }

            var tmp = Path.Combine(r.TargetDir, ".mfgunlocker-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(string.Join(nl, lines) + nl);
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
                    fs.Write(bytes, 0, bytes.Length);
                if (hadFile) File.Replace(tmp, ini, null); else File.Move(tmp, ini);
                tmp = null;
            }
            finally { if (tmp != null) try { File.Delete(tmp); } catch { } }
            return "Set Rtx30FlipMetering=0 in " + IniName + (hadFile ? " (the old file was backed up)." : " (new file).");
        }

        /// <summary>Copies logs and the status file into a new local folder and writes a summary. Nothing is sent anywhere.</summary>
        public string BuildReport(GameRecord r, AppPaths p, string gpu, string driver, string modVersion)
        {
            var gameName = Path.GetFileName(r.GameRoot.TrimEnd('\\', '/'));
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string((gameName ?? "game").Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var folder = Path.Combine(p.Reports, stamp + "_" + safe);
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(p.Reports, stamp + "_" + safe + "_" + i);
            Directory.CreateDirectory(folder);

            var sources = new List<string>();
            foreach (var d in new[] { Path.GetDirectoryName(r.ExePath ?? ""), r.TargetDir, Path.GetTempPath() }
                .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                sources.AddRange(Logs(d));
                var st = Path.Combine(d, "RTXMFG-Universal.status.json");
                if (File.Exists(st)) sources.Add(st);
            }
            foreach (var s in sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var dest = Path.Combine(folder, Path.GetFileName(s));
                for (int i = 2; File.Exists(dest); i++)
                    dest = Path.Combine(folder, Path.GetFileNameWithoutExtension(s) + "_" + i + Path.GetExtension(s));
                try { File.Copy(s, dest, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            var sb = new StringBuilder();
            sb.AppendLine("MFG Unlocker GUI report");
            sb.AppendLine("Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Game: " + gameName);
            sb.AppendLine("GPU: " + gpu);
            sb.AppendLine("NVIDIA driver: " + driver);
            sb.AppendLine("File name installed as: " + r.InstalledName);
            sb.AppendLine("Mod version: " + modVersion);
            sb.AppendLine("Installed (UTC): " + r.InstalledUtc.ToString("yyyy-MM-dd HH:mm"));
            if (r.FailedNames != null && r.FailedNames.Count > 0) sb.AppendLine("Names that did not work: " + string.Join(", ", r.FailedNames));
            if (!string.IsNullOrEmpty(r.BinkHookedName)) sb.AppendLine("Bink route: yes (" + r.BinkHookedName + ")");
            sb.AppendLine("Report page: " + ReportUrl);
            File.WriteAllText(Path.Combine(folder, "summary.txt"), sb.ToString(), new UTF8Encoding(false));
            return folder;
        }

        // ---- helpers ----

        private static IEnumerable<string> Logs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "RTXMFG-*.log") : new string[0]; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new string[0]; }
        }

        /// <summary>
        /// Code Integrity events 3033, 3034 and 3077 since the install time whose XML names the DLL.
        /// Read-only; any failure to read the log means "not blocked".
        /// </summary>
        private static bool EventLogSacBlocked(DateTime sinceUtc, string dllName)
        {
            if (string.IsNullOrEmpty(dllName)) return false;
            try
            {
                var since = sinceUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var q = "*[System[(EventID=3033 or EventID=3034 or EventID=3077) and TimeCreated[@SystemTime>='" + since + "']]]";
                var query = new EventLogQuery("Microsoft-Windows-CodeIntegrity/Operational", PathType.LogName, q);
                using (var reader = new EventLogReader(query))
                {
                    EventRecord ev;
                    while ((ev = reader.ReadEvent()) != null)
                        using (ev)
                        {
                            var xml = ev.ToXml();
                            if (xml != null && xml.IndexOf(dllName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        }
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
