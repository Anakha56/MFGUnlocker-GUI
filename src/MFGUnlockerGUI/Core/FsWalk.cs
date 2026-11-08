using System.Collections.Generic;
using System.IO;

namespace MFGUnlockerGUI.Core
{
    internal static class FsWalk
    {
        /// <summary>All files and folders under root at any depth, as paths relative to root. Unreadable folders are skipped.</summary>
        public static IEnumerable<KeyValuePair<string, bool>> Entries(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                string[] dirs = new string[0], files = new string[0];
                try { dirs = Directory.GetDirectories(cur); } catch { }
                try { files = Directory.GetFiles(cur); } catch { }
                foreach (var d in dirs)
                {
                    yield return new KeyValuePair<string, bool>(Rel(root, d), true);
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(d);
                }
                foreach (var f in files)
                    yield return new KeyValuePair<string, bool>(Rel(root, f), false);
            }
        }

        private static string Rel(string root, string full)
        {
            return full.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
