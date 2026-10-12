using System;
using System.IO;
using System.Text;

namespace AgentPlay.Contract
{
    public static class AtomicFile
    {
        /// <summary>Writes through a temp file in the same folder so a reader never sees half a file.</summary>
        public static void WriteAllText(string path, string text)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
