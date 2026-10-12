using System;
using System.IO;

namespace AgentPlay.Runner.Learning
{
    /// <summary>
    /// One reflect run per game project at a time: two runs would both load the playbook and the last save would win.
    /// The lock is an exclusively opened file that the OS releases even if the process dies.
    /// </summary>
    public sealed class ReflectLock : IDisposable
    {
        readonly FileStream _stream;

        ReflectLock(FileStream stream)
        {
            _stream = stream;
        }

        /// <summary>Null when another run holds the lock.</summary>
        public static ReflectLock TryAcquire(string projectRoot)
        {
            string path = Path.Combine(projectRoot, "AgentPlay", ".reflect.lock");
            try
            {
                return new ReflectLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
