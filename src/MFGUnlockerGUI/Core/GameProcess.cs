using System;
using System.Diagnostics;
using System.IO;

namespace MFGUnlockerGUI.Core
{
    public static class GameProcess
    {
        /// <summary>
        /// True if a process with the exe's name is running. Where the process's path is readable it must match
        /// the exe path; where it is not readable (protected process) we assume it is the game, to be safe.
        /// </summary>
        public static bool IsRunning(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            var name = Path.GetFileNameWithoutExtension(exePath);
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch (Exception) { return false; }
            var full = SafeFull(exePath);
            foreach (var p in procs)
            {
                try
                {
                    string file;
                    try { file = p.MainModule.FileName; }
                    catch (Exception) { return true; }
                    if (string.Equals(SafeFull(file), full, StringComparison.OrdinalIgnoreCase)) return true;
                }
                finally { p.Dispose(); }
            }
            return false;
        }

        private static string SafeFull(string p)
        {
            try { return Path.GetFullPath(p); } catch (Exception) { return p; }
        }
    }
}
