using System;
using System.IO;
using System.Linq;
using AgentPlay.Contract;
using AgentPlay.Editor;
using Newtonsoft.Json;
using NUnit.Framework;

namespace AgentPlay.Tests
{
    public class ReflexBookTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "agentplay-book-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "AgentPlay"));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        static HelloMessage Hello(string version = "v1")
        {
            var h = new HelloMessage { Game = "G", Version = version };
            h.Events.Add(new EventInfo { Name = "guard.spotted" });
            h.Actions.Add(new ActionInfo { Name = "hide_nearest" });
            return h;
        }

        static Reflex R(string id, string status = ReflexStatuses.Proposed, string action = "hide_nearest") =>
            new Reflex { Id = id, When = "event:guard.spotted", Do = action, Status = status, GameVersion = "v1" };

        string Inbox => Path.Combine(_root, "AgentPlay", "Inbox");

        void WriteInbox(string name, object content)
        {
            Directory.CreateDirectory(Inbox);
            File.WriteAllText(Path.Combine(Inbox, name), JsonConvert.SerializeObject(content, ContractJson.Settings));
        }

        [Test]
        public void MissingFileGivesEmptyBook()
        {
            var book = ReflexBook.Load(_root);
            Assert.IsNull(book.LoadError);
            Assert.AreEqual(0, book.Reflexes.Count);
        }

        [Test]
        public void SavingAnEmptyBookCreatesNoFile()
        {
            Assert.IsTrue(ReflexBook.Load(_root).Save());
            Assert.IsFalse(File.Exists(Path.Combine(_root, "AgentPlay", "reflexes.json")), "games without reflexes get no reflexes.json");
        }

        [Test]
        public void InboxIsImportedOnceAndMovedAway()
        {
            WriteInbox("reflex-L1.json", R("R-L1"));
            var book = ReflexBook.Load(_root);
            Assert.AreEqual(1, book.ImportInbox());
            Assert.AreEqual(ReflexStatuses.Proposed, book.Find("R-L1").Status);
            Assert.IsTrue(File.Exists(Path.Combine(Inbox, "imported", "reflex-L1.json")));
            Assert.AreEqual(1, ReflexBook.Load(_root).Reflexes.Count, "import saved the file");
        }

        [Test]
        public void InboxDoesNotDuplicateExistingId()
        {
            var book = ReflexBook.Load(_root);
            WriteInbox("a.json", R("R-L1"));
            book.ImportInbox();
            book.Approve("R-L1", "v1");
            book.Find("R-L1").Stats.Fired = 3;
            book.Save();
            WriteInbox("b.json", R("R-L1"));
            Assert.AreEqual(0, book.ImportInbox());
            Assert.AreEqual(1, book.Reflexes.Count);
            Assert.AreEqual(3, book.Find("R-L1").Stats.Fired);
            Assert.AreEqual(ReflexStatuses.Active, book.Find("R-L1").Status);
        }

        [Test]
        public void BrokenInboxFileGoesToRejected()
        {
            Directory.CreateDirectory(Inbox);
            File.WriteAllText(Path.Combine(Inbox, "bad.json"), "{ not json");
            var book = ReflexBook.Load(_root);
            Assert.AreEqual(0, book.ImportInbox());
            Assert.IsTrue(File.Exists(Path.Combine(Inbox, "rejected", "bad.json")));
        }

        [Test]
        public void CorruptFileIsNotOverwritten()
        {
            string path = Path.Combine(_root, "AgentPlay", "reflexes.json");
            File.WriteAllText(path, "{ broken");
            var book = ReflexBook.Load(_root);
            Assert.IsNotNull(book.LoadError);
            Assert.IsFalse(book.Save());
            Assert.AreEqual("{ broken", File.ReadAllText(path));
        }

        [Test]
        public void PrepareSessionMarksVersionChangesAndReturnsValidActive()
        {
            var book = ReflexBook.Load(_root);
            book.File.Reflexes.Add(R("R-old", ReflexStatuses.Active));
            book.Save();
            var loaded = book.PrepareSession(Hello("v2"), _ => { });
            Assert.AreEqual(0, loaded.Count);
            Assert.AreEqual(ReflexStatuses.NeedsReview, ReflexBook.Load(_root).Find("R-old").Status);
        }

        [Test]
        public void PrepareSessionSkipsInvalidReflex()
        {
            var book = ReflexBook.Load(_root);
            book.File.Reflexes.Add(R("R-ok", ReflexStatuses.Active));
            book.File.Reflexes.Add(R("R-gone", ReflexStatuses.Active, action: "removed_action"));
            string warned = null;
            var loaded = book.PrepareSession(Hello(), w => warned = w);
            CollectionAssert.AreEqual(new[] { "R-ok" }, loaded.Select(r => r.Id));
            StringAssert.Contains("R-gone", warned);
            Assert.AreEqual(ReflexStatuses.Active, book.Find("R-gone").Status, "status is kept");
        }

        [Test]
        public void PrepareSessionCapsActiveReflexes()
        {
            var book = ReflexBook.Load(_root);
            for (int i = 0; i < 20; i++) book.File.Reflexes.Add(R("R" + i, ReflexStatuses.Active));
            Assert.AreEqual(ReflexLimits.MaxActive, book.PrepareSession(Hello(), _ => { }).Count);
        }

        [Test]
        public void TransitionsSave()
        {
            var book = ReflexBook.Load(_root);
            book.File.Reflexes.Add(R("R-L1"));
            Assert.IsTrue(book.Approve("R-L1", "v3"));
            Assert.AreEqual("v3", ReflexBook.Load(_root).Find("R-L1").GameVersion);
            Assert.IsTrue(book.Disable("R-L1"));
            Assert.IsTrue(book.Reenable("R-L1", "v3"));
            Assert.IsTrue(book.Reject("R-L1"));
            Assert.AreEqual(ReflexStatuses.Rejected, ReflexBook.Load(_root).Find("R-L1").Status);
            Assert.IsFalse(book.Approve("missing", "v3"));
        }
    }
}
