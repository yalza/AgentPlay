# Brief template

`<game>/AgentPlay/brief.md` is sent to the model at the start of every session. Write it for a player who sees only the observes and can only use the actions.

```markdown
# <Game>

<One paragraph: who the player is and what the world looks like, in terms of the observes.>

- Goal for this run: <the user's goal, e.g. "complete the first 3 missions">.
- Win: <the exact condition behind game.won>.
- Lose: <the condition behind game.lost, or "no lose condition; the run ends at the deadline">.
- How things work that the observes do not show: <timers, costs, what each action does in the world, how long things take>.
- Tips: <a known safe order of actions, if the survey found one>.
```

Keep it short (under 40 lines). Name observes and actions exactly as the adapter declares them.
