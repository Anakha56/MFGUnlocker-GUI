using System;
using System.IO;
using System.Linq;

namespace MFGUnlockerGUI.Core
{
    public enum Engine { Unknown, Unreal, Unity, REEngine, REDengine }

    public static class EngineDetector
    {
        public static Engine Detect(string gameRoot)
        {
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return Engine.Unknown;

            if (FileExists(gameRoot, "re_chunk_000.pak")) return Engine.REEngine;

            if (FileExists(gameRoot, Path.Combine("bin", "x64", "REDprelauncher.exe"))
                || FileExists(gameRoot, "REDprelauncher.exe"))
                return Engine.REDengine;

            if (ExeLocator.Walk(gameRoot, 2).Any(f =>
                    string.Equals(Path.GetFileName(f), "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase)))
                return Engine.Unity;

            if (ExeLocator.Walk(gameRoot, 5).Any(ExeLocator.IsUnrealShipping)) return Engine.Unreal;
            if (DirExists(gameRoot, "Engine") && ExeLocator.WalkDirs(gameRoot, 2).Any(d =>
                    string.Equals(Path.GetFileName(d), "Binaries", StringComparison.OrdinalIgnoreCase)))
                return Engine.Unreal;

            return Engine.Unknown;
        }

        private static bool FileExists(string root, string rel)
        {
            try { return File.Exists(Path.Combine(root, rel)); } catch { return false; }
        }

        private static bool DirExists(string root, string rel)
        {
            try { return Directory.Exists(Path.Combine(root, rel)); } catch { return false; }
        }
    }
}
