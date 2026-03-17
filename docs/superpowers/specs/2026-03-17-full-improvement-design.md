# Full Project Improvement — Design Spec

**Date:** 2026-03-17
**Scope:** VSIX template fix, compiler bug fixes, engine wrapper completion, new engine features
**Agents:** VSExtensionAgent, BasicLangAgent, EngineAgent

---

## Sub-Project A: VSIX Template Fix (VSExtensionAgent)

### Problem
Templates don't appear under "BasicLang" in VS 2022 New Project dialog. Root cause: `<ProjectType>VisualBasic</ProjectType>` in all vstemplates/vstman files doesn't match `LanguageVsTemplate = "BasicLang"` in ProvideProjectFactory.

### Fix
- Change `<ProjectType>VisualBasic</ProjectType>` → `<ProjectType>BasicLang</ProjectType>` in 7 vstemplates + 2 vstman files
- Add `<LanguageTag>BasicLang</LanguageTag>` to 3 item template vstman entries
- Rebuild VSIX with MSBuild

### Success Criteria
- VSIX builds without errors
- All 9 template files have matching ProjectType=BasicLang
- Templates appear under "BasicLang" filter in New Project dialog

---

## Sub-Project B: Compiler Bug Fixes (BasicLangAgent)

### Phase 1 — High Priority (5 items)

| # | Bug | File | Fix |
|---|-----|------|-----|
| 1 | Number parsing overflow | BasicLangLexer.cs | TryParse + InvariantCulture |
| 2 | No hex/octal/binary literals | BasicLangLexer.cs | &H, &O, &B prefix parsing |
| 3 | Missing type mappings | CSharpBackend.cs, MSILBackend.cs, LLVMBackend.cs, CppCodeGenerator.cs | Add Byte, Short, UByte, UShort, UInteger, ULong, Decimal |
| 4 | Protected modifier | Parser.cs | Add Protected/Protected Friend handling |
| 5 | Swallowed exception | IROptimizer.cs | Narrow catch, log exception |

### Phase 2 — Medium Priority (7 items)

| # | Item | File | Fix |
|---|------|------|-----|
| 6 | Async/Await stubs | LLVMBackend, MSILBackend, CppCodeGenerator | Emit #warning instead of silent comments |
| 7 | Modulo optimization | IROptimizer.cs | Use BitwiseAnd for x%pow2 |
| 8 | Line continuation | BasicLangLexer.cs | Handle _ at end of line |
| 9 | C++ ForEach body | CppCodeGenerator.cs | Verify block processing |
| 10 | Framework TODO → #error | CppCodeGenerator.cs | Change // TODO to #error |
| 11 | LSP incremental sync | LSP/TextDocumentSyncHandler.cs | Implement Incremental sync |
| 12 | Compiler test coverage | Tests/Compiler/ | Add SemanticAnalyzer, IRBuilder, backend tests |

### Phase 3 — Low Priority (6 items)

| # | Item | File | Fix |
|---|------|------|-----|
| 13 | Char literal syntax | BasicLangLexer.cs | "A"c VB-style |
| 14 | Rem comments | BasicLangLexer.cs | Rem keyword |
| 15 | Conditional namespace imports | CSharpBackend.cs | Track used namespaces |
| 16 | C++ indexer access | CppCodeGenerator.cs | Implement visitor |
| 17 | Multi-root workspaces | LSP/WorkspaceManager.cs | Multiple folders |
| 18 | Error codes | ErrorFormatter.cs | Assign codes to all diagnostics |

### Success Criteria
- All 1636+ existing tests pass after each phase
- New compiler tests added for semantic analyzer and backends
- No regressions in any backend output

---

## Sub-Project C: Engine Wrapper Completion (EngineAgent)

### Phase 1 — Wrap Existing Unwrapped Functions (~113)

| System | Functions | Lines in framework.h |
|--------|-----------|---------------------|
| 2D Lighting | ~93 | 3193-3303 |
| Introspection API | ~18 | 944-963 |
| Missing collisions | 2 | scattered |

### Success Criteria
- `dotnet build RaylibWrapper/RaylibWrapper.csproj` succeeds
- check_sync tool shows 0 missing wrappers for these systems

---

## Sub-Project D: New Engine Features (EngineAgent)

### Phase 2 — High/Medium Value Features (~130 new C++ functions + VB.NET wrappers)

| Feature | Functions | Priority |
|---------|-----------|----------|
| Random Number Generator | ~12 | High |
| Additional Shape Drawing | ~16 | Medium (Raylib wraps) |
| Text Measurement & Advanced Text | ~8 | Medium |
| Gamepad/Controller Input | ~14 | Medium (Raylib wraps) |
| Color Utilities | ~10 | Low |
| Window/Display Utilities | ~15 | Low |

### Phase 3 — Advanced Features

| Feature | Functions | Priority |
|---------|-----------|----------|
| Standalone Sprite Animation Player | ~14 | High |
| Tilemap Collision Integration | ~12 | High |
| Nine-Slice Drawing | ~6 | High |
| Coroutine/Sequence System | ~10 | Medium |
| Touch Input | ~10 | Medium |
| Screenshot/Recording | ~4 | Low |

### Success Criteria
- All new C++ functions compile without errors
- All new functions have matching VB.NET P/Invoke declarations
- check_sync shows 0 gaps
- Existing TestVbDLL still builds and runs

---

## Execution Plan

```
Phase 1 (parallel):
  VSExtensionAgent → Fix ProjectType in 9 files, rebuild VSIX
  BasicLangAgent   → Fix 5 high-priority compiler bugs
  EngineAgent      → Wrap 113 existing unwrapped functions

Phase 2 (parallel):
  BasicLangAgent   → Fix 7 medium-priority items + add tests
  EngineAgent      → Add RNG, shapes, text, gamepad, colors, window (C++ + VB.NET)

Phase 3 (parallel):
  BasicLangAgent   → Fix 6 low-priority items
  EngineAgent      → Add sprite animation, tilemap collision, nine-slice, coroutines, touch, screenshot

Each phase → commit + push
```

## Agent Assignment

| Agent | Profiles Used | MCP Tools Used |
|-------|--------------|----------------|
| VSExtensionAgent | templates, pkgdef | build_vsix, check_templates, validate_pkgdef |
| BasicLangAgent | compiler, tests | compile_basiclang, run_tests |
| EngineAgent | engine, wrapper | build_engine, build_wrapper, check_sync, count_exports, count_pinvokes |
