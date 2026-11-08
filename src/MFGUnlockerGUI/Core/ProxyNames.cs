using System;
using System.Collections.Generic;
using System.Linq;

namespace MFGUnlockerGUI.Core
{
    public static class ProxyNames
    {
        public static readonly IReadOnlyList<string> DefaultOrder = new[]
        {
            "version.dll", "winmm.dll", "dinput8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll",
            "dxgi.dll", "dsound.dll", "wininet.dll", "winhttp.dll", "xinput1_1.dll", "xinput1_2.dll",
            "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "xinputuap.dll"
        };

        public static readonly IReadOnlyList<string> BinkNames = new[] { "binkw64.dll", "bink2w64.dll" };

        public static readonly IReadOnlyList<string> OldLeftovers = new[]
        {
            "RTXMFG.dll", "RTX40MFGCore.dll", "RTX40MFG.asi", "RTX40MFG-UI.addon64"
        };

        public static bool IsSupported(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            return DefaultOrder.Concat(BinkNames).Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<string> OrderFor(Engine e)
        {
            switch (e)
            {
                case Engine.REEngine:
                    return DefaultOrder.Where(n => n != "dinput8.dll").ToList();
                default:
                    return DefaultOrder;
            }
        }
    }
}
