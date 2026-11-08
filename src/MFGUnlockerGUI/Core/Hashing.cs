using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MFGUnlockerGUI.Core
{
    public static class Hashing
    {
        /// <summary>Uppercase hex SHA-256, matching SHA256SUMS.txt.</summary>
        public static string Sha256File(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(fs));
        }

        public static string Sha256Bytes(byte[] data)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(data));
        }

        private static string Hex(byte[] h)
        {
            var sb = new StringBuilder(h.Length * 2);
            foreach (var b in h) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
