using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace MFGUnlockerGUI.Core
{
    [DataContract]
    public class TemplateInfo
    {
        [DataMember] public string Tag { get; set; }
        [DataMember] public DateTime PublishedUtc { get; set; }
        /// <summary>Filled in from the folder location when loaded; not stored.</summary>
        public string DllPath { get; set; }
        [DataMember] public string Sha256 { get; set; }
    }

    public class TemplateStore
    {
        public const string DllName = "MFGUnlocker.dll";
        private const string MetaName = "template.json";
        private const string IncomingName = ".incoming";

        private readonly AppPaths _paths;
        private readonly GitHubClient _gh;
        private readonly Dictionary<string, string> _known =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // hash -> tag

        public TemplateStore(AppPaths p, GitHubClient g)
        {
            _paths = p;
            _gh = g;
            LoadKnown();
        }

        public TemplateInfo Current
        {
            get { return LoadAll().OrderByDescending(t => t.PublishedUtc).ThenByDescending(t => t.Tag, StringComparer.Ordinal).FirstOrDefault(); }
        }

        public bool IsNewer(ReleaseInfo r)
        {
            var c = Current;
            return c == null || (!string.Equals(c.Tag, r.Tag, StringComparison.OrdinalIgnoreCase) && r.PublishedUtc > c.PublishedUtc);
        }

        public async Task<TemplateInfo> InstallReleaseAsync(ReleaseInfo r)
        {
            if (r == null || string.IsNullOrEmpty(r.ZipUrl) || string.IsNullOrEmpty(r.SumsUrl))
                throw new InvalidOperationException("This release has no zip or SHA256SUMS.txt asset, so it cannot be verified.");

            var incoming = Path.Combine(_paths.Templates, IncomingName);
            try
            {
                if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
                Directory.CreateDirectory(incoming);

                var zipBytes = await _gh.DownloadAsync(r.ZipUrl).ConfigureAwait(false);
                var sumsBytes = await _gh.DownloadAsync(r.SumsUrl).ConfigureAwait(false);
                var sums = ParseSums(Encoding.UTF8.GetString(sumsBytes));

                var zipName = Uri.UnescapeDataString(Path.GetFileName(new Uri(r.ZipUrl).AbsolutePath));
                string expectedZip, expectedDll;
                if (!sums.TryGetValue(zipName, out expectedZip))
                    throw new InvalidDataException("SHA256SUMS.txt has no entry for " + zipName + ".");
                if (!sums.TryGetValue(DllName, out expectedDll))
                    throw new InvalidDataException("SHA256SUMS.txt has no entry for " + DllName + ".");

                if (!string.Equals(Hashing.Sha256Bytes(zipBytes), expectedZip, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded zip does not match its published SHA-256. Nothing was installed.");

                var dllTmp = Path.Combine(incoming, DllName);
                using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, DllName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) throw new InvalidDataException("The zip does not contain " + DllName + ".");
                    using (var src = entry.Open())
                    using (var dst = new FileStream(dllTmp, FileMode.Create, FileAccess.Write))
                        src.CopyTo(dst);
                }
                var dllHash = Hashing.Sha256File(dllTmp);
                if (!string.Equals(dllHash, expectedDll, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(DllName + " does not match its published SHA-256. Nothing was installed.");

                var info = new TemplateInfo { Tag = r.Tag, PublishedUtc = r.PublishedUtc, Sha256 = dllHash };
                using (var fs = new FileStream(Path.Combine(incoming, MetaName), FileMode.Create, FileAccess.Write))
                    new DataContractJsonSerializer(typeof(TemplateInfo)).WriteObject(fs, info);

                var final = FolderFor(r.Tag);
                string aside = null;
                if (Directory.Exists(final))
                {
                    aside = Path.Combine(_paths.Templates, ".old-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(final, aside);
                }
                try
                {
                    Directory.Move(incoming, final);
                }
                catch
                {
                    if (aside != null && !Directory.Exists(final)) Directory.Move(aside, final);
                    throw;
                }
                if (aside != null) { try { Directory.Delete(aside, true); } catch { } }

                info.DllPath = Path.Combine(final, DllName);
                Prune(info.Tag);
                return info;
            }
            finally
            {
                try { if (Directory.Exists(incoming)) Directory.Delete(incoming, true); } catch { }
            }
        }

        public async Task RefreshKnownHashesAsync()
        {
            var releases = await _gh.GetReleasesAsync().ConfigureAwait(false);
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in releases)
            {
                if (string.IsNullOrEmpty(r.SumsUrl)) continue;
                try
                {
                    var bytes = await _gh.DownloadAsync(r.SumsUrl).ConfigureAwait(false);
                    foreach (var kv in ParseSums(Encoding.UTF8.GetString(bytes)))
                        found[kv.Value] = r.Tag;
                }
                catch (Exception ex)
                {
                    if (!(ex is System.Net.Http.HttpRequestException) && !(ex is TaskCanceledException)) throw;
                    // A single release failing must not lose what we already know; the old cache entries stay.
                }
            }
            foreach (var kv in found) _known[kv.Key] = kv.Value;

            var sb = new StringBuilder();
            foreach (var kv in _known.OrderBy(k => k.Value, StringComparer.Ordinal).ThenBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key.ToUpperInvariant()).Append('\t').Append(kv.Value).Append('\n');
            var tmp = _paths.KnownHashesFile + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(_paths.KnownHashesFile)) File.Replace(tmp, _paths.KnownHashesFile, null);
            else File.Move(tmp, _paths.KnownHashesFile);
        }

        public bool IsKnownRelease(string sha256)
        {
            return !string.IsNullOrEmpty(sha256) && _known.ContainsKey(sha256);
        }

        public string VersionForHash(string sha256)
        {
            string tag;
            return !string.IsNullOrEmpty(sha256) && _known.TryGetValue(sha256, out tag) ? tag : null;
        }

        // ---- helpers ----

        private void LoadKnown()
        {
            try
            {
                if (!File.Exists(_paths.KnownHashesFile)) return;
                foreach (var line in File.ReadAllLines(_paths.KnownHashesFile))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 2 && parts[0].Length > 0) _known[parts[0].Trim()] = parts[1].Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Parses "HASH  filename" lines into filename -> hash (names may carry a leading '*').</summary>
        private static Dictionary<string, string> ParseSums(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length < 66) continue;
                var hash = line.Substring(0, 64);
                if (!hash.All(Uri.IsHexDigit)) continue;
                var name = line.Substring(64).Trim().TrimStart('*');
                if (name.Length > 0) d[name] = hash.ToUpperInvariant();
            }
            return d;
        }

        private string FolderFor(string tag)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(tag.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return Path.Combine(_paths.Templates, safe);
        }

        private IEnumerable<TemplateInfo> LoadAll()
        {
            if (!Directory.Exists(_paths.Templates)) yield break;
            foreach (var dir in Directory.GetDirectories(_paths.Templates))
            {
                if (Path.GetFileName(dir).StartsWith(".", StringComparison.Ordinal)) continue;
                var meta = Path.Combine(dir, MetaName);
                var dll = Path.Combine(dir, DllName);
                if (!File.Exists(meta) || !File.Exists(dll)) continue;
                TemplateInfo t = null;
                try
                {
                    using (var fs = File.OpenRead(meta))
                        t = (TemplateInfo)new DataContractJsonSerializer(typeof(TemplateInfo)).ReadObject(fs);
                }
                catch (Exception) { }
                if (t == null || string.IsNullOrEmpty(t.Tag)) continue;
                t.DllPath = dll;
                yield return t;
            }
        }

        /// <summary>Keeps the template just installed plus the newest other one; removes the rest.</summary>
        private void Prune(string justInstalledTag)
        {
            var keepFolder = FolderFor(justInstalledTag);
            var others = LoadAll()
                .Where(t => !string.Equals(Path.GetDirectoryName(t.DllPath), keepFolder, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.PublishedUtc).ToList();
            foreach (var t in others.Skip(1))
                try { Directory.Delete(Path.GetDirectoryName(t.DllPath), true); } catch { }
        }
    }
}
