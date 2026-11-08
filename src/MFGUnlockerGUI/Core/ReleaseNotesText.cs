using System.Text.RegularExpressions;

namespace MFGUnlockerGUI.Core
{
    /// <summary>Turns GitHub release-note markdown into plain text for a text box.</summary>
    public static class ReleaseNotesText
    {
        private static readonly Regex Link = new Regex(@"\[([^\]]*)\]\(([^)\s]+)[^)]*\)", RegexOptions.Compiled);
        private static readonly Regex Heading = new Regex(@"^[ ]{0,3}#{1,6}[ \t]*", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Line breaks come back as plain "\n". Null gives an empty string.</summary>
        public static string ToPlain(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            var s = md.Replace("\r\n", "\n").Replace("\r", "\n");
            s = Link.Replace(s, m => m.Groups[1].Value + " (" + m.Groups[2].Value + ")");
            s = Heading.Replace(s, "");
            s = s.Replace("**", "").Replace("__", "").Replace("`", "");
            return s.Trim();
        }
    }
}
