# Mixed-model team protocol

Four agents in `.claude/agents/`, each pinned to a model, arranged so the
expensive one is used rarely and cheaply.

| Agent | Model | Owns | Reads repo? |
|---|---|---|---|
| `architect` | Fable 5.1 | Cross-cutting, hard-to-reverse decisions | **No** |
| `brief` | Haiku | Compressing the repo into a one-page brief | Yes |
| `implementer` | Opus | Task planning + all production code | Yes |
| `test-writer` | Sonnet | NUnit suite, specs, ADR transcription | Yes |

## The flow

```
implementer plans, collects architectural questions, does everything
        that doesn't depend on them
  -> 3+ questions stacked, or one that blocks everything?
       -> brief (Haiku) reads the repo, writes a <900-word brief
       -> architect (Fable) answers from the brief alone, <600 words
       -> test-writer transcribes the answer as an ADR
  -> implementer writes the code against the contract
  -> test-writer writes the suite and the docs
```

## Why it is shaped this way

Frontier-model cost is dominated by **input**, not output. An architect that
greps `framework.h` and reads six backends spends ~100k input tokens before
writing a word. So the architect is given no search tools and a hard two-file
`Read` cap — the restriction is enforced by the agent definition, not by asking
politely.

Three rules do the rest of the work:

1. **Batch.** Every consultation re-pays the system prompt and brief. One
   consultation answering six questions costs a fraction of six consultations.
2. **Write it down.** `docs/superpowers/decisions/` is checked *before*
   escalating. A recorded decision is free forever; re-asking is the largest
   avoidable cost in this setup.
3. **Default to not escalating.** Only hard-to-reverse questions qualify. The
   architect returning `OUT OF SCOPE — implementer's call` is a success.

## What earns an architect call in this repo

- Semantics that must hold across all five backends (C#, C++, JS, LLVM, MSIL)
- Bending the two-layer C++ std model (`shared_ptr<List<T>>` reference
  semantics vs. `String`/struct values)
- New IR node shape and the obligations it creates on every backend
- Changes to the `framework.h` / `RaylibWrapper.vb` export-sync invariant
- Shared-consumer source: `ModuleResolver.cs` (compiler + LSP),
  `ModuleTypeWalker.cs` (compiler + C++ backend/capability checkers)
- Layering between Core / Editor / ProjectSystem / Shell

Everything else is the implementer's call.

## Note on planning

"Architecture" and "planning" are the same activity at different altitudes, so
they are split by altitude rather than duplicated: the architect sets contracts
and invariants, the implementer sequences the work inside them. Giving both
agents open-ended planning produces contradictions that cost *more* architect
time to arbitrate.
