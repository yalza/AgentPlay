using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgentPlay
{
    public static class TracePaths
    {
        /// <summary>&lt;root&gt;/AgentPlay/Traces/yyyyMMdd-HHmmss-who-tag[-n].ndjson</summary>
        public static string Build(string projectRoot, DateTime localNow, string who, string tag, Func<string, bool> exists)
        {
            string dir = Path.Combine(projectRoot, "AgentPlay", "Traces");
            string stem = localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + who + "-" + tag;
            string path = Path.Combine(dir, stem + ".ndjson");
            for (int n = 2; exists(path); n++)
                path = Path.Combine(dir, stem + "-" + n + ".ndjson");
            return path;
        }

        public static TraceWriter Open(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var stream = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            return new TraceWriter(stream);
        }
    }
}
