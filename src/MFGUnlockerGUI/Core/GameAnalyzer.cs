using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFGUnlockerGUI.Core
{
    public class GameAnalysis
    {
        public string GameRoot { get; set; }
        public IReadOnlyList<ExeCandidate> Exes { get; set; }
        public bool ExeConfident { get; set; }
        public ExeCandidate ChosenExe { get; set; }
        public string TargetDir { get; set; }
        public Engine Engine { get; set; }
        public IReadOnlyList<AntiCheatFinding> AntiCheat { get; set; }
        public IReadOnlyList<string> FrameGenFiles { get; set; }
        public CompatEntry Compat { get; set; }
        /// <summary>False means Compat (if any) is only a loose "contains" match and must be shown as a possible match.</summary>
        public bool CompatExact { get; set; }
        public FilenameAdvice Advice { get; set; }
        public InstallStatus Status { get; set; }
        public string InstalledFile { get; set; }
        public IReadOnlyList<string> Leftovers { get; set; }
        public bool CanInstall { get; set; }
        public string BlockReason { get; set; }
    }

    /// <summary>Joins the detectors into one read-only picture of a game folder.</summary>
    public class GameAnalyzer
    {
        private readonly CompatibilityTable _table;
        private readonly StateStore _state;
        private readonly Installer _installer;
        private readonly TemplateStore _templates;

        public GameAnalyzer(CompatibilityTable t, StateStore s, Installer i, TemplateStore templates)
        {
            _table = t; _state = s; _installer = i; _templates = templates;
        }

        public GameAnalysis Analyze(string gameRoot, string chosenExePath = null)
        {
            var a = new GameAnalysis
            {
                GameRoot = gameRoot,
                Exes = new List<ExeCandidate>(),
                AntiCheat = new List<AntiCheatFinding>(),
                FrameGenFiles = new List<string>(),
                Leftovers = new List<string>(),
                Status = InstallStatus.NotInstalled
            };
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            {
                a.BlockReason = "That folder was not found.";
                return a;
            }

            var record = _state.Get(gameRoot);
            a.Exes = ExeLocator.FindCandidates(gameRoot);
            a.ExeConfident = ExeLocator.IsConfident(a.Exes) && !SeveralUnrealProjects(a.Exes);

            ExeCandidate chosen = null;
            if (!string.IsNullOrEmpty(chosenExePath))
                chosen = a.Exes.FirstOrDefault(c => SamePath(c.Path, chosenExePath))
                    ?? (File.Exists(chosenExePath) ? new ExeCandidate { Path = chosenExePath, Reason = "Chosen by you" } : null);
            if (chosen != null) a.ExeConfident = true;
            else if (record != null && !string.IsNullOrEmpty(record.ExePath) && File.Exists(record.ExePath))
            {
                chosen = a.Exes.FirstOrDefault(c => SamePath(c.Path, record.ExePath))
                    ?? new ExeCandidate { Path = record.ExePath, Reason = "Remembered from last time" };
                a.ExeConfident = true;
            }
            else if (a.Exes.Count > 0) chosen = a.Exes[0];
            a.ChosenExe = chosen;
            a.TargetDir = chosen != null ? Path.GetDirectoryName(chosen.Path) : null;

            a.Engine = EngineDetector.Detect(gameRoot);
            a.FrameGenFiles = FrameGenDetector.Find(gameRoot);

            var hints = NameHints(gameRoot, chosen);
            a.AntiCheat = AntiCheatDetector.Scan(gameRoot, hints, _table.AntiCheatGames);
            bool exact;
            a.Compat = _table.Find(hints, out exact);
            a.CompatExact = a.Compat != null && exact;

            if (a.TargetDir != null)
            {
                string file;
                a.Status = _installer.Status(gameRoot, a.TargetDir, out file);
                a.InstalledFile = file;
                a.Leftovers = _installer.FindLeftovers(a.TargetDir);
                a.Advice = FilenameAdvisor.Advise(a.TargetDir, a.Engine, a.CompatExact ? a.Compat : null,
                    record != null ? record.FailedNames : null, record != null ? record.WorkingName : null);
            }

            if (a.ChosenExe == null) a.BlockReason = "No game program (.exe) was found in this folder.";
            else if (a.AntiCheat.Count > 0)
                a.BlockReason = "Anti-cheat found: " + string.Join(", ", a.AntiCheat.Select(f => f.Name))
                    + ". Using the mod with anti-cheat can get you banned, so install is turned off.";
            else if (_templates.Current == null)
                a.BlockReason = "No MFG Unlocker download is available yet. Check for updates first.";
            else if (a.Advice == null || a.Advice.Recommended == null)
                a.BlockReason = "No usable file name is left to try in this folder.";
            a.CanInstall = a.BlockReason == null;
            return a;
        }

        private static bool SeveralUnrealProjects(IReadOnlyList<ExeCandidate> exes)
        {
            var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in exes)
            {
                if (c.Reason != "Unreal Shipping executable") continue;
                var win64 = Path.GetDirectoryName(c.Path);
                var bin = Path.GetDirectoryName(win64);
                var project = bin == null ? null : Path.GetDirectoryName(bin);
                projects.Add(project ?? win64);
            }
            return projects.Count > 1;
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static List<string> NameHints(string gameRoot, ExeCandidate exe)
        {
            var hints = new List<string>();
            var folder = Path.GetFileName(gameRoot.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(folder)) hints.Add(folder);
            if (exe != null)
            {
                var n = Path.GetFileNameWithoutExtension(exe.Path);
                n = Regex.Replace(n, "-Win64-Shipping$", "", RegexOptions.IgnoreCase);
                if (n.Length > 0) hints.Add(n);
            }
            try
            {
                var cfg = Path.Combine(gameRoot, "MicrosoftGame.config");
                if (File.Exists(cfg))
                {
                    var m = Regex.Match(File.ReadAllText(cfg), "DisplayName\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (m.Success && !m.Groups[1].Value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        hints.Add(m.Groups[1].Value);
                }
            }
            catch { }
            return hints;
        }
    }
}
