using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MFGUnlockerGUI.Core
{
    public class BackupManager
    {
        private readonly AppPaths _paths;
        private readonly AppSettings _settings;

        public BackupManager(AppPaths p, AppSettings s)
        {
            _paths = p;
            _settings = s;
        }

        /// <summary>Folder-safe name plus a short hash of the full game path.</summary>
        public string GameDir(string gameRoot)
        {
            var full = Path.GetFullPath(gameRoot).TrimEnd('\\', '/');
            var name = Path.GetFileName(full);
            if (string.IsNullOrEmpty(name)) name = "game";
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
            string hash;
            using (var sha = SHA256.Create())
                hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToLowerInvariant()))
                    .Take(4).Select(b => b.ToString("x2")));
            return Path.Combine(_paths.Backups, safe + "-" + hash);
        }

        /// <summary>Moves the files into a new dated backup folder (copy, verify by hash, then remove the originals).</summary>
        public string NewBackup(string gameRoot, IEnumerable<string> filesToMove)
        {
            return Make(gameRoot, filesToMove.ToList(), true);
        }

        /// <summary>Copies one file into a new dated backup folder; the original stays.</summary>
        public string CopyBackup(string gameRoot, string fileToCopy)
        {
            return Make(gameRoot, new List<string> { fileToCopy }, false);
        }

        /// <summary>Backup folders, oldest first.</summary>
        public IReadOnlyList<string> List(string gameRoot)
        {
            var dir = GameDir(gameRoot);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetDirectories(dir).OrderBy(d => SortKey(d), StringComparer.Ordinal).ToList();
        }

        private static string SortKey(string folder)
        {
            // "yyyy-MM-dd_HHmmss" or "yyyy-MM-dd_HHmmss_N": pad N so _10 sorts after _2.
            var parts = Path.GetFileName(folder).Split('_');
            int k = 1;
            if (parts.Length >= 3) int.TryParse(parts[2], out k);
            return parts[0] + "_" + (parts.Length > 1 ? parts[1] : "") + "_" + k.ToString("D6");
        }

        private string Make(string gameRoot, List<string> files, bool move)
        {
            var gameDir = GameDir(gameRoot);
            foreach (var f in files)
                if (!File.Exists(f)) throw new FileNotFoundException("Cannot back up missing file", f);
            Directory.CreateDirectory(gameDir);

            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string folder = null;
            for (int i = 1; folder == null; i++)
            {
                var candidate = Path.Combine(gameDir, i == 1 ? stamp : stamp + "_" + i);
                if (Directory.Exists(candidate)) continue;
                Directory.CreateDirectory(candidate);
                folder = candidate;
            }

            var copied = new List<KeyValuePair<string, string>>(); // source, destination
            try
            {
                foreach (var f in files)
                {
                    var dest = Path.Combine(folder, Path.GetFileName(f));
                    for (int i = 2; File.Exists(dest); i++)
                        dest = Path.Combine(folder, Path.GetFileNameWithoutExtension(f) + "_" + i + Path.GetExtension(f));
                    File.Copy(f, dest, false);
                    copied.Add(new KeyValuePair<string, string>(f, dest));
                }
                foreach (var c in copied)
                    if (Hashing.Sha256File(c.Key) != Hashing.Sha256File(c.Value))
                        throw new IOException("Backup verification failed for " + c.Key);
            }
            catch
            {
                try { Directory.Delete(folder, true); } catch { }
                throw;
            }

            // Only now, with every file verified, remove originals and prune old backups.
            if (move)
                foreach (var c in copied) File.Delete(c.Key);

            Prune(gameRoot);
            return folder;
        }

        private void Prune(string gameRoot)
        {
            var keep = Math.Max(1, _settings.BackupsToKeep);
            var all = List(gameRoot);
            for (int i = 0; i < all.Count - keep; i++)
                try { Directory.Delete(all[i], true); } catch { }
        }
    }
}
