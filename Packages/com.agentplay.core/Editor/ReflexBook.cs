using System;
using System.Collections.Generic;
using System.IO;
using AgentPlay.Contract;
using Newtonsoft.Json;

namespace AgentPlay.Editor
{
    /// <summary>The project's AgentPlay/reflexes.json. Unity is its only writer (spec M3 section 2).</summary>
    public sealed class ReflexBook
    {
        public string FilePath { get; private set; }
        public string InboxDir { get; private set; }
        public ReflexFile File { get; private set; } = new ReflexFile();
        /// <summary>Set when the file exists but cannot be read; the book then never writes.</summary>
        public string LoadError { get; private set; }
        public IReadOnlyList<Reflex> Reflexes => File.Reflexes;

        public static ReflexBook Load(string projectRoot)
        {
            var book = new ReflexBook
            {
                FilePath = Path.Combine(projectRoot, "AgentPlay", "reflexes.json"),
                InboxDir = Path.Combine(projectRoot, "AgentPlay", "Inbox"),
            };
            if (!System.IO.File.Exists(book.FilePath)) return book;
            try
            {
                var file = JsonConvert.DeserializeObject<ReflexFile>(System.IO.File.ReadAllText(book.FilePath), ContractJson.Settings) ?? new ReflexFile();
                if (file.Reflexes == null) file.Reflexes = new List<Reflex>();
                if (file.Policy == null) file.Policy = new ReflexPolicy();
                foreach (var r in file.Reflexes)
                    if (r.Stats == null) r.Stats = new ReflexStats();
                book.File = file;
            }
            catch (Exception e) when (e is JsonException || e is IOException)
            {
                book.File = new ReflexFile();
                book.LoadError = "Cannot read " + book.FilePath + ": " + e.Message;
            }
            return book;
        }

        public Reflex Find(string id)
        {
            foreach (var r in File.Reflexes)
                if (r.Id == id) return r;
            return null;
        }

        public bool Save()
        {
            if (LoadError != null) return false;
            AtomicFile.WriteAllText(FilePath, JsonConvert.SerializeObject(File, Formatting.Indented, ContractJson.Settings));
            return true;
        }

        /// <summary>Moves Inbox/*.json into the book as proposed reflexes. Returns how many were added.</summary>
        public int ImportInbox()
        {
            if (LoadError != null || !Directory.Exists(InboxDir)) return 0;
            int added = 0;
            foreach (var path in Directory.GetFiles(InboxDir, "*.json"))
            {
                Reflex r = null;
                try
                {
                    r = JsonConvert.DeserializeObject<Reflex>(System.IO.File.ReadAllText(path), ContractJson.Settings);
                }
                catch (JsonException)
                {
                }
                if (r == null || string.IsNullOrWhiteSpace(r.Id))
                {
                    MoveTo(path, "rejected");
                    continue;
                }
                if (Find(r.Id) == null)
                {
                    r.Status = ReflexStatuses.Proposed;
                    r.Stats = new ReflexStats();
                    if (r.Args == null) r.Args = new Newtonsoft.Json.Linq.JObject();
                    File.Reflexes.Add(r);
                    added++;
                }
                MoveTo(path, "imported");
            }
            if (added > 0) Save();
            return added;
        }

        /// <summary>Called at Play start: version check, then the valid active reflexes to load (at most ReflexLimits.MaxActive).</summary>
        public List<Reflex> PrepareSession(HelloMessage hello, Action<string> warn)
        {
            var result = new List<Reflex>();
            if (LoadError != null)
            {
                warn(LoadError);
                return result;
            }
            if (ReflexLifecycle.MarkVersionChanges(File, hello.Version).Count > 0) Save();
            foreach (var r in File.Reflexes)
            {
                if (r.Status != ReflexStatuses.Active) continue;
                var errors = ReflexValidator.Validate(r, hello);
                if (errors.Count > 0)
                {
                    warn("Reflex " + r.Id + " is not loaded: " + string.Join("; ", errors));
                    continue;
                }
                if (result.Count == ReflexLimits.MaxActive)
                {
                    warn("Only " + ReflexLimits.MaxActive + " reflexes can be active; " + r.Id + " is not loaded");
                    continue;
                }
                result.Add(r);
            }
            return result;
        }

        public bool Approve(string id, string version) => Apply(id, r => ReflexLifecycle.Approve(r, version));

        public bool Reject(string id) => Apply(id, ReflexLifecycle.Reject);

        public bool Disable(string id) => Apply(id, ReflexLifecycle.Disable);

        public bool Reenable(string id, string version) => Apply(id, r => ReflexLifecycle.Reenable(r, version));

        bool Apply(string id, Func<Reflex, bool> change)
        {
            var r = Find(id);
            if (r == null || !change(r)) return false;
            Save();
            return true;
        }

        void MoveTo(string path, string sub)
        {
            string dir = Path.Combine(InboxDir, sub);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, Path.GetFileName(path));
            if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
            System.IO.File.Move(path, target);
        }
    }
}
