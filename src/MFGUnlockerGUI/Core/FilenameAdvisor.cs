using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFGUnlockerGUI.Core
{
    public class NameOption
    {
        public string Name { get; set; }
        public bool Allowed { get; set; }
        public string Note { get; set; }
    }

    public class FilenameAdvice
    {
        /// <summary>Null when no name is left to try.</summary>
        public string Recommended { get; set; }
        public IReadOnlyList<NameOption> Options { get; set; }
        public IReadOnlyList<string> ModWarnings { get; set; }
        public bool BinkAvailable { get; set; }
    }

    public static class FilenameAdvisor
    {
        public static FilenameAdvice Advise(string targetDir, Engine engine, CompatEntry compat,
            IReadOnlyCollection<string> failedNames, string workingName)
        {
            var failed = new HashSet<string>(
                (failedNames ?? new string[0]).Select(n => n.ToLowerInvariant()));
            var present = ListFileNames(targetDir);
            var dont = new Dictionary<string, string>();
            if (compat != null && compat.DontUse != null)
                foreach (var d in compat.DontUse)
                    dont[d.Name.ToLowerInvariant()] = d.Reason;
            var compatNames = new HashSet<string>(
                ((compat != null ? compat.Use : null) ?? new string[0])
                .Concat((compat != null ? compat.AlsoLoaded : null) ?? new string[0])
                .Select(n => n.ToLowerInvariant()));

            // Order: working name, compat Use, compat AlsoLoaded, engine order; then dont-use names
            // so they are still listed (as not allowed).
            var order = new List<string>();
            if (!string.IsNullOrEmpty(workingName)) order.Add(workingName);
            if (compat != null)
            {
                if (compat.Use != null) order.AddRange(compat.Use);
                if (compat.AlsoLoaded != null) order.AddRange(compat.AlsoLoaded);
            }
            order.AddRange(ProxyNames.OrderFor(engine));
            order.AddRange(dont.Keys);

            var seen = new HashSet<string>();
            var options = new List<NameOption>();
            bool binkAvailable = false;
            foreach (var raw in order)
            {
                var name = raw.ToLowerInvariant();
                if (!seen.Add(name)) continue;
                bool isBink = ProxyNames.BinkNames.Contains(name);
                bool isWorking = !string.IsNullOrEmpty(workingName)
                    && string.Equals(workingName, name, StringComparison.OrdinalIgnoreCase);
                bool taken = present.Contains(name);

                if (isBink)
                {
                    // The Bink route only makes sense when the game's own file is there.
                    if (!taken) continue;
                    binkAvailable = true;
                    options.Add(BinkOption(name, failed));
                    continue;
                }

                var opt = new NameOption { Name = name, Allowed = true, Note = "" };
                if (isWorking)
                {
                    opt.Note = "worked before";
                }
                else if (taken)
                {
                    opt.Allowed = false;
                    opt.Note = "Taken by a file already in the folder";
                }
                else if (failed.Contains(name))
                {
                    opt.Allowed = false;
                    opt.Note = "Tried, didn't load";
                }
                else if (dont.ContainsKey(name))
                {
                    opt.Allowed = false;
                    opt.Note = "Player reports: " + (string.IsNullOrWhiteSpace(dont[name]) ? "avoid" : dont[name]);
                }
                options.Add(opt);
            }

            // Bink files in the folder that no list mentioned.
            foreach (var b in ProxyNames.BinkNames)
            {
                if (seen.Contains(b) || !present.Contains(b)) continue;
                binkAvailable = true;
                options.Add(BinkOption(b, failed));
            }

            var rec = options.FirstOrDefault(o => o.Allowed && !ProxyNames.BinkNames.Contains(o.Name));
            if (rec != null)
            {
                if (rec.Note == "")
                    rec.Note = compatNames.Contains(rec.Name) ? "Recommended (from player reports)" : "Recommended";
                else
                    rec.Note = "Recommended (" + rec.Note + ")";
            }

            return new FilenameAdvice
            {
                Recommended = rec != null ? rec.Name : null,
                Options = options,
                ModWarnings = FindModWarnings(targetDir),
                BinkAvailable = binkAvailable
            };
        }

        static NameOption BinkOption(string name, HashSet<string> failed)
        {
            bool f = failed.Contains(name);
            return new NameOption
            {
                Name = name,
                Allowed = !f,
                Note = f ? "Tried, didn't load" : "Bink route: renames the game's " + name
            };
        }

        static HashSet<string> ListFileNames(string dir)
        {
            var set = new HashSet<string>();
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir))
                        set.Add(Path.GetFileName(f).ToLowerInvariant());
            }
            catch { }
            return set;
        }

        static IReadOnlyList<string> FindModWarnings(string targetDir)
        {
            var warnings = new List<string>();
            var roots = new List<string>();
            try
            {
                var d = targetDir;
                for (int i = 0; i < 3 && !string.IsNullOrEmpty(d); i++)
                {
                    roots.Add(d);
                    d = Path.GetDirectoryName(d.TrimEnd('\\', '/'));
                }
            }
            catch { }

            Func<string, bool> fileAny = rel => roots.Any(r => SafeExists(() => File.Exists(Path.Combine(r, rel))));
            Func<string, bool> dirAny = rel => roots.Any(r => SafeExists(() => Directory.Exists(Path.Combine(r, rel))));

            if (fileAny("ReShade.ini"))
                warnings.Add("ReShade found: its dxgi mod may hide the MFG menu even when the filename is right.");
            if (fileAny("OptiScaler.ini"))
                warnings.Add("OptiScaler found: it normally uses dxgi.dll, so avoid that name.");
            if (fileAny("SpecialK64.dll"))
                warnings.Add("Special K found: it normally uses dxgi.dll, so avoid that name.");
            if (dirAny("reframework"))
                warnings.Add("REFramework found: it uses dinput8.dll, so avoid that name.");
            if (dirAny("red4ext"))
                warnings.Add("RED4ext found: it uses winmm.dll, so avoid that name.");
            if (dirAny(Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks")))
                warnings.Add("Cyber Engine Tweaks found: it uses version.dll, so avoid that name.");
            return warnings;
        }

        static bool SafeExists(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }
    }
}
