using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace MFGUnlockerGUI.Core
{
    /// <summary>Pure helpers for the "Applied games" list.</summary>
    public static class AppliedGames
    {
        /// <summary>"The Outer Worlds 2 — version.dll (v1.7.0)".</summary>
        public static string Display(GameRecord r)
        {
            var root = (r.GameRoot ?? "").TrimEnd('\\', '/');
            var folder = Path.GetFileName(root);
            if (string.IsNullOrEmpty(folder)) folder = root;
            return folder + " \u2014 " + r.InstalledName + " (" + (string.IsNullOrEmpty(r.Version) ? "unknown version" : r.Version) + ")";
        }

        /// <summary>One line in the Game picker.</summary>
        public sealed class Entry
        {
            public string Root, Text;
            public bool Recorded;
        }

        /// <summary>
        /// Entries for the Game picker: every game this tool has installed into, plus the game now open
        /// ("folder — not installed yet") when it has no installed record. Sorted by text.
        /// </summary>
        public static List<Entry> BuildEntries(IEnumerable<GameRecord> records, string openRoot)
        {
            var list = new List<Entry>();
            if (records != null)
                foreach (var r in records)
                    if (r != null && !string.IsNullOrEmpty(r.InstalledName))
                        list.Add(new Entry { Root = r.GameRoot, Text = Display(r), Recorded = true });
            if (!string.IsNullOrEmpty(openRoot) &&
                !list.Any(e => string.Equals(Trim(e.Root), Trim(openRoot), StringComparison.OrdinalIgnoreCase)))
                list.Add(new Entry { Root = openRoot, Text = FolderName(openRoot) + " — not installed yet", Recorded = false });
            return list.OrderBy(e => e.Text, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Trim(string root) { return (root ?? "").TrimEnd('\\', '/'); }

        private static string FolderName(string root)
        {
            var t = Trim(root);
            var folder = Path.GetFileName(t);
            return string.IsNullOrEmpty(folder) ? t : folder;
        }

        /// <summary>True when the game's installed copy is older than the current template.</summary>
        public static bool NeedsUpgrade(GameRecord r, TemplateInfo t)
        {
            if (r == null || t == null || string.IsNullOrEmpty(r.InstalledName)) return false;
            if (string.IsNullOrEmpty(r.Version))
                return !string.IsNullOrEmpty(t.Sha256) && !string.Equals(r.Sha256, t.Sha256, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(r.Version, t.Tag, StringComparison.OrdinalIgnoreCase)) return false;
            var pa = Parts(r.Version); var pb = Parts(t.Tag);
            if (pa == null || pb == null) // can't tell older from newer: a different file means an upgrade is possible
                return !string.IsNullOrEmpty(t.Sha256) && !string.Equals(r.Sha256, t.Sha256, StringComparison.OrdinalIgnoreCase);
            return CompareTags(pa, pb) < 0;
        }

        /// <summary>Compares parsed tags like "v1.9.0" numerically.</summary>
        private static int CompareTags(int[] pa, int[] pb)
        {
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static int[] Parts(string tag)
        {
            var s = tag.Trim().TrimStart('v', 'V').Split('.');
            var r = new int[s.Length];
            for (int i = 0; i < s.Length; i++) if (!int.TryParse(s[i], out r[i])) return null;
            return r;
        }
    }
}
