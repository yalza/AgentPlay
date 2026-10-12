"""Checks the learning half of Tools/e2e-m3-learn.sh. Usage: verify_m3_learn.py PROJECT LOGS"""
import json
import pathlib
import re
import sys

project = pathlib.Path(sys.argv[1])
logs = pathlib.Path(sys.argv[2])
ap = project / "AgentPlay"
failures = []


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


traces = sorted((ap / "Traces").glob("*-human-play*.ndjson"))
check(len(traces) == 3, "three pilot traces (%d)" % len(traces))
names = {t.name for t in traces}
for t in traces:
    records = [json.loads(l) for l in t.read_text(encoding="utf-8").splitlines() if l.strip()]
    header = records[0]
    human = header.get("who") == "human" and header.get("tag") == "play" and "hello" in header
    no_agent = not any(r.get("type") == "control" and r.get("who") == "agent" for r in records)
    check(human and no_agent, t.name + " is a human play session with hello and no agent control")

reflect_log = (logs / "reflect.log").read_text(encoding="utf-8", errors="replace")
check(re.search(r"reflect done: 3 trace\(s\)", reflect_log) is not None, "reflect processed the three traces")

playbook = json.loads((ap / "playbook.json").read_text(encoding="utf-8")) if (ap / "playbook.json").exists() else {"lessons": []}
from_pilot = [l for l in playbook["lessons"] if names & set(l.get("sources", []))]
check(len(from_pilot) >= 1, "the playbook has lessons sourced from the pilot sessions (%d)" % len(from_pilot))
check((ap / "playbook.md").exists(), "playbook.md was written")
ledger = json.loads((ap / "reflected.json").read_text(encoding="utf-8")) if (ap / "reflected.json").exists() else []
check(names <= set(ledger), "reflected.json lists the three traces")
for l in playbook["lessons"]:
    print("lesson %s: confirmed %d, contradicted %d, reflex %s: %s" % (
        l["id"], l["confirmations"], l["contradictions"], "yes" if l.get("reflex") else "no", l["text"].encode("ascii", "replace").decode()))

proposals = sorted((ap / "Inbox").glob("reflex-*.json"))
valid = []
for p in proposals:
    r = json.loads(p.read_text(encoding="utf-8"))
    if r.get("status") == "proposed" and r.get("id", "").startswith("R-"):
        valid.append(r)
        print("proposal %s: when %s do %s(%s)" % (r["id"], r["when"], r["do"], json.dumps(r.get("args"))))
check(len(valid) >= 1, "the reflector proposed at least one reflex (%d)" % len(valid))

print("%d check(s) failed" % len(failures) if failures else "ALL CHECKS PASSED")
sys.exit(1 if failures else 0)
