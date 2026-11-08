using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFGUnlockerGUI.Core
{
    public class ExeCandidate
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public string Reason { get; set; }
    }

    public static class ExeLocator
    {
        private const long StubLimit = 2L * 1024 * 1024;
        private const string ReasonShipping = "Unreal Shipping executable";
        private const string ReasonDeclared = "Declared in MicrosoftGame.config";
        private const string ReasonLargest = "Largest executable";

        // Helpers, installers, crash handlers, anti-cheat setup tools: never the game.
        private static readonly Regex Excluded = new Regex(
            @"^(unitycrashhandler\d*|crashreportclient|crashreporter|unrealcefsubprocess|gamingrepair|dxsetup|" +
            @"unins\d*|vc_?redist.*|vcredist.*|dotnet.*|ue\d*prereq.*|easyanticheat.*|beservice.*|.*_?setup|.*installer|" +
            @"cefprocess|cefsubprocess|crashpad_handler|werfault)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] SkippedDirs = { "_commonredist", "redist", "__installer" };

        public static IReadOnlyList<ExeCandidate> FindCandidates(string gameRoot, int maxDepth = 6)
        {
            var empty = new List<ExeCandidate>();
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return empty;

            var all = new List<ExeCandidate>();
            foreach (var f in Walk(gameRoot, maxDepth))
            {
                if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (Excluded.IsMatch(System.IO.Path.GetFileNameWithoutExtension(f))) continue;
                long size;
                try { size = new FileInfo(f).Length; } catch { continue; }
                all.Add(new ExeCandidate { Path = f, Size = size });
            }
            if (all.Count == 0) return empty;

            var declared = DeclaredExecutables(gameRoot);
            long biggest = all.Max(c => c.Size);

            foreach (var c in all)
            {
                if (IsUnrealShipping(c.Path)) c.Reason = ReasonShipping;
                else if (declared.Contains(System.IO.Path.GetFileName(c.Path).ToLowerInvariant())
                         && !(c.Size < StubLimit && biggest > c.Size))
                    c.Reason = ReasonDeclared;
                else c.Reason = ReasonLargest;
            }

            return all
                .OrderBy(c => Tier(c))
                .ThenByDescending(c => c.Size)
                .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsConfident(IReadOnlyList<ExeCandidate> c)
        {
            if (c == null || c.Count == 0) return false;
            if (c.Count == 1) return true;
            if (Tier(c[0]) < 2) return true;   // a Shipping exe or a declared exe is a clear signal
            if (Tier(c[1]) == 0) return true;

            var a = c[0];
            var b = c[1];
            bool plausible = b.Size * 4 >= a.Size;
            bool sameFolder = string.Equals(System.IO.Path.GetDirectoryName(a.Path),
                System.IO.Path.GetDirectoryName(b.Path), StringComparison.OrdinalIgnoreCase);
            return !(plausible && !sameFolder);
        }

        private static int Tier(ExeCandidate c)
        {
            if (c.Reason == ReasonShipping) return 0;
            if (c.Reason == ReasonDeclared) return 1;
            return 2;
        }

        internal static bool IsUnrealShipping(string file)
        {
            var name = System.IO.Path.GetFileName(file);
            if (!name.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var dir = System.IO.Path.GetDirectoryName(file) ?? "";
            return dir.EndsWith(@"Binaries\Win64", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> DeclaredExecutables(string root)
        {
            var set = new HashSet<string>();
            try
            {
                var cfg = System.IO.Path.Combine(root, "MicrosoftGame.config");
                if (!File.Exists(cfg)) return set;
                foreach (Match m in Regex.Matches(File.ReadAllText(cfg),
                    "Executable[^>]*?\\bName\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    set.Add(System.IO.Path.GetFileName(m.Groups[1].Value).ToLowerInvariant());
            }
            catch { }
            return set;
        }

        /// <summary>Files under root up to maxDepth folders deep; unreadable folders are skipped silently.</summary>
        internal static IEnumerable<string> Walk(string root, int maxDepth)
        {
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                string[] files = new string[0], dirs = new string[0];
                try { files = Directory.GetFiles(cur.Key); } catch { }
                foreach (var f in files) yield return f;
                if (cur.Value >= maxDepth) continue;
                try { dirs = Directory.GetDirectories(cur.Key); } catch { }
                foreach (var d in dirs)
                {
                    if (SkippedDirs.Contains(System.IO.Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)) continue;
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1));
                }
            }
        }

        internal static IEnumerable<string> WalkDirs(string root, int maxDepth)
        {
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur.Value >= maxDepth) continue;
                string[] dirs = new string[0];
                try { dirs = Directory.GetDirectories(cur.Key); } catch { }
                foreach (var d in dirs)
                {
                    yield return d;
                    stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1));
                }
            }
        }
    }
}
