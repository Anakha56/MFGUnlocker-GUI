using System;
using System.IO;

namespace MFGUnlockerGUI.Core
{
    public class AppPaths
    {
        public AppPaths(string root)
        {
            Root = root;
            Templates = Path.Combine(root, "templates");
            Backups = Path.Combine(root, "backups");
            Reports = Path.Combine(root, "reports");
            StateFile = Path.Combine(root, "state.json");
            CompatCache = Path.Combine(root, "compat-cache.json");
            KnownHashesFile = Path.Combine(root, "known-hashes.json");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Templates);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Reports);
        }

        public static AppPaths Default
        {
            get
            {
                return new AppPaths(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MFGUnlockerGUI"));
            }
        }

        public string Root { get; private set; }
        public string Templates { get; private set; }
        public string Backups { get; private set; }
        public string Reports { get; private set; }
        public string StateFile { get; private set; }
        public string CompatCache { get; private set; }
        public string KnownHashesFile { get; private set; }
    }
}
