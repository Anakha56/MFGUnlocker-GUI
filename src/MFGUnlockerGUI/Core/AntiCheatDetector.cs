using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFGUnlockerGUI.Core
{
    public class AntiCheatFinding
    {
        public string Name { get; set; }
        public string Evidence { get; set; }
    }

    public static class AntiCheatDetector
    {
        public const string NamedEvidence = "Named in upstream anti-cheat list";

        private class Sig
        {
            public string Name;
            public Regex Pattern;
        }

        private static Sig S(string name, string pattern)
        {
            return new Sig { Name = name, Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled) };
        }

        // Specific signatures first; the generic "anti-cheat anywhere" pattern is only a fallback per entry.
        private static readonly Sig[] Specific =
        {
            S("EasyAntiCheat", @"^easyanticheat"),
            S("BattlEye", @"^battleye$|^beservice"),
            S("EA Javelin / EA AntiCheat", @"^eaanticheat"),
            S("PunkBuster", @"^(pb|punkbuster)$"),
            S("GameGuard / nProtect", @"gameguard|nprotect"),
            S("XIGNCODE", @"xigncode"),
            S("FACEIT", @"faceit"),
            S("Riot Vanguard", @"^vgk$|vanguard"),
            S("miHoYo mhyprot", @"^mhyprot"),
            S("Tencent ACE", @"^ace-|anticheatexpert"),
        };

        private static readonly Sig Generic = S("Anti-cheat (generic name match)", @"anti-?cheat");

        public static IReadOnlyList<AntiCheatFinding> Scan(string gameRoot, IEnumerable<string> gameNameHints,
            IReadOnlyList<string> antiCheatGames)
        {
            var findings = new List<AntiCheatFinding>();
            var seen = new HashSet<string>();

            foreach (var e in FsWalk.Entries(gameRoot))
            {
                var name = Path.GetFileName(e.Key);
                if (!e.Value) name = Path.GetFileNameWithoutExtension(name);
                var sig = Specific.FirstOrDefault(s => s.Pattern.IsMatch(name)) ?? (Generic.Pattern.IsMatch(name) ? Generic : null);
                if (sig == null || !seen.Add(sig.Name)) continue;
                findings.Add(new AntiCheatFinding { Name = sig.Name, Evidence = e.Key });
            }

            if (gameNameHints != null && antiCheatGames != null)
            {
                var hints = gameNameHints.Select(CompatibilityTable.Normalize).Where(h => h.Length > 0).ToList();
                foreach (var game in antiCheatGames)
                {
                    // "Battlefield 6 / RedSec" lists two names in one entry.
                    var keys = game.Split('/').Select(CompatibilityTable.Normalize).Where(k => k.Length > 0);
                    if (keys.Any(k => hints.Any(h => h.Contains(k))) && seen.Add("list:" + game))
                        findings.Add(new AntiCheatFinding { Name = game, Evidence = NamedEvidence });
                }
            }
            return findings;
        }
    }
}
