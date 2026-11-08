using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MFGUnlockerGUI.Core
{
    [DataContract]
    public class GameRecord
    {
        [DataMember] public string GameRoot { get; set; }
        [DataMember] public string ExePath { get; set; }
        [DataMember] public string TargetDir { get; set; }
        [DataMember] public string InstalledName { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public string Sha256 { get; set; }
        [DataMember] public DateTime InstalledUtc { get; set; }
        [DataMember] public List<string> FailedNames { get; set; }
        /// <summary>Nullable, e.g. "binkw64Hooked.dll".</summary>
        [DataMember] public string BinkHookedName { get; set; }
        [DataMember] public string WorkingName { get; set; }

        public GameRecord() { FailedNames = new List<string>(); }
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember] public int BackupsToKeep { get; set; }
        public AppSettings() { BackupsToKeep = 3; }
    }

    [DataContract]
    internal class StateFileModel
    {
        [DataMember] public List<GameRecord> Games { get; set; }
        [DataMember] public AppSettings Settings { get; set; }
    }

    public class StateStore
    {
        private readonly AppPaths _paths;
        private readonly Dictionary<string, GameRecord> _games =
            new Dictionary<string, GameRecord>(StringComparer.OrdinalIgnoreCase);

        public AppSettings Settings { get; private set; }

        public StateStore(AppPaths p)
        {
            _paths = p;
            Settings = new AppSettings();
            Load();
        }

        private static string Key(string gameRoot)
        {
            return Path.GetFullPath(gameRoot).TrimEnd('\\', '/');
        }

        private void Load()
        {
            if (!File.Exists(_paths.StateFile)) return;
            try
            {
                StateFileModel m;
                using (var fs = File.OpenRead(_paths.StateFile))
                    m = (StateFileModel)new DataContractJsonSerializer(typeof(StateFileModel)).ReadObject(fs);
                if (m == null) throw new InvalidDataException("empty state");
                if (m.Settings != null) Settings = m.Settings;
                if (Settings.BackupsToKeep < 1) Settings.BackupsToKeep = 1;
                if (m.Games != null)
                    foreach (var g in m.Games)
                        if (g != null && !string.IsNullOrEmpty(g.GameRoot))
                        {
                            if (g.FailedNames == null) g.FailedNames = new List<string>();
                            _games[Key(g.GameRoot)] = g;
                        }
            }
            catch (Exception)
            {
                _games.Clear();
                Settings = new AppSettings();
                var bad = _paths.StateFile + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
                try { File.Move(_paths.StateFile, bad); } catch { }
            }
        }

        public GameRecord Get(string gameRoot)
        {
            GameRecord r;
            return _games.TryGetValue(Key(gameRoot), out r) ? r : null;
        }

        public void Save(GameRecord r)
        {
            _games[Key(r.GameRoot)] = r;
            Write();
        }

        public void Remove(string gameRoot)
        {
            if (_games.Remove(Key(gameRoot))) Write();
        }

        public IReadOnlyList<GameRecord> All()
        {
            return _games.Values.ToList();
        }

        public void SaveSettings()
        {
            if (Settings.BackupsToKeep < 1) Settings.BackupsToKeep = 1;
            Write();
        }

        private void Write()
        {
            var m = new StateFileModel { Games = _games.Values.ToList(), Settings = Settings };
            var tmp = _paths.StateFile + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                new DataContractJsonSerializer(typeof(StateFileModel)).WriteObject(fs, m);
            if (File.Exists(_paths.StateFile))
                File.Replace(tmp, _paths.StateFile, null);
            else
                File.Move(tmp, _paths.StateFile);
        }
    }
}
