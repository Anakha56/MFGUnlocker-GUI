using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace MFGUnlockerGUI.Core
{
    public class ReleaseInfo
    {
        public string Tag { get; set; }
        public string Name { get; set; }
        public DateTime PublishedUtc { get; set; }
        public string Notes { get; set; }
        public string ZipUrl { get; set; }
        public string SumsUrl { get; set; }
        public bool Prerelease { get; set; }
    }

    [DataContract]
    internal class GhAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
    }

    [DataContract]
    internal class GhRelease
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "published_at")] public string PublishedAt { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "assets")] public List<GhAsset> Assets { get; set; }
    }

    /// <summary>Read-only GETs against the MFG Unlocker GitHub project. Nothing else is ever contacted.</summary>
    public class GitHubClient
    {
        public const string ReleasesUrl = "https://api.github.com/repos/dashdogy/MFG-Unlocker/releases";
        public const string CompatibilityUrl = "https://raw.githubusercontent.com/dashdogy/MFG-Unlocker/main/docs/compatibility.md";

        // host -> required path prefix (case-insensitive). Redirect targets are not checked.
        private static readonly KeyValuePair<string, string>[] AllowedPrefixes =
        {
            new KeyValuePair<string, string>("api.github.com", "/repos/dashdogy/MFG-Unlocker/"),
            new KeyValuePair<string, string>("github.com", "/dashdogy/MFG-Unlocker/releases/download/"),
            new KeyValuePair<string, string>("raw.githubusercontent.com", "/dashdogy/MFG-Unlocker/main/docs/compatibility.md")
        };
        private readonly HttpClient _http;

        static GitHubClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public GitHubClient(HttpMessageHandler h = null)
        {
            _http = h == null ? new HttpClient() : new HttpClient(h);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MFGUnlockerGUI");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json, */*");
        }

        public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync()
        {
            var bytes = await DownloadAsync(ReleasesUrl).ConfigureAwait(false);
            List<GhRelease> raw;
            using (var ms = new MemoryStream(bytes))
                raw = (List<GhRelease>)new DataContractJsonSerializer(typeof(List<GhRelease>)).ReadObject(ms);

            var result = new List<ReleaseInfo>();
            foreach (var r in raw ?? new List<GhRelease>())
            {
                if (r == null || r.Draft || string.IsNullOrEmpty(r.Tag)) continue;
                DateTimeOffset when;
                if (!DateTimeOffset.TryParse(r.PublishedAt, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out when))
                    when = DateTimeOffset.MinValue;
                var assets = r.Assets ?? new List<GhAsset>();
                var zip = assets.FirstOrDefault(a => a != null && a.Name != null
                    && a.Name.StartsWith("MFGUnlocker-", StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                var sums = assets.FirstOrDefault(a => a != null && string.Equals(a.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
                result.Add(new ReleaseInfo
                {
                    Tag = r.Tag,
                    Name = r.Name,
                    PublishedUtc = when.UtcDateTime,
                    Notes = r.Body,
                    Prerelease = r.Prerelease,
                    ZipUrl = zip == null ? null : zip.Url,
                    SumsUrl = sums == null ? null : sums.Url
                });
            }
            return result;
        }

        public async Task<ReleaseInfo> GetLatestAsync()
        {
            var all = await GetReleasesAsync().ConfigureAwait(false);
            var latest = all.Where(r => !r.Prerelease).OrderByDescending(r => r.PublishedUtc).FirstOrDefault();
            if (latest == null) throw new InvalidOperationException("No published releases were found.");
            return latest;
        }

        public async Task<byte[]> DownloadAsync(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps
                || !AllowedPrefixes.Any(a => string.Equals(u.Host, a.Key, StringComparison.OrdinalIgnoreCase)
                    && u.AbsolutePath.StartsWith(a.Value, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Refusing to contact " + url);
            using (var resp = await _http.GetAsync(u).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        public async Task<string> GetCompatibilityMarkdownAsync()
        {
            var bytes = await DownloadAsync(CompatibilityUrl).ConfigureAwait(false);
            return new UTF8Encoding(false).GetString(bytes);
        }
    }
}
