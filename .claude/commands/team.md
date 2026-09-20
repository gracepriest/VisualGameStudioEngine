---
description: Run a task through the mixed-model team (Fable architect / Opus implementer / Sonnet tests / Haiku briefs)
argument-hint: <the task to work on>
---

Use the mixed-model team in `.claude/agents/` for this work.

## Roles

- **architect** (Fable 5.1) — Principal System Architect. Cross-cutting,
  hard-to-reverse decisions ONLY. It cannot explore the repo; it answers from a
  written brief. Never hand it a raw question.
- **brief** (Haiku) — context broker. Reads the repo and compresses it into a
  brief for the architect.
- **implementer** (Opus) — task-level planning and all production code.
- **test-writer** (Sonnet) — NUnit suite, specs, and ADR transcription.

## Protocol

1. Start with `implementer`. Plan the work, then do everything that does NOT
   depend on an open architectural question first.
2. Keep a running list of architectural questions. Do not escalate one at a time.
3. Before escalating, check `docs/superpowers/decisions/`. If it is answered
   there, it is answered — do not re-ask.
4. Escalate only when you have 3+ stacked questions, or one that blocks
   everything. Then: spawn `brief` to write the brief, hand that brief to
   `architect`, and have `test-writer` record the answer as an ADR before
   implementing against it.
5. Anything reversible in an afternoon is the implementer's call. Decide it and
   note the assumption in the summary.
6. Hand finished code to `test-writer` with the contract it must satisfy, the
   risky edge cases, and which entry points need covering.

Escalating costs real money. Not escalating is the common case.

## Task

$ARGUMENTS
