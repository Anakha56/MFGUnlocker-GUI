using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MFGUnlockerGUI.Core
{
    public enum GpuClass { NvidiaRtx20to40, NvidiaRtx50, Unsupported }

    /// <summary>Read-only system queries. Each one is wrapped so a failure gives an empty or null answer, never an exception.</summary>
    public static class SystemInfo
    {
        private static readonly Regex GeForceRtx = new Regex(@"\bRTX\s*(\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AdaWord = new Regex(@"\bAda\b|Quadro", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<string> GpuNames()
        {
            var list = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (var results = s.Get())
                    foreach (ManagementBaseObject o in results)
                    {
                        var n = o["Name"] as string;
                        if (!string.IsNullOrWhiteSpace(n)) list.Add(n.Trim());
                        o.Dispose();
                    }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>
        /// Pure. GeForce RTX 20 to 40 series and 50 series are recognised by the four-digit model number.
        /// Workstation cards (RTX A4000, "Ada" generation, Quadro) are Unsupported: no MFG claim is made for them.
        /// </summary>
        public static GpuClass Classify(IEnumerable<string> gpuNames)
        {
            var best = GpuClass.Unsupported;
            if (gpuNames == null) return best;
            foreach (var name in gpuNames)
            {
                if (string.IsNullOrEmpty(name) || AdaWord.IsMatch(name)) continue;
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = GeForceRtx.Match(name);
                if (!m.Success) continue;
                int n = int.Parse(m.Groups[1].Value);
                if (n >= 5000 && n < 6000) return GpuClass.NvidiaRtx50;
                if (n >= 2000 && n < 5000) best = GpuClass.NvidiaRtx20to40;
            }
            return best;
        }

        /// <summary>True when any GeForce RTX 30xx (desktop or Laptop) is present.</summary>
        public static bool HasRtx30(IEnumerable<string> gpuNames)
        {
            return AnyRtxInRange(gpuNames, 3000, 4000);
        }

        /// <summary>True when any GeForce RTX 20xx or 30xx (desktop or Laptop) is present.</summary>
        public static bool HasRtx20or30(IEnumerable<string> gpuNames)
        {
            return AnyRtxInRange(gpuNames, 2000, 4000);
        }

        private static bool AnyRtxInRange(IEnumerable<string> gpuNames, int fromInclusive, int toExclusive)
        {
            if (gpuNames == null) return false;
            foreach (var name in gpuNames)
            {
                if (string.IsNullOrEmpty(name) || AdaWord.IsMatch(name)) continue;
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = GeForceRtx.Match(name);
                if (!m.Success) continue;
                int n = int.Parse(m.Groups[1].Value);
                if (n >= fromInclusive && n < toExclusive) return true;
            }
            return false;
        }

        /// <summary>Shown after an install on RTX 20/30 cards, where the game's frame generation option may need the mod's Unlock tick.</summary>
        public const string Rtx30UnlockHint = "Your RTX 30/20 card: if the game's frame generation option is missing or stays off, open the mod's menu (Backspace) and tick \"Game FG option: Unlock\", then turn frame generation on in the game's settings.";

        /// <summary>true = on (including evaluation mode), false = off, null = not present.</summary>
        public static bool? SmartAppControlOn()
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy"))
                {
                    if (k == null) return null;
                    var v = k.GetValue("VerifiedAndReputablePolicyState");
                    if (!(v is int)) return null;
                    switch ((int)v)
                    {
                        case 0: return false;
                        case 1:
                        case 2: return true;
                        default: return null;
                    }
                }
            }
            catch (Exception) { return null; }
        }

        public static bool IsElevated()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        public static string NvidiaDriverVersion()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                using (var results = s.Get())
                    foreach (ManagementBaseObject o in results)
                    {
                        var n = o["Name"] as string;
                        var d = o["DriverVersion"] as string;
                        o.Dispose();
                        if (n != null && n.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(d)) return d;
                    }
            }
            catch (Exception) { }
            return null;
        }
    }
}
