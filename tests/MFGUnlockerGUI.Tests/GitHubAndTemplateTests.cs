using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class GitHubAndTemplateTests : IDisposable
    {
        private const string Dl = "https://github.com/dashdogy/MFG-Unlocker/releases/download/";
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));
        private readonly AppPaths _paths;

        public GitHubAndTemplateTests()
        {
            Directory.CreateDirectory(_tmp);
            _paths = new AppPaths(Path.Combine(_tmp, "app"));
        }

        public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

        private class Rel
        {
            public string Tag, Published, DllText;
            public bool Prerelease, InSubfolder;
            public byte[] Zip;
            public string Sums;
            public string ZipUrl { get { return Dl + Tag + "/MFGUnlocker-" + Tag + ".zip"; } }
            public string SumsUrl { get { return Dl + Tag + "/SHA256SUMS.txt"; } }
            public string DllHash { get { return Hashing.Sha256Bytes(Encoding.ASCII.GetBytes(DllText)); } }
        }

        private static Rel MakeRel(string tag, string published, string dllText = null, bool prerelease = false, bool subfolder = false)
        {
            var r = new Rel { Tag = tag, Published = published, DllText = dllText ?? "FAKE", Prerelease = prerelease, InSubfolder = subfolder };
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var e = z.CreateEntry((subfolder ? "MFGUnlocker-" + tag + "/" : "") + "MFGUnlocker.dll");
                    using (var s = e.Open()) { var b = Encoding.ASCII.GetBytes(r.DllText); s.Write(b, 0, b.Length); }
                    var n = z.CreateEntry("README.txt");
                    using (var s = n.Open()) { var b = Encoding.ASCII.GetBytes("hi"); s.Write(b, 0, b.Length); }
                }
                r.Zip = ms.ToArray();
            }
            r.Sums = Hashing.Sha256Bytes(r.Zip) + "  MFGUnlocker-" + r.Tag + ".zip\r\n" + r.DllHash + "  MFGUnlocker.dll\r\n";
            return r;
        }

        private static string Json(params Rel[] rels)
        {
            var parts = rels.Select(r => "{\"tag_name\":\"" + r.Tag + "\",\"name\":\"" + r.Tag + "\",\"draft\":false,\"prerelease\":"
                + (r.Prerelease ? "true" : "false") + ",\"published_at\":\"" + r.Published + "\",\"body\":\"notes\","
                + "\"assets\":[{\"name\":\"MFGUnlocker-" + r.Tag + ".zip\",\"browser_download_url\":\"" + r.ZipUrl + "\"},"
                + "{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"" + r.SumsUrl + "\"}]}");
            return "[" + string.Join(",", parts) + "]";
        }

        private static FakeHttpHandler Serve(params Rel[] rels)
        {
            var h = new FakeHttpHandler().AddText(GitHubClient.ReleasesUrl, Json(rels));
            foreach (var r in rels) h.Add(r.ZipUrl, r.Zip).AddText(r.SumsUrl, r.Sums);
            return h;
        }

        private int TemplateFolders()
        {
            return Directory.GetDirectories(_paths.Templates).Length;
        }

        [Fact]
        public async Task Latest_SkipsPrerelease()
        {
            var a = MakeRel("v1.0.0", "2026-01-01T00:00:00Z");
            var b = MakeRel("v1.1.0", "2026-02-01T00:00:00Z");
            var c = MakeRel("v2.0.0-beta.1", "2026-03-01T00:00:00Z", prerelease: true);
            var h = Serve(a, b, c);
            var latest = await new GitHubClient(h).GetLatestAsync();
            Assert.Equal("v1.1.0", latest.Tag);
            Assert.Equal(b.ZipUrl, latest.ZipUrl);
            Assert.Equal(b.SumsUrl, latest.SumsUrl);
            Assert.All(h.Methods, m => Assert.Equal(HttpMethod.Get, m));
        }

        [Fact]
        public async Task InstallRelease_VerifiesZipAndDll_StoresTemplate()
        {
            var r = MakeRel("v9.9.9", "2026-05-01T00:00:00Z");
            var gh = new GitHubClient(Serve(r));
            var store = new TemplateStore(_paths, gh);
            Assert.Null(store.Current);
            var rel = await gh.GetLatestAsync();
            Assert.True(store.IsNewer(rel));
            var info = await store.InstallReleaseAsync(rel);
            Assert.Equal("v9.9.9", store.Current.Tag);
            Assert.Equal(r.DllHash, Hashing.Sha256File(store.Current.DllPath));
            Assert.Equal(info.DllPath, store.Current.DllPath);
            Assert.False(store.IsNewer(rel));
            Assert.False(Directory.Exists(Path.Combine(_paths.Templates, ".incoming")));
        }

        [Fact]
        public async Task InstallRelease_FindsDllInSubfolder()
        {
            var r = MakeRel("v9.9.9", "2026-05-01T00:00:00Z", subfolder: true);
            var gh = new GitHubClient(Serve(r));
            var store = new TemplateStore(_paths, gh);
            await store.InstallReleaseAsync(await gh.GetLatestAsync());
            Assert.Equal(r.DllHash, Hashing.Sha256File(store.Current.DllPath));
        }

        [Fact]
        public async Task InstallRelease_ZipHashMismatch_Throws_PreviousTemplateUntouched()
        {
            var good = MakeRel("v1.0.0", "2026-01-01T00:00:00Z", "GOOD");
            var bad = MakeRel("v2.0.0", "2026-02-01T00:00:00Z", "EVIL");
            var h = Serve(good, bad);
            // A VALID zip with a different dll body; SUMS still lists the original zip hash.
            h.Add(bad.ZipUrl, MakeRel("v2.0.0", "2026-02-01T00:00:00Z", "TAMPERED").Zip);
            var gh = new GitHubClient(h);
            var store = new TemplateStore(_paths, gh);
            var rels = await gh.GetReleasesAsync();
            await store.InstallReleaseAsync(rels.First(x => x.Tag == "v1.0.0"));
            var before = Hashing.Sha256File(store.Current.DllPath);

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallReleaseAsync(rels.First(x => x.Tag == "v2.0.0")));
            Assert.Contains("downloaded zip does not match", ex.Message);

            Assert.Equal("v1.0.0", store.Current.Tag);
            Assert.Equal(before, Hashing.Sha256File(store.Current.DllPath));
            Assert.Equal(new[] { "v1.0.0" }, Directory.GetDirectories(_paths.Templates).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public async Task InstallRelease_DllHashMismatch_Throws_NothingInstalled()
        {
            var r = MakeRel("v1.0.0", "2026-01-01T00:00:00Z");
            r.Sums = Hashing.Sha256Bytes(r.Zip) + "  MFGUnlocker-v1.0.0.zip\n" + new string('A', 64) + "  MFGUnlocker.dll\n";
            var gh = new GitHubClient(Serve(r));
            var store = new TemplateStore(_paths, gh);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await store.InstallReleaseAsync(await gh.GetLatestAsync()));
            Assert.Null(store.Current);
            Assert.Equal(0, TemplateFolders());
        }

        [Fact]
        public async Task InstallRelease_DownloadFails_NoPartialFilesInTemplates()
        {
            var r = MakeRel("v1.0.0", "2026-01-01T00:00:00Z");
            var h = Serve(r);
            var gh = new GitHubClient(h);
            var store = new TemplateStore(_paths, gh);
            var rel = await gh.GetLatestAsync();
            h.Remove(r.SumsUrl); // zip downloads, sums fails
            await Assert.ThrowsAsync<HttpRequestException>(() => store.InstallReleaseAsync(rel));
            Assert.Empty(Directory.GetFileSystemEntries(_paths.Templates));
            Assert.Null(store.Current);
        }

        [Fact]
        public async Task KeepsCurrentAndPrevious_RemovesOlder()
        {
            var rels = new[]
            {
                MakeRel("v1.0.0", "2026-01-01T00:00:00Z", "one"),
                MakeRel("v1.1.0", "2026-02-01T00:00:00Z", "two"),
                MakeRel("v1.2.0", "2026-03-01T00:00:00Z", "three")
            };
            var gh = new GitHubClient(Serve(rels));
            var store = new TemplateStore(_paths, gh);
            foreach (var x in (await gh.GetReleasesAsync()).OrderBy(x => x.PublishedUtc))
                await store.InstallReleaseAsync(x);
            Assert.Equal(2, TemplateFolders());
            Assert.Equal("v1.2.0", store.Current.Tag);
            Assert.True(Directory.Exists(Path.Combine(_paths.Templates, "v1.1.0")));
            Assert.False(Directory.Exists(Path.Combine(_paths.Templates, "v1.0.0")));
        }

        [Fact]
        public async Task KnownHashes_CollectsFromAllReleases_CachedOffline()
        {
            var a = MakeRel("v1.0.0", "2026-01-01T00:00:00Z", "one");
            var b = MakeRel("v1.1.0", "2026-02-01T00:00:00Z", "two");
            var store = new TemplateStore(_paths, new GitHubClient(Serve(a, b)));
            await store.RefreshKnownHashesAsync();
            Assert.True(store.IsKnownRelease(a.DllHash));
            Assert.Equal("v1.1.0", store.VersionForHash(b.DllHash));

            var offline = new TemplateStore(_paths, new GitHubClient(new FakeHttpHandler()));
            Assert.True(offline.IsKnownRelease(a.DllHash));
            Assert.True(offline.IsKnownRelease(b.DllHash.ToLowerInvariant()));
            Assert.Equal("v1.0.0", offline.VersionForHash(a.DllHash));
            Assert.False(offline.IsKnownRelease(new string('0', 64)));
            Assert.Null(offline.VersionForHash("nope"));
        }

        [Fact]
        public async Task Offline_LatestThrows_CallerCanContinue()
        {
            var gh = new GitHubClient(new FakeHttpHandler());
            await Assert.ThrowsAsync<HttpRequestException>(() => gh.GetLatestAsync());
            var store = new TemplateStore(_paths, gh);
            Assert.Null(store.Current);
        }

        [Fact]
        public async Task Compatibility_FetchesFromRawGithub()
        {
            var h = new FakeHttpHandler().AddText(GitHubClient.CompatibilityUrl, "# table");
            Assert.Equal("# table", await new GitHubClient(h).GetCompatibilityMarkdownAsync());
        }

        [Fact]
        public async Task Download_RefusesOtherRepoOnGithub()
        {
            var url = "https://github.com/someone-else/other/releases/download/v1/x.zip";
            var h = new FakeHttpHandler().AddText(url, "x");
            await Assert.ThrowsAsync<ArgumentException>(() => new GitHubClient(h).DownloadAsync(url));
            Assert.Empty(h.Requests);
        }

        [Fact]
        public async Task Download_RefusesOtherHosts()
        {
            var h = new FakeHttpHandler().AddText("https://example.com/x", "x");
            await Assert.ThrowsAsync<ArgumentException>(() => new GitHubClient(h).DownloadAsync("https://example.com/x"));
            Assert.Empty(h.Requests);
        }
    }
}
