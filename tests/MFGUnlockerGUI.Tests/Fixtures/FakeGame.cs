using System;
using System.IO;

namespace MFGUnlockerGUI.Tests.Fixtures
{
    /// <summary>Builds a fake game folder under %TEMP%; deleted on Dispose.</summary>
    public sealed class FakeGame : IDisposable
    {
        public string Root { get; private set; }

        private FakeGame(string root) { Root = root; }

        public static FakeGame Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "MFGUnlockerGUI-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new FakeGame(root);
        }

        public FakeGame File(string relPath, long sizeBytes = 1)
        {
            var full = Path.Combine(Root, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write))
                fs.SetLength(sizeBytes);
            return this;
        }

        public FakeGame TextFile(string relPath, string content)
        {
            var full = Path.Combine(Root, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            System.IO.File.WriteAllText(full, content);
            return this;
        }

        public FakeGame Dir(string relPath)
        {
            Directory.CreateDirectory(Path.Combine(Root, relPath));
            return this;
        }

        public string Path_(string relPath) { return Path.Combine(Root, relPath); }

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
        }
    }
}
