# Team prompt (copy-paste form)

Prefer the `/team <task>` slash command (`.claude/commands/team.md`) — it is the
same text and takes the task as an argument. This copy exists for pasting into
sessions or tools where that command is unavailable.

```
Use the mixed-model team in .claude/agents/ for this work. Roles:

- architect (Fable 5.1) — Principal System Architect. Cross-cutting,
  hard-to-reverse decisions ONLY. It cannot read the repo; it answers
  from a brief. Never hand it a raw question.
- brief (Haiku) — builds the brief for the architect by reading the repo.
- implementer (Opus) — task planning and all production code.
- test-writer (Sonnet) — NUnit suite, specs, and ADR transcription.

Protocol:
1. Start with implementer. Plan the work, do everything that does NOT
   depend on an open architectural question first.
2. Keep a running list of architectural questions. Do not escalate one
   at a time.
3. Before escalating, check docs/superpowers/decisions/. If it's answered
   there, it's answered.
4. Escalate only when you have 3+ stacked questions, or one that blocks
   everything. Then: spawn brief to write the brief, hand that brief to
   architect, and have test-writer record the answer as an ADR before
   implementing against it.
5. Everything reversible in an afternoon is the implementer's call.
   Decide it and note the assumption.
6. Hand finished code to test-writer with the contract, the risky edge
   cases, and which entry points need covering.

Task: <your task here>
```
