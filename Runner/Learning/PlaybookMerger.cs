using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentPlay.Contract;
using Newtonsoft.Json.Linq;

namespace AgentPlay.Runner.Learning
{
    public sealed class MergeResult
    {
        public int New;
        public int Confirmed;
        public int Contradicted;
        /// <summary>Reflexes to drop into AgentPlay/Inbox for the user to review.</summary>
        public readonly List<Reflex> Proposals = new List<Reflex>();
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>Folds one reflector answer into the playbook (spec M3 section 6.4). Pure: the caller saves.</summary>
    public static class PlaybookMerger
    {
        public static MergeResult Merge(Playbook pb, ReflectorOutput output, string traceName, HelloMessage hello, int minConfirmations, DateTime now)
        {
            var result = new MergeResult();
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string stamp = now.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

            foreach (var item in output.Lessons)
            {
                if (item == null) continue;
                var lesson = string.IsNullOrWhiteSpace(item.Id) ? null : pb.Lessons.FirstOrDefault(l => string.Equals(l.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                bool known = lesson != null && item.Verdict != ReflectorLesson.New;

                if (!known && item.Verdict == ReflectorLesson.Contradicted)
                {
                    result.Notes.Add("ignored a contradiction of unknown lesson " + item.Id);
                    continue;
                }
                if (!known)
                {
                    lesson = new Lesson { Id = "L" + pb.NextId++, Text = item.Text.Trim() };
                    pb.Lessons.Add(lesson);
                }
                // One trace is one piece of evidence per lesson.
                if (!touched.Add(lesson.Id) || lesson.Sources.Contains(traceName)) continue;

                if (!known)
                {
                    lesson.Confirmations = 1;
                    result.New++;
                }
                else if (item.Verdict == ReflectorLesson.Confirmed)
                {
                    lesson.Confirmations++;
                    if (!string.IsNullOrWhiteSpace(item.Text)) lesson.Text = item.Text.Trim();
                    result.Confirmed++;
                }
                else
                {
                    lesson.Contradictions++;
                    result.Contradicted++;
                }
                lesson.GameVersion = hello.Version;
                lesson.Sources.Add(traceName);
                lesson.Updated = stamp;

                if (item.Reflex != null)
                {
                    var errors = ReflexValidator.Validate(ToReflex(lesson, item.Reflex, hello), hello);
                    if (errors.Count == 0) lesson.Reflex = item.Reflex;
                    else result.Notes.Add(lesson.Id + ": reflex dropped: " + string.Join("; ", errors));
                }
            }

            foreach (var lesson in pb.Lessons)
            {
                if (lesson.ReflexProposed || lesson.Reflex == null || lesson.Contradictions > 0 || lesson.Confirmations < minConfirmations) continue;
                var reflex = ToReflex(lesson, lesson.Reflex, hello);
                if (ReflexValidator.Validate(reflex, hello).Count > 0) continue;   // the game changed since the candidate was stored
                result.Proposals.Add(reflex);
                lesson.ReflexProposed = true;
            }

            while (pb.Lessons.Count > Playbook.MaxLessons)
            {
                var weakest = pb.Lessons.OrderBy(Playbook.Score).ThenBy(l => l.Updated, StringComparer.Ordinal).First();
                pb.Lessons.Remove(weakest);
                result.Notes.Add("dropped lesson " + weakest.Id + " (playbook keeps " + Playbook.MaxLessons + ")");
            }
            return result;
        }

        static Reflex ToReflex(Lesson lesson, ReflexCandidate c, HelloMessage hello) => new Reflex
        {
            Id = "R-" + lesson.Id,
            LessonId = lesson.Id,
            Text = lesson.Text,
            When = c.When,
            Do = c.Do,
            Args = c.Args ?? new JObject(),
            CooldownSeconds = 1,
            Status = ReflexStatuses.Proposed,
            GameVersion = hello.Version,
        };
    }
}
