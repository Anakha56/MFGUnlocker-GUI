using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MFGUnlockerGUI.Core
{
    public class DontUse
    {
        public string Name { get; set; }
        public string Reason { get; set; }
    }

    public class CompatEntry
    {
        public string Game { get; set; }
        public IReadOnlyList<string> Use { get; set; }
        public IReadOnlyList<string> AlsoLoaded { get; set; }
        public IReadOnlyList<DontUse> DontUse { get; set; }
    }

    /// <summary>
    /// Upstream's community compatibility table. It is player reports and may be stale or wrong,
    /// so parsing is tolerant: garbage in gives an empty table, never an exception.
    /// </summary>
    public class CompatibilityTable
    {
        static readonly string[] FallbackAntiCheat =
        {
            "Battlefield 6 / RedSec", "Forza Horizon 6", "Gears of War: E-Day",
            "Grand Theft Auto V Enhanced", "Zenless Zone Zero"
        };

        static readonly Regex DllToken = new Regex("`([^`\\s]+\\.dll)`", RegexOptions.IgnoreCase);
        // Either a backticked DLL name or a parenthesised note.
        static readonly Regex DllWithNote = new Regex("`([^`\\s]+\\.dll)`|\\(([^)]*)\\)", RegexOptions.IgnoreCase);

        public IReadOnlyList<CompatEntry> Games { get; private set; }
        public IReadOnlyList<string> AntiCheatGames { get; private set; }
        /// <summary>"online yyyy-MM-dd" or "built-in".</summary>
        public string Source { get; private set; }

        CompatibilityTable()
        {
            Games = new CompatEntry[0];
            AntiCheatGames = new string[0];
            Source = "built-in";
        }

        public static CompatibilityTable BuiltIn()
        {
            string md = "";
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("compatibility.md"))
                {
                    if (s != null)
                        using (var r = new StreamReader(s, Encoding.UTF8)) md = r.ReadToEnd();
                }
            }
            catch { }
            return Parse(md, "built-in");
        }

        public static CompatibilityTable Parse(string markdown, string source)
        {
            var t = new CompatibilityTable { Source = source ?? "built-in" };
            try
            {
                if (!string.IsNullOrEmpty(markdown))
                {
                    t.Games = ParseGames(markdown);
                    t.AntiCheatGames = ParseAntiCheat(markdown);
                }
            }
            catch
            {
                t.Games = new CompatEntry[0];
                t.AntiCheatGames = new string[0];
            }
            if (t.AntiCheatGames.Count == 0) t.AntiCheatGames = FallbackAntiCheat;
            return t;
        }

        static string[] Lines(string md)
        {
            return md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        static IReadOnlyList<CompatEntry> ParseGames(string md)
        {
            var list = new List<CompatEntry>();
            bool inGames = false;
            foreach (var raw in Lines(md))
            {
                var line = raw.Trim();
                if (line.StartsWith("#"))
                {
                    inGames = Regex.IsMatch(line, @"^##\s+Games\s*$", RegexOptions.IgnoreCase);
                    continue;
                }
                if (!inGames || !line.StartsWith("|")) continue;
                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.Length < 2) continue;
                if (cells[0].Equals("Game", StringComparison.OrdinalIgnoreCase)) continue;
                if (cells[0].Length == 0 || Regex.IsMatch(cells[0], @"^:?-+:?$")) continue;
                list.Add(new CompatEntry
                {
                    Game = cells[0],
                    Use = Names(cells.ElementAtOrDefault(1)),
                    AlsoLoaded = Names(cells.ElementAtOrDefault(2)),
                    DontUse = Dont(cells.ElementAtOrDefault(3))
                });
            }
            return list;
        }

        static List<string> Names(string cell)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(cell)) return r;
            foreach (Match m in DllToken.Matches(cell)) r.Add(m.Groups[1].Value.ToLowerInvariant());
            return r;
        }

        // Names listed before a "(reason)" share that reason.
        static List<DontUse> Dont(string cell)
        {
            var r = new List<DontUse>();
            if (string.IsNullOrEmpty(cell)) return r;
            var pending = new List<DontUse>();
            foreach (Match m in DllWithNote.Matches(cell))
            {
                if (m.Groups[1].Success)
                {
                    var d = new DontUse { Name = m.Groups[1].Value.ToLowerInvariant(), Reason = "" };
                    pending.Add(d);
                    r.Add(d);
                }
                else
                {
                    foreach (var d in pending) d.Reason = m.Groups[2].Value.Trim();
                    pending.Clear();
                }
            }
            return r;
        }

        static IReadOnlyList<string> ParseAntiCheat(string md)
        {
            var sb = new StringBuilder();
            bool inSec = false;
            foreach (var raw in Lines(md))
            {
                var line = raw.Trim();
                if (line.StartsWith("#"))
                {
                    inSec = Regex.IsMatch(line, @"^##\s+Games with anti-?cheat", RegexOptions.IgnoreCase);
                    continue;
                }
                if (inSec) sb.Append(line).Append(' ');
            }
            var text = Regex.Replace(sb.ToString(), @"\s+", " ");
            var idx = text.IndexOf("trouble in", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return new string[0];
            text = text.Substring(idx + "trouble in".Length);
            text = Regex.Replace(text, @"\(\s*\[#.*?\)", "");
            text = Regex.Replace(text, @"\[#\d+\]", "");
            int end = text.IndexOf('.');
            if (end >= 0) text = text.Substring(0, end);
            var parts = Regex.Split(text, @",|\band\b");
            return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        }

        /// <summary>Lowercase and keep only a-z and 0-9, so "Cyberpunk 2077" and "cyberpunk2077" compare equal.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                var c = char.ToLowerInvariant(ch);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

        static string[] Tokens(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            return Regex.Split(s.ToLowerInvariant(), "[^a-z0-9]+").Where(t => t.Length > 0).ToArray();
        }

        static bool IsNumber(string t) { return t.Length > 0 && t.All(c => c >= '0' && c <= '9'); }

        // The game's words appear as a run inside the hint, and the hint is not a sequel
        // ("Dying Light" must not match "Dying Light 2 ..." unless the game's own name ends in a number).
        static bool StrongRun(string[] hay, string[] needle)
        {
            if (needle.Length == 0 || needle.Length > hay.Length) return false;
            bool endsInNumber = IsNumber(needle[needle.Length - 1]);
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k != needle.Length) continue;
                int next = i + needle.Length;
                if (!endsInNumber && next < hay.Length && IsNumber(hay[next])) continue;
                return true;
            }
            return false;
        }

        /// <summary>Finds the entry for the first hint that matches; exact = normalized names equal, or the game's whole word run sits inside the hint.</summary>
        public CompatEntry Find(IEnumerable<string> nameHints, out bool exact)
        {
            exact = false;
            var hints = (nameHints ?? new string[0]).Select(Normalize).Where(h => h.Length > 0).ToList();
            if (hints.Count == 0) return null;
            foreach (var h in hints)
            {
                var e = Games.FirstOrDefault(g => Normalize(g.Game) == h);
                if (e != null) { exact = true; return e; }
            }
            // Strong match: the game's whole word sequence appears inside the hint's words
            // ("The Witcher 3 Wild Hunt GOTY" contains "The Witcher 3"). Counts as exact.
            var hintTokens = (nameHints ?? new string[0]).Select(Tokens).Where(t => t.Length > 0).ToList();
            CompatEntry strong = null;
            int strongLen = 0;
            foreach (var ht in hintTokens)
                foreach (var g in Games)
                {
                    var gt = Tokens(g.Game);
                    if (gt.Length >= 2 && gt.Length > strongLen && StrongRun(ht, gt)) { strong = g; strongLen = gt.Length; }
                }
            if (strong != null) { exact = true; return strong; }
            foreach (var h in hints)
            {
                if (h.Length < 4) continue;
                var e = Games
                    .Where(g => { var n = Normalize(g.Game); return n.Length >= 4 && (n.Contains(h) || h.Contains(n)); })
                    .OrderByDescending(g => Normalize(g.Game).Length)
                    .FirstOrDefault();
                if (e != null) return e;
            }
            return null;
        }
    }
}
