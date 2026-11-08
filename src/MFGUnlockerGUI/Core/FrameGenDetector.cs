using System;
using System.Collections.Generic;
using System.IO;

namespace MFGUnlockerGUI.Core
{
    public static class FrameGenDetector
    {
        private static readonly string[] Names = { "nvngx_dlssg.dll", "sl.dlss_g.dll", "sl.interposer.dll" };

        /// <summary>Relative paths of DLSS frame generation files, at any depth.</summary>
        public static IReadOnlyList<string> Find(string gameRoot)
        {
            var found = new List<string>();
            foreach (var e in FsWalk.Entries(gameRoot))
            {
                if (e.Value) continue;
                var name = Path.GetFileName(e.Key);
                foreach (var n in Names)
                    if (string.Equals(name, n, StringComparison.OrdinalIgnoreCase)) { found.Add(e.Key); break; }
            }
            return found;
        }
    }
}
