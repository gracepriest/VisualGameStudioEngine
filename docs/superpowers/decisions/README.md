# Architecture Decision Records

Decisions made by the Principal System Architect (`architect` agent, Fable 5.1).

**Why this directory exists:** the architect is the most expensive model on the
team. A decision recorded here costs nothing to read forever. A decision not
recorded here gets re-asked, and re-asking is the single largest source of
wasted spend in this setup.

## Rules

- **Read this directory before escalating anything.** If the question is
  answered here, it is answered.
- Every architect consultation produces an ADR — no exceptions, even for a
  one-line answer. The `Rejected` section matters as much as the decision; it
  is what stops the team re-litigating settled ground.
- Filename: `NNNN-short-slug.md`, four-digit sequence.
- ADRs are append-only in spirit. To reverse one, write a new ADR that
  supersedes it and add a `Superseded by` line to the old one. Never edit a
  decision's body to say something different from what was decided.

## Template

See `0000-template.md`.
