"""Shows how calls per game minute change across runner sessions (spec M4a section 6, criterion 5).

Usage: telemetry_trend.py TELEMETRY_DIR [--last N]
Exit 0 when the last session is lower than the first and the regression slope is negative,
1 when not, 2 when there are fewer than two sessions.
"""
import json
import pathlib
import sys


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    folder = pathlib.Path(argv[0])
    last = None
    if "--last" in argv:
        last = int(argv[argv.index("--last") + 1])
    files = sorted(folder.glob("*.json"))
    if last:
        files = files[-last:]
    rows = []
    for f in files:
        r = json.loads(f.read_text(encoding="utf-8"))
        rows.append((f.stem, r.get("mode"), r.get("result"), r.get("model_calls", 0),
                     float(r.get("calls_per_game_minute", 0.0)), r.get("important_events_handled_by_reflex", 0)))
    print("%-20s %-10s %-9s %6s %10s %8s" % ("session", "mode", "result", "calls", "calls/min", "reflex"))
    for name, mode, result, calls, rate, reflex in rows:
        print("%-20s %-10s %-9s %6d %10.2f %8d" % (name, mode, result, calls, rate, reflex))
    if len(rows) < 2:
        print("need at least two sessions")
        return 2
    ys = [r[4] for r in rows]
    n = len(ys)
    mean_x = (n - 1) / 2.0
    mean_y = sum(ys) / n
    slope = sum((i - mean_x) * (y - mean_y) for i, y in enumerate(ys)) / sum((i - mean_x) ** 2 for i in range(n))
    ok = ys[-1] < ys[0] and slope < 0
    print("first %.2f, last %.2f, slope %.3f per session: %s" % (ys[0], ys[-1], slope, "decreasing" if ok else "not decreasing"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
