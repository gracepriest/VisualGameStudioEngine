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

## Index

One line per ADR; the file is the decision. The `-brief` files are the architect's inputs, kept beside the
ADR they produced. Amendments are appended to the ADR they amend ("Amended by" in its header).

| ADR | Decision |
|---|---|
| 0001 | C# backend: temp materialisation |
| 0002 | Interface property accessor flags |
| 0003 | CFG loop representation |
| 0004 | Family-111 rulings |
| 0005 | Integer division, CSE destination, interface property type |
| 0006 | Kill-vocabulary totality; dynamic-use call visibility |
| 0007 | Bare-name property lowering fidelity |
| 0008 | Guard semantics and call visibility of computed values |
| 0009 | For Each control variable |
| 0010 | Lambdas and closures on MSIL — closure conversion as an opt-in IR pass. **Amended by 0019** (D1: options/result contract, C++ as the second consumer, a function is lowered once) |
| 0011 | `Is` / `IsNot` reference identity |
| 0012 | Object comparison, late-bound |
| 0013 | Case-insensitive name binding, front end to IR |
| 0014 | Per-iteration loop-body `Dim` with copy-forward. **Amended by 0019** (D2's revisit-if: L8 agrees on C++, JavaScript and MSIL) |
| 0015 | C++ `Me` as a value; two-phase construction |
| 0016 | `MyBase.New(...)` is an instruction. **Amended by 0019** (D3's C++ arm: W2 is the by-copy fallback's soundness proof) |
| 0017 | DCE removal licence: the compiler-temp marker |
| 0018 | One reservation set per function; one door for an optimizer temp |
| 0019 | C++ closures: ClosureLowering by default, with a W2-gated by-copy fallback (#140). E16 (#229): the owner decided to admit it |
